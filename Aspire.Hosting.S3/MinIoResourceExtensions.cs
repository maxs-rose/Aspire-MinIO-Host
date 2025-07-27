using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.S3.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;

namespace Aspire.Hosting.S3;

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
            .WithImageTag(MinIoContainerImageTag.Tag)
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

        var healthCheckKey = $"{bucket.Name}_check";
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

    public static IResourceBuilder<MinIoBucketResource> WithPolicy(this IResourceBuilder<MinIoBucketResource> builder, [StringSyntax(StringSyntaxAttribute.Json)] string policy)
    {
        builder.Resource.Policy = policy;

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
}