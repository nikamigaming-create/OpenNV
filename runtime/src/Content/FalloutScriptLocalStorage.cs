using System.Collections;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptLocalCellSnapshot(int Ordinal, uint Index, byte StorageFlags,
    string Name, ulong Bits);

internal sealed record FalloutScriptLocalStorageSnapshot(FalloutFormKey Script, string ScriptSha256,
    IReadOnlyList<FalloutScriptLocalCellSnapshot> Entries, long Revision)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Script.OwnerPlugin) || Script.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            ScriptSha256 is not { Length: 64 } || !ScriptSha256.All(Uri.IsHexDigit) || Entries is null || Revision < 0)
            throw new InvalidDataException("Saved local storage has no complete source identity.");
        for (var ordinal = 0; ordinal < Entries.Count; ++ordinal)
        {
            var entry = Entries[ordinal];
            if (entry is null || entry.Ordinal != ordinal || entry.Index == 0 || entry.StorageFlags > 1 ||
                string.IsNullOrWhiteSpace(entry.Name))
                throw new InvalidDataException("Saved local storage lost its ordered source entry.");
        }
    }
}

internal readonly record struct FalloutScriptLocalCellChange(int Entry, uint Index, ulong Before, ulong After);

// One event-list owner retains every source entry. The dictionary interface is
// the engine's first-ID view of those same cells, rather than another value store.
internal sealed class FalloutScriptLocalStorage : IReadOnlyDictionary<uint, double>
{
    private readonly FalloutPluginRecord _script;
    private readonly FalloutScriptLocalMetadata[] _entries;
    private readonly Dictionary<uint, int> _first = [];
    private readonly IReadOnlyDictionary<uint, FalloutScriptLocalKind> _kinds;
    private readonly ulong[] _payloads;
    private long _revision;

    internal IReadOnlyList<FalloutScriptLocalMetadata> Entries => _entries;
    internal string ScriptSha256 { get; }
    internal bool HasMutations => _revision != 0;
    public int Count => _first.Count;
    public IEnumerable<uint> Keys => _first.Keys;
    public IEnumerable<double> Values => _first.Keys.Select(Read);
    public double this[uint index] => Read(index);

    internal FalloutScriptLocalStorage(FalloutPluginRecord script, IReadOnlyList<ulong> initialPayloads)
    {
        _script = script;
        _entries = FalloutScriptLocals.ReadMetadata(script).ToArray();
        if (initialPayloads.Count != _entries.Length)
            throw new InvalidDataException("Local construction lost its original ordered initial payloads.");
        _payloads = initialPayloads.ToArray();
        _kinds = FalloutScriptLocals.ReadStorageKinds(script);
        for (var ordinal = 0; ordinal < _entries.Length; ++ordinal) _first.TryAdd(_entries[ordinal].Index, ordinal);
        if (!_first.Keys.Order().SequenceEqual(_kinds.Keys.Order()))
            throw new InvalidDataException("Ordered local entries differ from their admitted scalar views.");
        ScriptSha256 = Convert.ToHexString(SHA256.HashData(script.ReadData())).ToLowerInvariant();
    }

    // The original loader copies the complete SLSD prefix into VarInfo; the
    // event-list constructor then copies that runtime payload for every entry.
    internal static IReadOnlyList<ulong> ReadInitialPayloads(FalloutPluginRecord script)
    {
        _ = FalloutScriptLocals.ReadMetadata(script);
        return script.ReadSubrecords().Where(field => field.Signature == "SLSD")
            .Select(field => BinaryPrimitives.ReadUInt64LittleEndian(field.Data.Span[8..16])).ToArray();
    }

    public bool ContainsKey(uint index) => _first.ContainsKey(index);
    public bool TryGetValue(uint index, out double value)
    {
        if (!_first.TryGetValue(index, out var ordinal)) { value = 0; return false; }
        value = Decode(index, _payloads[ordinal]);
        return true;
    }

    internal double Read(uint index) => TryGetValue(index, out var value) ? value :
        throw new NotSupportedException($"Script {_script.FormKey} has no declared local {index}.");

    internal bool Write(uint index, double value)
    {
        if (!_first.TryGetValue(index, out var ordinal))
            throw new NotSupportedException($"Script {_script.FormKey} has no declared local {index}.");
        if (!double.IsFinite(value)) throw new InvalidDataException("Local scalar write is non-finite.");
        var bits = _kinds[index] == FalloutScriptLocalKind.Form
            ? checked((ulong)FalloutScriptValue.Form(value).Number) : BitConverter.DoubleToUInt64Bits(value);
        if (_payloads[ordinal] == bits) return false;
        _payloads[ordinal] = bits;
        ++_revision;
        return true;
    }

    private double Decode(uint index, ulong bits)
    {
        if (_kinds[index] == FalloutScriptLocalKind.Form) return (uint)bits;
        var value = BitConverter.UInt64BitsToDouble(bits);
        if (!double.IsFinite(value)) throw new InvalidDataException("Reached local numeric view is non-finite.");
        return value;
    }

    internal ulong ReadEntry(int ordinal)
    {
        if ((uint)ordinal >= (uint)_entries.Length)
            throw new InvalidDataException("Native local entry is outside the source event list.");
        return _payloads[ordinal];
    }

    internal Action PrepareEntries(IReadOnlyList<FalloutScriptLocalCellChange> changes, Action requireCurrent,
        Action? changed = null)
    {
        requireCurrent();
        var revision = _revision;
        var prepared = changes.ToArray();
        if (prepared.Select(change => change.Entry).Distinct().Count() != prepared.Length)
            throw new InvalidDataException("Local publication contains duplicate entry ordinals.");
        void RequirePrefix()
        {
            requireCurrent();
            if (_revision != revision) throw new InvalidOperationException("Local owner changed during native publication.");
            foreach (var change in prepared)
                if ((uint)change.Entry >= (uint)_entries.Length || _entries[change.Entry].Index != change.Index ||
                    _payloads[change.Entry] != change.Before)
                    throw new InvalidOperationException("Local publication differs from its retained ordered prefix.");
        }
        RequirePrefix();
        var committed = false;
        return () =>
        {
            if (committed) throw new InvalidOperationException("Local publication has already committed.");
            RequirePrefix();
            foreach (var change in prepared) _payloads[change.Entry] = change.After;
            committed = true;
            if (prepared.Any(change => change.Before != change.After)) { ++_revision; changed?.Invoke(); }
        };
    }

    internal FalloutScriptLocalStorageSnapshot Capture() => new(_script.FormKey, ScriptSha256,
        _entries.Select(entry => new FalloutScriptLocalCellSnapshot(entry.Ordinal, entry.Index,
            entry.StorageFlags, entry.Name, _payloads[entry.Ordinal])).ToArray(), _revision);

    internal void Restore(FalloutScriptLocalStorageSnapshot snapshot, IReadOnlyDictionary<uint, double> firstValues)
    {
        if (HasMutations) throw new InvalidOperationException("Ordered local restoration requires a fresh owner.");
        snapshot.Validate();
        if (snapshot.Script != _script.FormKey || !string.Equals(snapshot.ScriptSha256, ScriptSha256, StringComparison.OrdinalIgnoreCase) ||
            snapshot.Entries.Count != _entries.Length || !firstValues.Keys.Order().SequenceEqual(_first.Keys.Order()))
            throw new InvalidDataException("Saved ordered locals differ from the winning source owner.");
        for (var ordinal = 0; ordinal < _entries.Length; ++ordinal)
        {
            var source = _entries[ordinal]; var entry = snapshot.Entries[ordinal];
            if (entry.Ordinal != source.Ordinal || entry.Index != source.Index || entry.StorageFlags != source.StorageFlags ||
                !string.Equals(entry.Name, source.Name, StringComparison.Ordinal))
                throw new InvalidDataException("Saved local declaration order or identity changed.");
            if (_first[source.Index] == ordinal &&
                BitConverter.DoubleToUInt64Bits(Decode(source.Index, entry.Bits)) != BitConverter.DoubleToUInt64Bits(firstValues[source.Index]))
                throw new InvalidDataException("Saved first-ID view differs from its complete ordered payload.");
        }
        foreach (var entry in snapshot.Entries) _payloads[entry.Ordinal] = entry.Bits;
        _revision = snapshot.Revision;
    }

    public IEnumerator<KeyValuePair<uint, double>> GetEnumerator() => _first.Keys.Select(index =>
        new KeyValuePair<uint, double>(index, Read(index))).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
