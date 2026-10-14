namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed record NativePluginModuleConstructionReceipt(ulong Generation, int Process, uint Thread,
    string Path, string Sha256, string StackSha256, string Family, bool NativeReply,
    bool? LoaderEntered, bool? LoaderReturned, bool? AttachReturned, uint? LastError,
    uint? Ucrt, uint? Vcruntime, uint? NewHandler, int? NewMode, bool ProviderBorrowReleased,
    bool ReleasedAfterChildExit);
internal sealed record NativePluginConstructorProvider(string Path, string Sha256);
internal sealed record NativePluginModuleCppBinding(ulong Generation, int Process, uint Thread,
    string OriginalSha256, uint Provider, string ProviderSha256, string Name, uint Rva, uint Address, bool Executable,
    bool LoaderReturned, bool AttachReturned);

internal sealed partial class NativePluginPrivateIo
{
    private (NativePluginConstructorProvider Provider, FileStream Lease)? _constructorVcruntime;
    internal (NativePluginCrtProviderSelection Ucrt, NativePluginConstructorProvider Vcruntime) ConstructionProviders()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var ucrt = CrtProviders.ToArray() is [var provider] ? provider :
            throw new NotSupportedException("Original memory construction requires its exact existing shared CRT source lease.");
        if (_constructorVcruntime is null)
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "vcruntime140.dll");
            NoReparse(path); var image = NativePluginImageDeclarations.Read(path, dll: true);
            foreach (var name in new[] { "memset", "__CxxFrameHandler3", "_CxxThrowException" })
                if (!image.Exports.Any(row => row.Name == name && row.Rva != 0 && row.Executable && !row.Forwarded))
                    throw new NotSupportedException("Original exception/memory provider lacks its exact public x86 entry: " + name);
            var lease = NativeNvseHostSource.Lease(path, image.Sha256, dll: true);
            _constructorVcruntime = (new(path, image.Sha256), lease);
        }
        return (ucrt, _constructorVcruntime.Value.Provider);
    }
    private void DisposeConstructorProvider()
    { _constructorVcruntime?.Lease.Dispose(); _constructorVcruntime = null; }
}

internal sealed partial class NativePluginExecutionDomain
{
    private NativePluginModuleConstructionSource? _enteredModuleConstruction;
    private NativePluginModuleCppSource? _enteredModuleCpp;
    private readonly List<NativePluginModuleConstructionReceipt> _moduleConstructionReceipts = [];
    private readonly List<NativePluginModuleCppBinding> _moduleCppBindings = [];
    internal IReadOnlyList<NativePluginModuleConstructionReceipt> NvseModuleConstructionReceipts => _moduleConstructionReceipts.AsReadOnly();
    internal NativePluginModuleConstructionReceipt? NvseLastModuleConstructionReceipt => _moduleConstructionReceipts.LastOrDefault();
    internal IReadOnlyList<NativePluginModuleCppBinding> NvseModuleCppBindings => _moduleCppBindings.AsReadOnly();
    private void BeginModuleConstruction(NativePluginModuleConstructionSource source, NativePluginModuleCppSource? cpp)
    {
        if (_enteredModuleConstruction is not null || _callDepth != 0 || _nvsePlugin is null)
            throw new InvalidOperationException("Original construction overlaps a current original call/source attempt.");
        _enteredModuleConstruction = source; _enteredModuleCpp = cpp;
        _moduleConstructionReceipts.Add(new(Generation, ProcessId, NativeThread, source.Path, source.Sha256, source.StackSha256, source.Family,
            false, null, null, null, null, null, null, null, null, false, false));
    }
    private void WriteModuleConstruction(BinaryWriter writer, NativePluginModuleConstructionSource source, NativePluginPrivateIo io)
    {
        writer.Write((uint)source.Kind); writer.Write(source.EntryRva); WriteText(writer, source.Sha256); WriteText(writer, source.StackSha256);
        writer.Write(checked((uint)source.Fields.Count));
        foreach (var field in source.Fields) { writer.Write(field.Rva); writer.Write(field.Bytes); writer.Write(field.InitialWord); WriteText(writer, field.Role); }
        if (source.Kind == NativePluginModuleConstructionKind.DynamicCrt)
        {
            var providers = io.ConstructionProviders();
            WriteText(writer, providers.Ucrt.Path); WriteText(writer, providers.Ucrt.Sha256);
            WriteText(writer, providers.Vcruntime.Path); WriteText(writer, providers.Vcruntime.Sha256);
        }
    }
    private void ReadModuleConstructionReply(BinaryReader reader, uint status)
    {
        var source = _enteredModuleConstruction ?? throw new InvalidDataException("Native construction reply has no entered source attempt.");
        var kind = (NativePluginModuleConstructionKind)reader.ReadUInt32(); var entry = reader.ReadUInt32();
        var thread = reader.ReadUInt32(); var prepared = reader.ReadUInt32(); var loader = reader.ReadUInt32();
        var returned = reader.ReadUInt32(); var error = reader.ReadUInt32();
        var ucrt = reader.ReadUInt32(); var vcruntime = reader.ReadUInt32(); var handler = reader.ReadUInt32();
        var mode = reader.ReadInt32(); var released = reader.ReadUInt32();
        if (kind != source.Kind || entry != source.EntryRva || thread != NativeThread || prepared != 1 || loader != 1 ||
            returned > 1 || (returned == 1) != (status == 0) || status != 0 && error != status || released != 1 || handler != 0 ||
            mode is not (0 or 1) || (kind == NativePluginModuleConstructionKind.DynamicCrt) != (ucrt != 0 && vcruntime != 0) ||
            kind != NativePluginModuleConstructionKind.DynamicCrt && (ucrt != 0 || vcruntime != 0 || mode != 0))
            throw new InvalidDataException("Original construction reply changed its actual source, provider, current predicate or loader result.");
        if (ucrt != 0 && _crtProviders.Values.Any(row => row.Module != ucrt))
            throw new InvalidDataException("Original constructor borrowed another CRT mapping than the real shared stream owner.");
        _moduleConstructionReceipts[^1] = new(Generation, ProcessId, thread, source.Path, source.Sha256, source.StackSha256, source.Family,
            true, true, true, returned == 1 && source.Kind != NativePluginModuleConstructionKind.NoEntry,
            error, ucrt, vcruntime, handler, mode, true, false);
        var cppImage = reader.ReadUInt32(); var cppCount = reader.ReadUInt32();
        var cpp = _enteredModuleCpp;
        if (cppCount != (cpp?.Provider.Exports.Count ?? 0) || (cppImage != 0) != (cpp is not null) ||
            cpp is not null && (!_cppRuntimePublished || _cppRuntimeRetired || _cppRuntimeModule != cppImage))
            throw new InvalidDataException("Additional C++ borrow lost its actual already published shared provider/export extent.");
        if (cpp is not null)
        {
            var declarations = cpp.Provider.Exports.ToDictionary(row => row.Name, StringComparer.Ordinal);
            for (var at = 0U; at < cppCount; ++at)
            {
                var name = ReadText(reader); var rva = reader.ReadUInt32(); var address = reader.ReadUInt32(); var code = reader.ReadUInt32();
                if (!declarations.Remove(name, out var item) || item.Rva != rva || code > 1 || item.Executable != (code == 1) || (ulong)cppImage + rva != address)
                    throw new InvalidDataException("Additional C++ borrow changed its exact source code/data/ABI/address association.");
                _moduleCppBindings.Add(new(Generation, ProcessId, thread, source.Sha256, cppImage, cpp.Provider.Sha256, name, rva, address,
                    code != 0, true, returned == 1 && source.Kind != NativePluginModuleConstructionKind.NoEntry));
            }
        }
    }
    private static void WriteModuleCppSource(BinaryWriter writer, NativePluginModuleCppSource? source)
    {
        writer.Write(source is null ? 0U : checked((uint)source.Provider.Exports.Count));
        if (source is null) return;
        WriteText(writer, source.Provider.Path); WriteText(writer, source.Provider.Sha256);
        foreach (var export in source.Provider.Exports)
        { WriteText(writer, export.Name); writer.Write(export.Rva); writer.Write(export.Executable ? 1U : 0U); }
    }
    private void ClearModuleConstructionAfterChildExit()
    {
        if (!ChildExited) throw new InvalidOperationException("Original construction prefix/source borrow survives its unconfirmed native child.");
        for (var index = 0; index < _moduleConstructionReceipts.Count; ++index)
            if (!_moduleConstructionReceipts[index].ProviderBorrowReleased)
                _moduleConstructionReceipts[index] = _moduleConstructionReceipts[index] with { ReleasedAfterChildExit = true };
        _enteredModuleConstruction = null; _enteredModuleCpp = null;
    }
}
