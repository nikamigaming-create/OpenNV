using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginGraphSource
{
    private sealed partial class ModAuthority : INativeNvseSourceFileAuthority
    {
        internal FalloutNativePluginGraphSource GraphSource => _source;
        string INativeNvseSourceFileAuthority.SourceFileSha256 => _mod.Sha256;
        void INativeNvseSourceFileAuthority.RequireSourceFileCurrent() => LoadedFile().RequireSourceFileCurrent();
        NativeNvseFileReadResult INativeNvseSourceFileAuthority.CallSourceFile(NativeNvseFileMethod method, uint capacity)
            => LoadedFile().CallSourceFile(method, capacity);
    }

    internal FalloutNativeLoadedFileSnapshot CaptureContributorFile(NativeNvseSourceObject contributor)
        => Contributor(contributor).LoadedFile().Capture();
    internal void RestoreContributorFile(NativeNvseSourceObject contributor, FalloutNativeLoadedFileSnapshot saved)
        => Contributor(contributor).LoadedFile().Restore(saved);
    private ModAuthority Contributor(NativeNvseSourceObject contributor)
    {
        if (contributor.Class != NativeNvseSourceClass.ModInfo || contributor.Staged || contributor.Retired ||
            contributor.Calls != 0 || contributor.Authority is not ModAuthority authority || !ReferenceEquals(authority.GraphSource, this))
            throw new InvalidDataException("Contributor parser has no idle actual campaign graph identity.");
        return authority;
    }

    // The reached source reader selects this contributor's original record,
    // including overridden/deleted bytes; stack winners never replace its cursor.
    internal void SelectContributorRecord(NativeNvseSourceObject contributor, FalloutPluginRecord record)
        => Contributor(contributor).LoadedFile().SelectRecord(record);
}
