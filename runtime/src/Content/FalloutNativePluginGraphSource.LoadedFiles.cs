using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginGraphSource
{
    private FalloutNativePluginLoadedFiles? _loadedContributors;
    private FalloutNativePluginLoadedFiles LoadedContributors() => _loadedContributors ??= new(_records);

    private sealed partial class ModAuthority
    {
        private FalloutNativePluginLoadedFiles.Lease? _loadedFile;
        internal FalloutNativePluginLoadedFiles.Lease LoadedFile() => _loadedFile ??
            throw new NotSupportedException("Actual contributor image has no retained shared binary/parser/metadata lifetime.");
        private IReadOnlyList<NativeNvseDataField> LoadedFileFields() => LoadedFile().NativeFields();

        private IDisposable RetainModLoadedSource()
        {
            if (_loadedFile is not null) throw new InvalidOperationException("Actual contributor loaded-file lease is already retained.");
            var original = SourceLease.Open(_mod.Plugin.Path, _source._records.OwnedSource!.FalloutExecutablePath,
                _source._construction.Value.RuntimeSha256, RequireCurrent);
            try
            {
                _loadedFile = _source.LoadedContributors().Retain(_mod);
                return new LoadedModSourceLease(this, _loadedFile, original);
            }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                try { _loadedFile?.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
                _loadedFile = null;
                try { original.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
                if (failures.Count != 1) throw new AggregateException("Contributor loaded-source construction/retirement failed.", failures);
                throw;
            }
        }

        private sealed class LoadedModSourceLease(ModAuthority authority,
            FalloutNativePluginLoadedFiles.Lease file, IDisposable original) : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return; _disposed = true;
                var failures = new List<Exception>();
                try { file.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
                finally { authority._loadedFile = null; }
                try { original.Dispose(); } catch (Exception failure) { failures.Add(failure); }
                if (failures.Count != 0) throw new AggregateException("Contributor loaded-source lease retirement failed.", failures);
            }
        }
    }

    internal FalloutNativeLoadedFilesSnapshot? CaptureLoadedContributors() => _loadedContributors?.Capture();
    internal void RestoreLoadedContributors(FalloutNativeLoadedFilesSnapshot saved)
        => (_loadedContributors ?? throw new NotSupportedException("Cold contributors lack the recreated actual native graph/source leases.")).Restore(saved);
    internal void RetireLoadedContributors()
    {
        if (_loadedContributors is not { } files) return;
        // Native child closure and every image source lease must happen before
        // this campaign collection retires. Source handles never prove native
        // object closure, and a failed native call never manufactures a receipt.
        if (files.HasRetainedReaders)
            throw new InvalidOperationException("Native contributor images still retain actual loaded-file source leases.");
        files.Dispose(); _loadedContributors = null;
    }
}
