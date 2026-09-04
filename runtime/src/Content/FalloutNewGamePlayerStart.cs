using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutPlayerStartCandidate(
    FalloutPlacedReference Reference,
    int DirectPackageLocationCount);

internal sealed record FalloutNewGamePlayerStart(
    FalloutPlacedReference Reference,
    FalloutFormKey Quest,
    short Stage,
    IReadOnlyList<FalloutPlayerStartCandidate> Candidates);

internal static class FalloutNewGamePlayerStartResolver
{
    private sealed record ScriptSelection(
        IReadOnlyList<string> MoveTargets,
        IReadOnlyList<FalloutFormKey> CompletionConditions);

    private sealed record ConditionFrame(
        bool ParentActive,
        bool Result,
        bool ElseSeen);

    private const string InitialQuestEditorId = "VCG00";
    private const string HeadingMarkerEditorId = "XMarkerHeading";
    private const string MoveCommand = "player.moveto";
    private const int QuestDataBytes = 8;
    private const int PackageLocationBytes = 12;
    private const int ConditionalExpressionTokenCount = 5;
    private const uint NearReferenceLocationType = 0;
    private const uint PersistentReferenceFlag = 0x0000_0400;
    private const short InitialStage = 0;

    internal static FalloutNewGamePlayerStart Resolve(
        FalloutPluginStack stack,
        FalloutCellScene cell)
    {
        var references = cell.References.Where(reference =>
            cell.BaseObjects.TryGetValue(reference.Base, out var baseObject) &&
            baseObject.EditorId.Equals(HeadingMarkerEditorId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (references.Length == 0)
            throw new InvalidDataException(
                $"Native CELL {cell.Cell.FormKey} has no {HeadingMarkerEditorId} candidates.");

        var packageCounts = ReadDirectPackageLocationCounts(stack, references);
        var candidates = references.Select(reference => new FalloutPlayerStartCandidate(
            reference,
            packageCounts.GetValueOrDefault(reference.FormKey))).ToArray();
        var quests = stack.EffectiveRecords("QUST").Where(record =>
            ReadEditorId(record).Equals(InitialQuestEditorId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (quests.Length != 1)
            throw new InvalidDataException(
                $"Native New Game requires exactly one winning {InitialQuestEditorId} QUST; found {quests.Length}.");
        var quest = quests[0];
        var subrecords = quest.ReadSubrecords().ToArray();
        var questData = RequiredSingle(subrecords, "DATA", quest);
        if (questData.Length != QuestDataBytes)
            throw Error(quest, $"DATA must contain exactly {QuestDataBytes} bytes");
        var stage = ReadStage(subrecords, quest, InitialStage);
        var scriptSelections = stage.Where(value => value.Signature == "SCTX")
            .Select(value => ReadScriptSelection(value.Data.Span, quest, stack))
            .ToArray();
        var moveTargets = scriptSelections.SelectMany(value => value.MoveTargets)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (moveTargets.Length != 1)
            throw Error(
                quest,
                $"stage {InitialStage} must contain exactly one active {MoveCommand} target; " +
                $"found {moveTargets.Length} ({string.Join(',', moveTargets)})");
        var matching = references.Where(reference =>
            reference.EditorId.Equals(moveTargets[0], StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length != 1)
            throw Error(
                quest,
                $"stage {InitialStage} target {moveTargets[0]} resolves to {matching.Length} CELL references");
        var selected = matching[0];
        var scriptReferences = stage.Where(value => value.Signature == "SCRO").Select(value =>
        {
            if (value.Data.Length != sizeof(uint))
                throw Error(quest, $"stage {InitialStage} SCRO must contain one FormID");
            return quest.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(value.Data.Span));
        }).ToArray();
        if (scriptReferences.Count(value => value == selected.FormKey) != 1)
            throw Error(
                quest,
                $"stage {InitialStage} compiled references do not bind exactly once to {selected.FormKey}");
        foreach (var condition in scriptSelections.SelectMany(value => value.CompletionConditions).Distinct())
        {
            if (!scriptReferences.Contains(condition))
                throw Error(
                    quest,
                    $"stage {InitialStage} compiled references do not bind New Game condition {condition}");
        }
        if (selected.Flags != PersistentReferenceFlag ||
            FalloutCellSceneReader.IsInitiallyDisabled(selected) ||
            selected.EnableParent is not null || selected.Scale != 1.0f)
            throw new NotSupportedException(
                $"Native New Game player start {selected.FormKey} is outside the evidenced active persistent " +
                $"reference contract: flags=0x{selected.Flags:x8} enableParent={selected.EnableParent} " +
                $"scale={selected.Scale:R}.");
        return new FalloutNewGamePlayerStart(selected, quest.FormKey, InitialStage, candidates);
    }

    private static Dictionary<FalloutFormKey, int> ReadDirectPackageLocationCounts(
        FalloutPluginStack stack,
        IReadOnlyCollection<FalloutPlacedReference> references)
    {
        var keys = references.Select(value => value.FormKey).ToHashSet();
        var result = keys.ToDictionary(key => key, _ => 0);
        foreach (var package in stack.EffectiveRecords("PACK"))
        {
            foreach (var location in package.ReadSubrecords().Where(value => value.Signature == "PLDT"))
            {
                if (location.Data.Length < sizeof(uint))
                    throw Error(package, "PLDT has no location type");
                if (BinaryPrimitives.ReadUInt32LittleEndian(location.Data.Span) != NearReferenceLocationType)
                    continue;
                if (location.Data.Length != PackageLocationBytes)
                    throw Error(package, $"near-reference PLDT must contain exactly {PackageLocationBytes} bytes");
                var target = package.Plugin.AdjustOptionalFormId(
                    BinaryPrimitives.ReadUInt32LittleEndian(location.Data.Span[sizeof(uint)..]));
                if (target is { } key && result.ContainsKey(key))
                    result[key]++;
            }
        }
        return result;
    }

    private static IReadOnlyList<FalloutPluginSubrecord> ReadStage(
        IReadOnlyList<FalloutPluginSubrecord> source,
        FalloutPluginRecord quest,
        short expectedStage)
    {
        var matches = new List<IReadOnlyList<FalloutPluginSubrecord>>();
        for (var index = 0; index < source.Count; ++index)
        {
            if (source[index].Signature != "INDX")
                continue;
            if (source[index].Data.Length != sizeof(short))
                throw Error(quest, "INDX must contain one int16 stage index");
            var stage = BinaryPrimitives.ReadInt16LittleEndian(source[index].Data.Span);
            var end = index + 1;
            while (end < source.Count && source[end].Signature is not ("INDX" or "QOBJ"))
                end++;
            if (stage == expectedStage)
                matches.Add(source.Skip(index + 1).Take(end - index - 1).ToArray());
            index = end - 1;
        }
        if (matches.Count != 1)
            throw Error(quest, $"must contain exactly one stage {expectedStage}; found {matches.Count}");
        return matches[0];
    }

    private static ScriptSelection ReadScriptSelection(
        ReadOnlySpan<byte> bytes,
        FalloutPluginRecord quest,
        FalloutPluginStack stack)
    {
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(quest, "SCTX contains unsupported non-ASCII source text");
        var text = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        if (text.IndexOf('\0') >= 0)
            throw Error(quest, "SCTX contains an embedded null");
        var targets = new List<string>();
        var completionConditions = new List<FalloutFormKey>();
        var conditions = new Stack<ConditionFrame>();
        var active = true;
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Split(';', 2)[0].Trim();
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;
            if (tokens[0].Equals("if", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length != ConditionalExpressionTokenCount ||
                    !tokens[1].Equals("GetQuestCompleted", StringComparison.OrdinalIgnoreCase) ||
                    tokens[3] != "==" || tokens[4] is not ("0" or "1"))
                    throw Error(quest, $"stage condition is outside the admitted New Game syntax: {line}");
                var conditionQuest = FindQuest(stack, tokens[2]);
                completionConditions.Add(conditionQuest.FormKey);
                var completedAtNewGame = false;
                var result = completedAtNewGame == (tokens[4] == "1");
                conditions.Push(new ConditionFrame(active, result, ElseSeen: false));
                active = active && result;
                continue;
            }
            if (tokens[0].Equals("else", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length != 1 || conditions.Count == 0 || conditions.Peek().ElseSeen)
                    throw Error(quest, $"stage script has an invalid else: {line}");
                var condition = conditions.Pop();
                conditions.Push(condition with { ElseSeen = true });
                active = condition.ParentActive && !condition.Result;
                continue;
            }
            if (tokens[0].Equals("endif", StringComparison.OrdinalIgnoreCase))
            {
                if (tokens.Length != 1 || conditions.Count == 0)
                    throw Error(quest, $"stage script has an invalid endif: {line}");
                active = conditions.Pop().ParentActive;
                continue;
            }
            if (!tokens[0].Equals(MoveCommand, StringComparison.OrdinalIgnoreCase) || !active)
                continue;
            if (tokens.Length != 2 || !tokens[1].All(character =>
                    char.IsAsciiLetterOrDigit(character) || character == '_'))
                throw Error(quest, $"stage player move command is outside the admitted syntax: {line}");
            targets.Add(tokens[1]);
        }
        if (conditions.Count != 0)
            throw Error(quest, "stage script has an unterminated New Game condition");
        return new ScriptSelection(targets, completionConditions);
    }

    private static FalloutPluginRecord FindQuest(FalloutPluginStack stack, string editorId)
    {
        var matches = stack.EffectiveRecords("QUST").Where(record =>
            ReadEditorId(record).Equals(editorId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(
                $"Native New Game condition QUST {editorId} has {matches.Length} winning records.");
        return matches[0];
    }

    private static string ReadEditorId(FalloutPluginRecord record)
    {
        var data = RequiredSingle(record.ReadSubrecords().ToArray(), "EDID", record).Span;
        var terminator = data.IndexOf((byte)0);
        if (terminator != data.Length - 1 || data[..terminator].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(record, "EDID must be a null-terminated ASCII identifier");
        return Encoding.ASCII.GetString(data[..terminator]);
    }

    private static ReadOnlyMemory<byte> RequiredSingle(
        IReadOnlyList<FalloutPluginSubrecord> source,
        string signature,
        FalloutPluginRecord record)
    {
        var matches = source.Where(value => value.Signature == signature).ToArray();
        if (matches.Length != 1)
            throw Error(record, $"must contain exactly one {signature}; found {matches.Length}");
        return matches[0].Data;
    }

    private static InvalidDataException Error(FalloutPluginRecord record, string detail) =>
        new($"Native {record.Signature} {record.FormKey} {detail}.");
}
