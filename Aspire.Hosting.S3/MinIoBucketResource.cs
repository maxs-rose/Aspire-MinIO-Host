using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.S3;

public sealed class MinIoBucketResource : Resource, IResourceWithParent<MinIoResource>, IResourceWithConnectionString
{
    internal MinIoBucketResource(MinIoResource parent, string name) : base(name)
    {
        Parent = parent;
    }

    public string Policy { get; internal set; } = string.Empty;

    public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"s3://{Name}");

    public MinIoResource Parent { get; }
}