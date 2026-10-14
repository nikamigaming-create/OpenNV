namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal enum NativePluginModuleReadOperation : uint { OpenW = 1, Close = 2, ProfileStringA = 3, GameModuleNameA = 4 }
internal sealed record NativePluginModuleReadReceipt(ulong Ordinal, ulong Generation, ulong Parent,
    ulong Module, ulong ResourceModule, int ProcessId, uint NativeThread, string ModuleSha256, string StackSha256, string Construction,
    NativePluginModuleReadOperation Operation, uint[] Arguments, string DeclaredPath, ulong Route, uint Handle,
    uint OutputAddress, uint Capacity, uint IncomingError, byte[] Before, byte[]? After,
    uint? Result, uint? LastError, bool Sdk, bool Returned, bool ReleasedAfterChildExit);

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ModuleReadCallback = 33;
    private const uint ModuleReadBufferLimit = MaximumPayload / 4;
    private const uint GenericRead = 0x80000000, FileReadData = 1, FileReadEa = 8, FileReadAttributes = 0x80;
    private const uint ReadControl = 0x20000, Synchronize = 0x100000, FileAttributeNormal = 0x80;
    private const uint FileFlagWriteThrough = 0x80000000, FileFlagNoBuffering = 0x20000000;
    private const uint FileFlagRandomAccess = 0x10000000, FileFlagSequentialScan = 0x08000000;
    private const uint OpenExisting = 3, ErrorInsufficientBuffer = 122;
    private readonly Dictionary<ulong, NativePluginModuleReadIoSource> _moduleReadSources = [];
    private readonly List<NativePluginModuleReadReceipt> _moduleReadReceipts = [];
    private NativePluginModuleReadReceipt? _moduleReadBorrow;
    private ulong _moduleReadOrdinal;
    internal IReadOnlyList<NativePluginModuleReadReceipt> NvseModuleReadIoReceipts => _moduleReadReceipts.AsReadOnly();
    internal NativePluginModuleReadReceipt? NvseLastModuleReadIoReceipt { get; private set; }

    private byte[] DispatchModuleReadIo(ulong parent, ulong module, BinaryReader reader)
    {
        var operation = (NativePluginModuleReadOperation)reader.ReadUInt32(); var stage = reader.ReadUInt32();
        var ordinal = reader.ReadUInt64(); var route = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var result = reader.ReadUInt32(); var last = reader.ReadUInt32();
        var output = reader.ReadUInt32(); var capacity = reader.ReadUInt32(); var incoming = reader.ReadUInt32();
        var count = reader.ReadUInt32();
        if (count > 8) throw new InvalidDataException("Module read I/O changed its bounded original Win32 arguments.");
        var arguments = new uint[count]; for (var at = 0; at < arguments.Length; ++at) arguments[at] = reader.ReadUInt32();
        var path = ReadText(reader); var before = ReadModuleReadBuffer(reader); var after = ReadModuleReadBuffer(reader); Finish(reader);
        var io = _privateIo ?? throw new NotSupportedException("Module read I/O lost the actual shared route/HANDLE owner.");
        var plugin = RetainedNvseModule(module);
        if (!_moduleReadSources.TryGetValue(module, out var source) ||
            !_nvseModuleDeclarations.TryGetValue(module, out var declaration))
            throw new NotSupportedException("Module read I/O has no returned original pre-entry source capability.");
        source.Check(declaration, io.Selection.StackSha256);
        if (!Enum.IsDefined(operation) || ordinal == 0 || plugin.Generation != Generation ||
            _primaryNvseModule is not { } resource || resource.Phase is NativeNvsePhase.Retired or NativeNvsePhase.Faulted ||
            resource.Generation != Generation)
            throw new InvalidDataException("Module read I/O lost its current executing/resource module lifetime.");
        var name = operation switch
        {
            NativePluginModuleReadOperation.OpenW => "CreateFileW",
            NativePluginModuleReadOperation.Close => "CloseHandle",
            NativePluginModuleReadOperation.ProfileStringA => "GetPrivateProfileStringA",
            NativePluginModuleReadOperation.GameModuleNameA => "GetModuleFileNameA",
            _ => throw new InvalidDataException("Module read I/O has no exact callable declaration.")
        };
        if (!declaration.Imports.Any(item => item.Name == name && source.Owns(item.Library, name)))
            throw new InvalidDataException("Module read I/O caller escaped its actual imported Windows export.");
        if (stage == 1)
        {
            if (_moduleReadBorrow is not null || ordinal != checked(_moduleReadOrdinal + 1) || result != 0 || last != 0 || after.Length != 0)
                throw new InvalidDataException("Module read I/O repeated an entered source call.");
            _moduleReadOrdinal = ordinal;
            _moduleReadBorrow = new(ordinal, Generation, parent, module, resource.Module, ProcessId, NativeThread, source.Sha256, source.StackSha256,
                source.Construction, operation, arguments, path, route, handle, output, capacity, incoming, before, null,
                null, null, operation != NativePluginModuleReadOperation.GameModuleNameA, false, false);
            NvseLastModuleReadIoReceipt = _moduleReadBorrow;
            // Retain the entered arguments before rejecting a write, foreign
            // handle or unsupported buffer arm. Refusal cannot rewrite access.
            RequireModuleReadArguments(operation, arguments, route, handle, output, capacity, before);
            string physical = string.Empty; uint error = 0;
            if (operation is NativePluginModuleReadOperation.OpenW or NativePluginModuleReadOperation.ProfileStringA)
            {
                var selected = io.ResolveModuleRead(parent, source, operation == NativePluginModuleReadOperation.OpenW ? 1U : 8U, path);
                route = selected.Id; physical = selected.PhysicalPath!; error = selected.Error;
                _moduleReadBorrow = _moduleReadBorrow with { Route = route };
            }
            else if (operation == NativePluginModuleReadOperation.Close)
            {
                if (!_ioFiles.TryGetValue(route, out var file) || file.Handle != handle || file.Writable)
                    throw new NotSupportedException("Module close has no exact retained read-only shared HANDLE provenance.");
            }
            else
            {
                var host = _nvseHostSource ?? throw new NotSupportedException("Module zero has no actual selected game executable source owner.");
                host.Check(Generation);
                if (!host.StackIdentity.Equals(source.StackSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Module zero filename changed its selected source stack.");
                physical = host.RuntimePath;
            }
            NvseLastModuleReadIoReceipt = _moduleReadBorrow;
            return Payload(writer => { writer.Write(route); writer.Write(error); WriteText(writer, physical); });
        }
        if (stage != 2 || _moduleReadBorrow is not { } entered || entered.Ordinal != ordinal || entered.Parent != parent ||
            entered.Module != module || entered.ResourceModule != resource.Module || entered.Operation != operation ||
            entered.Route != route || entered.Handle != handle || entered.OutputAddress != output || entered.Capacity != capacity ||
            entered.IncomingError != incoming || entered.DeclaredPath != path || !entered.Arguments.SequenceEqual(arguments) ||
            !entered.Before.AsSpan().SequenceEqual(before) || after.Length != before.Length)
            throw new InvalidDataException("Module read result lost its actual caller/arguments/output/route prefix.");
        _moduleReadBorrow = entered with { After = after, Result = result, LastError = last };
        NvseLastModuleReadIoReceipt = _moduleReadBorrow;
        switch (operation)
        {
            case NativePluginModuleReadOperation.OpenW:
                if (result is 0 or uint.MaxValue)
                {
                    if (last == 0) throw new NotSupportedException("Failed original open with zero error needs its independent returned-result route owner.");
                    io.Complete(parent, route, 1, last);
                }
                else
                {
                    io.ValidateResult(route, 1, writable: false);
                    if (_ioFiles.Values.Any(file => file.Handle == result) || !_ioFiles.TryAdd(route, (result, false)))
                        throw new InvalidDataException("Module open repeats a real live shared HANDLE/route.");
                    io.Complete(parent, route, 1, 0);
                }
                break;
            case NativePluginModuleReadOperation.Close:
                if (!_ioFiles.TryGetValue(route, out var current) || current.Handle != handle || current.Writable)
                    throw new InvalidDataException("Module close lost its current original read-only HANDLE.");
                if (result == 0 && last == 0) throw new NotSupportedException("Failed original close with zero error has no returned-result lifetime owner.");
                io.RecordHandle(parent, route, 2, 0, result != 0 ? 0 : last);
                if (result != 0) _ioFiles.Remove(route);
                break;
            case NativePluginModuleReadOperation.ProfileStringA:
                io.ProfileResult(parent, route, NativePluginProfileKind.StringA, capacity, result, last, mutating: false);
                break;
            case NativePluginModuleReadOperation.GameModuleNameA:
                if (result == 0 || result > capacity || result == capacity && last != ErrorInsufficientBuffer ||
                    result < capacity && last != incoming || after.Length == 0 || after[Math.Min(result, capacity - 1)] != 0)
                    throw new InvalidDataException("Game module filename changed its bounded Windows length/error/termination contract.");
                break;
        }
        var returned = _moduleReadBorrow with { Returned = true };
        _moduleReadReceipts.Add(returned); NvseLastModuleReadIoReceipt = returned; _moduleReadBorrow = null;
        return Payload(writer => writer.Write(1U));
    }
    private static byte[] ReadModuleReadBuffer(BinaryReader reader)
    {
        var length = reader.ReadUInt32();
        if (length > ModuleReadBufferLimit || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Module read buffer exceeds its actual correlated payload.");
        var value = reader.ReadBytes(checked((int)length));
        if (value.Length != length) throw new EndOfStreamException("Module read buffer lost original caller bytes.");
        return value;
    }
    private static void RequireModuleReadArguments(NativePluginModuleReadOperation operation, uint[] arguments,
        ulong route, uint handle, uint output, uint capacity, byte[] before)
    {
        if (operation == NativePluginModuleReadOperation.OpenW)
        {
            const uint readAccess = GenericRead | FileReadData | FileReadEa | FileReadAttributes | ReadControl | Synchronize;
            const uint readFlags = FileAttributeNormal | FileFlagWriteThrough | FileFlagNoBuffering | FileFlagRandomAccess | FileFlagSequentialScan;
            if (arguments.Length != 6 || route != 0 || handle != 0 || output != 0 || capacity != 0 || before.Length != 0)
                throw new InvalidDataException("Original CreateFileW lost its six independent requested words.");
            if ((arguments[0] & ~readAccess) != 0 || arguments[2] != 0 || arguments[3] != OpenExisting ||
                (arguments[4] & ~readFlags) != 0 || arguments[5] != 0)
                throw new NotSupportedException("Module opening has no source owner for requested write/create/security/template/async/other flags.");
        }
        else if (operation == NativePluginModuleReadOperation.Close)
        {
            if (arguments.Length != 0 || route == 0 || handle is 0 or uint.MaxValue || output != 0 || capacity != 0 || before.Length != 0)
                throw new NotSupportedException("Module close requires its real current shared read-only HANDLE.");
        }
        else if (operation == NativePluginModuleReadOperation.ProfileStringA)
        {
            if (arguments.Length != 3 || route != 0 || handle != 0 || before.Length != capacity || capacity != 0 && output == 0)
                throw new InvalidDataException("Original profile read changed its nullable section/key/default or actual caller buffer.");
        }
        else if (arguments.Length != 1 || arguments[0] != 0 || route != 0 || handle != 0 ||
            capacity == 0 || output == 0 || before.Length != capacity)
            throw new NotSupportedException("Game module filename owns only module zero with a real bounded nonempty caller buffer.");
    }
    private void RequireModuleReadIoRetired()
    {
        if (_moduleReadBorrow is null) return;
        if (!ChildExited) throw new InvalidDataException("Module read I/O still borrows its actual original caller/output/resource lifetime.");
        var failed = _moduleReadBorrow with { ReleasedAfterChildExit = true };
        _moduleReadReceipts.Add(failed); NvseLastModuleReadIoReceipt = failed; _moduleReadBorrow = null;
    }
    private void RequireModuleReadIoSaveOwned()
    {
        if (_moduleReadOrdinal != 0)
            throw new NotSupportedException("Original module read caller buffers/native globals have no complete cold owner.");
    }
}
