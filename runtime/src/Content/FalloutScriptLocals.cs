using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal enum FalloutScriptDeclarationAuthority { Automatic, SourceDiagnostic, CompiledVanilla }

internal static partial class FalloutScriptLocals
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
        FalloutPluginRecord script, FalloutScriptDeclarationAuthority authority = FalloutScriptDeclarationAuthority.Automatic)
    {
        if (script.Signature != "SCPT") throw new InvalidDataException("Variable declaration owner is not SCPT.");
        var variables = new Dictionary<string, (uint Index, byte Flags)>(StringComparer.OrdinalIgnoreCase);
        var fields = script.ReadSubrecords().ToArray();
        var metadata = ReadMetadata(script);
        foreach (var entry in metadata)
        {
            var value = (entry.Index, entry.StorageFlags);
            // Name lookup, like index lookup, returns the first matching entry.
            variables.TryAdd(entry.Name, value);
        }

        if (authority == FalloutScriptDeclarationAuthority.CompiledVanilla ||
            authority == FalloutScriptDeclarationAuthority.Automatic && fields.Any(field => field.Signature == "SCDA"))
        {
            // Vanilla event-list storage is scalar; SCRV marks the reference
            // slots in that same compiled list. SCTX cannot change the kind or
            // introduce a slot when instructions are present. Extension handle
            // registration/operations require their own reached opcode owner.
            var references = new HashSet<uint>();
            foreach (var field in fields.Where(field => field.Signature == "SCRV"))
            {
                if (field.Data.Length != 4)
                    throw new InvalidDataException("Compiled reference local extent is invalid.");
                var slot = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                if (slot == 0 || !metadata.Any(entry => entry.Index == slot && entry.StorageFlags == 0))
                    throw new InvalidDataException("Compiled reference local has no floating SLSD slot.");
                references.Add(slot);
            }
            if (metadata.Any(value => value.Index == 0 || value.StorageFlags > 1))
                throw new NotSupportedException("Compiled local storage flag/index has no vanilla scalar owner.");
            return variables.ToDictionary(pair => pair.Key,
                pair => new FalloutScriptLocalDeclaration(pair.Value.Index,
                    references.Contains(pair.Value.Index) && !HasMixedStorage(script, pair.Value.Index)
                        ? FalloutScriptLocalKind.Form : FalloutScriptLocalKind.Number),
                StringComparer.OrdinalIgnoreCase);
        }
        var sourceKinds = ReadSourceKinds(script);
        if (sourceKinds.Keys.Any(name => !variables.ContainsKey(name)))
            throw new InvalidDataException("Script source declares a variable without a compiled slot.");
        return variables.ToDictionary(pair => pair.Key,
            pair => new FalloutScriptLocalDeclaration(pair.Value.Index,
                sourceKinds.GetValueOrDefault(pair.Key, FalloutScriptLocalKind.Number)),
            StringComparer.OrdinalIgnoreCase);
    }

    internal static void RequireCompiledValue(FalloutPluginRecord script, uint slot, double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Compiled local is not finite.");
        if (!ReadStorageKinds(script, FalloutScriptDeclarationAuthority.CompiledVanilla).TryGetValue(slot, out var kind))
            throw new InvalidDataException("Compiled local has no declared storage slot.");
        // A numeric declaration and a reference declaration can address the
        // same original cell. Its backing payload is Float64 bits; the reached
        // operand/assignment supplies the view and validates its own domain.
        if (HasMixedStorage(script, slot)) return;
        var fields = script.ReadSubrecords().Where(field => field.Signature == "SLSD" &&
            field.Data.Length == 24 && BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span) == slot).ToArray();
        var integer = fields[0].Data.Span[16] == 1;
        if ((integer || kind == FalloutScriptLocalKind.Form) &&
            (value != Math.Truncate(value) || value < (integer ? int.MinValue : 0d) ||
                value > (integer ? int.MaxValue : uint.MaxValue)))
            throw new NotSupportedException("Compiled local value is outside its exact scalar storage domain.");
    }

    internal static IReadOnlyDictionary<uint, FalloutScriptLocalKind> ReadStorageKinds(FalloutPluginRecord script,
        FalloutScriptDeclarationAuthority authority = FalloutScriptDeclarationAuthority.Automatic)
    {
        var named = ReadDeclarations(script, authority);
        var compiled = authority == FalloutScriptDeclarationAuthority.CompiledVanilla ||
            authority == FalloutScriptDeclarationAuthority.Automatic && script.ReadSubrecords().Any(field => field.Signature == "SCDA");
        return ReadMetadata(script).GroupBy(entry => entry.Index).ToDictionary(group => group.Key,
            group => compiled ? HasReferenceView(script, group.Key) && !HasMixedStorage(script, group.Key)
                ? FalloutScriptLocalKind.Form : FalloutScriptLocalKind.Number
                : group.Select(entry => named[entry.Name].Kind).Distinct().Single());
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
            // Reading compiled local slots admits world state, not execution.
            // An unsupported expression elsewhere in the program must fault
            // that script when parsed, rather than prevent the entire cell
            // from loading before any reference can own its state.
            var keyword = Regex.Match(line, @"^[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant).Value;
            var kind = keyword.ToLowerInvariant() switch
            {
                "short" or "int" or "long" or "float" => FalloutScriptLocalKind.Number,
                "ref" or "reference" => FalloutScriptLocalKind.Form,
                "string_var" => FalloutScriptLocalKind.String,
                "array_var" => FalloutScriptLocalKind.Array,
                _ => (FalloutScriptLocalKind?)null,
            };
            if (kind is not { } localKind) continue;
            // Retail source contains digit-leading local names and trailing
            // author notes without comment delimiters. SLSD/SCVR owns the
            // slot; only its declared value kind is needed at admission.
            var declaration = Regex.Match(line, @"^[A-Za-z_][A-Za-z0-9_]*\s+([A-Za-z0-9_]+)(?=\s|$)", RegexOptions.CultureInvariant);
            var name = declaration.Groups[1].Value;
            if (!declaration.Success || result.TryGetValue(name, out var previous) && previous != localKind)
                throw new InvalidDataException($"Script {script.FormKey} source variable declaration is ambiguous: {line}");
            result[name] = localKind;
        }
        return result;
    }
}
