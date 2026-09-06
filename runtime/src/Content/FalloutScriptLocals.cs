using System.Buffers.Binary;

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

    internal static IReadOnlyDictionary<string, uint> Read(FalloutPluginRecord script)
    {
        if (script.Signature != "SCPT") throw new InvalidDataException("Variable declaration owner is not SCPT.");
        var variables = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        var declarations = new Dictionary<uint, (byte[] Data, string Name)>();
        uint? index = null;
        byte[]? declaration = null;
        foreach (var field in script.ReadSubrecords())
        {
            if (field.Signature == "SLSD")
            {
                if (field.Data.Length != 24 || index is not null)
                    throw new InvalidDataException("Script variable declaration extent or name is invalid.");
                index = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                declaration = field.Data.ToArray();
            }
            if (field.Signature != "SCVR") continue;
            if (index is null) throw new InvalidDataException("Script variable identity is ambiguous.");
            var name = FalloutDialogueTopic.Text(field.Data.Span);
            if (declarations.TryGetValue(index.Value, out var previous))
            {
                // Owned scripts can repeat the same declaration. One identical
                // slot/name/storage declaration still denotes one local; a
                // conflicting name or storage description remains ambiguous.
                if (previous.Name != name || !previous.Data.AsSpan().SequenceEqual(declaration))
                    throw new InvalidDataException("Conflicting duplicate script variable slot.");
            }
            else
            {
                if (!variables.TryAdd(name, index.Value)) throw new InvalidDataException("Script variable identity is ambiguous.");
                declarations.Add(index.Value, (declaration!, name));
            }
            index = null;
        }
        if (index is not null) throw new InvalidDataException("Script variable has no source name.");
        return variables;
    }
}
