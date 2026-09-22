using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal static class FalloutScriptLocals
{
    internal static FalloutPluginRecord? AttachedScript(FalloutPluginStack records, FalloutPluginRecord owner)
    {
        var record = owner.Signature is "REFR" or "ACHR" or "ACRE" ?
            records.GetEffective(FalloutDialogueTopic.RequiredForm(owner, "NAME")) : owner;
        var fields = record.ReadSubrecords().Where(field => field.Signature == "SCRI").ToArray();
        if (fields.Length == 0) return null;
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException($"Script attachment on {record.FormKey} is ambiguous or malformed.");
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(fields[0].Data.Span);
        if (raw == 0) return null;
        var script = records.GetEffective(record.Plugin.AdjustFormId(raw));
        return script.Signature == "SCPT" ? script : throw new InvalidDataException("Attached script is not SCPT.");
    }

    internal static IReadOnlyDictionary<string, uint> Read(FalloutPluginRecord script) =>
        ReadDeclarations(script).ToDictionary(pair => pair.Key, pair => pair.Value.Index,
            StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, FalloutScriptLocalDeclaration> ReadDeclarations(
        FalloutPluginRecord script)
    {
        if (script.Signature != "SCPT") throw new InvalidDataException("Variable declaration owner is not SCPT.");
        var variables = new Dictionary<string, (uint Index, byte Flags)>(StringComparer.OrdinalIgnoreCase);
        var declarations = new Dictionary<uint, (byte Flags, string Name)>();
        uint? index = null;
        byte flags = 0;
        foreach (var field in script.ReadSubrecords())
        {
            if (field.Signature == "SLSD")
            {
                if (field.Data.Length != 24 || index is not null)
                    throw new InvalidDataException("Script variable declaration extent or name is invalid.");
                index = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                flags = field.Data.Span[16];
            }
            if (field.Signature != "SCVR") continue;
            if (index is null) throw new InvalidDataException("Script variable identity is ambiguous.");
            var name = FalloutDialogueTopic.Text(field.Data.Span);
            if (declarations.TryGetValue(index.Value, out var previous))
            {
                // SLSD has an index, one flag byte and 19 unused bytes. Owned
                // duplicate declarations contain different compiler padding;
                // only the slot, name and flags define the same local.
                if (previous.Name != name || previous.Flags != flags)
                    throw new InvalidDataException($"Conflicting duplicate script variable slot {index} in {script.FormKey}.");
            }
            else
            {
                if (!variables.TryAdd(name, (index.Value, flags)))
                    throw new InvalidDataException("Script variable identity is ambiguous.");
                declarations.Add(index.Value, (flags, name));
            }
            index = null;
        }
        if (index is not null) throw new InvalidDataException("Script variable has no source name.");

        var sourceKinds = ReadSourceKinds(script);
        if (sourceKinds.Keys.Any(name => !variables.ContainsKey(name)))
            throw new InvalidDataException("Script source declares a variable without a compiled slot.");
        return variables.ToDictionary(pair => pair.Key,
            pair => new FalloutScriptLocalDeclaration(pair.Value.Index,
                sourceKinds.GetValueOrDefault(pair.Key, FalloutScriptLocalKind.Number)),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, FalloutScriptLocalKind> ReadSourceKinds(
        FalloutPluginRecord script)
    {
        var source = script.ReadSubrecords().Where(field => field.Signature == "SCTX").ToArray();
        if (source.Length == 0) return new Dictionary<string, FalloutScriptLocalKind>();
        if (source.Length != 1) throw new InvalidDataException("Script source is ambiguous.");
        var result = new Dictionary<string, FalloutScriptLocalKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in FalloutDialogueTopic.ScriptText(source[0].Data.Span).Split('\n'))
        {
            var line = FalloutGameModeProgram.StripComment(raw).Trim();
            if (line.Length == 0) continue;
            var tokens = FalloutGameModeProgram.Tokens(line);
            if (tokens.Length == 0) continue;
            var kind = tokens[0].ToLowerInvariant() switch
            {
                "short" or "int" or "long" or "float" => FalloutScriptLocalKind.Number,
                "ref" or "reference" => FalloutScriptLocalKind.Form,
                "string_var" => FalloutScriptLocalKind.String,
                "array_var" => FalloutScriptLocalKind.Array,
                _ => (FalloutScriptLocalKind?)null,
            };
            if (kind is not { } localKind) continue;
            if (tokens.Length != 2 || !Regex.IsMatch(tokens[1],
                    @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant) ||
                !result.TryAdd(tokens[1], localKind))
                throw new InvalidDataException("Script source variable declaration is ambiguous.");
        }
        return result;
    }
}
