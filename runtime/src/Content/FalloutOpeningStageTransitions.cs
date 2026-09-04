using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutOpeningStageTransition(
    string Kind,
    string FromQuestEditorId,
    short FromStage,
    string ToQuestEditorId,
    short ToStage,
    float? DelaySeconds,
    IReadOnlyList<string> Blockers);

internal sealed record FalloutOpeningStageTransitionGraph(
    IReadOnlyList<FalloutOpeningStageTransition> Transitions)
{
    internal IReadOnlyList<FalloutOpeningStageTransition> From(
        string questEditorId,
        short stage) => Transitions.Where(value =>
            value.FromQuestEditorId.Equals(questEditorId, StringComparison.OrdinalIgnoreCase) &&
            value.FromStage == stage).ToArray();
}

internal sealed class FalloutOpeningStageMachine
{
    private const int MaximumImmediateTransitions = 64;

    private readonly FalloutOpeningStageTransitionGraph _transitions;
    private readonly FalloutOpeningControlGraph _controls;
    private FalloutOpeningStageTransition? _timerTransition;
    private FalloutOpeningStageTransition? _blockedTransition;
    private HashSet<string> _pendingBlockers = new(StringComparer.OrdinalIgnoreCase);

    internal FalloutOpeningStageMachine(
        FalloutOpeningStageTransitionGraph transitions,
        FalloutOpeningControlGraph controls,
        string initialQuestEditorId,
        short initialStage,
        FalloutPlayerControlState? initialControlState = null)
    {
        _transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        ControlState = initialControlState ?? FalloutPlayerControlState.AllEnabled;
        Enter(initialQuestEditorId, initialStage);
    }

    internal string QuestEditorId { get; private set; } = string.Empty;
    internal short Stage { get; private set; }
    internal float? TimerSeconds { get; private set; }
    internal FalloutPlayerControlState ControlState { get; private set; } =
        FalloutPlayerControlState.AllEnabled;
    internal IReadOnlyCollection<string> PendingBlockers => _pendingBlockers;

    internal void EnterSourceStage(string questEditorId, short stage)
    {
        if (_pendingBlockers.Count != 0 || TimerSeconds is not null || _blockedTransition is not null ||
            _timerTransition is not null)
            throw new InvalidOperationException(
                "Native opening cannot accept an external source stage while a transition is pending.");
        _ = _controls.Stage(questEditorId, stage);
        Enter(questEditorId, stage);
    }

    internal bool CompleteBlocker(string blocker)
    {
        if (string.IsNullOrWhiteSpace(blocker) || !_pendingBlockers.Remove(blocker))
            throw new InvalidOperationException(
                $"Native opening blocker is not pending: {blocker}.");
        if (_pendingBlockers.Count != 0)
            return false;
        var transition = _blockedTransition ??
            throw new InvalidOperationException("Native opening blocked transition is absent.");
        _blockedTransition = null;
        if (transition.Kind == "timer" && transition.DelaySeconds is > 0.0f and { } delay)
        {
            _timerTransition = transition;
            TimerSeconds = delay;
            return true;
        }
        Enter(transition.ToQuestEditorId, transition.ToStage);
        return true;
    }

    internal bool AdvanceTime(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (TimerSeconds is not { } remaining)
            return false;
        remaining -= seconds;
        if (remaining > 0.0f)
        {
            TimerSeconds = remaining;
            return false;
        }
        var transition = _timerTransition ??
            throw new InvalidOperationException("Native opening timer transition is absent.");
        TimerSeconds = null;
        _timerTransition = null;
        Enter(transition.ToQuestEditorId, transition.ToStage);
        return true;
    }

    private void Enter(string questEditorId, short stage)
    {
        for (var count = 0; count < MaximumImmediateTransitions; ++count)
        {
            var source = _controls.Stage(questEditorId, stage);
            ControlState = source.Commands.Aggregate(
                ControlState,
                (state, command) => command.Apply(state));
            QuestEditorId = questEditorId;
            Stage = stage;
            TimerSeconds = null;
            _timerTransition = null;
            _blockedTransition = null;
            _pendingBlockers.Clear();

            var immediate = _transitions.From(questEditorId, stage)
                .Where(value => value.Kind is "stage-script" or "dialogue-result")
                .ToArray();
            if (immediate.Length > 1)
                throw new InvalidOperationException(
                    $"Native opening stage has multiple direct edges: {questEditorId}:{stage}.");
            if (immediate.Length == 1)
            {
                if (immediate[0].Blockers.Count != 0)
                {
                    _blockedTransition = immediate[0];
                    _pendingBlockers = immediate[0].Blockers.ToHashSet(
                        StringComparer.OrdinalIgnoreCase);
                    return;
                }
                questEditorId = immediate[0].ToQuestEditorId;
                stage = immediate[0].ToStage;
                continue;
            }

            var timers = _transitions.From(questEditorId, stage)
                .Where(value => value.Kind == "timer")
                .ToArray();
            if (timers.Length > 1)
                throw new InvalidOperationException(
                    $"Native opening stage has multiple timer edges: {questEditorId}:{stage}.");
            if (timers.Length == 0)
                return;
            if (timers[0].Blockers.Count != 0)
            {
                _blockedTransition = timers[0];
                _pendingBlockers = timers[0].Blockers.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
                return;
            }
            if (timers[0].DelaySeconds is > 0.0f and { } delay)
            {
                _timerTransition = timers[0];
                TimerSeconds = delay;
                return;
            }
            questEditorId = timers[0].ToQuestEditorId;
            stage = timers[0].ToStage;
        }
        throw new InvalidOperationException("Native opening immediate-transition limit was exceeded.");
    }
}

internal static partial class FalloutOpeningStageTransitionResolver
{
    private const byte TopLevelGroupType = 7;
    private static readonly string[] BlockingCommands =
    [
        "PlayBink",
        "SayTo",
        "GetPlayerName",
        "ShowRaceMenu",
        "SetTagSkills",
        "ShowTraitMenu",
        "StartConversation",
    ];

    internal static FalloutOpeningStageTransitionGraph Resolve(
        FalloutPluginStack stack,
        FalloutOpeningControlGraph stages)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(stages);
        var result = new List<FalloutOpeningStageTransition>();
        foreach (var quest in stages.Quests)
        {
            var record = stack.GetEffective(quest.Value.Values.First().Quest);
            var scriptSource = ReadQuestScriptSource(stack, record);
            foreach (var stage in quest.Value.Values.OrderBy(value => value.Stage))
            {
                var code = CodeLines(stage.Source).ToArray();
                var direct = code.Select(line => ParseSetStage(line, stage.QuestEditorId))
                    .Where(value => value is not null)
                    .Select(value => value!.Value)
                    .Where(value => stages.Quests.ContainsKey(value.QuestEditorId))
                    .Distinct()
                    .ToArray();
                if (direct.Length > 1)
                    throw Error(record, $"stage {stage.Stage} has ambiguous direct SetStage targets");
                if (direct.Length == 1)
                {
                    result.Add(new FalloutOpeningStageTransition(
                        "stage-script",
                        stage.QuestEditorId,
                        stage.Stage,
                        direct[0].QuestEditorId,
                        direct[0].Stage,
                        null,
                        ReadBlockers(code)));
                }

                var timerTarget = ReadTimerTarget(scriptSource, stage.QuestEditorId, stage.Stage);
                if (timerTarget is { } target)
                {
                    result.Add(new FalloutOpeningStageTransition(
                        "timer",
                        stage.QuestEditorId,
                        stage.Stage,
                        stage.QuestEditorId,
                        target,
                        ReadTimerSeconds(code, stage.QuestEditorId),
                        ReadBlockers(code)));
                }
            }
        }
        return new FalloutOpeningStageTransitionGraph(result);
    }

    internal static FalloutOpeningStageTransitionGraph AddDialogueResults(
        FalloutPluginStack stack,
        FalloutOpeningControlGraph stages,
        FalloutOpeningStageTransitionGraph transitions,
        string questEditorId,
        IReadOnlyList<short> dialogueStages)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(questEditorId);
        ArgumentNullException.ThrowIfNull(dialogueStages);
        if (dialogueStages.Count == 0 || dialogueStages.Distinct().Count() != dialogueStages.Count)
            throw new ArgumentException("Native opening dialogue stages are empty or duplicated.");

        var sourceStages = dialogueStages.Select(stage => stages.Stage(questEditorId, stage)).ToArray();
        foreach (var sourceStage in sourceStages)
        {
            if (transitions.From(questEditorId, sourceStage.Stage).Count != 0)
                throw new InvalidDataException(
                    $"Native opening dialogue entry already has a transition: " +
                    $"{questEditorId}:{sourceStage.Stage}.");
        }
        var topics = sourceStages.SelectMany(sourceStage => CodeLines(sourceStage.Source)
                .Select(line => SayToLine().Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["topic"].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (topics.Length != 1)
            throw new InvalidDataException(
                $"Native opening dialogue stages for {questEditorId} must name exactly one SayTo topic; " +
                $"found {topics.Length}.");

        var topicRecords = stack.EffectiveRecords("DIAL").Where(record =>
            TryReadEditorId(record, out var editorId) &&
            editorId.Equals(topics[0], StringComparison.OrdinalIgnoreCase)).ToArray();
        if (topicRecords.Length != 1)
            throw new InvalidDataException(
                $"Native opening dialogue topic {topics[0]} must have exactly one winning DIAL; " +
                $"found {topicRecords.Length}.");
        var topic = topicRecords[0];
        var infos = stack.EffectiveRecords("INFO").Where(record => record.Groups.Any(group =>
                group.Type == TopLevelGroupType &&
                record.Plugin.AdjustFormId(group.LabelAsUInt32) == topic.FormKey))
            .OrderBy(record => stack.RuntimeFormId(record.FormKey))
            .ToArray();
        if (infos.Length == 0)
            throw Error(topic, "has no winning child INFO records");

        var sourceQuest = sourceStages[0].Quest;
        var results = new List<(FalloutPluginRecord Info, short Stage)>();
        foreach (var info in infos)
        {
            var questLinks = info.ReadSubrecords().Where(value => value.Signature == "QSTI").ToArray();
            if (questLinks.Length != 1 || questLinks[0].Data.Length != sizeof(uint) ||
                info.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(questLinks[0].Data.Span)) !=
                    sourceQuest)
                continue;
            var targets = info.ReadSubrecords().Where(value => value.Signature == "SCTX")
                .Select(value => ReadAsciiSource(info, value.Data.Span))
                .SelectMany(CodeLines)
                .Select(line => ParseSetStage(line, questEditorId))
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .Where(value => value.QuestEditorId.Equals(
                    questEditorId, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToArray();
            if (targets.Length > 1)
                throw Error(info, $"dialogue result has {targets.Length} local SetStage targets");
            if (targets.Length == 1)
                results.Add((info, targets[0].Stage));
        }
        if (results.Count < sourceStages.Length)
            throw Error(
                topic,
                $"has {results.Count} ordered local SetStage results for {sourceStages.Length} dialogue stages");

        return new FalloutOpeningStageTransitionGraph(
            [
                .. transitions.Transitions,
                .. sourceStages.Select((sourceStage, index) => new FalloutOpeningStageTransition(
                        "dialogue-result",
                        questEditorId,
                        sourceStage.Stage,
                        questEditorId,
                        results[index].Stage,
                        null,
                        ["sayto"])),
            ]);
    }

    private static string ReadQuestScriptSource(
        FalloutPluginStack stack,
        FalloutPluginRecord quest)
    {
        var scriptLinks = quest.ReadSubrecords().Where(value => value.Signature == "SCRI").ToArray();
        if (scriptLinks.Length != 1 || scriptLinks[0].Data.Length != sizeof(uint))
            throw Error(quest, $"must contain exactly one SCRI FormID; found {scriptLinks.Length}");
        var scriptKey = quest.Plugin.AdjustFormId(
            BinaryPrimitives.ReadUInt32LittleEndian(scriptLinks[0].Data.Span));
        var script = stack.GetEffective(scriptKey);
        if (script.Signature != "SCPT")
            throw Error(quest, $"SCRI target {scriptKey} is not SCPT");
        var sources = script.ReadSubrecords().Where(value => value.Signature == "SCTX").ToArray();
        if (sources.Length != 1)
            throw Error(script, $"must contain exactly one SCTX; found {sources.Length}");
        var bytes = sources[0].Data.Span;
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(script, "SCTX contains unsupported non-ASCII source text");
        var source = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        if (source.IndexOf('\0') >= 0)
            throw Error(script, "SCTX contains an embedded null");
        return source;
    }

    private static string ReadAsciiSource(
        FalloutPluginRecord record,
        ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(record, "SCTX contains unsupported non-ASCII source text");
        var source = Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        if (source.IndexOf('\0') >= 0)
            throw Error(record, "SCTX contains an embedded null");
        return source;
    }

    private static bool TryReadEditorId(FalloutPluginRecord record, out string editorId)
    {
        var matches = record.ReadSubrecords().Where(value => value.Signature == "EDID").ToArray();
        if (matches.Length == 0)
        {
            editorId = string.Empty;
            return false;
        }
        if (matches.Length != 1)
            throw Error(record, $"contains {matches.Length} EDID subrecords");
        var bytes = matches[0].Data.Span;
        var terminator = bytes.IndexOf((byte)0);
        if (terminator != bytes.Length - 1 ||
            bytes[..terminator].IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw Error(record, "EDID must be null-terminated ASCII");
        editorId = Encoding.ASCII.GetString(bytes[..terminator]);
        return true;
    }

    private static (string QuestEditorId, short Stage)? ParseSetStage(
        string line,
        string currentQuestEditorId)
    {
        var match = SetStageLine().Match(line);
        if (!match.Success)
            return null;
        var target = match.Groups["quest"].Value;
        var stage = short.Parse(match.Groups["stage"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (target.Equals(currentQuestEditorId, StringComparison.OrdinalIgnoreCase)
            ? currentQuestEditorId
            : target, stage);
    }

    private static short? ReadTimerTarget(
        string scriptSource,
        string questEditorId,
        short stage)
    {
        var escaped = Regex.Escape(questEditorId);
        var pattern =
            $@"getstage\s+{escaped}\s*==\s*{stage}\b" +
            $@"(?:(?!elseif\s+getstage).)*?setstage\s+{escaped}\s+(?<target>\d+)";
        var matches = Regex.Matches(
            scriptSource,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => short.Parse(
                match.Groups["target"].Value,
                System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidDataException(
                $"Native opening timer transition is ambiguous: {questEditorId}:{stage}."),
        };
    }

    private static float? ReadTimerSeconds(
        IReadOnlyList<string> code,
        string questEditorId)
    {
        var values = code.Select(line => TimerLine().Match(line))
            .Where(match => match.Success &&
                (string.IsNullOrEmpty(match.Groups["quest"].Value) ||
                 match.Groups["quest"].Value.Equals(
                     questEditorId, StringComparison.OrdinalIgnoreCase)))
            .Select(match => float.Parse(
                match.Groups["seconds"].Value,
                System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Any(value => !float.IsFinite(value) || value < 0.0f))
            throw new InvalidDataException(
                $"Native opening timer duration is invalid: {questEditorId}.");
        return values.Length == 0 ? null : values[^1];
    }

    private static IReadOnlyList<string> ReadBlockers(IReadOnlyList<string> code) =>
        BlockingCommands.Where(command => code.Any(line =>
            Regex.IsMatch(line, $@"\b{Regex.Escape(command)}\b", RegexOptions.IgnoreCase)))
            .Select(command => command.ToLowerInvariant())
            .ToArray();

    private static IEnumerable<string> CodeLines(string source)
    {
        foreach (var rawLine in source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Split(';', 2)[0].Trim();
            if (line.Length > 0)
                yield return line;
        }
    }

    private static InvalidDataException Error(FalloutPluginRecord record, string detail) =>
        new($"Native {record.Signature} {record.FormKey} {detail}.");

    [GeneratedRegex(
        @"^setstage\s+(?<quest>[A-Za-z0-9_]+)\s+(?<stage>\d+)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SetStageLine();

    [GeneratedRegex(
        @"^set\s+(?:(?<quest>[A-Za-z0-9_]+)\.)?fTimer\s+to\s+(?<seconds>\d+(?:\.\d+)?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TimerLine();

    [GeneratedRegex(
        @"^[A-Za-z0-9_]+\.sayto\s+player\s+(?<topic>[A-Za-z0-9_]+)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex SayToLine();
}
