namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ValueGetString = 0x200, ValueSetString = 0x201, ValueCreateString = 0x202,
        ValueRegisterString = 0x203, ValueAssignString = 0x204, ValueCreateArray = 0x210,
        ValueCreateStringMap = 0x211, ValueCreateMap = 0x212, ValueAssignArray = 0x213,
        ValueSetElement = 0x214, ValueAppendElement = 0x215, ValueArraySize = 0x216,
        ValueLookupArray = 0x217, ValueGetElement = 0x218, ValueSnapshotBegin = 0x219,
        ValueArrayPacked = 0x21a, ValueArrayKind = 0x21b, ValueArrayHasKey = 0x21c,
        ValueSnapshotRead = 0x21d, ValueSnapshotEnd = 0x21e, ValueConstructorWrite = 0x221, ValueConstructorFinish = 0x222,
        ValueAssignNumber = 0x205;
    private NativeNvseValueAuthority? _nvseValues;
    private readonly Dictionary<ulong, NativeNvseValueCall> _nvseValueCalls = [];
    private readonly List<ulong> _nvseActiveValueCalls = [];
    private readonly Dictionary<ulong, (ulong Caller, uint Array, IReadOnlyList<NativeNvseArrayEntry> Entries)> _nvseValueSnapshots = [];
    private readonly Dictionary<ulong, (ulong Caller, int Kind, uint Count, string ScriptOwner, uint Script, List<NativeNvseArrayEntry> Entries)> _nvseValueConstructors = [];
    private readonly List<NativeNvseValueCallbackReceipt> _nvseValueReceipts = [];
    private ulong _nextNvseValueSnapshot;
    internal IReadOnlyList<NativeNvseValueCallbackReceipt> NvseValueCallbacks => _nvseValueReceipts.AsReadOnly();

    internal void AttachNvseValues(NativeNvsePlugin plugin, NativeNvseValueAuthority authority)
    {
        VerifyNvse(plugin); RequireNvseEmptyCall(); ArgumentNullException.ThrowIfNull(authority);
        if (_nvseValues is not null || plugin.Phase is not (NativeNvsePhase.Mapped or NativeNvsePhase.QueriedTrue))
            throw new InvalidOperationException("Native value authority must attach once before original Query/Load.");
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseValuesAttach, Payload(writer => writer.Write(plugin.Module)));
            if (reader.ReadUInt32() != 24 || reader.ReadUInt32() != 52 || reader.ReadUInt32() != 16 || reader.ReadUInt32() != 8)
                throw new InvalidDataException("Native StringVar/ArrayVar/Element declaration extents drifted.");
            Finish(reader); _nvseValues = authority;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }
    private void BeginNvseValueCall(NativeNvseExpressionCaller caller)
    {
        if (_nvseValues is null)
        {
            if (caller.ResultTarget is not null) throw new NotSupportedException("Typed native result lacks its authoritative string/array store.");
            return;
        }
        var lifetime = new NativeNvseValueCall(_nvseValues.BeginExecution());
        try { _nvseValueCalls.Add(caller.Id, lifetime); _nvseActiveValueCalls.Add(caller.Id); }
        catch { lifetime.Dispose(); throw; }
    }
    private void EndNvseValueCall(NativeNvseExpressionCaller caller)
    {
        if (!_nvseValueCalls.TryGetValue(caller.Id, out var lifetime)) return;
        if (_nvseActiveValueCalls.Count == 0 || _nvseActiveValueCalls[^1] != caller.Id)
            throw Fatal(new InvalidDataException("Native value caller retirement is not nested in owner order."));
        _nvseActiveValueCalls.RemoveAt(_nvseActiveValueCalls.Count - 1);
        _nvseValueCalls.Remove(caller.Id);
        var pending = _nvseValueSnapshots.Values.Any(snapshot => snapshot.Caller == caller.Id) || _nvseValueConstructors.Values.Any(lease => lease.Caller == caller.Id) ||
            _nvseArrayObjectLeases.Values.Any(lease => lease.Caller == caller.Id);
        try { lifetime.Retire(_nvseValues!); }
        catch (Exception error) { throw Fatal(error); }
        if (pending) throw Fatal(new InvalidDataException("Native command returned with a live array constructor/enumeration/object lease."));
        if (_nvseActiveValueCalls.Count == 0)
        {
            try { SynchronizeNvseArrayObjects(); } catch (Exception error) { throw Fatal(error); }
        }
    }
    private void RetainNvseArray(ulong caller, uint id)
    {
        if (id == 0) return;
        var authority = _nvseValues ?? throw new NotSupportedException("Native array reference lacks the authoritative shared store.");
        if (!authority.ContainsArray(id)) throw new InvalidDataException("Native array identity is absent from the authoritative shared store.");
        if (caller != 0)
        {
            if (!_nvseValueCalls.TryGetValue(caller, out var lifetime)) throw new InvalidDataException("Native array reference lacks its call lifetime.");
            lifetime.Retain(authority, id);
        }
    }
    private void RetainNvseArgumentArrays(ulong caller, NativeNvseExpressionValue value)
    {
        if (value.Type == NativeNvseTokenType.Array) RetainNvseArray(caller, checked((uint)value.Number));
        else if (value.Type == NativeNvseTokenType.Pair)
        { RetainNvseArgumentArrays(caller, value.Left!); RetainNvseArgumentArrays(caller, value.Right!); }
    }
    private void ClearNvseValueCapabilities()
    {
        _nvseValueSnapshots.Clear(); _nvseValueConstructors.Clear();
        if (_nvseValues is { } authority)
            foreach (var id in _nvseActiveValueCalls.AsEnumerable().Reverse())
                if (_nvseValueCalls.Remove(id, out var lifetime)) lifetime.Retire(authority);
        _nvseActiveValueCalls.Clear(); _nvseValueCalls.Clear(); ClearNvseArrayObjectCapabilities(); _nvseValues = null;
    }
    internal NativeNvseValueStatistics NvseValueStatistics(NativeNvsePlugin plugin)
    {
        VerifyNvse(plugin);
        if (_nvseValues is null) throw new InvalidOperationException("Native value statistics have no attached owner.");
        try
        {
            using var reader = Exchange(NativePluginDomainOperation.NvseValuesStatistics, Payload(writer => writer.Write(plugin.Module)));
            var row = new NativeNvseValueStatistics(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32()); Finish(reader);
            if (row.Callbacks != _nvseValueReceipts.Count) throw new InvalidDataException("Native value callback ledger differs from correlated C# receipts.");
            if (row.HeapCreated != _nvseHeapLifetimes.Count || row.HeapDestroyed != _nvseHeapLifetimes.Values.Count(lifetime => lifetime.RetirementCallback is not null) ||
                row.HeapLive != row.HeapCreated - row.HeapDestroyed) throw new InvalidDataException("Native heap lifetime counts differ from correlated C# birth/retirement receipts.");
            return row;
        }
        catch (NativePluginDomainRefusal) { throw; }
        catch (Exception error) { throw Fatal(error); }
    }

    private static NativeNvseElementValue ReadNvseElement(BinaryReader reader)
    {
        var type = (NativeNvseElementType)reader.ReadUInt32();
        return type switch
        {
            NativeNvseElementType.Number => NativeNvseElementValue.Numeric(reader.ReadDouble()),
            NativeNvseElementType.Form or NativeNvseElementType.Array => NativeNvseElementValue.Reference(type, reader.ReadUInt32()),
            NativeNvseElementType.String => NativeNvseElementValue.String(ReadNvseValueText(reader)),
            _ => throw new NotSupportedException("Native Element category has no typed state owner."),
        };
    }
    private static byte[] ReadNvseValueText(BinaryReader reader)
    {
        var count = reader.ReadUInt32();
        if (count > 16384 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Native string payload extent is invalid.");
        var bytes = reader.ReadBytes((int)count);
        if (bytes.Contains((byte)0)) throw new InvalidDataException("Native string payload contains a null.");
        return bytes;
    }
    private static void WriteNvseElement(BinaryWriter writer, NativeNvseElementValue value)
    {
        writer.Write((uint)value.Type);
        switch (value.Type)
        {
            case NativeNvseElementType.Number: writer.Write(value.Number); break;
            case NativeNvseElementType.Form: case NativeNvseElementType.Array: writer.Write(value.Identity); break;
            case NativeNvseElementType.String: writer.Write(checked((uint)value.Text.Length)); writer.Write(value.Text.AsSpan()); break;
            default: throw new InvalidDataException("Native output Element is invalid.");
        }
    }
    private NativeNvseExpressionCaller RequireNvseValueResult(ulong callerId, uint destination, NativeNvseCommandReturn kind)
    {
        if (!_nvseExpressionCallers.TryGetValue(callerId, out var caller) || destination == 0 || destination != caller.NativeResult ||
            caller.ResultTarget is null || caller.ResultTarget.Kind != kind || string.IsNullOrWhiteSpace(caller.ResultTarget.SourceOwner) ||
            caller.Command.ReturnType != kind && caller.Command.ReturnType != NativeNvseCommandReturn.Ambiguous &&
                !(kind == NativeNvseCommandReturn.String && caller.Command.ReturnType == NativeNvseCommandReturn.Default))
            throw new NotSupportedException("Native result target lacks its exact caller, declared return type and authoritative assignment.");
        return caller;
    }
    private byte[] DispatchNvseValueHost(Frame frame, ulong parent)
    {
        using var reader = Reader(frame.Payload);
        var thread = reader.ReadUInt32(); var module = reader.ReadUInt64(); var handle = reader.ReadUInt32(); var callerId = reader.ReadUInt64();
        var plugin = _nvsePlugin ?? throw new InvalidDataException("Native value callback has no retained original module.");
        var authority = _nvseValues ?? throw new NotSupportedException("StringVar/ArrayVar callback has no authoritative shared value store.");
        if (thread != NativeThread || module != plugin.Module || handle != plugin.Handle || plugin.Generation != Generation ||
            callerId != (_nvseActiveValueCalls.Count == 0 ? 0 : _nvseActiveValueCalls[^1]))
            throw new InvalidDataException("Native value callback has a foreign module/thread/generation/caller.");
        uint identity = 0;
        byte[] reply;
        switch (frame.Operation)
        {
            case ValueGetString:
                {
                    identity = reader.ReadUInt32(); Finish(reader); var bytes = authority.GetString(identity);
                    reply = Payload(writer => { writer.Write(bytes is null ? 0U : 1U); if (bytes is not null) { writer.Write(checked((uint)bytes.Length)); writer.Write(bytes); } }); break;
                }
            case ValueSetString:
                identity = reader.ReadUInt32(); var text = ReadNvseValueText(reader); Finish(reader); authority.SetString(identity, text);
                reply = Payload(writer => writer.Write(1U)); break;
            case ValueCreateString:
                {
                    var script = reader.ReadUInt32(); var bytes = ReadNvseValueText(reader); Finish(reader);
                    identity = script == 0 ? 0 : authority.CreateString(bytes, authority.ScriptOwner(script));
                    reply = Payload(writer => writer.Write(identity)); break;
                }
            case ValueRegisterString:
                if (reader.ReadUInt32() == 0) throw new InvalidDataException("StringVar.Register table is null.");
                Finish(reader); reply = Payload(writer => writer.Write(1U)); break;
            case ValueAssignString:
                {
                    var pointers = Enumerable.Range(0, 8).Select(_ => reader.ReadUInt32()).ToArray(); var cursor = reader.ReadUInt32();
                    var bytes = ReadNvseValueText(reader); Finish(reader);
                    var caller = RequireNvseValueResult(callerId, pointers[6], NativeNvseCommandReturn.String);
                    if (pointers[0] != caller.NativeParameters || pointers[1] != caller.ScriptData.Address || pointers[2] != 0 || pointers[3] != 0 || pointers[4] != caller.NativeScript || pointers[5] != caller.NativeEventList ||
                        pointers[7] != caller.NativeOffset || cursor < caller.Arguments.StartOffset || cursor > caller.Arguments.MaximumEndOffset)
                        throw new InvalidDataException("StringVar.Assign differs from the genuine eight-pointer command/cursor frame.");
                    caller.PublishedValue = NativeNvseElementValue.String(bytes); identity = caller.ResultTarget!.Publish(caller.PublishedValue);
                    caller.PublishedIdentity = identity; reply = Payload(writer => writer.Write(identity)); break;
                }
            case ValueCreateArray:
            case ValueCreateStringMap:
            case ValueCreateMap:
                {
                    var script = reader.ReadUInt32(); var count = reader.ReadUInt32(); Finish(reader);
                    if (count > 1_000_000) throw new NotSupportedException("Native constructor exceeds the shared array element budget.");
                    var scriptOwner = authority.ScriptOwner(script); // No null/proxy Script admission.
                    if (string.IsNullOrWhiteSpace(scriptOwner)) throw new InvalidDataException("Native constructor has no source script/mod ownership.");
                    if (callerId == 0) throw new NotSupportedException("Native array construction requires its actual execution lifetime.");
                    var kind = frame.Operation == ValueCreateArray ? 0 : frame.Operation == ValueCreateMap ? 1 : 2;
                    var lease = checked(++_nextNvseValueSnapshot);
                    _nvseValueConstructors.Add(lease, (callerId, kind, count, scriptOwner, script, new List<NativeNvseArrayEntry>((int)count)));
                    reply = Payload(writer => writer.Write(lease)); break;
                }
            case ValueConstructorWrite:
                {
                    var lease = reader.ReadUInt64(); var offset = reader.ReadUInt32(); var count = reader.ReadUInt32();
                    if (!_nvseValueConstructors.TryGetValue(lease, out var constructor) || constructor.Caller != callerId ||
                        offset != constructor.Entries.Count || count == 0 || count > constructor.Count - offset ||
                        count > (reader.BaseStream.Length - reader.BaseStream.Position) / 16)
                        throw new InvalidDataException("Native constructor chunk has no complete ordered lease extent.");
                    var entries = Enumerable.Range(0, (int)count).Select(_ => new NativeNvseArrayEntry(ReadNvseElement(reader), ReadNvseElement(reader))).ToArray();
                    Finish(reader); constructor.Entries.AddRange(entries); reply = Payload(writer => writer.Write(count)); break;
                }
            case ValueConstructorFinish:
                {
                    var lease = reader.ReadUInt64(); Finish(reader);
                    if (!_nvseValueConstructors.Remove(lease, out var constructor) || constructor.Caller != callerId || constructor.Entries.Count != constructor.Count)
                        throw new InvalidDataException("Native constructor completion has no full source key/value extent.");
                    identity = authority.CreateArrayForScript(constructor.Kind, constructor.Entries, constructor.Script); RetainNvseArray(callerId, identity);
                    reply = Payload(writer => writer.Write(identity)); break;
                }
            case ValueAssignNumber:
                {
                    var destination = reader.ReadUInt32(); var number = reader.ReadDouble(); Finish(reader);
                    if (!double.IsFinite(number) || !_nvseExpressionCallers.TryGetValue(callerId, out var caller) || destination != caller.NativeResult ||
                        caller.Command.ReturnType is not (NativeNvseCommandReturn.Default or NativeNvseCommandReturn.Ambiguous) || caller.ResultTarget is not null)
                        throw new NotSupportedException("Numeric Element result lacks its actual numeric command frame.");
                    caller.PublishedValue = NativeNvseElementValue.Numeric(number); reply = Payload(writer => writer.Write(1U)); break;
                }
            case ValueAssignArray:
                {
                    identity = reader.ReadUInt32(); var destination = reader.ReadUInt32(); Finish(reader);
                    var caller = RequireNvseValueResult(callerId, destination, NativeNvseCommandReturn.Array);
                    var exists = authority.ContainsArray(identity);
                    if (exists)
                    {
                        RetainNvseArray(callerId, identity); caller.PublishedValue = NativeNvseElementValue.Reference(NativeNvseElementType.Array, identity);
                        var published = caller.ResultTarget!.Publish(caller.PublishedValue);
                        if (published != identity) throw new InvalidDataException("Array result assignment changed its shared identity.");
                        caller.PublishedIdentity = identity;
                    }
                    reply = Payload(writer => writer.Write(exists ? 1U : 0U)); break;
                }
            case ValueSetElement:
                identity = reader.ReadUInt32(); var key = ReadNvseElement(reader); var value = ReadNvseElement(reader); Finish(reader);
                if (authority.ContainsArray(identity)) { RetainNvseArray(callerId, identity); authority.ArraySet(identity, key, value); }
                reply = Payload(writer => writer.Write(1U)); break;
            case ValueAppendElement:
                identity = reader.ReadUInt32(); var append = ReadNvseElement(reader); Finish(reader);
                if (authority.ContainsArray(identity)) { RetainNvseArray(callerId, identity); authority.ArrayAppend(identity, append); }
                reply = Payload(writer => writer.Write(1U)); break;
            case ValueArraySize:
            case ValueLookupArray:
            case ValueArrayPacked:
            case ValueArrayKind:
                {
                    identity = reader.ReadUInt32(); Finish(reader); var exists = authority.ContainsArray(identity);
                    if (exists) RetainNvseArray(callerId, identity);
                    var scalar = frame.Operation switch
                    {
                        ValueArraySize => checked((uint)authority.ArraySize(identity)),
                        ValueLookupArray => exists ? identity : 0,
                        ValueArrayPacked => authority.ArrayKind(identity) == 0 ? 1U : 0U,
                        _ => unchecked((uint)authority.ArrayKind(identity)),
                    };
                    reply = Payload(writer => writer.Write(scalar)); break;
                }
            case ValueGetElement:
            case ValueArrayHasKey:
                {
                    identity = reader.ReadUInt32(); var elementKey = ReadNvseElement(reader); Finish(reader);
                    var found = authority.ContainsArray(identity) && authority.ArrayHasKey(identity, elementKey);
                    if (found) RetainNvseArray(callerId, identity);
                    var element = found && frame.Operation == ValueGetElement ? authority.ArrayGet(identity, elementKey) : null;
                    if (found && frame.Operation == ValueGetElement && element is null) throw new InvalidDataException("Native array key lookup lost its actual value.");
                    if (element?.Type == NativeNvseElementType.Array) RetainNvseArray(callerId, element.Identity);
                    reply = Payload(writer => { writer.Write(found ? 1U : 0U); if (element is not null) WriteNvseElement(writer, element); }); break;
                }
            case ValueSnapshotBegin:
                {
                    identity = reader.ReadUInt32(); Finish(reader); var exists = authority.ContainsArray(identity);
                    IReadOnlyList<NativeNvseArrayEntry> entries = exists ? authority.ArrayEntries(identity) : [];
                    ulong lease = 0;
                    if (exists)
                    {
                        RetainNvseArray(callerId, identity);
                        foreach (var entry in entries) if (entry.Value.Type == NativeNvseElementType.Array) RetainNvseArray(callerId, entry.Value.Identity);
                        lease = checked(++_nextNvseValueSnapshot); _nvseValueSnapshots.Add(lease, (callerId, identity, entries));
                    }
                    reply = Payload(writer => { writer.Write(lease); writer.Write(checked((uint)entries.Count)); }); break;
                }
            case ValueSnapshotRead:
                {
                    var lease = reader.ReadUInt64(); var start = reader.ReadUInt32(); Finish(reader);
                    if (!_nvseValueSnapshots.TryGetValue(lease, out var snapshot) || snapshot.Caller != callerId || start >= snapshot.Entries.Count)
                        throw new InvalidDataException("Native enumeration has an absent/foreign/retired source snapshot extent.");
                    identity = snapshot.Array;
                    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
                    writer.Write(0U); uint count = 0;
                    for (var index = (int)start; index < snapshot.Entries.Count; ++index)
                    {
                        var entry = snapshot.Entries[index]; var bytes = Payload(pair => { WriteNvseElement(pair, entry.Key); WriteNvseElement(pair, entry.Value); });
                        if (stream.Length + bytes.Length > MaximumPayload - 128) break;
                        writer.Write(bytes); ++count;
                    }
                    if (count == 0) throw new NotSupportedException("One native array entry exceeds the correlated reply budget.");
                    stream.Position = 0; writer.Write(count); reply = stream.ToArray(); break;
                }
            case ValueSnapshotEnd:
                {
                    var lease = reader.ReadUInt64(); Finish(reader);
                    if (!_nvseValueSnapshots.Remove(lease, out var snapshot) || snapshot.Caller != callerId)
                        throw new InvalidDataException("Native enumeration lease was foreign, absent or already retired.");
                    identity = snapshot.Array; reply = Payload(writer => writer.Write(1U)); break;
                }
            case ValueArrayObjectBegin:
            case ValueArrayObjectRead:
            case ValueArrayObjectPublish:
            case ValueArrayObjectRetire:
                reply = DispatchNvseArrayObject(frame, parent, callerId, reader); break;
            case ValueHeapEvent: reply = AcceptNvseHeapEvent(frame, callerId, reader); break;
            default: throw new InvalidDataException($"Unknown native value callback {frame.Operation}.");
        }
        _nvseValueReceipts.Add(new(Generation, frame.Id, parent, callerId, frame.Operation, identity));
        return reply;
    }
}
