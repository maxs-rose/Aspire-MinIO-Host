using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.S3.Clients;
using Microsoft.Net.Http.Headers;
using Minio;
using Refit;

namespace Aspire.Hosting.S3;

public sealed class MinIoResource : ContainerResource, IResourceWithConnectionString
{
    internal const string ConsoleEndpointName = "console";
    internal const ushort ConsoleEndpointPort = 9001;

    internal const string ApiEndpointName = "api";
    internal const ushort ApiEndpointPort = 9000;
    private IMinioAdminClient? _adminClient;

    private IMinioClient? _client;

    internal MinIoResource(string name, ParameterResource accessKey, ParameterResource secretAccessKey) : base(name)
    {
        AccessKey = accessKey;
        SecretAccessKey = secretAccessKey;

        PrimaryEndpoint = new EndpointReference(this, ApiEndpointName);
        ConsoleEndpoint = new EndpointReference(this, ConsoleEndpointName);
    }

    internal List<MinIoBucketResource> Buckets { get; } = [];
    internal List<MinIoPolicyResource> Policies { get; } = [];

    public ParameterResource AccessKey { get; }
    public ParameterResource SecretAccessKey { get; }

    internal EndpointReference PrimaryEndpoint { get; }
    internal EndpointReference ConsoleEndpoint { get; }

    internal ReferenceExpression UsernameReference => ReferenceExpression.Create($"{AccessKey}");
    internal ReferenceExpression PasswordReference => ReferenceExpression.Create($"{SecretAccessKey}");

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create(
        $"http://{PrimaryEndpoint.Property(EndpointProperty.HostAndPort)}");

    public async ValueTask<IMinioClient> GetClient(CancellationToken cancellationToken)
    {
        if (_client is not null) return _client;

        _client = new MinioClient()
            .WithEndpoint($"{await PrimaryEndpoint.Property(EndpointProperty.HostAndPort).GetValueAsync(cancellationToken)}")
            .WithCredentials(
                await UsernameReference.GetValueAsync(cancellationToken),
                await PasswordReference.GetValueAsync(cancellationToken))
            .Build();

        return _client!;
    }

    internal void ResetAdminClient()
    {
        _adminClient = null;
    }

    internal async ValueTask<IMinioAdminClient> GetAdminClient(CancellationToken cancellationToken)
    {
        if (_adminClient is not null)
            return _adminClient;

        var adminClient = RestService.For<IMinioAdminClient>($"http://{await ConsoleEndpoint.Property(EndpointProperty.HostAndPort).GetValueAsync(cancellationToken)}");


        var loginResponse = await adminClient.Login(new LoginRequest(
            $"{await UsernameReference.GetValueAsync(cancellationToken)}",
            $"{await PasswordReference.GetValueAsync(cancellationToken)}"));

        if (!loginResponse.IsSuccessStatusCode)
            throw new DistributedApplicationException("Failed to login to MinIO console");

        var token = loginResponse.Headers.GetValues("Set-Cookie")
            .Select(v => SetCookieHeaderValue.Parse(v));

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://{await ConsoleEndpoint.Property(EndpointProperty.HostAndPort).GetValueAsync(cancellationToken)}")
        };

        foreach (var cookie in token)
            httpClient.DefaultRequestHeaders.Add("Cookie", cookie.ToString());

        adminClient = RestService.For<IMinioAdminClient>(httpClient);

        return adminClient;
    }
}