using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginLoadedFiles
{
    internal sealed partial class Lease : INativeNvseSourceFileAuthority, IDisposable
    {
        private readonly FalloutNativePluginLoadedFiles _owner;
        private readonly Reader _reader;
        private bool _disposed;
        internal Lease(FalloutNativePluginLoadedFiles owner, Reader reader) { _owner = owner; _reader = reader; }

        internal FalloutPluginContext Context { get { RequireCurrent(); return _reader.Context; } }
        internal ulong Generation { get { RequireCurrent(); return _reader.Generation; } }
        public string SourceFileSha256 { get { RequireCurrent(); return _reader.File.SourceSha256; } }
        public void RequireSourceFileCurrent() => RequireCurrent();

        internal void RequireCurrent()
        {
            ObjectDisposedException.ThrowIf(_disposed, this); _owner.RequireReader(_reader);
        }
        internal FalloutNativePluginBinaryFile BinaryFile()
        {
            RequireCurrent(); return _reader.File.Binary;
        }

        internal IReadOnlyList<NativeNvseDataField> NativeFields()
        {
            RequireCurrent(); return _reader.File.NativeFields();
        }
        internal FalloutNativeLoadedFileSnapshot Capture()
        {
            RequireCurrent(); RequireIdle(); return _reader.File.Capture();
        }
        internal void Restore(FalloutNativeLoadedFileSnapshot saved)
        {
            RequireCurrent(); RequireIdle(); _reader.File.Restore(saved);
        }
        internal void SelectRecord(FalloutPluginRecord record)
        {
            RequireCurrent(); RequireIdle(); _reader.File.SelectRecord(record);
        }
        private void RequireIdle()
        {
            if (_reader.Calls != 0) throw new InvalidOperationException("Loaded contributor has an active source call.");
            _reader.File.RequireIdle();
        }

        public NativeNvseFileReadResult CallSourceFile(NativeNvseFileMethod method, uint capacity)
        {
            RequireCurrent(); RequireIdle();
            if (!Enum.IsDefined(method) || method is NativeNvseFileMethod.NextChunk or NativeNvseFileMethod.AdvanceChunk && capacity != 0 ||
                method == NativeNvseFileMethod.Read32 && capacity != 4)
                throw new InvalidDataException("Loaded contributor call differs from its exact admitted source method/argument ABI.");
            _reader.Calls = checked(_reader.Calls + 1);
            try
            {
                if (method == NativeNvseFileMethod.NextChunk) return new(_reader.File.NextChunk(), default, null);
                if (method == NativeNvseFileMethod.AdvanceChunk) return new(_reader.File.AdvanceChunk() ? 1U : 0U, default, null);
                var read = _reader.File.ReadChunk(capacity);
                return new(1, read.Bytes, read.Diagnostic);
            }
            catch (Exception failure) { _reader.File.RetainFailure(failure); throw; }
            finally { --_reader.Calls; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            // Only successful retirement consumes this lease. A reentrant
            // refusal leaves the genuine retainer available for later cleanup.
            // A last-retainer file failure has already closed/retired its owner.
            try { _owner.Release(_reader); _disposed = true; }
            catch { if (_reader.Retired || _owner._disposed) _disposed = true; throw; }
        }
    }
}
