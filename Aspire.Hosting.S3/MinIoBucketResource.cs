using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.S3;

public sealed class MinIoBucketResource(MinIoResource parent, string name) : Resource(name), IResourceWithParent<MinIoResource>, IResourceWithConnectionString
{
    public string Policy { get; internal set; } = string.Empty;

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"s3://{Name}");

    public MinIoResource Parent { get; } = parent;
}