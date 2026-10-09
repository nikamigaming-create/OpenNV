using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private NativeNvsePlugin? _nvsePlugin;
    private FileStream? _nvseModuleSource;
    private NativeNvseHostSource? _nvseHostSource;
    private uint _nvseCallbackCount, _nvseCallbackBudget;
    private bool _nvseImageAttempted;
    private uint NativeModuleCount => _module is null && _nvsePlugin is null ? 0U : 1U;
    internal NativeNvsePlugin? NvsePlugin => _nvsePlugin;

    internal NativeNvsePlugin LoadNvseImage(NativeNvseHostSource host, string originalDll, string expectedSha256,
        uint maximumInterfaceCallbacks = 16384, uint sourcePluginHandle = 1)
    {
        VerifyOwner(); ArgumentNullException.ThrowIfNull(host);
        if (_callDepth != 0 || NativeModuleCount != 0 || _nvseHostSource is not null || _nvseImageAttempted)
            throw new InvalidOperationException("NVSE image admission requires an empty module/call/source owner.");
        if (maximumInterfaceCallbacks is 0 or > 1048576) throw new ArgumentOutOfRangeException(nameof(maximumInterfaceCallbacks));
        if (sourcePluginHandle is 0 or uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(sourcePluginHandle));
        var path = Path.GetFullPath(originalDll); var source = NativeNvseHostSource.Lease(path, expectedSha256, dll: true);
        var started = false;
        try
        {
            var io = _privateIo ?? throw new NotSupportedException("Original DLL loader entry requires its pre-entry restricted private I/O owner.");
            io.CheckModule(path, expectedSha256, host.StackIdentity);
            if (!StringComparer.OrdinalIgnoreCase.Equals(NativePluginPrivateIo.Canonical(host.RuntimeDirectory), NativePluginPrivateIo.Canonical(io.Selection.RuntimeDirectory)))
                throw new InvalidDataException("Native I/O and NVSE disagree about the exact owned runtime directory.");
            NativePluginIoImports.Admit(source, io);
            NativeNvseHostSource.RequirePluginExports(source);
            host.Claim(Generation); _nvseHostSource = host; _nvseCallbackBudget = maximumInterfaceCallbacks;
            started = true; _nvseImageAttempted = true;
            using var reader = Exchange(NativePluginDomainOperation.LoadNvse, Payload(writer =>
            {
                WriteText(writer, path); WriteText(writer, host.RuntimeDirectory);
                writer.Write(host.NvseVersion); writer.Write(host.RuntimeVersion); writer.Write(host.NoGore); writer.Write(maximumInterfaceCallbacks); writer.Write(sourcePluginHandle);
            }));
            var module = reader.ReadUInt64(); var image = reader.ReadUInt32(); var handle = reader.ReadUInt32();
            var nativeInterface = reader.ReadUInt32(); var query = reader.ReadUInt32(); var load = reader.ReadUInt32(); Finish(reader);
            if (module == 0 || module >= uint.MaxValue || handle != sourcePluginHandle || image == 0 || nativeInterface == 0 || query == 0 || load == 0)
                throw new InvalidDataException("Original NVSE image has no actual module/interface/Query/Load capability.");
            var plugin = new NativeNvsePlugin(Generation, module, handle, image, nativeInterface, query, load, path, expectedSha256.ToUpperInvariant());
            VerifyOwner(); _nvsePlugin = plugin; _nvseModuleSource = source; return plugin;
        }
        catch (NativePluginDomainRefusal)
        {
            source.Dispose(); _nvseHostSource?.Retire(Generation); _nvseHostSource = null; throw;
        }
        catch (Exception error)
        {
            source.Dispose();
            if (!started) throw;
            throw Fatal(error);
        }
    }
    internal NativeNvseInitializationReceipt QueryNvse(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin);
        if (plugin.Phase != NativeNvsePhase.Mapped) throw new InvalidOperationException("Original NVSE Query has already been invoked.");
        return InvokeNvseInitialization(plugin, load: false);
    }
    internal NativeNvseInitializationReceipt InitializeNvse(NativeNvsePlugin plugin, uint? sourceLoadHandle = null)
    {
        VerifyNvse(plugin);
        var info = plugin.QueryReceipt?.Info;
        if (plugin.Phase != NativeNvsePhase.QueriedTrue || info is null || info.InfoVersion != 1 || info.Name.IsNull || info.Name.Bytes.IsEmpty)
            throw new InvalidOperationException("Original NVSE Load requires its actual successful Query and typed PluginInfo.");
        if (sourceLoadHandle is 0 or uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(sourceLoadHandle));
        return InvokeNvseInitialization(plugin, load: true, sourceLoadHandle);
    }
    private NativeNvseInitializationReceipt InvokeNvseInitialization(NativeNvsePlugin plugin, bool load, uint? sourceLoadHandle = null)
    {
        RequireNvseEmptyCall();
        if (load) plugin.Handle = sourceLoadHandle ?? plugin.Handle;
        plugin.Phase = load ? NativeNvsePhase.Loading : NativeNvsePhase.Querying;
        ++_callDepth;
        try
        {
            using var reader = Exchange(load ? NativePluginDomainOperation.NvseLoad : NativePluginDomainOperation.NvseQuery,
                Payload(writer => { writer.Write(plugin.Module); if (load) writer.Write(plugin.Handle); }));
            var raw = reader.ReadUInt32(); var stack = reader.ReadInt32(); var preserved = reader.ReadUInt32(); var exception = reader.ReadUInt32();
            var returned = reader.ReadUInt32(); var infoVersion = reader.ReadUInt32(); var version = reader.ReadUInt32(); var name = ReadNvseText(reader);
            var counts = ReadNvseCounts(reader); Finish(reader); CheckNvseBoundary(stack, preserved, exception);
            if (returned > 1 || returned != (raw & 255)) throw new InvalidDataException("Original NVSE bool/AL return receipt drifted.");
            CheckNvseCounts(plugin, counts);
            var receipt = new NativeNvseInitializationReceipt(load ? "NVSEPlugin_Load" : "NVSEPlugin_Query", returned == 1,
                raw, stack, preserved, exception, new(infoVersion, version, name), counts);
            if (load) { plugin.LoadReceipt = receipt; plugin.Phase = returned == 1 ? NativeNvsePhase.LoadedTrue : NativeNvsePhase.LoadedFalse; }
            else { plugin.QueryReceipt = receipt; plugin.Phase = returned == 1 ? NativeNvsePhase.QueriedTrue : NativeNvsePhase.QueriedFalse; }
            VerifyOwner(); return receipt;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { plugin.Phase = NativeNvsePhase.Faulted; throw Fatal(error); }
        finally { --_callDepth; }
    }
    // Caller must publish the genuine authoritative event and its source-defined
    // data. This does not manufacture PostLoad/DeferredInit or claim hooks work.
    internal uint DeliverNvseMessage(NativeNvsePlugin plugin, uint type, ReadOnlySpan<byte> payload)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall();
        if (!(plugin.Phase == NativeNvsePhase.LoadedTrue || plugin.Phase == NativeNvsePhase.QueriedTrue && type == 23 && payload.IsEmpty))
            throw new InvalidOperationException("Original message lacks its actual queried/loaded source phase.");
        if (payload.Length > MaximumPayload - 256) throw new ArgumentOutOfRangeException(nameof(payload));
        var data = payload.ToArray(); ++_callDepth;
        try
        {
            var previous = plugin.Registry.MessageCompletions.Count;
            using var reader = Exchange(NativePluginDomainOperation.NvseMessage, Payload(writer =>
            { writer.Write(plugin.Module); writer.Write(type); writer.Write(checked((uint)data.Length)); writer.Write(data); }));
            var delivered = reader.ReadUInt32(); var counts = ReadNvseCounts(reader); Finish(reader); CheckNvseCounts(plugin, counts);
            if (delivered != plugin.Registry.MessageCompletions.Count - previous)
                throw new InvalidDataException("Original NVSE event lacks its actual callback delivery cardinality.");
            VerifyOwner(); return delivered;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { plugin.Phase = NativeNvsePhase.Faulted; throw Fatal(error); }
        finally { --_callDepth; }
    }
    internal void DeliverNvseNewGame(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseLoaded(plugin); RequireNvseEmptyCall(); ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseSerialization, Payload(writer =>
            { writer.Write(plugin.Module); writer.Write((uint)NativeNvseSerializationEvent.NewGame); }));
            _ = reader.ReadUInt32(); var stack = reader.ReadInt32(); var registers = reader.ReadUInt32(); var exception = reader.ReadUInt32();
            var counts = ReadNvseCounts(reader); Finish(reader); CheckNvseBoundary(stack, registers, exception); CheckNvseCounts(plugin, counts);
            VerifyOwner();
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { plugin.Phase = NativeNvsePhase.Faulted; throw Fatal(error); }
        finally { --_callDepth; }
    }
    internal NativeNvseRetirementReceipt UnloadNvse(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); RequireNvseSourceFilesIdle(); RequireNvseBinaryIdle(); ++_callDepth;
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.UnloadNvse, Payload(writer => writer.Write(plugin.Module)));
            var counts = ReadNvseCounts(reader); var image = reader.ReadUInt32(); var interfaces = reader.ReadUInt32(); Finish(reader);
            CheckNvseCounts(plugin, counts);
            if (image != 0 || interfaces != 0) throw new InvalidDataException("Original NVSE image/interface mapping remains after retirement.");
            RequirePrivateIoRetired();
            RequireNvseHeapRetired();
            var receipt = new NativeNvseRetirementReceipt(counts, false, false); plugin.Retirement = receipt;
            plugin.Registry.Retire(); plugin.Phase = NativeNvsePhase.Retired;
            _nvseModuleSource!.Dispose(); _nvseModuleSource = null; _nvseHostSource!.Retire(Generation); _nvseHostSource = null;
            ClearNvseData(); ClearNvseCommandTable(); ClearNvseLocalCapabilities(); ClearNvseExpressionCapabilities(); ClearNvseValueCapabilities(); ClearNvseSourceObjects(); _nvsePlugin = null; VerifyOwner(); return receipt;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { plugin.Phase = NativeNvsePhase.Faulted; throw Fatal(error); }
        finally { --_callDepth; }
    }
    private uint DispatchNvseHost(Frame frame, ulong parent)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("NVSE callback has no retained original image.");
        if (thread != NativeThread || plugin.Generation != Generation || plugin.Module != module || plugin.Handle != handle)
            throw new InvalidDataException("NVSE callback belongs to a foreign module/handle/thread/generation.");
        var registry = plugin.Registry;
        uint value;
        switch ((NativeNvseHostCall)frame.Operation)
        {
            case NativeNvseHostCall.QueryInterface: value = registry.QueryInterface(frame.Id, reader.ReadUInt32(), _nvseValues is not null, _nvseScriptInterface, _nvseCommandTable is not null, _nvseData is null ? 0 : _nvseDataVersion); break;
            case NativeNvseHostCall.SetOpcode: value = registry.SetOpcode(frame.Id, reader.ReadUInt32()); break;
            case NativeNvseHostCall.RegisterCommand:
                {
                    var source = reader.ReadUInt32(); var rawOpcode = reader.ReadUInt32(); var returnType = reader.ReadUInt32(); var required = reader.ReadUInt32();
                    var needsParent = reader.ReadUInt16(); var count = reader.ReadUInt16(); var flags = reader.ReadUInt32();
                    var execute = reader.ReadUInt32(); var parse = reader.ReadUInt32(); var evaluate = reader.ReadUInt32();
                    var name = ReadNvseText(reader); var alias = ReadNvseText(reader); var help = ReadNvseText(reader); var parametersAddress = reader.ReadUInt32();
                    if (returnType > byte.MaxValue || source == 0 || name.IsNull || count != 0 && parametersAddress == 0)
                        throw new InvalidDataException("Original NVSE command declaration is malformed.");
                    var parameters = ImmutableArray.CreateBuilder<NativeNvseParameter>(count);
                    for (var index = 0; index < count; ++index) parameters.Add(new(ReadNvseText(reader), reader.ReadUInt32(), reader.ReadUInt32()));
                    value = registry.Register(frame.Id, source, rawOpcode, (byte)returnType, required, needsParent, flags,
                        parametersAddress, name, alias, help, parameters.MoveToImmutable(), execute, parse, evaluate); break;
                }
            case NativeNvseHostCall.RegisterListener:
                { var registeredHandle = reader.ReadUInt32(); var sender = ReadNvseText(reader); value = registry.RegisterListener(frame.Id, registeredHandle, sender, reader.ReadUInt32()); break; }
            case NativeNvseHostCall.SerializationCallback:
                { var registeredHandle = reader.ReadUInt32(); var @event = (NativeNvseSerializationEvent)reader.ReadUInt32(); value = registry.RegisterSerialization(frame.Id, registeredHandle, @event, reader.ReadUInt32()); break; }
            case NativeNvseHostCall.Unsupported: registry.Missing(frame.Id, ReadText(reader)); value = 0; break;
            case NativeNvseHostCall.DispatchMessage:
                {
                    var sequence = reader.ReadUInt64(); var sender = reader.ReadUInt32(); var type = reader.ReadUInt32(); var pointer = reader.ReadUInt32(); var length = reader.ReadUInt32(); var receiver = ReadNvseText(reader);
                    if (length > MaximumPayload || length > reader.BaseStream.Length - reader.BaseStream.Position || length != 0 && pointer == 0)
                        throw new InvalidDataException("Original NVSE message has no complete native data extent.");
                    value = registry.Dispatch(frame.Id, parent, sequence, sender, type, pointer, ImmutableArray.CreateRange(reader.ReadBytes(checked((int)length))), receiver); break;
                }
            case NativeNvseHostCall.DeliveredMessage:
                value = registry.CompleteMessage(frame.Id, parent, reader.ReadUInt64(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()); break;
            case NativeNvseHostCall.DeliveredSerialization:
                value = registry.CompleteSerialization(frame.Id, parent, (NativeNvseSerializationEvent)reader.ReadUInt32(), reader.ReadUInt32()); break;
            case NativeNvseHostCall.BeginSerialization:
                value = registry.BeginSerialization(parent, (NativeNvseSerializationEvent)reader.ReadUInt32(), reader.ReadUInt32()); break;
            default: throw new InvalidDataException("Unknown NVSE interface callback operation.");
        }
        Finish(reader); return value;
    }
    private void ClearNvseCapabilities()
    {
        if (!ChildExited) throw new InvalidOperationException("Native source/capability owners require verified exact child closure.");
        ClearNvseBinaryAfterChildClosure();
        ClearNvseData();
        ClearNvseCommandTable();
        ClearNvseLocalCapabilities(); ClearNvseValueCapabilities(); _nvseHeapDeclaration = null;
        ClearNvseExpressionCapabilities(); ClearNvseSourceObjects();
        if (_nvsePlugin is not null)
        {
            _nvsePlugin.Registry.Retire();
            _nvsePlugin.Phase = NativeNvsePhase.Faulted;
        }
        _nvsePlugin = null; _nvseModuleSource?.Dispose(); _nvseModuleSource = null;
        _nvseHostSource?.Retire(Generation); _nvseHostSource = null;
    }
    private void VerifyNvse(NativeNvsePlugin plugin)
    {
        VerifyOwner();
        if (!ReferenceEquals(_nvsePlugin, plugin) || plugin.Generation != Generation || plugin.Phase == NativeNvsePhase.Retired)
            throw new InvalidOperationException("Original NVSE module belongs to a stale image/process generation.");
        _nvseHostSource!.Check(Generation);
    }
    private void RequireNvseEmptyCall()
    {
        if (_callDepth != 0) throw new InvalidOperationException("An original call/callback still owns the NVSE lifecycle.");
    }
    private static void RequireNvseLoaded(NativeNvsePlugin plugin)
    {
        if (plugin.Phase != NativeNvsePhase.LoadedTrue) throw new InvalidOperationException("The original NVSE Load did not actually return true.");
    }
    private static void CheckNvseBoundary(int stack, uint registers, uint exception)
    {
        if (stack != 0 || registers != 7 || exception != 0) throw new InvalidDataException("Original NVSE ABI boundary is not clean.");
    }
    private static NativeNvseRegistryCounts ReadNvseCounts(BinaryReader reader) =>
        new(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
    private static void CheckNvseCounts(NativeNvsePlugin plugin, NativeNvseRegistryCounts counts)
    {
        if (counts != plugin.Registry.Counts) throw new InvalidDataException("Actual NVSE native/C# registration ownership counts diverged.");
        plugin.Registry.RequireCompletedInvocations();
    }
    private static NativeNvseText ReadNvseText(BinaryReader reader)
    {
        var pointer = reader.ReadUInt32(); var length = reader.ReadUInt32();
        if (length > MaximumPayload || length > reader.BaseStream.Length - reader.BaseStream.Position || pointer == 0 && length != 0)
            throw new InvalidDataException("NVSE declaration string has no exact native extent.");
        var bytes = reader.ReadBytes(checked((int)length));
        if (bytes.Contains((byte)0)) throw new InvalidDataException("NVSE declaration text contains an unexpected interior terminator.");
        return new(pointer, ImmutableArray.CreateRange(bytes));
    }
}
