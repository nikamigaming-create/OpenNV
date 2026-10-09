using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginLoadedFiles
{
    internal sealed partial class Lease
    {
        internal NativeNvseSourceFileCurrentStamp SourceCurrentStamp()
        {
            RequireCurrent(); RequireIdle(); return _reader.File.SourceCurrentStamp();
        }

        internal void RetainSourceOutputFailure(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            // This callback is entered only after the actual source/caller
            // identity was validated. The one campaign reader retains its
            // consumed prefix across every module sharing this lease.
            ObjectDisposedException.ThrowIf(_disposed, this);
            _owner.RequireCurrent(); _owner.RequireContext(_reader.Context);
            if (_reader.Retired || _reader.Retainers <= 0 || _reader.Generation == 0 ||
                !_owner._readers.TryGetValue(_reader.Context, out var actual) || !ReferenceEquals(actual, _reader))
                throw new InvalidOperationException("Failed source output has a foreign or retired shared contributor lease.");
            // An already-failed source is precisely the lifetime this method
            // must retain; the ordinary healthy-file guard is inappropriate.
            _reader.File.RetainFailure(failure);
        }
    }
}
