using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal interface INativeSandboxActivityOwner
{
    bool Supports(FalloutSandboxActivity activity);
    bool ReleaseForReselection(FalloutSandboxActivityProgress activity);
    void Advance(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutSandboxActivityProgress activity, double delta);
}

internal sealed partial class RuntimeNativeActorCombat
{
    private long _ambientAttempts, _ambientStarted;
    private FalloutGameTimeBindings? _sandboxTimeBindings;
    private FalloutSandboxActivityProgress? _executingSandboxActivity;
    internal FalloutSandboxActivityProgress? CurrentSandboxActivity => _executingSandboxActivity ?? _state.PackageMotion?.Sandbox;
    internal int ScriptSandboxProcedure
    {
        get
        {
            var activity = CurrentSandboxActivity ?? throw new NotSupportedException("Sandbox has no retained activity phase.");
            if (activity.NativePhase is { } phase) return FalloutSandboxProcedure.Read(phase, activity.Activity);
            if (activity.Activity == FalloutSandboxActivity.Wander && _state.PackageMotion?.Wander is not null)
                return FalloutSandboxProcedure.Read(3, activity.Activity);
            throw new NotSupportedException("Legacy Sandbox activity has no qualified native phase.");
        }
    }

    internal FalloutSandboxActivityProgress ObserveSandboxPhase(int phase)
    {
        var previous = CurrentSandboxActivity ?? throw new InvalidOperationException("Sandbox phase has no selected activity.");
        _ = FalloutSandboxProcedure.Read(phase, previous.Activity);
        // Reobserving a phase must not invalidate a queued physical request
        // which retains this exact selected activity instance.
        if (previous.NativePhase == phase) return previous;
        var current = previous with { NativePhase = phase };
        if (_executingSandboxActivity is not null) _executingSandboxActivity = current;
        if (_state.PackageMotion is { } motion && motion.Sandbox == previous)
            _state.PackageMotion = motion with { Sandbox = current };
        return current;
    }
    internal object SandboxObservation => new { attempts = _ambientAttempts, started = _ambientStarted };

    internal FalloutWanderProgress? AdvanceSandboxWander(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutWanderProgress? progress, double delta, INativeSandboxActivityOwner? physical = null)
    {
        try { return AdvanceSandboxWanderCore(package, source, progress, delta, physical); }
        finally { _executingSandboxActivity = null; }
    }

    private FalloutWanderProgress? AdvanceSandboxWanderCore(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutWanderProgress? progress, double delta, INativeSandboxActivityOwner? physical)
    {
        if (OwnsPose || !PackageMovementReady) return progress;
        var context = _context ?? throw new NotSupportedException("Sandbox has no gameplay context.");
        var busy = context.DialogueBusy ?? throw new NotSupportedException("Sandbox has no dialogue participant owner.");
        if (busy(_state.Reference)) { StopPackageMotion(); return progress; }
        var time = _sandboxTimeBindings ??= FalloutGameTimeBindings.Read(_records);
        var hour = context.Globals.Get(time.Hour);
        var day = SandboxDay(context);
        _state.SandboxDailyWindow = FalloutSandboxDailyWindow.Refresh(_state.SandboxDailyWindow,
            day, hour, _records.NumericSettings,
            (minimum, maximum) => minimum + (maximum - minimum) * _state.SoundRandom.NextUnitFloat());
        var activity = _state.PackageMotion?.Package == package.FormKey ? _state.PackageMotion.Sandbox : null;
        var scheduled = activity is null ? null : _state.SandboxDailyWindow.EligibleActivity(day, hour,
            activity.DailyFoodAvailable, activity.DailyBedAvailable);
        var dailyChanged = activity is not null && scheduled != activity.ScheduledActivity;
        var randomBeforeSelection = _state.SoundRandom.State;
        var selectedActivity = activity is null || activity.Activity == FalloutSandboxActivityProgress.NoActivity || (activity.Expired(hour) || dailyChanged) &&
            (physical?.Supports(activity.Activity) != true || physical.ReleaseForReselection(activity));
        if (selectedActivity)
        {
            using (OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxActivitySelection))
                activity = SelectSandboxActivity(source, activity, hour, delta, context, physical);
            progress = null;
            if (_state.PackageMotion is { } previousMotion && previousMotion.Package == package.FormKey &&
                _state.DeferredPackageContinuation?.RetainedAssignment is null)
                _state.PackageMotion = previousMotion with { Eat = null };
        }
        else if (_state.PackageMotion?.Package == package.FormKey)
            activity = _state.PackageMotion.Sandbox ?? activity; // Release can publish phase one before the handler runs.
        if (activity is null) throw new InvalidOperationException("Sandbox has no selected activity.");
        _executingSandboxActivity = activity;
        if (activity.Activity == FalloutSandboxActivityProgress.NoActivity)
        {
            HoldSandboxActivity(package, source, activity, delta);
            return null;
        }
        if (activity.NativePhase == 1 && activity.Activity is not (FalloutSandboxActivity.Furniture or FalloutSandboxActivity.IdleMarker))
            activity = ObserveSandboxPhase(2);
        if (physical?.Supports(activity.Activity) == true)
        {
            if (_state.PackageMotion?.Package != package.FormKey || _state.PackageMotion.Sandbox != activity)
                HoldSandboxActivity(package, source, activity, 0);
            physical.Advance(package, source, activity, delta);
            activity = CurrentSandboxActivity ?? throw new InvalidOperationException("Sandbox activity lost its live phase.");
            _state.PackageMotion = (_state.PackageMotion ?? throw new InvalidOperationException("Physical Sandbox activity lost its motion owner."))
                with
            { Sandbox = activity, Wander = null };
            return null;
        }
        if (activity.Activity == FalloutSandboxActivity.Eat)
        {
            using var scope = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxEat);
            var randomBeforeMeal = _state.SoundRandom.State;
            try { AdvanceSandboxEat(package, source, activity, delta, context); }
            catch (FalloutSandboxMealSelectionException error) when (_actor is RuntimeNativeNpc &&
                _state.PackageMotion is null && randomBeforeMeal == _state.SoundRandom.State)
            {
                // Selection has consumed its draws, but no meal, pose, idle or
                // ingestion transaction has been selected or published.
                var prefix = new FalloutSandboxUnchosenMeal(activity, error.Inputs, selectedActivity,
                    randomBeforeSelection, randomBeforeMeal, _state.SoundRandom.State);
                prefix.Validate();
                throw new FalloutSandboxAdmissionException(package.FormKey, error.Message, prefix);
            }
            return null;
        }
        if (activity.Activity == FalloutSandboxActivity.Dialogue)
        {
            using var scope = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxDialogue);
            // A refused opening retains this selection. A successful attempt
            // reserves both participants and immediately selects the following
            // activity; speech owns their pose until the exchange settles.
            using (OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxDialogueHold))
                HoldSandboxActivity(package, source, activity, delta);
            var target = activity.Target ?? throw new InvalidDataException("Sandbox dialogue has no listener.");
            ++_ambientAttempts;
            bool started;
            using (OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxDialogueAttempt))
                started = (context.TryAmbientConversation ?? throw new NotSupportedException("Sandbox has no ambient speech owner."))
                    (_state.Reference, target);
            if (started)
            {
                ++_ambientStarted;
                using (OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxActivitySelection))
                    activity = SelectSandboxActivity(source, activity, hour, delta, context, physical);
                _executingSandboxActivity = activity;
                if (activity.Activity is not (FalloutSandboxActivityProgress.NoActivity or FalloutSandboxActivity.Furniture or FalloutSandboxActivity.IdleMarker)) activity = ObserveSandboxPhase(2);
                _state.PackageMotion = _state.PackageMotion! with { Sandbox = activity, Wander = null };
                StopPackageMotion();
            }
            return null;
        }
        // Native Wander acknowledges phase three before its handler, whether
        // this accepted destination is moving or already reached.
        activity = ObserveSandboxPhase(3);
        var selectedDestination = progress is null || progress.Arrived;
        if (selectedDestination)
        {
            using var scope = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxDestination);
            var location = source.Location(_records, _world, _state.Reference);
            if (_world.TravelRegion(location.Cell) != _world.TravelRegion(_world.Placement(_state.Reference).Cell))
                throw new NotSupportedException("Sandbox location needs an other-region approach owner.");
            progress = _world.BeginPackageWander(_state.Reference,
                new Vector3(location.Location[0], location.Location[1], location.Location[2]), source.Radius,
                _records.NumericSettings, context.Globals, _skeleton.UnitsToMetres);
        }
        if (progress is null) throw new InvalidOperationException("Sandbox has no accepted wander request.");
        var randomBeforeMotion = _state.SoundRandom.State;
        try
        {
            using var scope = OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxMovement);
            progress = AdvancePackageWander(package, progress, source.Running, source.WeaponDrawn, delta);
        }
        catch (Exception error) when (FalloutSandboxMotionException.CanRetain(error) &&
            randomBeforeMotion == _state.SoundRandom.State)
        {
            // Preserve the accepted request and consumed selection prefix. A
            // later capture may retain this fault, never replay those draws.
            throw new FalloutSandboxMotionException(package.FormKey,
                new(activity, progress, selectedActivity, selectedDestination, randomBeforeSelection,
                    randomBeforeMotion, _state.SoundRandom.State), error);
        }
        _state.PackageMotion = _state.PackageMotion! with { Sandbox = activity };
        return progress;
    }

    private FalloutSandboxActivityProgress SelectSandboxActivity(FalloutSandboxPackage source,
        FalloutSandboxActivityProgress? previous, float hour, double delta, NativeActorCombatContext context,
        INativeSandboxActivityOwner? physical)
    {
        var busy = context.DialogueBusy ?? throw new NotSupportedException("Sandbox has no dialogue participant owner.");
        var candidates = _world.SandboxCandidatesAtRescan(_state.Reference, source,
            context.Level(), context.Globals, _skeleton.UnitsToMetres);
        var admitted = new List<FalloutSandboxCandidateSource>();
        foreach (var candidate in candidates)
        {
            if (candidate.Activity == FalloutSandboxActivity.Wander) { admitted.Add(candidate); continue; }
            if (candidate.Activity == FalloutSandboxActivity.IdleMarker)
            {
                if (!_world.IdleMarkerAvailable(candidate.Reference ??
                    throw new InvalidDataException("Idle marker candidate has no source reference."), 0,
                    _state.Reference, _world.IdleMarkerOccupied)) continue;
                admitted.Add(candidate); continue;
            }
            if (candidate.Activity is FalloutSandboxActivity.Furniture or FalloutSandboxActivity.Sleep)
            {
                if (!_world.ActorMayUseFurniture(_state.Reference, candidate.Reference ??
                        throw new InvalidDataException("Furniture candidate has no source reference."))) continue;
                admitted.Add(candidate); continue;
            }
            if (candidate.Activity == FalloutSandboxActivity.Eat)
            {
                if (candidate.Reference is { } food && !_world.ActorMayEatWorldFood(_state.Reference, food)) continue;
                admitted.Add(candidate); continue;
            }
            if (candidate.Activity != FalloutSandboxActivity.Dialogue) { admitted.Add(candidate); continue; }
            if (_state.ConversationSuppressionSeconds is > 0 || busy(_state.Reference) || _world.BlocksAmbientConversation(_state.Reference)) continue;
            var target = candidate.Reference ?? throw new InvalidDataException("Sandbox dialogue candidate has no reference.");
            if (busy(target) || _world.BlocksAmbientConversationTarget(target) ||
                _world.IsRecentConversationPartner(_state.Reference, target, delta) ||
                _world.BlocksAmbientConversationRelationship(_state.Reference, target)) continue;
            admitted.Add(candidate);
        }
        var preferences = previous is null ? new FalloutSandboxPreferences() : new FalloutSandboxPreferences(previous.Preferences);
        var foodAvailable = admitted.Any(candidate => candidate.Activity == FalloutSandboxActivity.Eat);
        var furnitureAvailable = admitted.Any(candidate => candidate.Activity == FalloutSandboxActivity.Furniture);
        var bedAvailable = admitted.Any(candidate => candidate.Activity == FalloutSandboxActivity.Sleep);
        var scheduled = (_state.SandboxDailyWindow ?? throw new InvalidOperationException("Sandbox has no retained daily window."))
            .EligibleActivity(SandboxDay(context), hour, foodAvailable, bedAvailable);
        var categories = preferences.ActivityWeights(scheduled, foodAvailable, furnitureAvailable, _state.ConversationSuppressionSeconds is > 0);
        var weights = admitted.Select(candidate => FalloutSandboxPreferences.CandidateWeight(1, categories[(int)candidate.Activity])).ToArray();
        var total = weights.Aggregate(0, (sum, weight) => checked(sum + weight));
        if (total == 0) return FalloutSandboxActivityProgress.Empty(hour, preferences);
        var selected = admitted[FalloutSandboxPreferences.Choose(weights, _state.SoundRandom.NextUInt32() % (uint)total)];
        if (selected.Activity is not (FalloutSandboxActivity.Wander or FalloutSandboxActivity.Eat or FalloutSandboxActivity.Dialogue) &&
            physical?.Supports(selected.Activity) != true)
            throw new NotSupportedException($"Sandbox selected activity requires its execution owner: {selected.Activity} " +
                $"reference={selected.Reference} base={selected.Base} inventory={selected.Inventory}");
        var threat = CurrentThreat();
        return FalloutSandboxActivityProgress.Select(_records.NumericSettings, threat.Energy,
            selected.Activity, hour, preferences, _state.SoundRandom.NextUnitFloat, selected.Reference) with
        { ScheduledActivity = scheduled, DailyFoodAvailable = foodAvailable, DailyBedAvailable = bedAvailable, NativePhase = 1 };
    }

    private uint SandboxDay(NativeActorCombatContext context)
    {
        var days = context.Globals.Get((_sandboxTimeBindings ??= FalloutGameTimeBindings.Read(_records)).DaysPassed);
        if (!float.IsFinite(days) || days < 0 || (double)days > uint.MaxValue)
            throw new InvalidDataException("Sandbox daily scheduling has an invalid game-day clock.");
        return (uint)days;
    }

    private void HoldSandboxActivity(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutSandboxActivityProgress activity, double delta)
    {
        AdvancePackageMotion(package, _actor.GlobalPosition, 0, false, delta, source.WeaponDrawn);
        _state.PackageMotion = (_state.PackageMotion ?? throw new InvalidOperationException("Sandbox has no native idle observation."))
            with
        { Sandbox = activity, Wander = null };
        if (_state.ProcedureCaptureBlocker?.StartsWith(FalloutSandboxPackage.CaptureBlockerPrefix, StringComparison.Ordinal) == true)
            _state.ProcedureCaptureBlocker = null;
    }

    internal FalloutWanderProgress AdvancePackageWander(FalloutPluginRecord package,
        FalloutWanderProgress progress, bool running, bool weaponDrawn, double delta)
    {
        // Keep the accepted request while another owner controls the body.
        // In particular, combat must never consume another wander draw.
        if (OwnsPose || !PackageMovementReady) return progress;
        using (OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Measure(OpenNV.Runtime.Diagnostics.RuntimeFrameWork.Owner.SandboxValidation))
            _world.ValidatePackageWander(_state.Reference, progress);
        var point = progress.Destination;
        var destination = _actor.GetParent<Node3D>().ToGlobal(
            new Vector3(point[0], point[2], -point[1]) * _skeleton.UnitsToMetres);
        var actor = _actor as CharacterBody3D ??
            throw new NotSupportedException("Wandering has no native actor capsule.");
        var tolerance = 5 * _skeleton.UnitsToMetres;
        var nativeArrival = AdvancePackageMotion(package, destination, tolerance, running, delta, weaponDrawn,
            requireArrivalHeight: true, navigationSha256: progress.NavigationSha256);
        progress = progress with
        {
            Arrived = nativeArrival || actor.IsOnFloor() && _actor.GlobalPosition.DistanceTo(destination) <= tolerance
        };
        if (_state.PackageMotion is not { } motion || motion.Package != package.FormKey)
            throw new InvalidOperationException("Wandering has no matching native motion observation.");
        _state.PackageMotion = motion with { Wander = progress };
        if (_state.ProcedureCaptureBlocker?.StartsWith(FalloutSandboxPackage.CaptureBlockerPrefix, StringComparison.Ordinal) == true)
            _state.ProcedureCaptureBlocker = null;
        // Arrival ends this movement request, not the Sandbox package. Activity
        // scheduling and package result events remain with their respective owners.
        return progress;
    }
}
