using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutSleepPackage? _sleepPackage;
    private FalloutActorSleepProgress? _sleepProgress;
    private FalloutSleepCellSource? _sleepBeds;
    private (FalloutPluginRecord Package, bool Initializing)? _pendingCurrentLocationSleep;
    private FalloutFormKey? _deferredSleepAdmission;
    private const string SleepCaptureBlocker = "Sleep awaits its first native search or furniture observation.";

    private void BeginSleep(FalloutPluginRecord package, bool initializing)
    {
        var source = FalloutSleepPackage.Read(package);
        var world = _aiWorld!;
        var actor = Appearance.Reference!.Value;
        var state = world.Get(actor);
        if (source.Location.Type == 2 && (!initializing || state.PackageStartLocation is null))
        {
            if (!IsInsideTree())
            {
                _pendingCurrentLocationSleep = (package, initializing);
                _aiPackage = package;
                state.ProcedureCaptureBlocker = SleepCaptureBlocker;
                return;
            }
            world.CaptureActorPackageStartLocation(actor, Skeleton.UnitsToMetres);
        }
        var retained = initializing && state.PackageMotion?.Package == package.FormKey ? state.PackageMotion : null;
        var progress = retained?.Sleep;
        if (progress is null)
        {
            if (initializing && _deferredSleepAdmission != package.FormKey &&
                state.PackageAssignment is { Done: false } assignment && assignment.Package == package.FormKey)
                throw new NotSupportedException("Saved active Sleep has no retained source bed intent.");
            var candidates = source.Candidates(_aiStack!, world, actor);
            var placement = world.Placement(actor);
            var selected = candidates.OrderBy(reference =>
            {
                var current = world.Placement(reference.FormKey);
                return world.SameTravelRegion(placement.Cell, current.Cell)
                    ? Enumerable.Range(0, 3).Sum(index => Math.Pow((double)placement.Position[index] - current.Position[index], 2))
                    : double.PositiveInfinity;
            }).FirstOrDefault() ??
                throw new NotSupportedException("Sleep has no admitted source bed; its no-bed fallback is unbound.");
            progress = new(world.Placement(selected.FormKey).Cell, selected.FormKey, false);
        }
        source.Validate(_aiStack!, world, actor, progress);
        if (progress.Furniture) throw new InvalidDataException("Physical Sleep has no furniture continuation.");
        _sleepPackage = source; _sleepProgress = progress; _sleepBeds = source.Source(_aiStack!, world, actor); _aiPackage = package;
        _furnitureSearchRemaining = 0;
        _deferredSleepAdmission = null;
        if (_packageEvents!.Active is null)
        {
            if (initializing && state.PackageAssignment is { } assignment && assignment.Package == package.FormKey)
                assignment.Bind(_aiStack!, _packageEvents);
            else _packageEvents.Change(_packageIdleSource);
        }
        else if (_packageEvents.Active.Form != package.FormKey || _packageEvents.Done)
            throw new InvalidDataException("Sleep differs from its retained active package lifecycle.");
        state.ProcedureCaptureBlocker = retained is null ? SleepCaptureBlocker : null;
    }

    private void AdvanceSleep(double delta)
    {
        if (Combat is null ||
            _aiError is not null || Combat.OwnsPose || !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try
        {
            if (_pendingCurrentLocationSleep is { } pending)
            {
                _pendingCurrentLocationSleep = null;
                BeginSleep(pending.Package, pending.Initializing);
            }
            if (_sleepPackage is not { } source || _sleepProgress is not { } progress) return;
            if (progress.Furniture)
            {
                if (_furnitureApproaching) AdvanceFurnitureApproach(delta);
                ObserveSleepFurniture();
                return;
            }
            source.Validate(_aiStack!, _aiWorld!, Appearance.Reference!.Value, progress, _sleepBeds);
            if (!_aiWorld!.IsResident(progress.Bed) ||
                !_aiWorld.SameTravelRegion(_aiWorld.Placement(Appearance.Reference!.Value).Cell, progress.Cell))
            {
                if (_nativeMarkerTravel is null)
                    BeginMarkerTravel(_aiPackage!, initializing: Combat.PackageMotion?.Package == source.Form && Combat.PackageMotion.Travel is not null,
                        FalloutPackageTravelGoal.Read(_aiStack!, _aiWorld, Appearance.Reference!.Value, _aiPackage!, progress));
                AdvanceMarkerTravel(delta);
                if (Combat.PackageMotion?.Package == source.Form) Combat.SetSleepProgress(progress);
                return;
            }
            // Region arrival retires only the movement leg. The source bed's
            // real entry root, reservation and finite KF own physical arrival.
            if (_nativeMarkerTravel is not null) Combat.RetirePackageMotionNavigation(source.Form);
            _nativeMarkerTravel = null; _nativeMarkerTravelProgress = null; _nativeMarkerTravelDestination = null;
            _travelPackage = null; _travelTarget = null; _travelPurpose = null; _nativeMarkerTravelRestored = false;
            Combat.AdvancePackageMotion(_aiPackage!, GlobalPosition, 0, false, 0);
            _aiReferenceState!.PackageMotion = Combat.PackageMotion! with { Travel = null, Sleep = progress };
            _furnitureSearchRemaining = Math.Max(0, _furnitureSearchRemaining - delta);
            if (_furnitureSearchRemaining > 0) return;
            var selected = _aiWorld.PlacedSource(progress.Bed);
            if (!TryBeginFurniturePackage(_aiPackage!, selected.Reference, _aiStack!.GetEffective(selected.Reference.Base), initializing: false))
            {
                // Contention retains this source intent and the actual idle
                // pose; it never steals the other actor's reservation.
                _furnitureSearchRemaining = .5;
                _aiReferenceState.ProcedureCaptureBlocker = null;
                return;
            }
            _sleepProgress = progress with { Furniture = true };
            Combat.SetSleepProgress(_sleepProgress);
            AdvanceFurnitureApproach(delta);
            ObserveSleepFurniture();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            BlockSelectionCapture($"Sleep continuation is unbound: {error.Message}");
            GD.PushError($"OPENNV_NATIVE_SLEEP_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private void ObserveSleepFurniture()
    {
        if (_sleepProgress is not { Furniture: true } progress || _aiReferenceState?.PackageMotion is not { } motion) return;
        var position = GlobalPosition; var rotation = GlobalBasis.Orthonormalized().GetRotationQuaternion();
        _aiReferenceState.PackageMotion = motion with
        {
            Animation = _baseResource,
            AnimationSha256 = _baseHash,
            Seconds = _baseClock.ElapsedSeconds,
            StartPending = false,
            Position = [position.X, position.Y, position.Z],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            Travel = null,
            Sleep = progress
        };
    }

    private void InterruptSleepFurniture()
    {
        if (_sleepPackage is null || _sleepProgress is not { Furniture: true } progress) return;
        _requestedSelection ??= _furniturePendingSelection;
        _furniturePendingSelection = null;
        ClearFurniture();
        _sleepProgress = progress with { Furniture = false };
        if (_aiReferenceState?.PackageMotion is { } motion)
            _aiReferenceState.PackageMotion = motion with { Sleep = _sleepProgress, Travel = null };
        PlayLocomotion(moving: false, publishPose: false);
    }
}
