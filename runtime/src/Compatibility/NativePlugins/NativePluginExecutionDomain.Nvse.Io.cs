namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativePluginPrivateIo? _privateIo;
    private uint _ioCallbackCount;
    private readonly Dictionary<ulong, (uint Handle, bool Writable)> _ioFiles = [];
    private void PreparePrivateIo(NativePluginPrivateIo owner)
    {
        owner.Claim(Generation); _privateIo = owner;
        using var reader = Exchange(NativePluginDomainOperation.PrivateIoPrepare, Payload(writer =>
        {
            WriteText(writer, owner.ModuleRoot); WriteText(writer, owner.CurrentDirectory); WriteText(writer, owner.RestrictingSid);
            writer.Write(checked((uint)owner.Selection.DeclaredNonIoImports.Count));
            foreach (var import in owner.Selection.DeclaredNonIoImports.Order(StringComparer.Ordinal)) WriteText(writer, import);
            WriteCrtProviders(writer, owner); WriteCryptoProvider(writer, owner);
        }));
        if (reader.ReadUInt32() != 1) throw new InvalidDataException("Private native I/O has no restricted-token/diagnostic initialization receipt.");
        Finish(reader);
    }
    private byte[] DispatchPrivateIo(Frame frame, ulong parent)
    {
        var owner = _privateIo ?? throw new NotSupportedException("Original native I/O has no selected module-owned private storage.");
        using var reader = Reader(frame.Payload); var thread = reader.ReadUInt32(); var module = reader.ReadUInt64();
        var enteredLoader = IsEnteredOriginalLoaderIo(frame.Operation, module, parent);
        if (thread != NativeThread || !enteredLoader && (_nvsePlugin is null || module != _nvsePlugin.Module || _nvsePlugin.Generation != Generation))
            throw new InvalidDataException("Native I/O callback has a foreign thread/module/generation.");
        if (frame.Operation == MutexCallback)
        {
            if (enteredLoader) throw new NotSupportedException("Original pre-entry mutex construction has no intercepted loader/TLS caller owner.");
            return DispatchPrivateMutex(parent, thread, owner, reader);
        }
        if (frame.Operation == MappingCallback) return DispatchPrivateMapping(parent, owner, reader);
        if (frame.Operation == CryptoCallback)
        {
            if (enteredLoader)
            {
                var category = reader.ReadUInt32();
                if (category != 4) throw new NotSupportedException("Original loader entry only owns the actual separate CNG service callback lane.");
                return DispatchCngSystemService(parent, owner, reader);
            }
            return DispatchPrivateCrypto(parent, owner, reader);
        }
        if (frame.Operation == ProfileResultCallback) return DispatchPrivateProfile(frame, parent, owner, reader);
        if (frame.Operation is >= FindBeginCallback and <= FindBackendCallback) return DispatchPrivateFind(frame, parent, owner, reader);
        if (frame.Operation is >= CrtProvider and <= CrtPathResult) return DispatchPrivateCrt(frame, parent, owner, reader);
        switch (frame.Operation)
        {
            case 1:
                {
                    var api = reader.ReadUInt32(); var action = (NativePluginIoAction)reader.ReadUInt32(); var path = ReadText(reader); Finish(reader);
                    if (!Enum.IsDefined(action)) throw new InvalidDataException("Native I/O action is undeclared.");
                    var route = owner.Resolve(parent, api, action, path);
                    return Payload(writer => { writer.Write(route.Id); writer.Write(route.Error); WriteText(writer, route.PhysicalPath ?? string.Empty); });
                }
            case 2:
                {
                    var id = reader.ReadUInt64(); var api = reader.ReadUInt32(); var error = reader.ReadUInt32();
                    var handle = reader.ReadUInt32(); var writable = reader.ReadUInt32(); var deleted = reader.ReadUInt32(); Finish(reader);
                    if (writable > 1 || deleted > 1 || error != 0 && handle != 0 || handle != 0 && api != 1 || deleted != 0 && api != 6)
                        throw new InvalidDataException("Native file result has an invalid category/lifetime.");
                    owner.ValidateResult(id, api, writable != 0);
                    if (deleted == 1) owner.Deleted(parent, id, api, error); else owner.Complete(parent, id, api, error);
                    if (handle != 0 && (!_ioFiles.TryAdd(id, (handle, writable != 0)) || _ioFiles.Values.Count(file => file.Handle == handle) != 1))
                        throw new InvalidDataException("Native open repeats a live file capability/handle.");
                    return Payload(writer => writer.Write(1U));
                }
            case 3:
                {
                    var id = reader.ReadUInt64(); var api = reader.ReadUInt32(); var handle = reader.ReadUInt32(); var count = reader.ReadUInt32(); var error = reader.ReadUInt32(); Finish(reader);
                    if (!_ioFiles.TryGetValue(id, out var file) || file.Handle != handle || api is < 2 or > 12 || api is 5 or 6 or 7 or 8 or 9 || api == 4 && !file.Writable)
                        throw new InvalidDataException("Native file operation has no exact admitted handle/access lifetime.");
                    if (api == 2 && error == 0) _ioFiles.Remove(id);
                    owner.RecordHandle(parent, id, api, count, error);
                    return Payload(writer => writer.Write(1U));
                }
            case 4:
                {
                    var path = ReadText(reader); Finish(reader); owner.ChangeDirectory(path);
                    return Payload(writer => WriteText(writer, owner.CurrentDirectory));
                }
            default: throw new NotSupportedException("Native I/O callback has no first-party operation owner.");
        }
    }
    private void RequirePrivateIoRetired()
    {
        if (_ioFiles.Count != 0) throw new InvalidDataException("Original module retirement retains native file handles.");
        RequirePrivateMutexesRetired(); RequirePrivateMappingsRetired(); RequirePrivateCryptoRetired();
        RequirePrivateCrtRetired(); _privateIo?.RequireFindRetired();
        _privateIo?.RequireRetired();
    }
    private void ClearPrivateIo()
    {
        if (!ChildExited) throw new InvalidOperationException("Native I/O source/provider cleanup requires verified child closure.");
        RetainPrivateProfileReceipts(); RetainPrivateFindReceipts();
        ClearPrivateMutexesAfterChildExit(); ClearNativeImportProvidersAfterChildExit();
        ClearPrivateMappings(); ClearPrivateCrypto(); ClearPrivateCrt(); _ioFiles.Clear(); _privateIo?.Dispose(); _privateIo = null;
    }
}
