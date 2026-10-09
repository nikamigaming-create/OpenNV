using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Complete ordered source cells, independent of the declaration name index.
// A bare variable addresses the first matching slot; later entries remain owned
// and retain their actual initial/mutated payload through a cold snapshot.
internal sealed partial class FalloutScriptEffectLocalStorage
{
    private readonly FalloutPluginRecord _script;
    private readonly IReadOnlyDictionary<string, FalloutScriptLocalDeclaration> _declarations;
    private readonly List<(uint Index, byte Flags, ulong Payload)> _entries = [];
    private readonly Dictionary<uint, int> _first = [];
    internal FalloutFormKey Script { get; }

    internal FalloutScriptEffectLocalStorage(FalloutPluginRecord script,
        IReadOnlyList<FalloutScriptEffectLocalCell>? restore = null)
    {
        Script = script.FormKey; _script = script;
        _declarations = FalloutScriptLocals.ReadDeclarations(script);
        if (_declarations.Values.Any(value => value.Kind is not (FalloutScriptLocalKind.Number or FalloutScriptLocalKind.Form)))
            throw new NotSupportedException("Active-effect string/array local retention requires its value lifetime owner.");
        foreach (var field in script.ReadSubrecords().Where(field => field.Signature == "SLSD"))
        {
            if (field.Data.Length != 24) throw new InvalidDataException("Active-effect SLSD extent is invalid.");
            var index = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
            var flags = field.Data.Span[16];
            var payload = BinaryPrimitives.ReadUInt64LittleEndian(field.Data.Span[8..16]);
            if (index == 0 || flags > 1)
                throw new InvalidDataException("Active-effect local slot/storage flag is invalid.");
            _first.TryAdd(index, _entries.Count);
            _entries.Add((index, flags, payload));
        }
        if (!_first.Keys.Order().SequenceEqual(_declarations.Values.Select(value => value.Index).Distinct().Order()))
            throw new InvalidDataException("Active-effect local cells differ from their declared slots.");
        if (restore is null) return;
        if (restore.Count != _entries.Count || restore.Any(cell => cell is null))
            throw new InvalidDataException("Saved active-effect local entry count differs from the original declaration.");
        for (var ordinal = 0; ordinal < restore.Count; ordinal++)
        {
            var saved = restore[ordinal];
            if (saved.Ordinal != ordinal || saved.Index != _entries[ordinal].Index)
                throw new InvalidDataException("Saved active-effect local order/slot differs from the original declaration.");
        }
        // Preserve all raw bits. The actually reached numeric/form operand
        // validates its own view; inactive duplicate tails are not interpreted.
        for (var ordinal = 0; ordinal < restore.Count; ordinal++)
        {
            var entry = _entries[ordinal];
            _entries[ordinal] = (entry.Index, entry.Flags, restore[ordinal].Payload);
        }
    }

    internal bool Contains(string name) => !name.Contains('.') && _declarations.ContainsKey(name);
    internal FalloutScriptLocalKind Kind(string name) => Declaration(name).Kind;
    internal FalloutScriptValue Read(string name)
    {
        var declaration = Declaration(name);
        var entry = _entries[_first[declaration.Index]];
        if (declaration.Kind == FalloutScriptLocalKind.Form)
            return FalloutScriptValue.Form(unchecked((uint)entry.Payload));
        var number = BitConverter.UInt64BitsToDouble(entry.Payload);
        ValidateNumber(entry.Flags, number);
        return number;
    }
    internal void Write(string name, FalloutScriptValue value)
    {
        var declaration = Declaration(name);
        if (value.Kind is not (FalloutScriptValueKind.Number or FalloutScriptValueKind.Form))
            throw new InvalidDataException("Active-effect scalar local received a non-scalar value.");
        var ordinal = _first[declaration.Index];
        var entry = _entries[ordinal];
        ulong payload;
        if (declaration.Kind == FalloutScriptLocalKind.Form)
            payload = checked((ulong)FalloutScriptValue.Form(value.Number).Number);
        else
        {
            ValidateNumber(entry.Flags, value.Number);
            payload = BitConverter.DoubleToUInt64Bits(value.Number);
        }
        _entries[ordinal] = (entry.Index, entry.Flags, payload);
    }
    internal IReadOnlyList<FalloutScriptEffectLocalCell> Capture() => _entries.Select((entry, ordinal) =>
        new FalloutScriptEffectLocalCell(entry.Index, entry.Payload, ordinal)).ToArray();

    private FalloutScriptLocalDeclaration Declaration(string name) => Contains(name) ? _declarations[name] :
        throw new NotSupportedException($"Active-effect operand {name} has no local cell.");
    private static void ValidateNumber(byte flags, double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Reached active-effect numeric payload is non-finite.");
        if (flags == 1 && (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue))
            throw new NotSupportedException("Active-effect integer local coercion is unbound for this value.");
    }
}
