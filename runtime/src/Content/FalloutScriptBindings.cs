using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Names are only spellings of compiled references and declared variable slots.
// A matching EDID elsewhere in the load order does not grant script access.
internal sealed class FalloutScriptBindings
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPluginRecord _owner;
    private readonly Dictionary<string, FalloutPluginRecord> _forms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FalloutFormKey, IReadOnlyDictionary<string, uint>> _variables = [];
    private readonly Dictionary<string, (FalloutFormKey Owner, uint Index)> _slots = new(StringComparer.OrdinalIgnoreCase);
    internal bool HasPlayerReference { get; }
    private readonly FalloutFormKey? _playerReference;

    internal FalloutScriptBindings(FalloutPluginStack records, FalloutPluginRecord quest,
        FalloutPluginRecord source, IEnumerable<FalloutPluginSubrecord> fields)
    {
        _records = records;
        _owner = quest;
        foreach (var field in fields.Where(field => field.Signature == "SCRO"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Script reference extent is invalid.");
            var form = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            if (records.RuntimeFormId(form) == 0x14) { HasPlayerReference = true; _playerReference = form; continue; }
            var record = records.GetEffective(form);
            var ids = record.ReadSubrecords().Where(value => value.Signature == "EDID").ToArray();
            if (ids.Length == 0) continue;
            if (ids.Length != 1) throw new InvalidDataException("Script reference has ambiguous editor identity.");
            var id = FalloutDialogueTopic.Text(ids[0].Data.Span);
            if (_forms.TryGetValue(id, out var previous) && previous.FormKey != form)
                throw new InvalidDataException("Compiled script references have ambiguous names.");
            _forms[id] = record;
        }
    }

    internal FalloutPluginRecord? TryForm(string name) => _forms.GetValueOrDefault(name);
    internal FalloutPluginRecord Form(string name) => TryForm(name) ??
        throw new NotSupportedException($"Script reference {name} has no compiled binding.");

    internal FalloutFormKey Reference(string name)
    {
        if (name.Equals("player", StringComparison.OrdinalIgnoreCase))
            return _playerReference ?? throw new NotSupportedException("Player reference has no compiled binding.");
        var record = Form(name);
        return record.Signature is "REFR" or "ACHR" or "ACRE" ? record.FormKey :
            throw new NotSupportedException($"Script target {name} is not an admitted placed reference.");
    }

    internal (FalloutFormKey Owner, uint Index) Variable(string name)
    {
        if (_slots.TryGetValue(name, out var slot)) return slot;
        var split = name.Split('.');
        var owner = split.Length == 1 ? _owner : split.Length == 2 ? Form(split[0]) :
            throw new NotSupportedException("Script variable path is unbound.");
        if (owner.Signature is not ("QUST" or "REFR" or "ACHR" or "ACRE"))
            throw new NotSupportedException("Script variables require a quest or placed reference instance.");
        var script = FalloutScriptLocals.AttachedScript(_records, owner) ??
            throw new NotSupportedException($"Script variable owner {owner.FormKey} has no attached script.");
        if (!_variables.TryGetValue(script.FormKey, out var variables))
        {
            variables = FalloutScriptLocals.Read(script);
            _variables.Add(script.FormKey, variables);
        }
        if (!variables.TryGetValue(split[^1], out var value))
            throw new NotSupportedException($"Script operand {name} has no variable owner.");
        slot = (owner.FormKey, value);
        _slots.Add(name, slot);
        return slot;
    }
}
