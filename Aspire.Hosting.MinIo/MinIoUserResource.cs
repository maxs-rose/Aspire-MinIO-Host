using System.Collections.Immutable;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.MinIo.Models;

namespace Aspire.Hosting.MinIo;

public sealed class MinIoUserResource : Resource, IResourceWithParent<MinIoResource>
{
    private readonly List<MinIoPolicyResource> _policies = [];

    internal MinIoUserResource(MinIoResource parent, string name, ParameterResource secretAccessKey) : base(name)
    {
        var param = new ParameterResource($"{name}-accessKey", x => x!.GetDefaultValue())
        {
            Default = new ConstantParameterDefault(name)
        };

        AccessKey = ReferenceExpression.Create($"{param}");
        SecretAccessKey = ReferenceExpression.Create($"{secretAccessKey}");
        Parent = parent;
    }

    public ReferenceExpression AccessKey { get; }
    public ReferenceExpression SecretAccessKey { get; }

    public ImmutableArray<MinIoPolicyResource> Policies => [.._policies];
    public MinIoResource Parent { get; }

    public void AddPolicy(MinIoPolicyResource policyResource)
    {
        _policies.Add(policyResource);
    }
}