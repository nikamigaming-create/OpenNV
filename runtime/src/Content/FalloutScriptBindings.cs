using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Names are only spellings of compiled references and declared variable slots.
// A matching EDID elsewhere in the load order does not grant script access.
internal sealed class FalloutScriptBindings
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutPluginRecord _quest;
    private readonly Dictionary<string, FalloutPluginRecord> _forms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<FalloutFormKey, Dictionary<string, uint>> _variables = [];
    internal bool HasPlayerReference { get; }

    internal FalloutScriptBindings(FalloutPluginStack records, FalloutPluginRecord quest,
        FalloutPluginRecord source, IEnumerable<FalloutPluginSubrecord> fields)
    {
        _records = records;
        _quest = quest;
        foreach (var field in fields.Where(field => field.Signature == "SCRO"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Script reference extent is invalid.");
            var form = source.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            if (records.RuntimeFormId(form) == 0x14) { HasPlayerReference = true; continue; }
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

    internal (FalloutFormKey Quest, uint Index) Variable(string name)
    {
        var split = name.Split('.');
        var quest = split.Length == 1 ? _quest : split.Length == 2 ? Form(split[0]) :
            throw new NotSupportedException("Script variable path is unbound.");
        if (quest.Signature != "QUST") throw new NotSupportedException("Reference script variables need an instance owner.");
        if (!_variables.TryGetValue(quest.FormKey, out var variables))
        {
            variables = new(StringComparer.OrdinalIgnoreCase);
            var script = _records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
            if (script.Signature != "SCPT") throw new InvalidDataException("Quest variable owner is not SCPT.");
            uint? index = null;
            var indices = new HashSet<uint>();
            foreach (var field in script.ReadSubrecords())
            {
                if (field.Signature == "SLSD")
                {
                    if (field.Data.Length != 24 || index is not null)
                        throw new InvalidDataException("Script variable declaration extent or name is invalid.");
                    index = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                    if (!indices.Add(index.Value)) throw new InvalidDataException("Duplicate script variable slot.");
                }
                if (field.Signature != "SCVR") continue;
                if (index is null || !variables.TryAdd(FalloutDialogueTopic.Text(field.Data.Span), index.Value))
                    throw new InvalidDataException("Script variable identity is ambiguous.");
                index = null;
            }
            if (index is not null) throw new InvalidDataException("Script variable has no source name.");
            _variables[quest.FormKey] = variables;
        }
        return variables.TryGetValue(split[^1], out var value) ? (quest.FormKey, value) :
            throw new NotSupportedException($"Script operand {name} has no variable owner.");
    }
}
