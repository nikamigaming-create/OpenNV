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
    private readonly Dictionary<FalloutFormKey, IReadOnlyDictionary<string, FalloutScriptLocalDeclaration>> _variables = [];
    private readonly Dictionary<string, (FalloutFormKey Owner, uint Index, FalloutScriptLocalKind Kind)> _slots = new(StringComparer.OrdinalIgnoreCase);
    internal bool HasPlayerReference { get; }
    internal FalloutFormKey Source { get; }
    private readonly FalloutFormKey? _playerReference;
    private readonly bool _compiled;
    private readonly Dictionary<FalloutFormKey, string> _compiledNames = [];

    private FalloutScriptBindings(FalloutPluginStack records, FalloutPluginRecord owner,
        FalloutPluginRecord source, Func<FalloutPluginRecord, FalloutPluginRecord?> attachedScript)
    {
        _records = records; _owner = owner; Source = source.FormKey; _attachedScript = attachedScript;
        _compiled = true;
        // This spelling is an internal binding of the reserved engine player,
        // reachable only from a decoded table entry or an authoritative typed
        // query result. No source name or synthetic placed record is required.
        _playerReference = records.RuntimeFormKey(0x14); HasPlayerReference = true;
    }

    internal static FalloutScriptBindings ForCompiled(FalloutPluginStack records, FalloutPluginRecord owner,
        FalloutPluginRecord source, Func<FalloutPluginRecord, FalloutPluginRecord?> attachedScript) =>
        new(records, owner, source, attachedScript);

    internal string BindCompiledForm(FalloutFormKey form)
    {
        if (!_compiled) throw new InvalidOperationException("Binary operands require their compiled binding owner.");
        if (_records.RuntimeFormId(form) == 0x14) return "player";
        if (_compiledNames.TryGetValue(form, out var previous)) return previous;
        var name = "opennvCompiledOperand" + _compiledNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _forms.Add(name, _records.GetEffective(form)); _compiledNames.Add(form, name); return name;
    }

    internal FalloutScriptBindings(FalloutPluginStack records, FalloutPluginRecord quest,
        FalloutPluginRecord source, IEnumerable<FalloutPluginSubrecord> fields,
        Func<FalloutPluginRecord, FalloutPluginRecord?>? attachedScript = null)
    {
        _records = records;
        _owner = quest;
        _attachedScript = attachedScript;
        Source = source.FormKey;
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
            _variables.Add(script.FormKey, variables = FalloutScriptLocals.ReadDeclarations(script, _compiled ?
                FalloutScriptDeclarationAuthority.CompiledVanilla : FalloutScriptDeclarationAuthority.SourceDiagnostic));
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
            variables = FalloutScriptLocals.ReadDeclarations(script, _compiled ?
                FalloutScriptDeclarationAuthority.CompiledVanilla : FalloutScriptDeclarationAuthority.SourceDiagnostic);
            _variables.Add(script.FormKey, variables);
        }
        if (!variables.TryGetValue(split[^1], out var value))
            throw new NotSupportedException($"Script operand {name} has no variable owner.");
        slot = (owner.FormKey, value.Index, value.Kind);
        _slots.Add(name, slot);
        return slot;
    }
}
