using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Publishing;

namespace Aspire.Hosting.MinIo.Models;

internal sealed class ConstantParameterDefault(string value) : ParameterDefault
{
    public override void WriteToManifest(ManifestPublishingContext context)
    {
        throw new NotImplementedException();
    }

    public override string GetDefaultValue()
    {
        return value;
    }
}