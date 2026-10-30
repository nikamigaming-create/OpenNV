using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using Procedure = OpenNV.Runtime.Content.Definitions.FalloutAiProcedure;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private const string EatCaptureBlocker = "Eat source intent awaits its native physical continuation.";
    private FalloutEatPackage? _eatPackage;
    private FalloutActorEatPackageProgress? _eatProgress;
    private (FalloutPluginRecord Package, bool Initializing)? _pendingCurrentLocationEat;

    private int CurrentEatAiProcedure => _sitting == 4 ? (int)Procedure.GetUp :
        _nativeMarkerTravel is not null || _travelActive ? (int)Procedure.Travel :
        _aiReferenceState?.PackageMotion is { } motion && motion.Package == _eatPackage!.Form && motion.EatPackage is not null
        ? (int)(motion.Eat is not null ? Procedure.Eat : Procedure.Wander)
        : throw new NotSupportedException("Eat has no observed native procedure phase.");

    private void BeginEat(FalloutPluginRecord package, bool initializing)
    {
        var source = FalloutEatPackage.Read(package);
        var world = _aiWorld!;
        var actor = Appearance.Reference!.Value;
        var state = world.Get(actor);
        if (source.Location.Type == 2 && (!initializing || state.PackageStartLocation is null))
        {
            if (!IsInsideTree())
            {
                _pendingCurrentLocationEat = (package, initializing);
                _aiPackage = package; state.ProcedureCaptureBlocker = EatCaptureBlocker;
                return;
            }
            world.CaptureActorPackageStartLocation(actor, Skeleton.UnitsToMetres);
        }
        var progress = initializing && state.PackageMotion?.Package == package.FormKey ? state.PackageMotion.EatPackage : null;
        if (progress is null)
        {
            if (initializing && state.PackageAssignment is { Done: false } assignment && assignment.Package == package.FormKey)
                throw new NotSupportedException("Saved active Eat has no retained source procedure intent.");
            progress = new(source.Scope(world, actor).Cell);
        }
        source.Validate(_aiStack!, world, actor, progress);
        if (progress.Furniture) throw new InvalidDataException("Physical Eat has no furniture continuation.");
        _eatPackage = source; _eatProgress = progress; _aiPackage = package;
        if (_packageEvents!.Active is null)
        {
            if (initializing && state.PackageAssignment is { } assignment && assignment.Package == package.FormKey)
                assignment.Bind(_aiStack!, _packageEvents);
            else _packageEvents.Change(_packageIdleSource);
        }
        else if (_packageEvents.Active.Form != package.FormKey || _packageEvents.Done)
            throw new InvalidDataException("Eat differs from its active source package lifecycle.");
        if (state.PackageMotion?.Package != package.FormKey) state.ProcedureCaptureBlocker = EatCaptureBlocker;
    }

    private void AdvanceEat(double delta)
    {
        if (Combat is null || _aiError is not null || Combat.OwnsPose || !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try
        {
            if (_pendingCurrentLocationEat is { } pending)
            {
                _pendingCurrentLocationEat = null;
                BeginEat(pending.Package, pending.Initializing);
            }
            if (_eatPackage is not { } source || _eatProgress is not { } progress) return;
            source.Validate(_aiStack!, _aiWorld!, Appearance.Reference!.Value, progress);
            if (!_aiWorld!.SameTravelRegion(_aiWorld.Placement(Appearance.Reference!.Value).Cell, progress.Cell))
            {
                if (_nativeMarkerTravel is null)
                    BeginMarkerTravel(_aiPackage!, initializing: Combat.PackageMotion?.Package == source.Form && Combat.PackageMotion.Travel is not null,
                        FalloutPackageTravelGoal.Read(_aiStack!, _aiWorld, Appearance.Reference!.Value, _aiPackage!, eat: progress));
                AdvanceMarkerTravel(delta);
                if (Combat.PackageMotion?.Package == source.Form) Combat.SetEatPackageProgress(progress);
                return;
            }
            AdvanceEatInRegion(source, progress, delta);
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message; _failedPackage = _aiPackage?.FormKey;
            BlockSelectionCapture($"Eat continuation is unbound: {error.Message}");
            GD.PushError($"OPENNV_NATIVE_EAT_DIVERGENCE reference={Appearance.Reference}: {error.Message}");
        }
    }

    private void AdvanceEatInRegion(FalloutEatPackage source, FalloutActorEatPackageProgress progress, double delta)
    {
        if (progress.Furniture)
        {
            if (_furnitureApproaching) AdvanceFurnitureApproach(delta);
            if (FurniturePhase != 3) { ObserveEatFurniture(); return; }
            var seatedAction = FalloutActorEatPackageProgress.NextAction(source.Form, Combat!.PackageMotion, true, () => Combat.HasInventoryFood(source));
            if (seatedAction is FalloutEatContinuationAction.ResumeMeal or FalloutEatContinuationAction.SelectFood)
                Combat.AdvanceEatPackage(_aiPackage!, source, progress, delta);
            else
                Combat.HoldEatPackage(_aiPackage!, source, progress, delta);
            return;
        }
        if (_nativeMarkerTravel is not null) Combat!.RetirePackageMotionNavigation(source.Form);
        _nativeMarkerTravel = null; _nativeMarkerTravelProgress = null; _nativeMarkerTravelDestination = null;
        _travelPackage = null; _travelTarget = null; _travelPurpose = null; _nativeMarkerTravelRestored = false;

        var action = FalloutActorEatPackageProgress.NextAction(source.Form, Combat!.PackageMotion, false, () => Combat.HasInventoryFood(source));
        if (action == FalloutEatContinuationAction.ResumeMeal)
        {
            Combat.AdvanceEatPackage(_aiPackage!, source, progress, delta);
            return;
        }

        // Food is checked before either fallback clock. A different food must
        // not satisfy an authored restriction and keep this branch suppressed.
        if (action == FalloutEatContinuationAction.SelectFood)
        {
            if (Combat.PackageMotion?.Wander is not null) Combat.RetirePackageMotionNavigation(source.Form);
            if (source.Location.Type == 0 && source.Location.Form is { } target &&
                _aiStack!.GetEffective(_aiWorld!.Get(target).Base).Signature == "FURN")
            {
                var placed = _aiWorld.PlacedSource(target);
                Combat.HoldEatPackage(_aiPackage!, source, progress, delta);
                if (!TryBeginFurniturePackage(_aiPackage!, placed.Reference, _aiStack.GetEffective(placed.Reference.Base),
                    initializing: false, parentOwnsLifecycle: true)) return;
                _eatProgress = progress with { Seat = target, Furniture = true };
                Combat.SetEatPackageProgress(_eatProgress);
                AdvanceFurnitureApproach(delta);
                ObserveEatFurniture();
                return;
            }
            if (source.Scope(_aiWorld!, Appearance.Reference!.Value).Center is { } center)
            {
                var authored = GetParent<Node3D>().ToGlobal(new Vector3(center[0], center[2], -center[1]) * Skeleton.UnitsToMetres);
                var destination = Combat.ProjectPackageDestination(authored);
                var arrived = Combat.AdvancePackageMotion(_aiPackage!, destination, source.Location.Radius * Skeleton.UnitsToMetres,
                    source.Running, delta, source.WeaponDrawn, requireArrivalHeight: true,
                    navigationSha256: _aiWorld!.TravelNavigation(_aiWorld.Placement(Appearance.Reference.Value).Cell).SourceSha256);
                Combat.SetEatPackageProgress(progress);
                if (!arrived) return;
            }
            Combat.AdvanceEatPackage(_aiPackage!, source, progress, delta);
        }
        else
        {
            var clock = progress.AdvanceNoFood(delta, Combat.EatWanderCanRetry);
            _eatProgress = progress = clock.Progress;
            if (clock.ReturnToEat)
            {
                Combat.RetirePackageMotionNavigation(source.Form);
                Combat.HoldEatPackage(_aiPackage!, source, progress, delta);
            }
            else Combat.AdvanceEatWander(_aiPackage!, source, progress, delta);
        }
        if (_aiReferenceState?.ProcedureCaptureBlocker == EatCaptureBlocker)
            _aiReferenceState.ProcedureCaptureBlocker = null;
    }

    private void ObserveEatFurniture()
    {
        if (_eatProgress is not { Furniture: true } progress || _aiReferenceState?.PackageMotion is not { } motion) return;
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
            Wander = null,
            EatPackage = progress
        };
    }

    private bool RetainEatFurniture(FalloutPluginRecord package, FalloutScriptPackage declaration)
    {
        if (_seat?.Sleeping != false || _furnitureReference is not { } chair || FurniturePhase != 3) return false;
        var source = FalloutEatPackage.Read(package);
        if (!source.ContainsChair(_aiWorld!, Appearance.Reference!.Value, chair)) return false;
        var progress = new FalloutActorEatPackageProgress(source.Scope(_aiWorld!, Appearance.Reference.Value).Cell, chair, true);
        source.Validate(_aiStack!, _aiWorld!, Appearance.Reference.Value, progress);
        CancelIdle(); CancelProcedureIdle("eat"); ClearDialoguePackage();
        if (_aiPackage is { } previous) Combat!.RetirePackageMotionNavigation(previous.FormKey);
        _sandboxPackage = null; _sandboxWander = null; _pendingCurrentLocationSandbox = null;
        _sleepPackage = null; _sleepProgress = null; _sleepBeds = null;
        _furnitureSandboxActivity = false; _sandboxFurniturePackageExit = false; _findFurniture = null;
        _eatPackage = source; _eatProgress = progress; _aiPackage = package; _packageIdleSource = declaration;
        _packageIdles = new(declaration, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
        _packageEvents!.Change(declaration);
        _aiReferenceState!.PackageMotion = CaptureEatFurnitureMotion(package, progress);
        _aiReferenceState.ProcedureCaptureBlocker = FindFurnitureCaptureBlocker;
        return true;
    }

    internal bool OwnsEatIntent(FalloutEatPackage source, FalloutActorEatPackageProgress progress) =>
        ReferenceEquals(_eatPackage, source) && _eatProgress is { } current &&
        current.Cell == progress.Cell && current.Seat == progress.Seat && current.Furniture == progress.Furniture;

    internal FalloutActorEatPackageProgress UpdateEatIntent(FalloutEatPackage source, FalloutActorEatPackageProgress progress)
    {
        if (!OwnsEatIntent(source, progress)) throw new InvalidOperationException("Eat clock has no current package owner.");
        _eatProgress = progress;
        if (_aiReferenceState?.PackageMotion?.Package == source.Form) Combat!.SetEatPackageProgress(progress);
        return progress;
    }

    internal FalloutActorEatPackageProgress CurrentEatIntent(FalloutEatPackage source) =>
        ReferenceEquals(_eatPackage, source) && _eatProgress is { } progress ? progress :
            throw new InvalidOperationException("Eat request lost its source procedure generation.");

    internal void EatIdleAttempted(FalloutEatPackage source)
    {
        var progress = CurrentEatIntent(source);
        UpdateEatIntent(source, progress.AfterIdleAttempt(_aiReferenceState!.SoundRandom.NextUnitFloat()));
    }

    private void InterruptEatFurniture()
    {
        CancelProcedureIdle("eat");
        if (_eatPackage is null || _eatProgress is not { Furniture: true } progress) return;
        _requestedSelection ??= _furniturePendingSelection;
        _furniturePendingSelection = null;
        ClearFurniture();
        _eatProgress = progress with { Furniture = false, Seat = null };
        if (_aiReferenceState?.PackageMotion is { } motion)
            _aiReferenceState.PackageMotion = motion with { EatPackage = _eatProgress, Eat = null, Travel = null };
        PlayLocomotion(moving: false, publishPose: false);
    }

    internal FalloutActorPackageMotion CaptureEatFurnitureMotion(FalloutPluginRecord package,
        FalloutActorEatPackageProgress progress)
    {
        progress.Validate();
        if (!progress.Furniture || progress.Seat != _furnitureReference || FurniturePhase != 3 ||
            _seat?.Sleeping != false || _baseAnimation is null || _baseResource is null || _baseHash is null)
            throw new InvalidOperationException("Eating has no matching occupied physical chair and source clock.");
        var position = GlobalPosition;
        var rotation = GlobalBasis.Orthonormalized().GetRotationQuaternion();
        return new(package.FormKey, FalloutActorFurnitureContinuation.RecordHash(package), _baseResource, _baseHash,
            _baseClock.ElapsedSeconds, _baseClock.StartPending,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W], EatPackage: progress);
    }
}
