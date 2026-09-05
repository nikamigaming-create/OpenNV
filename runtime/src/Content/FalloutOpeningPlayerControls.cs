using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutPlayerControlState(
    bool Movement,
    bool PipBoy,
    bool Fighting,
    bool PointOfView,
    bool Looking,
    bool RolloverText,
    bool Sneaking)
{
    internal static FalloutPlayerControlState AllEnabled { get; } =
        new(true, true, true, true, true, true, true);
}

internal sealed record FalloutPlayerControlCommand(
    bool Enable,
    IReadOnlyList<bool> Arguments)
{
    private const int RolloverTextIndex = 5;
    private const int SneakingIndex = 6;

    internal FalloutPlayerControlState Apply(FalloutPlayerControlState state)
    {
        var values = new[]
        {
            state.Movement,
            state.PipBoy,
            state.Fighting,
            state.PointOfView,
            state.Looking,
            state.RolloverText,
            state.Sneaking,
        };
        for (var index = 0; index < Arguments.Count; ++index)
        {
            if (Arguments[index])
                values[index] = Enable;
        }
        return new FalloutPlayerControlState(
            values[0], values[1], values[2], values[3], values[4],
            values[RolloverTextIndex], values[SneakingIndex]);
    }
}

internal sealed record FalloutOpeningControlStage(
    FalloutFormKey Quest,
    string QuestEditorId,
    short Stage,
    string Source,
    IReadOnlyList<FalloutPlayerControlCommand> Commands);

internal sealed record FalloutOpeningControlGraph(
    IReadOnlyDictionary<string, IReadOnlyDictionary<short, FalloutOpeningControlStage>> Quests)
{
    internal FalloutOpeningControlStage Stage(string questEditorId, short stage)
    {
        if (!Quests.TryGetValue(questEditorId, out var stages) ||
            !stages.TryGetValue(stage, out var result))
            throw new KeyNotFoundException(
                $"Native opening control stage is absent: {questEditorId}:{stage}.");
        return result;
    }
}

internal static class FalloutOpeningPlayerControlResolver
{
    private const int ControlCount = 7;

    internal static FalloutOpeningControlGraph Resolve(
        FalloutPluginStack stack,
        IReadOnlyList<string> questEditorIds)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(questEditorIds);
        if (questEditorIds.Count == 0 ||
            questEditorIds.Any(string.IsNullOrWhiteSpace) ||
            questEditorIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != questEditorIds.Count)
            throw new ArgumentException("Native opening control quest identities are invalid.");

        var quests = new Dictionary<string, IReadOnlyDictionary<short, FalloutOpeningControlStage>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var editorId in questEditorIds)
        {
            var matches = stack.EffectiveRecords("QUST").Where(record =>
                ReadEditorId(record).Equals(editorId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException(
                    $"Native opening requires exactly one winning {editorId} QUST; found {matches.Length}.");
            var quest = matches[0];
            var stages = ReadStages(quest, editorId);
            quests.Add(editorId, stages);
        }
        return new FalloutOpeningControlGraph(quests);
    }

    private static IReadOnlyDictionary<short, FalloutOpeningControlStage> ReadStages(
        FalloutPluginRecord quest,
        string editorId)
    {
        var subrecords = quest.ReadSubrecords().ToArray();
        var result = new Dictionary<short, FalloutOpeningControlStage>();
        for (var index = 0; index < subrecords.Length; ++index)
        {
            if (subrecords[index].Signature != "INDX")
                continue;
            if (subrecords[index].Data.Length != sizeof(short))
                throw Error(quest, "INDX must contain one int16 stage index");
            var stage = BinaryPrimitives.ReadInt16LittleEndian(subrecords[index].Data.Span);
            var end = index + 1;
            while (end < subrecords.Length && subrecords[end].Signature is not ("INDX" or "QOBJ"))
                end++;
            var sources = subrecords[(index + 1)..end]
                .Where(value => value.Signature == "SCTX")
                .Select(value => ReadSource(quest, value.Data.Span))
                .ToArray();
            var source = string.Join(System.Environment.NewLine, sources);
            var commands = sources.SelectMany(value => ReadCommands(quest, value)).ToArray();
            if (!result.TryAdd(
                    stage,
                    new FalloutOpeningControlStage(quest.FormKey, editorId, stage, source, commands)))
                throw Error(quest, $"contains duplicate stage {stage}");
            index = end - 1;
        }
        if (result.Count == 0)
            throw Error(quest, "contains no stage records");
        return result;
    }

    internal static string ReadSource(
        FalloutPluginRecord quest,
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(quest, "SCTX contains unsupported non-ASCII source text");
        var source = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        if (source.IndexOf('\0') >= 0)
            throw Error(quest, "SCTX contains an embedded null");
        return source;
    }

    private static IReadOnlyList<FalloutPlayerControlCommand> ReadCommands(
        FalloutPluginRecord quest,
        string source)
    {
        var result = new List<FalloutPlayerControlCommand>();
        foreach (var rawLine in source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Split(';', 2)[0].Trim();
            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;
            var enable = tokens[0].Equals("EnablePlayerControls", StringComparison.OrdinalIgnoreCase);
            var disable = tokens[0].Equals("DisablePlayerControls", StringComparison.OrdinalIgnoreCase);
            if (!enable && !disable)
                continue;
            if (tokens.Length is < 2 or > ControlCount + 1 ||
                tokens.Skip(1).Any(token => token is not ("0" or "1")))
                throw Error(quest, $"player-control syntax is unsupported: {line}");
            result.Add(new FalloutPlayerControlCommand(
                enable,
                tokens.Skip(1).Select(token => token == "1").ToArray()));
        }
        return result;
    }

    private static string ReadEditorId(FalloutPluginRecord record)
    {
        var matches = record.ReadSubrecords().Where(value => value.Signature == "EDID").ToArray();
        if (matches.Length != 1)
            throw Error(record, $"must contain exactly one EDID; found {matches.Length}");
        var data = matches[0].Data.Span;
        var terminator = data.IndexOf((byte)0);
        if (terminator != data.Length - 1 ||
            data[..terminator].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(record, "EDID must be a null-terminated ASCII identifier");
        return Encoding.ASCII.GetString(data[..terminator]);
    }

    private static InvalidDataException Error(FalloutPluginRecord record, string detail) =>
        new($"Native {record.Signature} {record.FormKey} {detail}.");
}
