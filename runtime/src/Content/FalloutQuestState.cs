using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuestSnapshot(FalloutFormKey Quest, short Stage, bool Completed,
    IReadOnlyList<short> EnteredStages, IReadOnlyDictionary<uint, double> Variables,
    IReadOnlyList<FalloutQuestObjectiveSnapshot>? Objectives = null);

internal sealed record FalloutQuestObjectiveSnapshot(uint Index, bool Displayed, bool Completed);
internal sealed record FalloutQuestObjectiveCommand(string QuestEditorId, uint Index, bool Display, bool Value);
internal sealed record FalloutQuestObjectiveChange(FalloutFormKey Quest, string Text,
    FalloutQuestObjectiveSnapshot Before, FalloutQuestObjectiveSnapshot After, long Revision);

/// <summary>Authoritative new-game quest values; observations never populate this owner.</summary>
internal sealed class FalloutQuestState(FalloutPluginStack stack)
{
    private sealed class State
    {
        internal short Stage;
        internal bool Completed;
        internal readonly HashSet<short> Stages = [];
        internal readonly Dictionary<uint, double> Variables = [];
        internal readonly Dictionary<uint, string> ObjectiveText = [];
        internal readonly Dictionary<uint, FalloutQuestObjectiveSnapshot> Objectives = [];
    }

    private readonly Dictionary<FalloutFormKey, State> _states = [];
    internal long Revision { get; private set; }
    internal event Action<FalloutQuestObjectiveChange>? ObjectiveChanged;

    internal IReadOnlyList<FalloutQuestSnapshot> Capture() => _states.OrderBy(pair => stack.RuntimeFormId(pair.Key))
        .Select(pair => new FalloutQuestSnapshot(pair.Key, pair.Value.Stage, pair.Value.Completed,
            pair.Value.Stages.Order().ToArray(), new Dictionary<uint, double>(pair.Value.Variables),
            pair.Value.Objectives.Values.OrderBy(value => value.Index).ToArray())).ToArray();

    internal object ObjectiveState => new
    {
        revision = Revision,
        quests = _states.Where(pair => pair.Value.Objectives.Count != 0).Select(pair => new
        {
            quest = pair.Key.ToString(),
            objectives = pair.Value.Objectives.Values.OrderBy(value => value.Index).Select(value => new
            {
                value.Index,
                text = pair.Value.ObjectiveText[value.Index],
                value.Displayed,
                value.Completed,
            }).ToArray(),
        }).ToArray(),
        presentation = "unbound",
        targets = "unbound",
    };

    internal void Restore(IReadOnlyList<FalloutQuestSnapshot> snapshots)
    {
        if (Revision != 0) throw new InvalidOperationException("Quest restoration requires an unmodified owner.");
        var validated = new FalloutQuestState(stack);
        foreach (var snapshot in snapshots)
        {
            if (validated._states.ContainsKey(snapshot.Quest) || snapshot.EnteredStages.Distinct().Count() != snapshot.EnteredStages.Count ||
                snapshot.Stage < 0 || snapshot.EnteredStages.Any(stage => stage < 0 || stage > snapshot.Stage) ||
                snapshot.Variables.Values.Any(value => !double.IsFinite(value)))
                throw new InvalidDataException("Saved quest state is invalid or duplicated.");
            var state = validated.Require(snapshot.Quest);
            if (!state.Variables.Keys.Order().SequenceEqual(snapshot.Variables.Keys.Order()))
                throw new InvalidDataException("Saved quest variables differ from the winning script declarations.");
            if (snapshot.Objectives is null && state.Objectives.Count != 0)
                throw new InvalidDataException("Saved quest objectives are absent; their state cannot be inferred from quest stages.");
            var objectives = snapshot.Objectives ?? [];
            if (!state.Objectives.Keys.Order().SequenceEqual(objectives.Select(value => value.Index).Order()))
                throw new InvalidDataException("Saved quest objectives differ from the winning declarations or contain duplicates.");
            state.Stage = snapshot.Stage;
            state.Completed = snapshot.Completed;
            state.Stages.UnionWith(snapshot.EnteredStages);
            foreach (var (key, value) in snapshot.Variables) state.Variables[key] = value;
            foreach (var objective in objectives) state.Objectives[objective.Index] = objective;
        }
        _states.Clear();
        foreach (var (quest, state) in validated._states) _states.Add(quest, state);
        Revision++;
    }

    private State Require(FalloutFormKey quest)
    {
        if (_states.TryGetValue(quest, out var state)) return state;
        var record = stack.GetEffective(quest);
        if (record.Signature != "QUST") throw new InvalidDataException($"Quest state target {quest} is not QUST.");
        state = new();
        uint? objective = null;
        var textIndices = new HashSet<uint>();
        foreach (var field in record.ReadSubrecords())
        {
            if (field.Signature == "QOBJ")
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Quest objective index extent is invalid.");
                objective = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                if (!state.Objectives.TryAdd(objective.Value, new(objective.Value, false, false)))
                    throw new InvalidDataException("Duplicate source quest objective index.");
                state.ObjectiveText.Add(objective.Value, "");
            }
            else if (field.Signature == "NNAM")
            {
                if (objective is not { } index || !textIndices.Add(index))
                    throw new InvalidDataException("Quest objective text has no unique source index.");
                state.ObjectiveText[index] = FalloutDialogueTopic.Text(field.Data.Span);
            }
        }
        var scripts = record.ReadSubrecords().Where(field => field.Signature == "SCRI").ToArray();
        if (scripts.Length != 0)
        {
            var script = stack.GetEffective(FalloutDialogueTopic.RequiredForm(record, "SCRI"));
            if (script.Signature != "SCPT") throw new InvalidDataException("Quest script target is not SCPT.");
            foreach (var index in FalloutScriptLocals.Read(script).Values) state.Variables.Add(index, 0);
        }
        _states.Add(quest, state);
        return state;
    }

    internal void EnterStage(FalloutFormKey quest, short stage)
    {
        var state = Require(quest);
        state.Stage = Math.Max(state.Stage, stage);
        state.Stages.Add(stage);
        Revision++;
    }

    internal void Complete(FalloutFormKey quest)
    {
        var state = Require(quest);
        if (state.Completed) return;
        state.Completed = true;
        Revision++;
    }

    internal static IReadOnlyList<FalloutQuestObjectiveCommand> ReadObjectiveCommands(string source)
    {
        var lines = FalloutDialogueTopic.CodeLines(source).ToArray();
        var commands = new List<FalloutQuestObjectiveCommand>();
        foreach (var line in lines)
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var display = words[0].Equals("SetObjectiveDisplayed", StringComparison.OrdinalIgnoreCase);
            if (!display && !words[0].Equals("SetObjectiveCompleted", StringComparison.OrdinalIgnoreCase)) continue;
            if (words.Length != 4 || !uint.TryParse(words[2], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var index) || words[3] is not ("0" or "1"))
                throw new NotSupportedException($"Quest objective command arguments are unbound: {line}");
            commands.Add(new(words[1], index, display, words[3] == "1"));
        }
        if (commands.Count != 0 && lines.Any(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0]
                .ToLowerInvariant() is "if" or "elseif" or "else" or "endif"))
            throw new NotSupportedException("Conditional quest-objective commands require their script control-flow owner.");
        return commands;
    }

    internal void ApplyObjective(FalloutQuestObjectiveCommand command)
    {
        var quest = FalloutDialogueTopic.Find(stack, "QUST", command.QuestEditorId).FormKey;
        ApplyObjective(quest, command.Index, command.Display, command.Value);
    }

    internal void ApplyObjective(FalloutFormKey quest, uint index, bool display, bool value)
    {
        var state = Require(quest);
        if (!state.Objectives.TryGetValue(index, out var before))
            throw new NotSupportedException($"Quest {quest} has no declared objective {index}.");
        var after = display ? before with { Displayed = value } : before with { Completed = value };
        if (after == before) return;
        state.Objectives[index] = after;
        Revision++;
        ObjectiveChanged?.Invoke(new(quest, state.ObjectiveText[index], before, after, Revision));
    }

    internal short Stage(FalloutFormKey quest) => Require(quest).Stage;

    internal object VariableState => _states.Where(pair => pair.Value.Variables.Count != 0).Select(pair => new
    {
        quest = pair.Key.ToString(),
        variables = pair.Value.Variables.Select(variable => new
        {
            index = variable.Key,
            value = variable.Value,
            bits = BitConverter.DoubleToInt64Bits(variable.Value).ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
        }).ToArray(),
    }).ToArray();

    internal FalloutQuestObjectiveSnapshot Objective(FalloutFormKey quest, uint index) =>
        Require(quest).Objectives.TryGetValue(index, out var objective) ? objective :
            throw new NotSupportedException($"Quest {quest} has no declared objective {index}.");

    internal double Variable(FalloutFormKey quest, uint index) => Require(quest).Variables.TryGetValue(index, out var value)
        ? value : throw new NotSupportedException($"Quest {quest} has no declared variable {index}.");

    internal void SetVariable(FalloutFormKey quest, uint index, double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Non-finite quest variable.");
        var previous = Variable(quest, index);
        if (previous == value) return;
        Require(quest).Variables[index] = value;
        Revision++;
    }

    internal float Evaluate(FalloutCondition condition)
    {
        var state = Require(condition.FormArgument1);
        return condition.Function switch
        {
            58 => state.Stage,
            59 => state.Stages.Contains(checked((short)condition.Argument2)) ? 1 : 0,
            79 => state.Variables.TryGetValue(condition.Argument2, out var value) ? (float)value :
                throw new NotSupportedException($"Quest {condition.FormArgument1} has no declared variable {condition.Argument2}."),
            546 => state.Completed ? 1 : 0,
            _ => throw new NotSupportedException($"Quest condition {condition.Function} has no runtime owner."),
        };
    }
}
