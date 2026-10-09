using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutPluginStack NativeSourceRecords => records;
    internal IFalloutNativePluginCampaign? NativePlugins { get; set; }
}
