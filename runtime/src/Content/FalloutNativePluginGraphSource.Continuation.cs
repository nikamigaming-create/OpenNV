using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginGraphSource
{
    private sealed partial class ModAuthority : INativeNvseSourceFileCurrentAuthority
    {
        NativeNvseSourceFileCurrentStamp INativeNvseSourceFileCurrentAuthority.ReadSourceFileCurrentStamp()
            => LoadedFile().SourceCurrentStamp();
        void INativeNvseSourceFileCurrentAuthority.RetainSourceFileOutputFailure(Exception error)
            => LoadedFile().RetainSourceOutputFailure(error);
    }

    internal FalloutNativeSourceCollectionState? CaptureLoadedSourceState() => _loadedContributors?.CaptureSourceState();
    internal void RestoreLoadedSourceState(FalloutNativeSourceCollectionState saved)
        => (_loadedContributors ?? throw new NotSupportedException("Original cold source continuation lacks its genuine recreated contributor leases.")).RestoreSourceState(saved);
}
