using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutQuestSnapshot(FalloutFormKey Quest, short Stage, bool Completed,
    IReadOnlyList<short> EnteredStages, IReadOnlyDictionary<uint, double> Variables);

/// <summary>Authoritative new-game quest values; observations never populate this owner.</summary>
internal sealed class FalloutQuestState(FalloutPluginStack stack)
{
    private sealed class State
    {
        internal short Stage;
        internal bool Completed;
        internal readonly HashSet<short> Stages = [];
        internal readonly Dictionary<uint, double> Variables = [];
    }

    private readonly Dictionary<FalloutFormKey, State> _states = [];
    internal long Revision { get; private set; }

    internal IReadOnlyList<FalloutQuestSnapshot> Capture() => _states.OrderBy(pair => stack.RuntimeFormId(pair.Key))
        .Select(pair => new FalloutQuestSnapshot(pair.Key, pair.Value.Stage, pair.Value.Completed,
            pair.Value.Stages.Order().ToArray(), new Dictionary<uint, double>(pair.Value.Variables))).ToArray();

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
            state.Stage = snapshot.Stage;
            state.Completed = snapshot.Completed;
            state.Stages.UnionWith(snapshot.EnteredStages);
            foreach (var (key, value) in snapshot.Variables) state.Variables[key] = value;
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
        var scripts = record.ReadSubrecords().Where(field => field.Signature == "SCRI").ToArray();
        if (scripts.Length != 0)
        {
            var script = stack.GetEffective(FalloutDialogueTopic.RequiredForm(record, "SCRI"));
            if (script.Signature != "SCPT") throw new InvalidDataException("Quest script target is not SCPT.");
            foreach (var field in script.ReadSubrecords().Where(field => field.Signature == "SLSD"))
            {
                if (field.Data.Length != 24) throw new InvalidDataException("Quest variable declaration has an invalid extent.");
                var index = BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span);
                if (!state.Variables.TryAdd(index, 0)) throw new InvalidDataException("Duplicate quest variable declaration.");
            }
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
