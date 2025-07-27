using Aspire.Hosting.ApplicationModel;
using Minio;

namespace Aspire.Hosting.S3;

public sealed class MinIoResource : ContainerResource, IResourceWithConnectionString
{
    internal const string ConsoleEndpointName = "console";
    internal const ushort ConsoleEndpointPort = 9001;

    internal const string ApiEndpointName = "api";
    internal const ushort ApiEndpointPort = 9000;

    private IMinioClient? _client;

    internal MinIoResource(string name, ParameterResource accessKey, ParameterResource secretAccessKey) : base(name)
    {
        AccessKey = accessKey;
        SecretAccessKey = secretAccessKey;

        PrimaryEndpoint = new EndpointReference(this, ApiEndpointName);
        ConsoleEndpoint = new EndpointReference(this, ConsoleEndpointName);
    }

    internal List<MinIoBucketResource> Buckets { get; } = new();

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
}