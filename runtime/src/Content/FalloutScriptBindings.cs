using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Names are only spellings of compiled references and declared variable slots.
// A matching EDID elsewhere in the load order does not grant script access.
internal sealed class FalloutScriptBindings
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPluginRecord _owner;
    private readonly Func<FalloutPluginRecord, FalloutPluginRecord?>? _attachedScript;
    private readonly Dictionary<string, FalloutPluginRecord> _forms = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(FalloutFormKey? Form, uint? LocalIndex)> _indexedReferences = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyDictionary<string, FalloutScriptLocalDeclaration>> _variables = [];
    private readonly Dictionary<FalloutFormKey, IReadOnlyDictionary<uint, FalloutScriptLocalDeclaration>> _slotsByIndex = [];
    private readonly Dictionary<string, (FalloutFormKey Owner, uint Index, FalloutScriptLocalKind Kind)> _slots = new(StringComparer.OrdinalIgnoreCase);
    internal bool HasPlayerReference { get; }
    internal FalloutFormKey Source { get; }
    private readonly FalloutFormKey? _playerReference;

    internal FalloutScriptBindings(FalloutPluginStack records, FalloutPluginRecord quest,
        FalloutPluginRecord source, IEnumerable<FalloutPluginSubrecord> fields,
        Func<FalloutPluginRecord, FalloutPluginRecord?>? attachedScript = null)
    {
        _records = records;
        _owner = quest;
        _attachedScript = attachedScript;
        Source = source.FormKey;
        foreach (var field in fields.Where(field => field.Signature is "SCRO" or "SCRV"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Script reference extent is invalid.");
            var raw = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
            if (field.Signature == "SCRV")
            {
                _indexedReferences.Add((null, raw));
                continue;
            }
            var form = source.Plugin.AdjustFormId(raw);
            _indexedReferences.Add((form, null));
            var index = _indexedReferences.Count;
            if (records.RuntimeFormId(form) == 0x14) { HasPlayerReference = true; _playerReference = form; continue; }
            var record = records.GetEffective(form);
            _forms[$"$scro_{index}"] = record;
            var ids = record.ReadSubrecords().Where(value => value.Signature == "EDID").ToArray();
            if (ids.Length == 0) continue;
            if (ids.Length != 1) throw new InvalidDataException("Script reference has ambiguous editor identity.");
            var id = FalloutDialogueTopic.Text(ids[0].Data.Span);
            if (_forms.TryGetValue(id, out var previous) && previous.FormKey != form)
                throw new InvalidDataException("Compiled script references have ambiguous names.");
            _forms[id] = record;
        }
    }

    internal (FalloutFormKey? Form, uint? LocalIndex) IndexedReference(ushort index)
    {
        if (index == 0 || index > _indexedReferences.Count)
            throw new InvalidDataException($"Compiled script reference index {index} is out of range (1..{_indexedReferences.Count}).");
        return _indexedReferences[index - 1];
    }

    internal FalloutFormKey ReferenceFormKey(ushort index, Func<uint, double> readLocal)
    {
        var entry = IndexedReference(index);
        if (entry.Form is { } form) return form;
        var raw = readLocal(entry.LocalIndex!.Value);
        if (!double.IsFinite(raw) || raw <= 0 || raw > uint.MaxValue || raw != Math.Truncate(raw))
            throw new InvalidDataException($"Compiled script reference variable {entry.LocalIndex} has no valid form identity.");
        return _records.RuntimeFormKey((uint)raw);
    }

    internal string ReferenceToken(ushort index, Func<uint, double> readLocal)
    {
        var entry = IndexedReference(index);
        if (entry.Form is { } form)
            return _records.RuntimeFormId(form) == 0x14 ? "player" : $"$scro_{index}";
        var raw = readLocal(entry.LocalIndex!.Value);
        if (!double.IsFinite(raw) || raw <= 0 || raw > uint.MaxValue || raw != Math.Truncate(raw))
            throw new InvalidDataException($"Compiled script reference variable {entry.LocalIndex} has no valid form identity.");
        return ((uint)raw).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal (FalloutFormKey Owner, uint Index, FalloutScriptLocalKind Kind, bool IsInteger) VariableSlot(
        ushort? referenceIndex, ushort variableIndex, byte storageKind, Func<uint, double> readLocal)
    {
        FalloutPluginRecord owner;
        if (referenceIndex is { } refIdx)
        {
            var formKey = ReferenceFormKey(refIdx, readLocal);
            owner = _records.GetEffective(formKey);
        }
        else
        {
            owner = _owner;
        }
        if (owner.Signature is not ("QUST" or "REFR" or "ACHR" or "ACRE"))
            throw new NotSupportedException("Script variables require a quest or placed reference instance.");
        var script = (_attachedScript is null ? FalloutScriptLocals.AttachedScript(_records, owner) : _attachedScript(owner)) ??
            throw new NotSupportedException($"Script variable owner {owner.FormKey} has no attached script.");
        if (!_slotsByIndex.TryGetValue(script.FormKey, out var slots))
        {
            slots = FalloutScriptLocals.ReadSlots(script);
            _slotsByIndex.Add(script.FormKey, slots);
        }
        if (!slots.TryGetValue(variableIndex, out var declaration))
            throw new NotSupportedException($"Script variable slot {variableIndex} on {owner.FormKey} has no declaration.");
        var isInteger = declaration.IsInteger || storageKind is (byte)'s' or (byte)'l';
        return (owner.FormKey, declaration.Index, declaration.Kind, isInteger);
    }

    internal FalloutPluginRecord? TryForm(string name) => _forms.GetValueOrDefault(name);
    internal FalloutPluginRecord Form(string name) => TryForm(name) ??
        throw new NotSupportedException($"Script reference {name} has no compiled binding.");

    internal FalloutFormKey Reference(string name)
    {
        if (IsPlayer(name))
            return _playerReference ?? throw new NotSupportedException("Player reference has no compiled binding.");
        var record = Form(name);
        return record.Signature is "REFR" or "ACHR" or "ACRE" ? record.FormKey :
            throw new NotSupportedException($"Script target {name} is not an admitted placed reference.");
    }

    internal static bool IsPlayer(string name) => name.Equals("player", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("playerref", StringComparison.OrdinalIgnoreCase);

    internal (FalloutFormKey Owner, uint Index) Variable(string name)
    {
        var slot = Local(name);
        return (slot.Owner, slot.Index);
    }

    internal FalloutScriptLocalKind VariableKind(string name) => Local(name).Kind;

    internal bool HasVariable(string name)
    {
        var parts = name.Split('.');
        var owner = parts.Length == 1 ? _owner : parts.Length == 2 ? TryForm(parts[0]) : null;
        if (owner?.Signature is not ("QUST" or "REFR" or "ACHR" or "ACRE")) return false;
        var script = _attachedScript is null ? FalloutScriptLocals.AttachedScript(_records, owner) : _attachedScript(owner);
        if (script is null) return false;
        if (!_variables.TryGetValue(script.FormKey, out var variables))
            _variables.Add(script.FormKey, variables = FalloutScriptLocals.ReadDeclarations(script));
        return variables.ContainsKey(parts[^1]);
    }

    private (FalloutFormKey Owner, uint Index, FalloutScriptLocalKind Kind) Local(string name)
    {
        if (_slots.TryGetValue(name, out var slot)) return slot;
        var split = name.Split('.');
        var owner = split.Length == 1 ? _owner : split.Length == 2 ? Form(split[0]) :
            throw new NotSupportedException("Script variable path is unbound.");
        if (owner.Signature is not ("QUST" or "REFR" or "ACHR" or "ACRE"))
            throw new NotSupportedException("Script variables require a quest or placed reference instance.");
        var script = (_attachedScript is null ? FalloutScriptLocals.AttachedScript(_records, owner) : _attachedScript(owner)) ??
            throw new NotSupportedException($"Script variable owner {owner.FormKey} has no attached script.");
        if (!_variables.TryGetValue(script.FormKey, out var variables))
        {
            variables = FalloutScriptLocals.ReadDeclarations(script);
            _variables.Add(script.FormKey, variables);
        }
        if (!variables.TryGetValue(split[^1], out var value))
            throw new NotSupportedException($"Script operand {name} has no variable owner.");
        slot = (owner.FormKey, value.Index, value.Kind);
        _slots.Add(name, slot);
        return slot;
    }
}
