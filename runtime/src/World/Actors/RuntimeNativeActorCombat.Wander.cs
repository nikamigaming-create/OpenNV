using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private long _ambientAttempts, _ambientStarted;
    internal object SandboxObservation => new { attempts = _ambientAttempts, started = _ambientStarted };

    internal FalloutWanderProgress? AdvanceSandboxWander(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutWanderProgress? progress, double delta)
    {
        if (OwnsPose || !PackageMovementReady) return progress;
        var context = _context ?? throw new NotSupportedException("Sandbox has no gameplay context.");
        var busy = context.DialogueBusy ?? throw new NotSupportedException("Sandbox has no dialogue participant owner.");
        if (busy(_state.Reference)) { StopPackageMotion(); return progress; }
        var hour = context.Globals.Get(FalloutGameTimeBindings.Read(_records).Hour);
        var activity = _state.PackageMotion?.Package == package.FormKey ? _state.PackageMotion.Sandbox : null;
        var randomBeforeSelection = _state.SoundRandom.State;
        var selectedActivity = activity is null || activity.Expired(hour);
        if (selectedActivity)
        {
            activity = SelectSandboxActivity(source, activity, hour, delta, context);
            progress = null;
        }
        if (activity is null) throw new InvalidOperationException("Sandbox has no selected activity.");
        if (activity.Activity == FalloutSandboxActivity.Dialogue)
        {
            // A refused opening retains this selection. A successful attempt
            // reserves both participants and immediately selects the following
            // activity; speech owns their pose until the exchange settles.
            HoldSandboxActivity(package, source, activity, delta);
            var target = activity.Target ?? throw new InvalidDataException("Sandbox dialogue has no listener.");
            ++_ambientAttempts;
            if ((context.TryAmbientConversation ?? throw new NotSupportedException("Sandbox has no ambient speech owner."))
                (_state.Reference, target))
            {
                ++_ambientStarted;
                activity = SelectSandboxActivity(source, activity, hour, delta, context);
                _state.PackageMotion = _state.PackageMotion! with { Sandbox = activity, Wander = null };
                StopPackageMotion();
            }
            return null;
        }
        var selectedDestination = progress is null || progress.Arrived;
        if (selectedDestination)
        {
            var location = source.Location(_records, _world, _state.Reference);
            if (_world.TravelRegion(location.Cell) != _world.TravelRegion(_world.Placement(_state.Reference).Cell))
                throw new NotSupportedException("Sandbox location needs an other-region approach owner.");
            progress = _world.BeginPackageWander(_state.Reference,
                new Vector3(location.Location[0], location.Location[1], location.Location[2]), source.Radius,
                _records.NumericSettings, context.Globals, _skeleton.UnitsToMetres);
        }
        if (progress is null) throw new InvalidOperationException("Sandbox has no accepted wander request.");
        var randomBeforeMotion = _state.SoundRandom.State;
        try { progress = AdvancePackageWander(package, progress, source.Running, source.WeaponDrawn, delta); }
        catch (FileNotFoundException error) when (FalloutSandboxMotionException.IsMissingMovement(error.Message) &&
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
        FalloutSandboxActivityProgress? previous, float hour, double delta, NativeActorCombatContext context)
    {
        var busy = context.DialogueBusy ?? throw new NotSupportedException("Sandbox has no dialogue participant owner.");
        var candidates = _world.SandboxCandidateSources(_state.Reference, source,
            context.Level(), context.Globals, _skeleton.UnitsToMetres);
        var admitted = new List<FalloutSandboxCandidateSource>();
        foreach (var candidate in candidates)
        {
            if (candidate.Activity == FalloutSandboxActivity.Wander) { admitted.Add(candidate); continue; }
            if (candidate.Activity != FalloutSandboxActivity.Dialogue)
                throw new FalloutSandboxAdmissionException(source.Form,
                    $"{FalloutActorSandboxAdmissionFailure.AdmissionErrorPrefix}{candidate.Activity} " +
                    $"reference={candidate.Reference} base={candidate.Base} inventory={candidate.Inventory}");
            if (_state.ConversationSuppressionSeconds is > 0 || busy(_state.Reference) || _world.BlocksAmbientConversation(_state.Reference)) continue;
            var target = candidate.Reference ?? throw new InvalidDataException("Sandbox dialogue candidate has no reference.");
            if (busy(target) || _world.BlocksAmbientConversationTarget(target) ||
                _world.IsRecentConversationPartner(_state.Reference, target, delta) ||
                _world.BlocksAmbientConversationRelationship(_state.Reference, target)) continue;
            admitted.Add(candidate);
        }
        var preferences = previous is null ? new FalloutSandboxPreferences() : new FalloutSandboxPreferences(previous.Preferences);
        var categories = preferences.ActivityWeights(null, false, false, _state.ConversationSuppressionSeconds is > 0);
        var weights = admitted.Select(candidate => FalloutSandboxPreferences.CandidateWeight(1, categories[(int)candidate.Activity])).ToArray();
        var total = weights.Aggregate(0, (sum, weight) => checked(sum + weight));
        if (total == 0) throw new NotSupportedException("Sandbox has no admitted activity; its retry owner is unbound.");
        var selected = admitted[FalloutSandboxPreferences.Choose(weights, _state.SoundRandom.NextUInt32() % (uint)total)];
        var threat = FalloutActorThreat.Read(_records, _state.Base, _state.Templates);
        return FalloutSandboxActivityProgress.Select(_records.NumericSettings, threat.Energy,
            selected.Activity, hour, preferences, _state.SoundRandom.NextUnitFloat, selected.Reference);
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
        _world.ValidatePackageWander(_state.Reference, progress);
        var point = progress.Destination;
        var destination = _actor.GetParent<Node3D>().ToGlobal(
            new Vector3(point[0], point[2], -point[1]) * _skeleton.UnitsToMetres);
        var actor = _actor as CharacterBody3D ??
            throw new NotSupportedException("Wandering has no native actor capsule.");
        var tolerance = 5 * _skeleton.UnitsToMetres;
        AdvancePackageMotion(package, destination, tolerance, running, delta, weaponDrawn,
            requireArrivalHeight: true);
        progress = progress with
        {
            Arrived = actor.IsOnFloor() && _actor.GlobalPosition.DistanceTo(destination) <= tolerance
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
