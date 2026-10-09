namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutScriptValueStore
{
    internal FalloutScriptValueStore BindArraySource(FalloutPluginStack records)
    { Arrays.BindNativeOwnershipSource(records); return this; }
}
