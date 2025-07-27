using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.MinIo;

public sealed class MinIoPolicyResource : Resource, IResourceWithParent<MinIoResource>
{
    internal MinIoPolicyResource(string name, MinIoResource parent, string policy) : base(name)
    {
        Parent = parent;
        Policy = policy;
    }

    public string Policy { get; }

    public MinIoResource Parent { get; }
}