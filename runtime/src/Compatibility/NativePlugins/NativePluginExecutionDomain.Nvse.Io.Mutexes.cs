namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint MutexCallback = 22;
    private NativePluginMutexLedger? _mutexLedger;
    private NativePluginMutexSource? _mutexSource;
    private readonly List<NativePluginMutexReceipt> _retiredMutexReceipts = [];
    private readonly List<NativePluginMutexDetachReceipt> _retiredMutexDetachReceipts = [];
    internal IReadOnlyList<NativePluginMutexReceipt> NvseMutexReceipts => _mutexLedger?.Receipts ?? _retiredMutexReceipts.AsReadOnly();
    internal IReadOnlyList<NativePluginMutexDetachReceipt> NvseMutexDetachReceipts => _mutexLedger?.DetachReceipts ?? _retiredMutexDetachReceipts.AsReadOnly();
    internal IReadOnlyList<NativePluginMutexComparisonReceipt> NvseMutexComparisons => _process.MutexComparisons;
    internal NativePluginMutexPending? NvseMutexPending => _mutexLedger?.Pending;
    private byte[] DispatchPrivateMutex(ulong parent, uint thread, NativePluginPrivateIo io, BinaryReader reader)
    {
        var plugin = _nvsePlugin ?? throw new NotSupportedException("Mutex caller has no actual entered original NVSE image owner.");
        if (parent == 0 || parent != _currentCall || thread != NativeThread || plugin.Generation != Generation)
            throw new InvalidDataException("Mutex callback has a foreign actual current caller/thread/generation.");
        io.CheckModule(plugin.Path, plugin.Sha256, _nvseHostSource!.StackIdentity);
        _mutexSource ??= new(plugin); _mutexLedger ??= new(Generation, plugin.Module, NativeThread);
        var stage = reader.ReadUInt32();
        if (stage == 1)
        {
            var api = (NativePluginMutexApi)reader.ReadUInt32(); var incomingError = reader.ReadUInt32();
            var security = reader.ReadUInt32(); var securityLength = reader.ReadUInt32(); var descriptor = reader.ReadUInt32(); var inherit = reader.ReadUInt32();
            var flags = reader.ReadUInt32(); var access = reader.ReadUInt32(); var timeout = reader.ReadUInt32();
            var all = reader.ReadUInt32(); var alertable = reader.ReadUInt32(); var name = reader.ReadUInt32();
            var wide = reader.ReadUInt32(); var codePage = reader.ReadUInt32();
            var raw = ReadMutexBytes(reader, 520); var converted = ReadMutexBytes(reader, 520);
            var count = reader.ReadUInt32(); if (count > 64) throw new InvalidDataException("Mutex handle array exceeds its public SDK extent.");
            var handles = new List<NativePluginMutexHandle>();
            for (var at = 0U; at < count; ++at) handles.Add(new(reader.ReadUInt64(), reader.ReadUInt32()));
            Finish(reader); if (wide > 1) throw new InvalidDataException("Mutex source name has an unknown text ABI.");
            _mutexSource.RequireApi(api);
            var sourceName = _mutexSource.Name(name, raw, codePage, converted, wide != 0);
            if (name != 0) _process.RequireMutexLiteral(plugin.Image, name, raw);
            var request = new NativePluginMutexRequest(api, thread, incomingError, security, securityLength, descriptor,
                inherit, flags, access, timeout, all, alertable, sourceName, handles);
            return Payload(writer => writer.Write(_mutexLedger.Begin(parent, request)));
        }
        if (stage == 2)
        {
            var id = reader.ReadUInt64(); var result = reader.ReadUInt32(); var lastError = reader.ReadUInt32();
            var construction = reader.ReadUInt32(); Finish(reader);
            if (construction > 1 || (construction != 0) != _mutexLedger.PendingConstruction)
                throw new InvalidDataException("Mutex result changed its entered construction category.");
            ulong? alias = construction != 0 && result != 0 ? _process.MatchOwnedMutexObject(result, _mutexLedger.LiveHandles) : null;
            _mutexLedger.Complete(parent, id, result, lastError, alias);
            return Payload(writer => writer.Write(1U));
        }
        if (stage is 3 or 4)
        {
            var image = reader.ReadUInt32(); var result = reader.ReadUInt32(); var lastError = reader.ReadUInt32(); Finish(reader);
            if (!_cngOriginalUnloading || _operation != NativePluginDomainOperation.UnloadNvse.ToString() || plugin.Image != image)
                throw new InvalidDataException("Mutex cleanup scope has no exact real original FreeLibrary caller.");
            if (stage == 3)
            {
                if (result != 0 || lastError != 0) throw new InvalidDataException("Mutex cleanup entry fabricates a Windows unload result.");
                _mutexLedger.EnterDetach(parent, image);
            }
            else _mutexLedger.ReturnDetach(parent, image, result, lastError);
            return Payload(writer => writer.Write(1U));
        }
        if (stage == 5)
        {
            var sequence = reader.ReadUInt64(); var api = (NativePluginMutexApi)reader.ReadUInt32();
            var handle = new NativePluginMutexHandle(reader.ReadUInt64(), reader.ReadUInt32());
            var incomingError = reader.ReadUInt32();
            var result = reader.ReadUInt32(); var lastError = reader.ReadUInt32(); Finish(reader);
            _mutexSource.RequireApi(api); _mutexLedger.Deferred(parent, sequence, thread, api, handle, incomingError, result, lastError);
            return Payload(writer => writer.Write(1U));
        }
        throw new NotSupportedException("Mutex callback stage has no real source/SDK lifetime owner.");
    }
    private static byte[] ReadMutexBytes(BinaryReader reader, uint maximum)
    {
        var count = reader.ReadUInt32();
        if (count > maximum || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Mutex declaration exceeds the actual callback/source extent.");
        var bytes = reader.ReadBytes(checked((int)count));
        if (bytes.Length != count) throw new EndOfStreamException("Mutex declaration is truncated."); return bytes;
    }
    private void RequirePrivateMutexesRetired()
    { _mutexLedger?.RequireRetired(); _process.RequireMutexVerificationRetired(); }
    private void ClearPrivateMutexesAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Mutex source/capability cleanup requires actual original child closure.");
        if (_mutexLedger is not null)
        {
            _retiredMutexReceipts.Clear(); _retiredMutexReceipts.AddRange(_mutexLedger.Receipts);
            _retiredMutexDetachReceipts.Clear(); _retiredMutexDetachReceipts.AddRange(_mutexLedger.DetachReceipts);
        }
        _process.CloseMutexVerificationAfterChildExit();
        _mutexSource?.Dispose(); _mutexSource = null; _mutexLedger = null;
    }
    internal void RequirePrivateMutexSaveOwned()
    {
        VerifyOwner();
        if (NvseMutexReceipts.Count != 0 || NvseMutexDetachReceipts.Count != 0 || NvseMutexPending is not null)
            throw new NotSupportedException("Original native mutex handles, process objects and calling-thread acquisitions lack a genuine current/cold reconstruction owner.");
    }
}
