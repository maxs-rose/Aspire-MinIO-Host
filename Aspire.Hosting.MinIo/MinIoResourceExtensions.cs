using System.Diagnostics.CodeAnalysis;
using System.Net;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.MinIo.Clients;
using Aspire.Hosting.MinIo.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace Aspire.Hosting.MinIo;

public static class MinIoResourceExtensions
{
    private const string UserNameEnvVar = "MINIO_ROOT_USER";
    private const string PasswordEnvVar = "MINIO_ROOT_PASSWORD";

    private static readonly ParameterResource DefaultUsername = new(
        "minio-username",
        x => x?.GetDefaultValue() ??
             throw new DistributedApplicationException("Parameter resource could not be used because configuration key 'minio-username' is missing and the Parameter has no default value."))
    {
        Default = new ConstantParameterDefault("minio")
    };

    private static readonly ParameterResource DefaultPassword = new(
        "minio-password",
        x => x?.GetDefaultValue() ??
             throw new DistributedApplicationException("Parameter resource could not be used because configuration key 'minio-password' is missing and the Parameter has no default value."),
        true)
    {
        Default = new ConstantParameterDefault("miniopassword")
    };

    public static IResourceBuilder<MinIoResource> AddMinIo(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<ParameterResource>? username = null,
        IResourceBuilder<ParameterResource>? password = null)
    {
        var minio = new MinIoResource(
            name,
            username?.Resource ?? DefaultUsername,
            password?.Resource ?? DefaultPassword);

        var minioResource = builder.AddResource(minio)
            .WithImage(MinIoContainerImageTag.Image)
            .WithImageTag(MinIoContainerImageTag.Latest)
            .WithImageRegistry(MinIoContainerImageTag.Registry)
            .WithHttpEndpoint(targetPort: MinIoResource.ApiEndpointPort, name: MinIoResource.ApiEndpointName)
            .WithHttpEndpoint(targetPort: MinIoResource.ConsoleEndpointPort, name: MinIoResource.ConsoleEndpointName)
            .WithEnvironment(ctx =>
            {
                ctx.EnvironmentVariables[UserNameEnvVar] = minio.UsernameReference;
                ctx.EnvironmentVariables[PasswordEnvVar] = minio.PasswordReference;
            })
            .WithArgs("server", "/data", "--console-address", $":{MinIoResource.ConsoleEndpointPort}")
            .WithHttpHealthCheck("/minio/health/live", endpointName: MinIoResource.ApiEndpointName)
            .WithHttpHealthCheck("/login", endpointName: MinIoResource.ConsoleEndpointName);

        builder.Eventing.Subscribe<ResourceReadyEvent>(
            minio,
            async (e, ct) =>
            {
                if (e.Resource is not MinIoResource tenant)
                    return;

                var client = await tenant.GetClient(ct);

                foreach (var bucket in tenant.Buckets)
                    await CreateBucket(client, bucket, e.Services, ct);

                foreach (var policy in tenant.Policies)
                    await CreatePolicy(tenant, policy, e.Services, ct);

                foreach (var user in tenant.Users)
                    await CreateUser(tenant, user, e.Services, ct);
            });

        return minioResource;
    }

    public static IResourceBuilder<MinIoResource> WithDataVolume(this IResourceBuilder<MinIoResource> builder, string? name = null)
    {
        builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), "/data");

        return builder;
    }

    public static IResourceBuilder<MinIoBucketResource> AddBucket(this IResourceBuilder<MinIoResource> builder, string name)
    {
        var bucket = new MinIoBucketResource(builder.Resource, name);

        builder.Resource.Buckets.Add(bucket);

        var bucketBuilder = builder.ApplicationBuilder
            .AddResource(bucket)
            .WithPolicy(BucketPolicy.Private);

        var tenantHasStarted = false;

        builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(
            bucket,
            (_, _) =>
            {
                tenantHasStarted = true;

                return Task.CompletedTask;
            });

        var healthCheckKey = $"{bucket.Name}_bucket_check";
        builder.ApplicationBuilder.Services.AddHealthChecks()
            .AddAsyncCheck(
                healthCheckKey,
                async ct =>
                {
                    if (!tenantHasStarted)
                        return HealthCheckResult.Unhealthy("Tenant is not available");

                    var client = await bucket.Parent.GetClient(ct);

                    if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket.Name), ct))
                        return HealthCheckResult.Unhealthy("Bucket not found");

                    return HealthCheckResult.Healthy();
                });

        bucketBuilder.WithHealthCheck(healthCheckKey);

        return bucketBuilder;
    }

    public static IResourceBuilder<MinIoBucketResource> WithPolicy(this IResourceBuilder<MinIoBucketResource> builder, BucketPolicy policy)
    {
        builder.Resource.Policy = policy switch
        {
            BucketPolicy.Public =>
                $$"""
                  {
                      "Version": "2012-10-17",
                      "Statement": [
                          {
                              "Effect": "Allow",
                              "Principal": {
                                "AWS": ["*"]
                              },
                              "Action": "s3:*",
                              "Resource": [
                                "arn:aws:s3:::{{builder.Resource.Name}}",
                                "arn:aws:s3:::{{builder.Resource.Name}}/*"
                              ]
                          }
                      ]
                  }
                  """,
            BucketPolicy.Private =>
                $$"""
                  {
                    "Version": "2012-10-17",
                    "Statement": [
                      {
                        "Effect": "Allow",
                        "Principal": {
                          "AWS": [
                            "*"
                          ]
                        },
                        "Action": [
                          "s3:GetBucketLocation",
                          "s3:ListBucket",
                          "s3:ListBucketMultipartUploads"
                        ],
                        "Resource": [
                          "arn:aws:s3:::{{builder.Resource.Name}}"
                        ]
                      },
                      {
                        "Effect": "Allow",
                        "Principal": {
                          "AWS": [
                            "*"
                          ]
                        },
                        "Action": [
                          "s3:ListMultipartUploadParts",
                          "s3:PutObject",
                          "s3:AbortMultipartUpload",
                          "s3:DeleteObject",
                          "s3:GetObject"
                        ],
                        "Resource": [
                          "arn:aws:s3:::{{builder.Resource.Name}}/*"
                        ]
                      }
                    ]
                  }
                  """,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null)
        };
        return builder;
    }

    public static IResourceBuilder<MinIoBucketResource> WithPolicy(this IResourceBuilder<MinIoBucketResource> builder, [StringSyntax(StringSyntaxAttribute.Json)] string policyJson)
    {
        builder.Resource.Policy = policyJson;

        return builder;
    }

    /// <remarks>
    ///     Cannot be used if using an image produced after RELEASE.2025-04-08T15-41-24Z-cpuv1
    /// </remarks>
    public static IResourceBuilder<MinIoPolicyResource> AddPolicy(this IResourceBuilder<MinIoResource> builder, string name, [StringSyntax(StringSyntaxAttribute.Json)] string policyJson)
    {
        var policyResource = new MinIoPolicyResource(name, builder.Resource, policyJson);

        builder.Resource.Policies.Add(policyResource);

        var tenantHasStarted = false;

        builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(
            builder.Resource,
            (_, _) =>
            {
                tenantHasStarted = true;

                return Task.CompletedTask;
            });

        var healthCheckKey = $"{policyResource.Name}_policy_check";
        builder.ApplicationBuilder.Services.AddHealthChecks()
            .AddAsyncCheck(
                healthCheckKey,
                async ct =>
                {
                    if (!tenantHasStarted)
                        return HealthCheckResult.Unhealthy("Tenant is not available");

                    var client = await policyResource.Parent.GetAdminClient(ct);
                    var getResponse = await client.GetPolicy(policyResource.Name);

                    if (getResponse.StatusCode == HttpStatusCode.Forbidden)
                        policyResource.Parent.ResetAdminClient();

                    if (!getResponse.IsSuccessStatusCode)
                        return HealthCheckResult.Unhealthy("Policy not found");

                    return HealthCheckResult.Healthy();
                });

        return builder.ApplicationBuilder.AddResource(policyResource)
            .WithHealthCheck(healthCheckKey);
    }

    /// <remarks>
    ///     Cannot be used if using an image produced after RELEASE.2025-04-08T15-41-24Z-cpuv1
    /// </remarks>
    public static IResourceBuilder<MinIoUserResource> AddUser(this IResourceBuilder<MinIoResource> builder, string name, IResourceBuilder<ParameterResource>? secretAccessKey = null)
    {
        var user = new MinIoUserResource(
            builder.Resource,
            name,
            secretAccessKey?.Resource ?? ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder.ApplicationBuilder, $"{name}-secretAccessKey"));

        builder.Resource.Users.Add(user);

        var tenantHasStarted = false;

        builder.ApplicationBuilder.Eventing.Subscribe<ConnectionStringAvailableEvent>(
            builder.Resource,
            (_, _) =>
            {
                tenantHasStarted = true;

                return Task.CompletedTask;
            });

        var healthCheckKey = $"{user.Name}_user_check";
        builder.ApplicationBuilder.Services.AddHealthChecks()
            .AddAsyncCheck(
                healthCheckKey,
                async ct =>
                {
                    if (!tenantHasStarted)
                        return HealthCheckResult.Unhealthy("Tenant is not available");

                    var client = await user.Parent.GetAdminClient(ct);
                    var getResponse = await client.GetUser(user.Name);

                    if (getResponse.StatusCode == HttpStatusCode.Forbidden)
                        user.Parent.ResetAdminClient();

                    if (!getResponse.IsSuccessStatusCode)
                        return HealthCheckResult.Unhealthy("User not found");

                    return HealthCheckResult.Healthy();
                });

        return builder.ApplicationBuilder.AddResource(user)
            .WithHealthCheck(healthCheckKey);
    }

    public static IResourceBuilder<MinIoUserResource> WithPolicy(this IResourceBuilder<MinIoUserResource> builder, params IEnumerable<IResourceBuilder<MinIoPolicyResource>> policies)
    {
        foreach (var policy in policies.Where(p => !builder.Resource.Policies.Contains(p.Resource)))
            builder.Resource.AddPolicy(policy.Resource);

        return builder;
    }

    private static async Task CreateBucket(IMinioClient client, MinIoBucketResource bucket, IServiceProvider serviceProvider, CancellationToken ct)
    {
        var logger = serviceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(bucket);

        try
        {
            if (!await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket.Name), ct))
            {
                await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket.Name), ct);
                logger.LogDebug("Bucket '{BucketName}': Created successfully", bucket.Name);
            }
            else
            {
                logger.LogDebug("Bucket '{BucketName}': Already exists", bucket.Name);
            }

            await client.SetPolicyAsync(new SetPolicyArgs().WithBucket(bucket.Name).WithPolicy(bucket.Policy), ct);
            logger.LogDebug("Bucket '{BucketName}': Successfully set access policy", bucket.Name);
        }
        catch (Exception exception)
        {
            throw new DistributedApplicationException($"Failed to create MinIO bucket '{bucket.Name}'", exception);
        }
    }

    private static async Task CreatePolicy(MinIoResource tenant, MinIoPolicyResource minIoPolicy, IServiceProvider serviceProvider, CancellationToken ct)
    {
        var logger = serviceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(minIoPolicy);

        try
        {
            var client = await tenant.GetAdminClient(ct);

            if (!(await client.GetPolicy(minIoPolicy.Name)).IsSuccessStatusCode)
            {
                var res = await client.CreatePolicy(new CreatePolicy(minIoPolicy.Name, minIoPolicy.Policy));

                if (res.StatusCode == HttpStatusCode.Forbidden)
                {
                    logger.LogWarning("Authentication expired refreshing token");
                    tenant.ResetAdminClient();
                    await CreatePolicy(tenant, minIoPolicy, serviceProvider, ct);
                    return;
                }

                if (!res.IsSuccessStatusCode)
                {
                    logger.LogError(res.Error, "Policy '{PolicyName}': Failed to create", minIoPolicy.Name);
                    return;
                }

                logger.LogDebug("Bucket '{PolicyName}': Created successfully", minIoPolicy.Name);
            }
            else
            {
                logger.LogDebug("Policy '{PolicyName}': Already exists", minIoPolicy.Name);
            }
        }
        catch (Exception exception)
        {
            throw new DistributedApplicationException($"Failed to create MinIO policy '{minIoPolicy.Name}'", exception);
        }
    }

    private static async Task CreateUser(MinIoResource tenant, MinIoUserResource user, IServiceProvider serviceProvider, CancellationToken ct)
    {
        var logger = serviceProvider.GetRequiredService<ResourceLoggerService>().GetLogger(user);

        try
        {
            var client = await tenant.GetAdminClient(ct);

            if (!(await client.GetUser(user.Name)).IsSuccessStatusCode)
            {
                var res = await client.CreateUser(new CreateUser(
                    user.Name,
                    (await user.SecretAccessKey.GetValueAsync(ct))!,
                    user.Policies.Select(p => p.Name).ToArray()));

                if (res.StatusCode == HttpStatusCode.Forbidden)
                {
                    logger.LogWarning("Authentication expired refreshing token");
                    tenant.ResetAdminClient();
                    await CreateUser(tenant, user, serviceProvider, ct);
                    return;
                }

                if (!res.IsSuccessStatusCode)
                {
                    logger.LogError(res.Error, "User '{UserName}': Failed to create", user.Name);
                    return;
                }

                logger.LogDebug("User '{UserName}': Created successfully", user.Name);
            }
            else
            {
                logger.LogDebug("User '{UserName}': Already exists", user.Name);
            }
        }
        catch (Exception exception)
        {
            throw new DistributedApplicationException($"Failed to create MinIO user '{user.Name}'", exception);
        }
    }
}