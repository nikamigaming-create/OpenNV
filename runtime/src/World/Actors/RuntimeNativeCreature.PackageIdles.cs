using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutScriptPackage? _collectionSource;
    private FalloutIdleCollectionPlayback? _collection;
    private FalloutIdleConditions? _collectionConditions;
    private readonly FalloutIdleReplayState _collectionReplays = new();
    private RuntimeNativeNifAnimation? _collectionAnimation;
    private FalloutIdleAnimationPlayback? _collectionClock;
    private FalloutActorIdleSource? _collectionIdle;
    private string? _collectionMediaSha256, _collectionFailure;
    private long _collectionRevision;
    private Func<FalloutActorPackageCollectionContinuation?>? _collectionCapture;

    private void BindCollectionCapture()
    {
        _collectionConditions = new(_aiRecords!);
        _aiState!.CapturePackageCollection = _collectionCapture = CaptureCollection;
        if (_aiState.PackageCollection is { } saved)
        {
            if (_aiState.PackageAssignment is not { } assignment || assignment.Package != saved.IdleState.Package)
                throw new InvalidDataException("Cold creature collection lost its actual package assignment.");
            saved.Validate(_aiRecords!, assignment.Package);
            var election = saved.Election ?? throw new InvalidDataException("Cold creature collection lost its actual source election clock.");
            if (_packageEvents!.Active is null) assignment.Bind(_aiRecords!, _packageEvents);
            election.RestoreHistory(_packageEvents);
            Activity.Restore(election.Activity);
            _packageClock = election.PollRemaining; _aiScheduleTime = election.ScheduleTime;
            _aiQuestRevision = election.QuestRevision; _evaluateRequested = election.EvaluateRequested;
        }
    }

    private void BeginCollection(FalloutScriptPackage? source, bool restored)
    {
        _collectionAnimation = null; _collectionClock = null; _collectionIdle = null;
        _collectionMediaSha256 = null; _collectionFailure = null;
        _collectionSource = source;
        _collection = source is null ? null : new(source, _collectionReplays,
            idle => _collectionConditions!.AllPass(idle, PackageCondition), _aiState!.SoundRandom.NextBounded);
        if (!restored) { _aiState!.PackageCollection = null; return; }
        if (_aiState!.PackageCollection is not { } saved) return;
        if (source is null) throw new InvalidDataException("Cold creature collection lost its source package.");
        saved.Validate(_aiRecords!, source.Form);
        _collection!.Restore(saved.IdleState.Collection);
        _collectionReplays.Restore(saved.IdleState.Cooldowns.ToDictionary(value => value.Idle, value => value.Remaining));
        _collectionRevision = saved.AnimationRevision;
        _collectionFailure = saved.IdleState.Error;
        if (saved.IdleState.ActiveAnimation is { } active) StartCollectionIdle(active.Idle, active);
    }

    private void StartCollectionIdle(FalloutFormKey form, FalloutActorPackageIdleAnimation? saved = null,
        FalloutSandboxNativeIdleContinuation? nativeChild = null)
    {
        var record = _aiRecords!.GetEffective(form);
        var idle = FalloutActorIdleSource.Resolve(_aiRecords, record);
        var data = FalloutIdleAnimationData.Read(record);
        var owned = ReadIdle(idle);
        var animation = owned.Animation;
        var sequence = animation.Sequence;
        if (saved is not null)
        {
            if (nativeChild is null) saved.Validate(_aiRecords, _collectionSource!.Form);
            else
            {
                nativeChild.ValidateSource(_aiRecords, _aiWorld!, Appearance.Reference!.Value);
                if (nativeChild.Animation != saved) throw new InvalidDataException("Cold creature KF differs from its actual retained IDLM child.");
            }
            if (!saved.Sha256.Equals(owned.Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cold creature collection KF differs from its winning resource.");
        }
        var repeats = saved?.Clock.SelectedAdditionalLoops ?? data.SelectAdditionalLoops(_aiState!.SoundRandom.NextBounded);
        var clock = new FalloutIdleAnimationPlayback(sequence.StartTime, sequence.StopTime, sequence.Frequency,
            sequence.CycleType, animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), repeats);
        var basis = saved is not null && (nativeChild?.UsesPackageBase ?? _aiState!.PackageCollection?.UsesPackageBase) == true
            ? (Combat ?? throw new NotSupportedException("Cold collection has no actual package base owner."))
                .RestoredCollectionBase(_collectionSource!.Form)
            : CollectionBaseLayer();
        if (saved is not null)
        {
            clock.Restore(saved.Clock);
            RestoreCollectionResidualPose(saved.ResidualPose!, animation, basis.Animation);
        }
        else
        {
            if (_collectionRevision == long.MaxValue - 1) throw new InvalidOperationException("Creature collection revision exhausted.");
            ++_collectionRevision;
            _collectionReplays.Started(form, data.ReplayDelaySeconds);
        }
        _collectionIdle = idle; _collectionMediaSha256 = owned.Hash;
        _collectionAnimation = animation; _collectionClock = clock;
        RuntimeNativeNifAnimation.ApplyLayers((basis.Animation, basis.Seconds), (animation, clock.SourceSeconds));
    }

    private void AdvanceCollectionReplayClock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _collectionReplays.Advance((float)seconds);
    }

    private bool AdvanceCollectionIdle(double seconds)
    {
        if (_collectionSource is null || _collection is null || _collectionFailure is not null || _aiError is not null) return false;
        if (_eventIdleClock is not null || Combat?.PackageMoving == true || _travelProgress is { Complete: false } || _guardProgress is { Complete: false } ||
            _followPackage is not null && _aiState!.PackageMotion?.Follow?.Target != _followPackage.Target ||
            _sandboxSource is not null && _sandbox?.AtLocation != true || _dialoguePackage is not null && !_dialogueRequested) return false;
        if (_sandboxNativeAction is not null && !_sandboxNativeRetired &&
            (_sandboxMarkerCollection is null || !_sandboxMarkerAtLocation || _sandboxNativeRetiring)) return false;
        var activeCollection = _sandboxMarkerCollection ?? _collection;
        var participated = false;
        while (true)
        {
            if (_collectionClock is null)
            {
                if (_sandboxMarkerCollection is null && (_collectionSource.Flags & 0x01000000) != 0) return false;
                seconds = activeCollection.AdvanceWait(seconds);
                FalloutFormKey? selected;
                try { selected = activeCollection.Select(); }
                catch (Exception error) { _collectionFailure ??= error.Message; throw; }
                if (selected is not { } idle) return participated;
                // Select has consumed its source cursor and random draw before any
                // resource or native channel can fail. A failed suffix cannot retry.
                try { StartCollectionIdle(idle); }
                catch (Exception error) { _collectionFailure ??= error.Message; throw; }
            }
            var animation = _collectionAnimation!; var clock = _collectionClock!;
            try
            {
                seconds = clock.Advance(seconds, interval =>
                {
                    animation.ApplySourceTime(interval.To);
                    foreach (var key in animation.TextKeys.Where(key => key.Time <= interval.To &&
                        (key.Time > interval.From || interval.IncludeFrom && key.Time == interval.From)))
                        foreach (var value in key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
                        {
                            var structural = value.Equals("start", StringComparison.OrdinalIgnoreCase) || value.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                                value.Equals("StartLoop", StringComparison.OrdinalIgnoreCase) || value.Equals("EndLoop", StringComparison.OrdinalIgnoreCase);
                            var disposition = structural ? "source-phase-owner" : _sounds.Dispatch(value);
                            _lastEvent = new { ordinal = ++_eventCount, idle = _collectionIdle!.Form.ToString(), key.Time, key = value, disposition };
                            if (disposition.Contains("unbound", StringComparison.Ordinal))
                                throw new NotSupportedException("Creature collection source key is unbound: " + value);
                        }
                });
                participated = true;
                ApplyCollectionLayers(animation, clock.SourceSeconds);
                if (clock.Complete)
                {
                    activeCollection.Finish();
                    _collectionAnimation = null; _collectionClock = null; _collectionIdle = null;
                    _collectionMediaSha256 = null;
                }
                if (!clock.Complete || seconds <= 0) return true;
            }
            catch (Exception error) { _collectionFailure ??= error.Message; throw; }
        }
    }

    private FalloutActorPackageCollectionContinuation? CaptureCollection()
    {
        var nativeChild = _sandboxNativeAction is not null && !_sandboxNativeRetired;
        if (nativeChild) _ = CaptureSandboxNativeIdle(_sandboxNativeAction!);
        if (_collectionSource is null || _collection is null)
        {
            if (_collectionReplays.Remaining.Count != 0)
                throw new NotSupportedException("Unassigned creature replay delays require their independent actor-wide cold owner.");
            return _aiState?.PackageCollection?.Copy();
        }
        if (_collectionFailure is not null || _aiState!.ScriptError is not null || Error is not null)
            throw new NotSupportedException("Creature collection retains an entered failed suffix: " + (_collectionFailure ?? _aiState?.ScriptError ?? Error));
        FalloutActorPackageIdleAnimation? active = null;
        if (_collectionClock is { Complete: false } clock && !nativeChild)
        {
            if (_eventIdleClock is not null || _collectionAnimation is null || _collectionIdle is null || _collectionMediaSha256 is null)
                throw new NotSupportedException("Creature collection has an overlapping or unbound animation owner.");
            active = new(_collectionIdle.Form, Hash(_aiRecords!.GetEffective(_collectionIdle.Form).ReadData()),
                _collectionIdle.AnimationPath, _collectionMediaSha256, clock.Capture(), _collectionRevision,
                CaptureCollectionResidualPose(_collectionAnimation));
        }
        var idles = new FalloutActorPackageIdleState(_collectionSource.Form,
            Hash(_aiRecords!.GetEffective(_collectionSource.Form).ReadData()), _collection.Capture(),
            _collectionReplays.Remaining.Select(value => new FalloutIdleReplayCooldown(value.Key,
                Hash(_aiRecords.GetEffective(value.Key).ReadData()), value.Value)).ToArray(), null, active);
        var lifecycle = _packageEvents ?? throw new NotSupportedException("Creature collection lost its package lifecycle.");
        var election = new FalloutFollowElection(_packageClock, _aiScheduleTime, _aiQuestRevision, null,
            _evaluateRequested, Activity.Capture(), lifecycle.Revision, lifecycle.LastEvent, lifecycle.LastPackage);
        var saved = new FalloutActorPackageCollectionContinuation(idles, _collectionRevision, election,
            UsesPackageBase: active is not null && Combat?.PackageOwnsPose == true);
        saved.Validate(_aiRecords, _collectionSource.Form);
        return saved;
    }

    private (RuntimeNativeNifAnimation Animation, float Seconds) CollectionBaseLayer()
    {
        if (Combat?.PackageOwnsPose != true) return (_animation, SourceSeconds);
        return Combat.PackageBaseLayer ?? throw new NotSupportedException("Creature collection has no actual published package base layer.");
    }

    private void ApplyCollectionLayers(RuntimeNativeNifAnimation overlay, float seconds)
    { var basis = CollectionBaseLayer(); RuntimeNativeNifAnimation.ApplyLayers((basis.Animation, basis.Seconds), (overlay, seconds)); }

    private void RetainCollection()
    {
        if (_aiState is not { } state || !ReferenceEquals(state.CapturePackageCollection, _collectionCapture)) return;
        try { state.PackageCollection = CaptureCollection(); }
        catch (Exception error)
        {
            state.ProcedureCaptureBlocker ??= "Creature collection retirement lacks continuation: " + error.Message;
            GD.PushError($"OPENNV_CREATURE_COLLECTION_RETIREMENT_REFUSED reference={state.Reference}: {error.Message}");
        }
        finally { state.CapturePackageCollection = null; }
    }

    private void RestoreCollectionElection(double elapsed)
    {
        var election = _aiState!.PackageCollection?.Election ?? throw new InvalidDataException("Cold collection lost its actual source election clock.");
        Activity.Restore(election.Activity);
        _packageClock = election.PollRemaining - elapsed; _aiScheduleTime = election.ScheduleTime;
        _aiQuestRevision = election.QuestRevision; _evaluateRequested = election.EvaluateRequested;
    }
}
