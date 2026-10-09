using System.Buffers.Binary;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginExecutionDomain
{
    private const uint ValueArrayObjectBegin = 0x240, ValueArrayObjectRead = 0x241,
        ValueArrayObjectPublish = 0x242, ValueArrayObjectRetire = 0x243;
    private readonly Dictionary<ulong, NativeNvseArrayObjectLease> _nvseArrayObjectLeases = [];
    private readonly Dictionary<uint, (ulong Lifetime, uint Address)> _nvseArrayObjects = [];
    private readonly List<NativeNvseArrayObjectReceipt> _nvseArrayObjectReceipts = [];
    internal IReadOnlyList<NativeNvseArrayObjectReceipt> NvseArrayObjectReceipts => _nvseArrayObjectReceipts.AsReadOnly();

    internal NativeNvseSourceObject RequireNvseArrayCreator(NativeNvsePlugin plugin, uint pointer)
    {
        VerifyNvse(plugin);
        var script = _nvseSourceObjects.Values.SingleOrDefault(value => value.Address == pointer) ??
            throw new NotSupportedException("Native array creator is not a retained actual source object.");
        VerifyNvseSourceObject(plugin, script); script.Authority.RequireCurrent();
        if (script.Class != NativeNvseSourceClass.Script || script.Staged || script.Retired ||
            script.Authority is not NativeNvseScriptAuthority authority || authority.Read().FormId != script.FormId)
            throw new NotSupportedException("Native array creator lacks its complete current Script authority.");
        return script;
    }
    private byte[] DispatchNvseArrayObject(Frame frame, ulong parent, ulong caller, BinaryReader reader)
    {
        var authority = _nvseValues ?? throw new NotSupportedException("Internal ArrayVar has no shared store.");
        switch (frame.Operation)
        {
            case ValueArrayObjectBegin:
                {
                    var id = reader.ReadUInt32(); Finish(reader);
                    var snapshot = authority.ArrayObject(id);
                    if (snapshot is null) return Payload(writer => writer.Write(0UL));
                    if (snapshot.Id != id) throw new InvalidDataException("Array object description changed its actual store identity.");
                    NativeNvseArrayObjectLayout.Validate(snapshot); RetainNvseArray(caller, id);
                    foreach (var entry in snapshot.Entries)
                        if (entry.Value.Type == NativeNvseElementType.Array) RetainNvseArray(caller, entry.Value.Identity);
                    var lease = checked(++_nextNvseValueSnapshot); _nvseArrayObjectLeases.Add(lease, new(caller, snapshot));
                    return Payload(writer =>
                    {
                        writer.Write(lease); writer.Write(id); writer.Write(snapshot.Kind); writer.Write(snapshot.OwningMod);
                        writer.Write(checked((uint)snapshot.Entries.Count)); writer.Write(checked((uint)snapshot.ReferringMods.Length));
                        WriteText(writer, snapshot.SourceOwner);
                    });
                }
            case ValueArrayObjectRead:
                {
                    var lease = reader.ReadUInt64(); var part = reader.ReadUInt32(); var offset = reader.ReadUInt32(); Finish(reader);
                    var source = RequireArrayObjectLease(lease, caller); var snapshot = source.Snapshot;
                    if (part == 1)
                    {
                        if (offset != source.ReferencesSent || offset >= snapshot.ReferringMods.Length)
                            throw new InvalidDataException("Array reference chunk omits or repeats a source occurrence.");
                        var count = Math.Min(snapshot.ReferringMods.Length - checked((int)offset), MaximumPayload - 128);
                        source.ReferencesSent = checked(offset + (uint)count);
                        return Payload(writer => { writer.Write(checked((uint)count)); writer.Write(snapshot.ReferringMods.AsSpan(checked((int)offset), count)); });
                    }
                    if (part != 0 || offset != source.EntriesSent || offset >= snapshot.Entries.Count)
                        throw new InvalidDataException("Array entry chunk omits or repeats a source extent.");
                    using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
                    writer.Write(0U); uint sent = 0;
                    for (var index = checked((int)offset); index < snapshot.Entries.Count; ++index)
                    {
                        var entry = snapshot.Entries[index];
                        var bytes = Payload(pair => { WriteNvseElement(pair, entry.Key); WriteNvseElement(pair, entry.Value); });
                        if (stream.Length + bytes.Length > MaximumPayload - 128) break;
                        writer.Write(bytes); ++sent;
                    }
                    if (sent == 0) throw new NotSupportedException("One internal array entry exceeds its callback extent.");
                    source.EntriesSent = checked(offset + sent); stream.Position = 0; writer.Write(sent); return stream.ToArray();
                }
            case ValueArrayObjectPublish:
                {
                    var lease = reader.ReadUInt64(); var lifetime = reader.ReadUInt64(); var address = reader.ReadUInt32();
                    var bytes = reader.ReadBytes(checked((int)NativeNvseArrayObjectLayout.ObjectBytes)); Finish(reader);
                    var source = RequireArrayObjectLease(lease, caller); var snapshot = source.Snapshot;
                    if (lifetime == 0 || address == 0 || (address & 3) != 0 || address > uint.MaxValue - 43 ||
                        source.EntriesSent != snapshot.Entries.Count || source.ReferencesSent != snapshot.ReferringMods.Length ||
                        bytes.Length != 44 || BinaryPrimitives.ReadInt32LittleEndian(bytes) != snapshot.Kind ||
                        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != snapshot.Entries.Count ||
                        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) != snapshot.Id || bytes[20] != snapshot.OwningMod ||
                        bytes[21] != (snapshot.Kind == 2 ? 3 : 1) || bytes[22] != (snapshot.Kind == 0 ? 1 : 0) || bytes[23] != 0 ||
                        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)) != snapshot.ReferringMods.Length ||
                        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)) < Math.Max(2, snapshot.Entries.Count) ||
                        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32)) < Math.Max(8, snapshot.ReferringMods.Length))
                        throw new InvalidDataException("Native ArrayVar publication lacks its complete current source/header extent.");
                    if (snapshot.Entries.Count != 0 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) == 0 ||
                        snapshot.ReferringMods.Length != 0 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)) == 0 ||
                        (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)) == 0) != (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(36)) == 0) ||
                        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)) != 0 && BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(36)) != NativeThread)
                        throw new InvalidDataException("Native ArrayVar source buffers do not match their actual empty/nonempty lifetimes.");
                    if (_nvseArrayObjects.TryGetValue(snapshot.Id, out var previous) && previous != (lifetime, address))
                        throw new InvalidDataException("A living array object changed its native identity without retirement.");
                    _nvseArrayObjects[snapshot.Id] = (lifetime, address); _nvseArrayObjectLeases.Remove(lease);
                    _nvseArrayObjectReceipts.Add(new(Generation, frame.Id, parent, caller, snapshot.Id, lifetime, address,
                        checked((uint)snapshot.Entries.Count), snapshot.SourceOwner));
                    return Payload(writer => { writer.Write(lifetime); writer.Write(address); });
                }
            case ValueArrayObjectRetire:
                {
                    var id = reader.ReadUInt32(); var lifetime = reader.ReadUInt64(); var address = reader.ReadUInt32(); Finish(reader);
                    if (!_nvseArrayObjects.TryGetValue(id, out var value) || value != (lifetime, address) ||
                        _nvseArrayObjectLeases.Values.Any(source => source.Snapshot.Id == id))
                        throw new InvalidDataException("Array object retirement has a foreign, pending or absent actual native lifetime.");
                    _nvseArrayObjects.Remove(id); return Payload(writer => writer.Write(1U));
                }
            default: throw new InvalidDataException("Unknown internal array object callback.");
        }
    }
    private NativeNvseArrayObjectLease RequireArrayObjectLease(ulong lease, ulong caller) =>
        _nvseArrayObjectLeases.TryGetValue(lease, out var value) && value.Caller == caller ? value :
            throw new InvalidDataException("Array object snapshot is absent, retired or belongs to another caller.");
    private void SynchronizeNvseArrayObjects()
    {
        if (_nvsePlugin is not { } plugin || _nvseValues is null || _fault is not null || ChildExited) return;
        VerifyNvse(plugin);
        using var reader = Exchange(NativePluginDomainOperation.NvseArrayObjects,
            Payload(writer => writer.Write(plugin.Module)));
        var live = reader.ReadUInt32(); var pending = reader.ReadUInt32(); Finish(reader);
        if (live != _nvseArrayObjects.Count || pending != 0 || _nvseArrayObjectLeases.Count != 0)
            throw new InvalidDataException("Internal array refresh/collection has no complete actual lifetime ledger.");
    }
    private void ClearNvseArrayObjectCapabilities()
    {
        if (!ChildExited) throw new InvalidOperationException("Array object/source leases survive until the exact native child closes.");
        _nvseArrayObjects.Clear(); _nvseArrayObjectLeases.Clear();
    }
}
