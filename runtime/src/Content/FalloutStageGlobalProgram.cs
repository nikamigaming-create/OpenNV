using System.Buffers.Binary;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutStageGlobalWrite(int Line, FalloutFormKey Form, float Value);

// Result scripts use the same Float32 global storage and expression evaluator
// as recurring scripts. Compiled references bind operands; visible names alone
// cannot grant a stage access to a global. Conditional stage execution is a
// separate owner and must not be flattened into unconditional assignments.
internal sealed class FalloutStageGlobalProgram
{
    private readonly List<(int Line, FalloutFormKey Form, string[] Expression)> _assignments = [];
    private readonly Dictionary<string, FalloutFormKey> _references = new(StringComparer.OrdinalIgnoreCase);

    internal static FalloutStageGlobalProgram Read(FalloutPluginStack records, FalloutOpeningControlStage stage)
    {
        var result = new FalloutStageGlobalProgram();
        var globals = records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read)
            .ToDictionary(global => global.EditorId, StringComparer.OrdinalIgnoreCase);
        var globalsByForm = globals.Values.ToDictionary(global => global.Form);
        var lines = FalloutDialogueTopic.CodeLines(stage.Source).ToArray();
        var candidates = lines.Select((line, index) => (Line: line, Index: index,
            Target: Regex.Match(line, @"^set\s+([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            .Where(value => value.Target.Success && globals.ContainsKey(value.Target.Groups[1].Value)).ToArray();
        if (candidates.Length == 0) return result;

        var quest = records.GetEffective(stage.Quest);
        if (quest.Signature != "QUST") throw new InvalidDataException("Stage global source is not QUST.");
        var fields = quest.ReadSubrecords().ToArray();
        var starts = fields.Select((field, index) => (field, index)).Where(value =>
            value.field.Signature == "INDX" && value.field.Data.Length == 2 &&
            BinaryPrimitives.ReadInt16LittleEndian(value.field.Data.Span) == stage.Stage).ToArray();
        if (starts.Length != 1) throw new InvalidDataException("Stage global script identity is ambiguous.");
        var scope = fields.Skip(starts[0].index + 1).TakeWhile(field => field.Signature is not ("INDX" or "QOBJ")).ToArray();
        var sources = scope.Where(field => field.Signature == "SCTX").ToArray();
        var headers = scope.Where(field => field.Signature == "SCHR").ToArray();
        if (headers.Length != 1 || headers[0].Data.Length != 20)
            throw new NotSupportedException("Stage global script header ownership is absent or ambiguous.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[4..]) !=
            scope.Count(field => field.Signature is "SCRO" or "SCRV"))
            throw new InvalidDataException("Stage script reference count disagrees with its header.");
        if (sources.Length != 1 || FalloutOpeningPlayerControlResolver.ReadSource(quest, sources[0].Data.Span) != stage.Source ||
            scope.Any(field => field.Signature == "CTDA") || lines.Any(line =>
                Regex.IsMatch(line, @"^(if|elseif|else|endif|while|loop|goto|return)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new NotSupportedException("Stage global assignments need an unconditional, single result-script owner.");
        foreach (var reference in scope.Where(field => field.Signature == "SCRO"))
        {
            if (reference.Data.Length != 4) throw new InvalidDataException("Stage script reference extent is invalid.");
            var key = quest.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(reference.Data.Span));
            if (!globalsByForm.TryGetValue(key, out var global)) continue;
            if (result._references.TryGetValue(global.EditorId, out var existing) && existing != global.Form)
                throw new InvalidDataException("Stage global reference identity is ambiguous.");
            result._references[global.EditorId] = global.Form;
        }
        var locals = scope.Where(field => field.Signature == "SCVR")
            .Select(field => FalloutDialogueTopic.Text(field.Data.Span)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parentScript = fields.SingleOrDefault(field => field.Signature == "SCRI").Data;
        if (parentScript.Length != 0)
        {
            if (parentScript.Length != 4) throw new InvalidDataException("Stage parent script reference extent is invalid.");
            var id = BinaryPrimitives.ReadUInt32LittleEndian(parentScript.Span);
            if (id != 0)
                locals.UnionWith(records.GetEffective(quest.Plugin.AdjustFormId(id)).ReadSubrecords()
                    .Where(field => field.Signature == "SCVR").Select(field => FalloutDialogueTopic.Text(field.Data.Span)));
        }
        foreach (var candidate in candidates)
        {
            var name = candidate.Target.Groups[1].Value;
            if (locals.Contains(name))
                throw new NotSupportedException("Stage assignment has ambiguous local/global ownership.");
            if (!result._references.TryGetValue(name, out var form))
                throw new InvalidDataException($"Stage global {name} has no compiled reference binding.");
            var tokens = FalloutGameModeProgram.Tokens(candidate.Line);
            if (tokens.Length < 4 || !tokens[1].Equals(name, StringComparison.OrdinalIgnoreCase) ||
                !tokens[2].Equals("to", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Stage global assignment syntax is unbound.");
            result._assignments.Add((candidate.Index, form, tokens[3..]));
        }
        return result;
    }

    internal IReadOnlyList<FalloutStageGlobalWrite> Prepare(FalloutGlobalState? globals)
    {
        if (_assignments.Count == 0) return [];
        if (globals is null) throw new NotSupportedException("Stage globals have no shared state owner.");
        var pending = new Dictionary<FalloutFormKey, float>();
        var writes = new List<FalloutStageGlobalWrite>();
        double Read(string name)
        {
            if (!_references.TryGetValue(name, out var form))
                throw new NotSupportedException($"Stage global expression operand {name} has no compiled global binding.");
            return pending.TryGetValue(form, out var value) ? value : globals.Get(form);
        }
        foreach (var assignment in _assignments)
        {
            _ = globals.Get(assignment.Form);
            var value = (float)FalloutGameModeProgram.Evaluate(assignment.Expression, Read);
            if (!float.IsFinite(value)) throw new InvalidDataException("Stage global exceeds Float32 storage.");
            pending[assignment.Form] = value;
            writes.Add(new(assignment.Line, assignment.Form, value));
        }
        return writes;
    }
}
