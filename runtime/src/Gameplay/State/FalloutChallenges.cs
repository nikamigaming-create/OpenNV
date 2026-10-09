using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

// One campaign registry owns the actual progress/flags and synchronous source
// dispatch stack. Completing a challenge never manually increments another CHAL.
internal sealed partial class FalloutChallenges
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutHudNotifications _notifications;
    internal FalloutChallengeEventSource? Source { get; }
    private readonly Dictionary<FalloutFormKey, FalloutChallengeDefinition> _definitions = [];
    private readonly Dictionary<FalloutFormKey, FalloutChallengeSnapshot> _state = [];
    private readonly List<FalloutFormKey> _registrationOrder = [];
    private readonly FalloutChallengeBuckets _buckets = new();
    private readonly Stack<DispatchFrame> _executing = [];
    private DispatchFrame? _last;
    private FalloutChallengeDispatch? _restoredLast;
    private FalloutPlayerStatistics? _statistics;
    private FalloutReferenceScripts? _rewardExecutor;
    private int _counterEntry, _rewardEntry;
    private bool _retired;
    private long _events, _bucketGeneration;
    private readonly string _registrySha256;
    internal bool Restored { get; private set; }
    internal FalloutFormKey EnginePlayer => _records.RuntimeFormKey(0x14);
    internal string? Failure => _last?.Error ?? _restoredLast?.Error;
    internal string? SaveBlocker => _executing.Count != 0 ? "challenge-dispatch-attempted-prefix" : null;
    internal int ChallengesCompleted => Source is null ? 0 : (_statistics ??
        throw new NotSupportedException("Challenge completion counter has no campaign statistic owner.")).Read(Source.CompletionStatistic);

    internal FalloutChallenges(FalloutPluginStack records, FalloutHudNotifications notifications,
        FalloutChallengeEventSource? source = null)
    {
        _records = records; _notifications = notifications; Source = source; source?.Validate();
        var registry = new StringBuilder();
        foreach (var record in records.EffectiveRecordsInRegistrationOrder("CHAL"))
        {
            var definition = FalloutChallengeDefinition.Read(record);
            if (definition.Type >= 14)
                throw new NotSupportedException("CHAL event type has no original fourteen-bucket storage owner.");
            _definitions.Add(record.FormKey, definition);
            _state.Add(record.FormKey, new(record.FormKey, definition.Sha256, 0, 0));
            _registrationOrder.Add(record.FormKey);
            registry.Append(record.FormKey).Append('\0').Append(definition.Sha256).Append('\0');
        }
        _registrySha256 = FalloutAdvancementRuntimeReceipt.Hash(registry.ToString());
        RebuildBuckets();
    }
    private FalloutChallengeDefinition Definition(FalloutFormKey form)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (!_definitions.TryGetValue(form, out var definition))
            throw new InvalidDataException("Challenge form has no winning registered source definition.");
        var current = _records.GetEffective(form);
        if (current.Plugin != definition.Record.Plugin || current.HeaderOffset != definition.Record.HeaderOffset)
            throw new InvalidDataException("Challenge source winner changed during its campaign lifetime.");
        return definition;
    }
    internal FalloutChallengeSnapshot State(FalloutFormKey form) { _ = Definition(form); return _state[form]; }
    internal bool Locked(FalloutFormKey form) => Definition(form).StartDisabled && !State(form).Unlocked;
    internal void Unlock(FalloutFormKey form)
    {
        _ = Definition(form);
        RequireSource();
        _state[form] = _state[form] with { RuntimeFlags = _state[form].RuntimeFlags | 1u };
        _buckets.Unlock(_definitions[form].Type, form, _bucketGeneration);
    }
    internal void BindStatistics(FalloutPlayerStatistics statistics)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_statistics is not null || Source is null) throw new InvalidOperationException("Challenge counter binding needs one admitted campaign source.");
        Source.Require(statistics.Source); _statistics = statistics; statistics.BindChallengeOwner(this);
    }
    internal void BindRewardExecutor(FalloutReferenceScripts executor)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_executing.Count != 0 || !executor.OwnsChallengeCampaign(_records, this))
            throw new InvalidOperationException("Challenge executor is executing or belongs to another campaign.");
        _rewardExecutor = executor;
    }
    internal bool AllowsNestedStatistic(FalloutPlayerStatistics statistics) =>
        !_retired && ReferenceEquals(_statistics, statistics) && _executing.TryPeek(out var current) &&
        (_counterEntry != 0 || _rewardEntry != 0 && current.Attempts.LastOrDefault()?.Reward?.ActiveCompiledInvocation == true);
    internal FalloutStatisticChallengeReceipt ConsumeStatistic(FalloutStatisticMutation mutation, uint kind)
    {
        RequireSource();
        var statistics = _statistics ?? throw new NotSupportedException("Source CHAL11 has no actual statistic producer.");
        statistics.RequireChallengeMutation(mutation);
        if (kind != Source!.StatisticEvent || mutation.SourceSha256 != Source.StatisticSourceSha256)
            throw new InvalidDataException("Challenge event differs from the actual entered statistic mutation.");
        var result = Dispatch(kind, mutation.Delta, mutation.Index, statistic: mutation);
        return new(mutation, kind, Source.Identity, result.Ordinal);
    }
    internal void IncrementScripted(FalloutFormKey form)
    {
        if (Definition(form).Type != 13) return;
        RequireSource(); _ = Dispatch(13, 1, 0, scripted: form);
    }
    private void RequireSource()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (Source is null) throw new NotSupportedException("Challenge runtime has no reviewed selected source dispatcher.");
        if (Failure is not null) throw new InvalidOperationException("A closed challenge prefix cannot be replayed: " + Failure);
    }
    private void RebuildBuckets()
    {
        _buckets.Clear();
        _bucketGeneration = checked(_bucketGeneration + 1);
        foreach (var form in _registrationOrder)
        {
            var definition = _definitions[form];
            var state = _state[form];
            var active = !state.Completed && (!definition.StartDisabled || state.Unlocked);
            _buckets.Prepend(definition.Type, form, active, _bucketGeneration);
        }
    }
    internal void RequireReward(FalloutChallengeRewardInvocation invocation)
    {
        if (_retired || _rewardEntry == 0 || !_executing.TryPeek(out var frame) || frame.Ordinal != invocation.DispatchOrdinal ||
            frame.Attempts.LastOrDefault()?.Reward != invocation || invocation.Owner != this ||
            Definition(invocation.Challenge).Script != invocation.Program.Source.FormKey)
            throw new InvalidOperationException("Immediate challenge script has no genuine entered completion owner.");
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_executing.Count != 0) throw new InvalidOperationException("Challenge retirement reentered an actual completion prefix.");
        _retired = true; _rewardExecutor = null; _interfaceSounds = null; _buckets.Clear();
    }
    private sealed class AttemptFrame(FalloutChallengeAttempt value)
    {
        internal FalloutChallengeAttempt Value = value;
        internal FalloutChallengeRewardInvocation? Reward;
        internal FalloutChallengeAttempt Capture() => Value with { Reward = Reward?.Capture() };
    }
    private sealed class DispatchFrame(long ordinal, uint kind, int amount, ushort value1,
        FalloutStatisticMutation? statistic, FalloutFormKey? scripted, long generation)
    {
        internal long Ordinal { get; } = ordinal;
        internal long ThroughOrdinal = ordinal;
        internal readonly List<AttemptFrame> Attempts = [];
        internal readonly List<DispatchFrame> Children = [];
        internal readonly List<FalloutChallengeCursorObservation> Traversal = [];
        internal long BucketGenerationAfter;
        internal bool Complete, RebuildEntered;
        internal string? FailureType, Error;
        internal FalloutChallengeDispatch Capture(long? through = null) => new(Ordinal, through ?? ThroughOrdinal,
            kind, amount, value1, 0, 0, null, null, statistic, scripted, generation,
            Attempts.Select(attempt => attempt.Capture()).ToArray(), Children.Select(child => child.Capture()).ToArray(),
            Complete, RebuildEntered, FailureType, Error)
        { Traversal = Traversal.ToArray(), BucketGenerationAfter = BucketGenerationAfter };
    }
}
