namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    // In-process neutral source declarations belong to this exact selection.
    // No serialized address/decoded object table becomes a launch input.
    private FalloutNativePluginCampaignSelection? _nativePluginDeclarations;
    private FalloutNativePluginHostDependency? _nativePluginHostDependency;
    internal void BindNativePluginHostDependency(FalloutNativePluginHostDependency dependency)
    {
        if (_nativePluginDeclarations is not null || _nativePluginHostDependency is not null ||
            string.IsNullOrWhiteSpace(dependency.SourceOwner) || !File.Exists(dependency.PhysicalPath))
            throw new InvalidOperationException("Native host dependency is absent or already selected.");
        _nativePluginHostDependency = dependency with { PhysicalPath = Path.GetFullPath(dependency.PhysicalPath) };
    }
    internal FalloutNativePluginCampaignSelection EnsureNativePluginDeclarations()
    {
        if (_nativePluginDeclarations is null)
            BindNativePluginDeclarations(FalloutNativePluginStartupDeclarations.Build(this, _nativePluginHostDependency));
        return _nativePluginDeclarations!;
    }
    internal FalloutNativePluginCampaignSelection? NativePluginDeclarations => _nativePluginDeclarations;
    internal void BindNativePluginDeclarations(FalloutNativePluginCampaignSelection declaration)
    {
        if (_nativePluginDeclarations is not null || !ReferenceEquals(declaration.Source, this))
            throw new InvalidOperationException("Native plugin declarations are foreign or already bound to the selected source.");
        _nativePluginDeclarations = declaration;
    }
}
