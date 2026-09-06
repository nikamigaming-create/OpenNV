using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutStageQuestVariableWrite(int Line, FalloutFormKey Quest, uint Index, double Value);

internal sealed class FalloutStageQuestVariableProgram
{
    private readonly List<(int Line, string Target, string[] Expression)> _assignments = [];
    private FalloutScriptBindings? _bindings;
    private readonly HashSet<string> _writtenGlobals = new(StringComparer.OrdinalIgnoreCase);

    internal static FalloutStageQuestVariableProgram Read(FalloutPluginStack records, FalloutOpeningControlStage stage)
    {
        var result = new FalloutStageQuestVariableProgram();
        var lines = FalloutDialogueTopic.CodeLines(stage.Source).ToArray();
        var globals = records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read)
            .Select(value => value.EditorId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            if (!line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].Equals("set", StringComparison.OrdinalIgnoreCase)) continue;
            var tokens = FalloutGameModeProgram.Tokens(line);
            if (tokens.Length < 4 || !tokens[2].Equals("to", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Stage assignment syntax is unbound.");
            if (!globals.Contains(tokens[1])) result._assignments.Add((index, tokens[1], tokens[3..]));
            else result._writtenGlobals.Add(tokens[1]);
        }
        if (result._assignments.Count == 0) return result;
        var quest = records.GetEffective(stage.Quest);
        var fields = quest.ReadSubrecords().ToArray();
        var starts = fields.Select((field, index) => (field, index)).Where(value =>
            value.field.Signature == "INDX" && value.field.Data.Length == 2 &&
            BinaryPrimitives.ReadInt16LittleEndian(value.field.Data.Span) == stage.Stage).ToArray();
        if (quest.Signature != "QUST" || starts.Length != 1)
            throw new InvalidDataException("Stage variable script identity is ambiguous.");
        var scope = fields.Skip(starts[0].index + 1).TakeWhile(field => field.Signature is not ("INDX" or "QOBJ")).ToArray();
        var sources = scope.Where(field => field.Signature == "SCTX").ToArray();
        var headers = scope.Where(field => field.Signature == "SCHR").ToArray();
        if (headers.Length != 1 || headers[0].Data.Length != 20 || sources.Length != 1 ||
            FalloutOpeningPlayerControlResolver.ReadSource(quest, sources[0].Data.Span) != stage.Source)
            throw new NotSupportedException("Stage variable script needs one matching compiled source owner.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(headers[0].Data.Span[4..]) !=
            scope.Count(field => field.Signature is "SCRO" or "SCRV"))
            throw new InvalidDataException("Stage script reference count disagrees with its header.");
        if (scope.Any(field => field.Signature == "CTDA") || lines.Any(line =>
            line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0].ToLowerInvariant() is "if" or "elseif" or "else" or "endif" or "return" or "while" or "loop" or "goto"))
            throw new NotSupportedException("Conditional stage assignments need their result-script control-flow owner.");
        result._bindings = new(records, quest, quest, scope);
        foreach (var assignment in result._assignments) _ = result._bindings.Variable(assignment.Target);
        return result;
    }

    internal IReadOnlyList<FalloutStageQuestVariableWrite> Prepare(FalloutQuestState quests, FalloutGlobalState? globals)
    {
        var pending = new Dictionary<(FalloutFormKey Quest, uint Index), double>();
        var writes = new List<FalloutStageQuestVariableWrite>();
        double Read(string name)
        {
            if (_bindings!.TryForm(name) is { Signature: "GLOB" } global)
            {
                if (_writtenGlobals.Contains(name))
                    throw new NotSupportedException("Mixed stage global/local expressions require one ordered assignment owner.");
                return globals?.Get(global.FormKey) ?? throw new NotSupportedException("Stage expression has no global state owner.");
            }
            var key = _bindings.Variable(name);
            return pending.TryGetValue(key, out var value) ? value : quests.Variable(key.Quest, key.Index);
        }
        foreach (var assignment in _assignments)
        {
            var key = _bindings!.Variable(assignment.Target);
            _ = quests.Variable(key.Quest, key.Index);
            var value = FalloutGameModeProgram.Evaluate(assignment.Expression, Read);
            pending[key] = value;
            writes.Add(new(assignment.Line, key.Quest, key.Index, value));
        }
        return writes;
    }
}
