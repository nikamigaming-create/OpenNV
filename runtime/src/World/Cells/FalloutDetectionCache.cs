using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutDetectionDirection { Detected, Detecting }
internal enum FalloutDetectionCommitPhase { Idle, NotifyCombatController, Detecting }

internal sealed record FalloutDetectionObservation(float[] Position, float Seconds)
{
    internal FalloutDetectionObservation Copy() => this with { Position = Position.ToArray() };
}

// LastObservation == null represents the source's never-observed time sentinel.
// A nonpositive score does not erase the last known positive observation.
internal sealed record FalloutDetectionCacheEntry(FalloutFormKey Actor, int Level, int Score,
    bool Visible, bool VisibilityBeforeCone, bool ActorInCombat, FalloutDetectionDirection Direction,
    int SoundScore, FalloutDetectionObservation? LastObservation, long ProducerWrite)
{
    internal FalloutDetectionCacheEntry Copy() => this with { LastObservation = LastObservation?.Copy() };
}

internal sealed record FalloutDetectionPendingReceipt(FalloutFormKey Receiver, FalloutFormKey Actor,
    FalloutDetectionDirection Direction, long ProducerWrite);

internal sealed record FalloutDetectionCacheSnapshot(string Schema, FalloutFormKey Owner,
    FalloutDetectionProcessLevel Process, long Revision, FalloutDetectionCommitPhase CommitPhase,
    IReadOnlyList<FalloutDetectionCacheEntry> Detected, IReadOnlyList<FalloutDetectionCacheEntry> Detecting,
    IReadOnlyList<FalloutDetectionCacheEntry> PendingDetected, IReadOnlyList<FalloutDetectionCacheEntry> PendingDetecting,
    long CompletedCommits, long CombatNotificationRevision);

// Pure first-party process-cache owner. Process activation, sensory calculation,
// native 3D and controller publication must be bound by their independent owners.
// Constructing this component does not admit a native actor as a HighProcess.
internal sealed partial class FalloutDetectionCache
{
    internal const string SnapshotSchema = "opennv-directional-detection-cache/v1";
    private readonly FalloutFormKey _owner;
    private readonly FalloutDetectionProcessLevel _process;
    private readonly Func<FalloutFormKey, bool> _actor;
    private readonly int _actorBound;
    private List<FalloutDetectionCacheEntry> _detected = [];
    private List<FalloutDetectionCacheEntry> _detecting = [];
    private List<FalloutDetectionCacheEntry> _pendingDetected = [];
    private List<FalloutDetectionCacheEntry> _pendingDetecting = [];
    private long _revision;
    private long _completedCommits;
    private long _combatNotificationRevision;
    private FalloutDetectionCommitPhase _commitPhase;

    internal FalloutFormKey Owner => _owner;
    internal FalloutDetectionProcessLevel Process => _process;
    internal FalloutDetectionCommitPhase CommitPhase => _commitPhase;
    internal bool HasPending => _pendingDetected.Count != 0 || _pendingDetecting.Count != 0 || _commitPhase != FalloutDetectionCommitPhase.Idle;

    internal FalloutDetectionCache(FalloutFormKey owner, FalloutDetectionProcessLevel process,
        int sourceActorBound, Func<FalloutFormKey, bool> actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor(owner) || !Enum.IsDefined(process) || sourceActorBound < 1)
            throw new InvalidDataException("Detection cache has an invalid actor, process or finite source domain.");
        _owner = owner; _process = process; _actorBound = sourceActorBound;
        _actor = actor;
    }

    internal FalloutDetectionCacheEntry? Read(FalloutFormKey actor, FalloutDetectionDirection direction)
    {
        RequireActor(actor); RequireDirection(direction);
        if (_process != FalloutDetectionProcessLevel.High) return null;
        return Committed(direction).FirstOrDefault(entry => entry.Actor == actor)?.Copy();
    }

    internal FalloutDetectionCacheEntry? Stage(FalloutFormKey actor, int level, int score,
        bool visible, bool visibilityBeforeCone, bool actorInCombat, FalloutDetectionDirection direction,
        float[] sourcePosition, float simulationSeconds)
    {
        RequireActor(actor); RequireDirection(direction);
        if (_commitPhase != FalloutDetectionCommitPhase.Idle)
            throw new NotSupportedException("Detection staging cannot overwrite an unfinished commit notification.");
        // These classes have an actual null getter/no-op staging implementation.
        // Unknown process classes must never be supplied as one of these classes.
        if (_process != FalloutDetectionProcessLevel.High) return null;
        if (!float.IsFinite(simulationSeconds) || simulationSeconds < 0 || sourcePosition is not { Length: 3 } || sourcePosition.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Detection staging has no finite authoritative source pose or clock.");
        var pending = Pending(direction);
        var observation = score > 0 ? new FalloutDetectionObservation(sourcePosition.ToArray(), simulationSeconds) : null;
        var revision = checked(_revision + 1);
        var entry = new FalloutDetectionCacheEntry(actor, level, score, visible, visibilityBeforeCone,
            actorInCombat, direction, -100, observation, revision);
        // The source list insertion pushes a new head; do not replace an older
        // pending write for the same placed identity.
        pending.Insert(0, entry);
        _revision = revision;
        return entry.Copy();
    }

    // The producer can refine its just-staged sound/visibility/observation.
    // It cannot write the committed list through this operation.
    internal FalloutDetectionPendingReceipt PendingReceipt(FalloutDetectionCacheEntry entry) =>
        new(_owner, entry.Actor, entry.Direction, entry.ProducerWrite);

    internal void RefinePending(FalloutDetectionPendingReceipt receipt,
        int score, bool visible, int soundScore, FalloutDetectionObservation? observation)
    {
        RequireDirection(receipt.Direction);
        var pending = Pending(receipt.Direction);
        var pendingHead = pending.FindIndex(entry => entry.Actor == receipt.Actor && entry.ProducerWrite == receipt.ProducerWrite);
        if (_process != FalloutDetectionProcessLevel.High || _commitPhase != FalloutDetectionCommitPhase.Idle ||
            receipt.Receiver != _owner || pendingHead < 0)
            throw new InvalidDataException("Detection refinement has no matching unfinished producer entry.");
        if (observation is not null) ValidateObservation(observation, float.MaxValue);
        if (score > 0 && observation is null && pending[pendingHead].LastObservation is null)
            throw new InvalidDataException("Positive refined detection lost its actual source observation.");
        var revision = checked(_revision + 1);
        pending[pendingHead] = pending[pendingHead] with
        {
            Score = score, Visible = visible, SoundScore = soundScore,
            LastObservation = observation?.Copy() ?? pending[pendingHead].LastObservation
        };
        _revision = revision;
    }

    // Return true only for the real source notification boundary. The caller
    // must resolve the actual controller and apply its detection-updated flag,
    // or retain this unfinished phase; no opaque callback is stored in a save.
    internal bool BeginCommit()
    {
        if (_commitPhase != FalloutDetectionCommitPhase.Idle)
            throw new InvalidOperationException("Detection commit already has an unfinished phase.");
        if (_process != FalloutDetectionProcessLevel.High) return false;
        var wroteDetected = _pendingDetected.Count != 0;
        var detected = MergePending(_pendingDetected, _detected);
        var detecting = MergePending(_pendingDetecting, _detecting);
        var revision = checked(_revision + 1);
        var completed = wroteDetected ? _completedCommits : checked(_completedCommits + 1);
        _detected = detected;
        _pendingDetected.Clear();
        if (wroteDetected)
        {
            _commitPhase = FalloutDetectionCommitPhase.NotifyCombatController;
            _revision = revision;
            return true;
        }
        _detecting = detecting;
        _pendingDetecting.Clear();
        _completedCommits = completed;
        _revision = revision;
        return false;
    }

    internal void CompleteCombatNotification(bool sourceControllerPresent, bool detectionUpdatedFlagApplied)
    {
        if (_commitPhase != FalloutDetectionCommitPhase.NotifyCombatController ||
            sourceControllerPresent != detectionUpdatedFlagApplied)
            throw new InvalidDataException("Detection commit lacks the exact controller notification outcome.");
        // Known absent is independently admitted by the controller owner. An
        // unknown controller may not call this method with false.
        var detecting = MergePending(_pendingDetecting, _detecting);
        var notification = checked(_combatNotificationRevision + 1);
        var revision = checked(_revision + 1);
        var completed = checked(_completedCommits + 1);
        _detecting = detecting;
        _pendingDetecting.Clear();
        _commitPhase = FalloutDetectionCommitPhase.Idle;
        _completedCommits = completed;
        _combatNotificationRevision = notification;
        _revision = revision;
    }

    private List<FalloutDetectionCacheEntry> MergePending(List<FalloutDetectionCacheEntry> pending, List<FalloutDetectionCacheEntry> previous)
    {
        var committed = previous.Select(entry => entry.Copy()).ToList();
        foreach (var entry in pending)
        {
            var index = committed.FindIndex(value => value.Actor == entry.Actor);
            if (index >= 0)
                committed[index] = entry.Copy() with
                {
                    LastObservation = entry.LastObservation?.Copy() ?? committed[index].LastObservation?.Copy()
                };
            else
            {
                if (committed.Count >= _actorBound)
                    throw new InvalidDataException("Detection cache exceeds the selected source actor domain.");
                committed.Insert(0, entry.Copy());
            }
        }
        return committed;
    }

    internal FalloutDetectionCacheSnapshot Capture() => new(SnapshotSchema, _owner, _process, _revision,
        _commitPhase, Copy(_detected), Copy(_detecting), Copy(_pendingDetected), Copy(_pendingDetecting),
        _completedCommits, _combatNotificationRevision);

    internal void Restore(FalloutDetectionCacheSnapshot snapshot, float simulationSeconds)
    {
        if (snapshot.Schema != SnapshotSchema || snapshot.Owner != _owner || snapshot.Process != _process ||
            !Enum.IsDefined(snapshot.CommitPhase) || snapshot.Revision < 0 || snapshot.CompletedCommits < 0 ||
            snapshot.CombatNotificationRevision < 0 || snapshot.CompletedCommits > snapshot.Revision ||
            snapshot.CombatNotificationRevision > snapshot.Revision || !float.IsFinite(simulationSeconds) || simulationSeconds < 0)
            throw new InvalidDataException("Detection snapshot has incompatible source/process identity or chronology.");
        var writes = new HashSet<long>();
        var detected = ValidateList(snapshot.Detected, FalloutDetectionDirection.Detected, false, simulationSeconds, snapshot.Revision, writes);
        var detecting = ValidateList(snapshot.Detecting, FalloutDetectionDirection.Detecting, false, simulationSeconds, snapshot.Revision, writes);
        var pendingDetected = ValidateList(snapshot.PendingDetected, FalloutDetectionDirection.Detected, true, simulationSeconds, snapshot.Revision, writes);
        var pendingDetecting = ValidateList(snapshot.PendingDetecting, FalloutDetectionDirection.Detecting, true, simulationSeconds, snapshot.Revision, writes);
        if (_process != FalloutDetectionProcessLevel.High && (detected.Count != 0 || detecting.Count != 0 ||
            pendingDetected.Count != 0 || pendingDetecting.Count != 0 || snapshot.CommitPhase != FalloutDetectionCommitPhase.Idle ||
            snapshot.CompletedCommits != 0 || snapshot.CombatNotificationRevision != 0) ||
            snapshot.CommitPhase == FalloutDetectionCommitPhase.Detecting ||
            snapshot.CommitPhase == FalloutDetectionCommitPhase.NotifyCombatController &&
                (pendingDetected.Count != 0 || detected.Count == 0 || snapshot.Revision == 0))
            throw new InvalidDataException("Detection snapshot contains an impossible process/commit phase.");
        // Validate the whole receipt before replacing any live state.
        _detected = detected; _detecting = detecting;
        _pendingDetected = pendingDetected; _pendingDetecting = pendingDetecting;
        _revision = snapshot.Revision; _completedCommits = snapshot.CompletedCommits;
        _combatNotificationRevision = snapshot.CombatNotificationRevision; _commitPhase = snapshot.CommitPhase;
    }

    private List<FalloutDetectionCacheEntry> ValidateList(IReadOnlyList<FalloutDetectionCacheEntry>? entries,
        FalloutDetectionDirection direction, bool pending, float simulationSeconds, long revision, HashSet<long> writes)
    {
        if (entries is null || !pending && entries.Count > _actorBound)
            throw new InvalidDataException("Saved detection entries exceed their finite producer/actor domain.");
        var identities = new HashSet<FalloutFormKey>();
        var result = new List<FalloutDetectionCacheEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry is null || !_actor(entry.Actor) || entry.Direction != direction ||
                entry.ProducerWrite <= 0 || entry.ProducerWrite > revision || !writes.Add(entry.ProducerWrite) ||
                !pending && !identities.Add(entry.Actor))
                throw new InvalidDataException("Saved detection entry has an invalid placed actor, direction or duplicate committed identity.");
            if (entry.Score > 0 && entry.LastObservation is null)
                throw new InvalidDataException("Positive detection entry lost its actual source observation.");
            if (entry.LastObservation is not null) ValidateObservation(entry.LastObservation, simulationSeconds);
            result.Add(entry.Copy());
        }
        return result;
    }

    private static void ValidateObservation(FalloutDetectionObservation observation, float simulationSeconds)
    {
        if (observation.Position is not { Length: 3 } || observation.Position.Any(value => !float.IsFinite(value)) ||
            !float.IsFinite(observation.Seconds) || observation.Seconds < 0 || observation.Seconds > simulationSeconds)
            throw new InvalidDataException("Saved detection observation has no finite source position/time.");
    }
    private void RequireActor(FalloutFormKey actor)
    {
        if (!_actor(actor)) throw new InvalidDataException("Detection cache target is not a selected source actor.");
    }
    private static void RequireDirection(FalloutDetectionDirection direction)
    {
        if (!Enum.IsDefined(direction)) throw new InvalidDataException("Detection direction is invalid.");
    }
    private List<FalloutDetectionCacheEntry> Committed(FalloutDetectionDirection direction) =>
        direction == FalloutDetectionDirection.Detected ? _detected : _detecting;
    private List<FalloutDetectionCacheEntry> Pending(FalloutDetectionDirection direction) =>
        direction == FalloutDetectionDirection.Detected ? _pendingDetected : _pendingDetecting;
    private static FalloutDetectionCacheEntry[] Copy(IEnumerable<FalloutDetectionCacheEntry> values) => values.Select(value => value.Copy()).ToArray();
}
