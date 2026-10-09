using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessManager
{
    internal void AdvanceSourceDetection(float delta)
    {
        Operation(null, "original-full-high-cohort-detection-schedule", () =>
        {
            var faultsAtEntry = _faults.Count;
            if (_factory is { Phase: not FalloutActorProcessFactoryPhase.Complete })
                throw new NotSupportedException("Detection schedule cannot pass an unfinished source process factory.");
            if (!float.IsFinite(delta) || delta < 0 || !float.IsFinite(_seconds + delta))
                throw new InvalidDataException("Original detection update has no finite simulation delta.");
            if (_schedule is { Complete: false })
                throw new NotSupportedException("Failed source schedule retains its cursor; a new frame cannot replay its prefix.");
            _seconds += delta;
            var start = _cohort.Start(0); var end = _cohort.End(0);
            var window = _source.RandomWindow(checked((uint)end), delta);
            var visits = new List<FalloutActorProcessVisit>();
            _schedule = new(Next(), _seconds, delta, start, end, window, start, visits.ToArray(), 0, false, null,
                _cohort.Segment(0), _cohort.Revision, null);
            try
            {
                if (end == 0) { _playerScheduleFlag = false; Next(); }
                // Source reevaluates the current end on every iteration. It
                // does not snapshot/sort/limit actors or treat 5s as a budget.
                for (var slot = start; slot < _cohort.End(0); slot++)
                {
                    _schedule = _schedule with { NextSlot = slot };
                    var key = _cohort.At(slot);
                    if (key is null)
                    { Visit(new(slot, null, 0, FalloutActorDetectionVisitKind.NullSlot, null, null, null, null, 0, "original-null-array-slot")); continue; }
                    if (!_sourceActor(key.Value))
                    { Visit(new(slot, key, 0, FalloutActorDetectionVisitKind.NonActor, null, null, null, null, 0, "original-actor-class-predicate")); continue; }
                    var actor = RequireActor(key.Value); RequireEpoch(actor, actor.Epoch);
                    if (actor.Retired || !actor.Registered || actor.Level != FalloutDetectionProcessLevel.High || key == _player)
                        throw new InvalidDataException("Actual High array contains a retired/unregistered/non-High/player object.");
                    var observed = _inputs.Observe(key.Value, actor.Epoch); observed.Validate(key.Value, actor.Epoch);
                    if (observed.Source3D != _perception.RequiresNativePublication(key.Value))
                        throw new InvalidDataException("Source schedule substituted renderer residency for the actual 3D producer.");
                    if (!observed.ActorUpdateEnabled.Require())
                    { Visit(Simple(FalloutActorDetectionVisitKind.UpdateDisabled)); continue; }
                    if (!observed.Source3D)
                    { Visit(Simple(FalloutActorDetectionVisitKind.Missing3D)); continue; }
                    // The distance getter is called before the source's
                    // explicit Player identity skip. Player cannot be in this
                    // registered array, but the independent pose still matters.
                    var player = _inputs.Observe(_player, RequireActor(_player).Epoch);
                    player.Validate(_player, RequireActor(_player).Epoch);
                    var distance = FalloutActorPerception.Distance(observed.SourcePosition, player.SourcePosition);
                    if (slot == 0) { _playerScheduleFlag = false; Next(); }
                    var before = actor.DetectionTimer ?? throw new InvalidDataException("Actual High process omitted its source detection timer.");
                    if (!float.IsFinite(before)) throw new InvalidDataException("Actor detection timer is non-finite.");
                    if (!FalloutActorProcessDeclaration.TimerNeedsProducer(before))
                    { Decrement(); Visit(Simple(FalloutActorDetectionVisitKind.Countdown, before)); continue; }
                    var life = observed.LifeState.Require();
                    if (life is 1 or 2 or 3 or 6)
                    {
                        _inputs.RemovePlayerTarget(key.Value);
                        Decrement(); Visit(Simple(FalloutActorDetectionVisitKind.RejectedLifeState, before)); continue;
                    }
                    var category = _inputs.CategoryOneClock();
                    if (!float.IsFinite(category)) throw new InvalidDataException("Original category-one update clock is non-finite.");
                    if (category > 0f)
                    { Visit(Simple(FalloutActorDetectionVisitKind.CategoryClockHeld, before)); continue; }
                    var word = _inputs.SharedWord(); var interval = _source.CadenceDraw(word, window);
                    _schedule = _schedule with
                    {
                        InFlight = new(slot, key, actor.Epoch,
                        FalloutActorDetectionVisitKind.ProducerEntered, before, before, word, interval, 0,
                        "actual-shared-random-word-and-stored-interval-consumed-before-high-producer")
                    };
                    Next();
                    var produced = ProduceOriginalHigh(key.Value, actor.Epoch, interval, distance);
                    var current = RequireActor(key.Value);
                    visits.Add(new(slot, key, actor.Epoch,
                        produced.ReachedFinalTimerWrite ? FalloutActorDetectionVisitKind.ProducerCompleted : FalloutActorDetectionVisitKind.ProducerEarlyReturn,
                        before, current.DetectionTimer, word, interval, produced.CompletedPairs, produced.OriginalEarlyReturn));
                    _schedule = _schedule with { Visits = visits.ToArray(), NextSlot = slot + 1, InFlight = null };
                    if (observed.BaseEligibility.Require()) continue;
                    if (!observed.ProcessEligibility.Require()) continue;
                    var detected = _inputs.PlayerDetection(key.Value);
                    if (detected.Target != key || string.IsNullOrWhiteSpace(detected.Owner))
                        throw new InvalidDataException("Player detection publication belongs to another source actor.");
                    var hostility = ReadOriginalPlayerHostility(key.Value);
                    if (distance < _inputs.Setting("fShaderShadowUpdateDistance")) _inputs.Shadow(key.Value);
                    if (_source.PlayerScoreAdmitted(detected.Score, _inputs.Setting("fPlayerDetectActorValue")))
                        _inputs.AddPlayerTarget(key.Value, hostility, false);
                    else if (detected.OtherFlag || detected.VisibilityFlag) _inputs.AddPlayerTarget(key.Value, hostility, true);
                    else _inputs.RemovePlayerTarget(key.Value);

                    FalloutActorProcessVisit Simple(FalloutActorDetectionVisitKind kind, float? prior = null) =>
                        new(slot, key, actor.Epoch, kind, prior, RequireActor(key.Value).DetectionTimer, null, null, 0, observed.Owner);
                    void Decrement()
                    {
                        var remaining = FalloutActorProcessDeclaration.DecrementTimer(RequireActor(key.Value).DetectionTimer ??
                            throw new InvalidDataException("Actual High process lost its detection countdown."), delta);
                        _actors[key.Value] = RequireActor(key.Value) with { DetectionTimer = remaining, LastTimerOwner = "original-actor-frame-decrement" };
                        Next();
                    }
                }
                // Source commits Player separately, then every registered High
                // entry. It does not duplicate Player through a cache-owner list.
                _perception.CommitSourceCache(_player);
                _schedule = _schedule with { CompletedCommits = 1 };
                for (var slot = _cohort.Start(0); slot < _cohort.End(0); slot++)
                    if (_cohort.At(slot) is { } key && _sourceActor(key))
                    {
                        _perception.CommitSourceCache(key);
                        _schedule = _schedule with { CompletedCommits = checked(_schedule.CompletedCommits + 1) };
                    }
                if (_source.Arithmetic == FalloutDetectionScalarKind.Fallout3) _inputs.EndFallout3Player();
                if (_faults.Count != faultsAtEntry) throw new InvalidOperationException("Source detection child owner caught an actual reentry failure.");
                _schedule = _schedule with { NextSlot = _cohort.End(0), Complete = true };
                Next();

                void Visit(FalloutActorProcessVisit visit)
                { visits.Add(visit); _schedule = _schedule with { Visits = visits.ToArray(), NextSlot = visit.Slot + 1 }; }
            }
            catch (Exception error)
            {
                _schedule = _schedule with { Failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message };
                throw;
            }
        });
    }

    private bool ReadOriginalPlayerHostility(FalloutFormKey actor)
    {
        var source = _inputs.Hostility(actor);
        if (source.Actor != actor || string.IsNullOrWhiteSpace(source.Owner))
            throw new InvalidDataException("Actor-to-player hostility belongs to another source actor.");
        if (source.Reaction.Require() || source.ControllerTargetsPlayer.Require()) return true;
        var reaction = source.ReactionClass.Require();
        return reaction is 0 or 1 && source.BaseFallback.Require() && !source.BlockingEffect.Require();
    }

    private FalloutActorProcessProducerReceipt ProduceOriginalHigh(FalloutFormKey actor, long epoch,
        float randomInterval, float playerDistance)
    {
        var schedule = _schedule ?? throw new InvalidDataException("High producer has no actual source schedule.");
        var inFlight = schedule.InFlight ?? throw new InvalidDataException("High producer lost its already consumed source random word.");
        if (inFlight.Actor != actor || inFlight.Epoch != epoch || inFlight.RandomInterval != randomInterval)
            throw new InvalidDataException("High producer belongs to another actor/process/random draw.");
        _perception.BeginOriginalHighProducer(actor, epoch);
        _schedule = schedule with
        {
            InFlight = inFlight with
            { Owner = "actual-high-producer-both-pending-directions-cleared-before-early-guards" }
        };
        Next();
        var clock = _inputs.CategoryOneClock();
        if (!float.IsFinite(clock)) throw new InvalidDataException("Original producer category clock is non-finite.");
        if (clock > 0f) return Early("original-category-one-clock-positive");
        var guards = _inputs.Guards(actor, epoch);
        if (guards.Actor != actor || guards.Epoch != epoch || string.IsNullOrWhiteSpace(guards.Owner) ||
            guards.OriginalMaximumPlayerDistance != _source.MaximumPlayerProducerDistance)
            throw new InvalidDataException("High producer guard belongs to a foreign/changed source process.");
        if (guards.ActorRejectsDetection.Require()) return Early("original-actor-rejects-detection");
        if (guards.DyingWithBlockingEffect.Require()) return Early("original-dying-actor-blocking-effect");
        if (playerDistance >= _source.MaximumPlayerProducerDistance) return Early("original-player-distance-bound");
        if (!guards.GlobalDetectionEnabled.Require()) return Early("original-detection-disabled");
        // This entry reaches the original controller redirection, hearing
        // observation and reactive refinement program. Neither a sight ray nor
        // scalar calculation owns those effects. Retain the actual cleared
        // pending-list prefix and leave the negative timer untouched.
        throw new NotSupportedException("Original High detection controlled-actor/hearing/reactive/perk refinement producer is unowned; timer final write was not reached.");

        FalloutActorProcessProducerReceipt Early(string owner) => new(actor, epoch, owner, 0, false, null, Next());
    }
}
