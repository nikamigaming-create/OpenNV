using System.Xml.Linq;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutUiComponentStore
{
    internal XElement CopyComponent(FalloutUiComponentBinding binding)
    {
        RequireComponent(binding);
        return new XElement(_tiles[binding.Path].Source);
    }
}
