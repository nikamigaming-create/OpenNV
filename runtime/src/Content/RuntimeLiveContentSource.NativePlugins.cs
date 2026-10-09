namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    // In-process neutral source declarations belong to this exact selection.
    // No serialized address/decoded object table becomes a launch input.
    private FalloutNativePluginCampaignSelection? _nativePluginDeclarations;
    internal FalloutNativePluginCampaignSelection? NativePluginDeclarations => _nativePluginDeclarations;
    internal void BindNativePluginDeclarations(FalloutNativePluginCampaignSelection declaration)
    {
        if (_nativePluginDeclarations is not null || !ReferenceEquals(declaration.Source, this))
            throw new InvalidOperationException("Native plugin declarations are foreign or already bound to the selected source.");
        _nativePluginDeclarations = declaration;
    }
}
