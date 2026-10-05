using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private Func<bool>? _furnitureCaptureReady;
    private Func<FalloutActorFurnitureContinuation?>? _furnitureCapture;

    private bool FurnitureProcedureActive => _findFurniture is not null || _seat is not null;
    private bool CanCaptureFurnitureContinuation() => FurnitureProcedureActive && !_bindingInitialBase &&
        !_furnitureApproaching && !_travelActive && _requestedSelection is null && _aiError is null &&
        AnimationError is null && _packageEvents is { Active: not null, Error: null } &&
        _packageIdleSource is not null && (_animation is null || CanCaptureFurnitureIdleAnimation()) && !_responseIdleActive &&
        _conversationTarget is null && Combat?.OwnsPose != true &&
        _baseClock.Resource.Length != 0 && _aiReferenceState?.ProcedureCaptureBlocker == FindFurnitureCaptureBlocker;

    private void BindFurnitureCapture()
    {
        if (_aiReferenceState is not { } state) return;
        state.CanCaptureFurniture = _furnitureCaptureReady = CanCaptureFurnitureContinuation;
        state.CaptureFurniture = _furnitureCapture = CaptureFurnitureContinuation;
    }

    private FalloutActorFurnitureContinuation? CaptureFurnitureContinuation()
    {
        if (!FurnitureProcedureActive) return null;
        if (!CanCaptureFurnitureContinuation())
            throw new NotSupportedException("Furniture still has an unowned approach, idle or source selection continuation.");
        var lifecycle = _packageEvents!;
        var state = new FalloutActorFurnitureContinuation(FalloutActorPackageAssignment.Capture(_aiStack!, lifecycle)!,
            lifecycle.Revision, lifecycle.LastEvent, lifecycle.LastPackage,
            lifecycle.LastPackage is { } previous ? FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(previous)) : null,
            SittingState, Math.Clamp(_furnitureSearchRemaining, 0, .5), WriteFurniturePose(Transform),
            _selectedSourcePackage, _pendingPackage?.FormKey, _aiRandom.State, Math.Max(0, _aiPollRemaining),
            _aiScheduleTime, _blink?.Capture(), _reservedFurniture,
            _seat is { } seat ? FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(seat.Furniture)) : null,
            _furnitureModel, _furnitureModelHash, _seat is null ? null : _seat with { PlacementOffset = (float[])_seat.PlacementOffset.Clone() },
            _seat is null ? null : WriteFurniturePose(_furnitureOccupied),
            _furnitureClip is { } clip ? new(clip.Idle, clip.IdleHash, clip.Path, clip.Nif.Sha256) : null,
            _furnitureInitialPlacement, CapturePackageIdleState(captureAnimation: true));
        state.Validate();
        return state;
    }

    private void RestoreFurnitureContinuation(FalloutActorFurnitureContinuation saved)
    {
        saved.Validate(_aiStack!, _aiWorld!, _aiReferenceState!);
        var package = _aiStack!.GetEffective(saved.Assignment.Package);
        var source = FalloutScriptPackage.Read(package);
        FurnitureClip? clip = null;
        if (saved.Furniture is { } furniture)
        {
            var reference = _aiCell!.References.Single(value => value.FormKey == furniture);
            var model = _aiCell.BaseObjects[reference.Base].ModelPath;
            var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned furniture files are absent.");
            if (!string.Equals(model, saved.Model, StringComparison.OrdinalIgnoreCase) ||
                !content.TryRead(saved.Model!, null, out var bytes, out _))
                throw new InvalidDataException("Saved furniture model differs from its winning source.");
            var nif = FalloutNifFile.Read(bytes);
            var seat = FalloutFurnitureSource.ReadSeats(_aiStack, _aiStack.GetEffective(reference.Base), nif)
                .SingleOrDefault(value => value.Index == saved.Seat!.Index);
            if (!nif.Sha256.Equals(saved.ModelSha256, StringComparison.OrdinalIgnoreCase) || seat is null ||
                seat.Furniture != saved.Seat!.Furniture || seat.MarkerId != saved.Seat.MarkerId || seat.Marker != saved.Seat.Marker ||
                seat.HeadingDelta != saved.Seat.HeadingDelta || !seat.PlacementOffset.SequenceEqual(saved.Seat.PlacementOffset))
                throw new InvalidDataException("Saved furniture marker, placement settings or NIF differs from its winning source.");
            clip = ReadFurnitureClip(_aiStack.GetEffective(saved.Clip!.Idle), saved.Phase == 3 ? 1 : saved.Phase);
            if (!clip.Path.Equals(saved.Clip.Resource, StringComparison.OrdinalIgnoreCase) ||
                !clip.Nif.Sha256.Equals(saved.Clip.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !clip.IdleHash.Equals(saved.Clip.IdleSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved furniture KF or selected IDLE differs from its winning source.");
            if (!_aiWorld!.OwnsFurnitureSeat(furniture, seat.Index, Appearance.Reference!.Value))
                throw new InvalidDataException("Saved furniture reservation has no matching shared owner.");
            _seat = seat; _reservedFurniture = furniture; _furnitureReference = furniture;
            _furnitureOccupied = ReadFurniturePose(saved.Occupied!);
            _furnitureModel = saved.Model; _furnitureModelHash = saved.ModelSha256;
            _furnitureInitialPlacement = saved.InitialPlacement;
        }
        _aiPackage = package; _packageIdleSource = source;
        _findFurniture = source.Procedure == 0 ? FalloutFindFurniturePackage.Read(package) : null;
        _packageIdles = new(source, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition));
        RestorePackageIdleState(saved.IdleState);
        _furnitureIdles = new(_aiStack, Appearance.SkeletonPath);
        _furnitureSearchRemaining = saved.SearchRemaining;
        _sitting = saved.Phase == 3 ? 1 : saved.Phase;
        _furnitureEntry = _sitting == 2 ? clip : null;
        _pendingPackage = saved.PendingPackage is { } pending ? _aiStack.GetEffective(pending) : null;
        _selectedSourcePackage = saved.SelectedPackage; _sourceSelectionKnown = true;
        if (_packageEvents!.Active is null) saved.Assignment.Bind(_aiStack, _packageEvents);
        else if (_packageEvents.Active.Form != saved.Assignment.Package || _packageEvents.Done != saved.Assignment.Done)
            throw new InvalidDataException("Restored furniture lifecycle disagrees with its retained package.");
        _packageEvents.RestoreHistory(saved.EventRevision, saved.LastEvent, saved.LastPackage);
        _aiRandom.Restore(saved.RandomState);
        if (saved.Blink is { } blink)
            (_blink ?? throw new InvalidDataException("Saved furniture has no source blink owner.")).Restore(blink);
        else if (_blink is not null) throw new InvalidDataException("Saved furniture is missing its source blink continuation.");
        Transform = ReadFurniturePose(saved.Pose);
        if (clip is not null) StartFurnitureAnimation(clip);
        else PlayLocomotion(false);
        if (saved.IdleState?.ActiveAnimation is { } animation) RestoreFurnitureIdleAnimation(animation);
        _aiQuestRevision = _questState!.Revision; _aiActivityRevision = Activity.Revision;
        _aiPollRemaining = saved.PollRemaining; _aiScheduleTime = saved.ScheduleTime;
        _aiReferenceState!.ProcedureCaptureBlocker = FindFurnitureCaptureBlocker;
    }

    private bool RetainFurnitureContinuation()
    {
        if (_aiReferenceState is not { } state) return false;
        var retained = CanCaptureFurnitureContinuation();
        if (retained)
        {
            var continuation = CaptureFurnitureContinuation() ??
                throw new InvalidOperationException("Capturable furniture lost its source continuation during retirement.");
            state.PackageAssignment = continuation.Assignment;
            state.FurnitureContinuation = continuation;
        }
        if (ReferenceEquals(state.CanCaptureFurniture, _furnitureCaptureReady)) state.CanCaptureFurniture = null;
        if (ReferenceEquals(state.CaptureFurniture, _furnitureCapture)) state.CaptureFurniture = null;
        return retained;
    }

    private static float[] WriteFurniturePose(Transform3D pose) =>
        [pose.Basis.X.X, pose.Basis.X.Y, pose.Basis.X.Z, pose.Basis.Y.X, pose.Basis.Y.Y, pose.Basis.Y.Z,
            pose.Basis.Z.X, pose.Basis.Z.Y, pose.Basis.Z.Z, pose.Origin.X, pose.Origin.Y, pose.Origin.Z];
    private static Transform3D ReadFurniturePose(float[] pose) => new(
        new Basis(new Vector3(pose[0], pose[1], pose[2]), new Vector3(pose[3], pose[4], pose[5]), new Vector3(pose[6], pose[7], pose[8])),
            new Vector3(pose[9], pose[10], pose[11]));

    private FalloutActorPackageIdleState CapturePackageIdleState(bool captureAnimation = false) => new(_packageIdleSource!.Form,
        FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(_packageIdleSource.Form)),
        _packageIdles!.Capture(), _idleReplays.Remaining.Select(value => new FalloutIdleReplayCooldown(value.Key,
            FalloutActorFurnitureContinuation.RecordHash(_aiStack.GetEffective(value.Key)), value.Value)).ToArray(), _packageIdleError,
        captureAnimation && _animation is not null ? CaptureFurnitureIdleAnimation() : null);

    private void RestorePackageIdleState(FalloutActorPackageIdleState? saved)
    {
        if (saved is null) return;
        saved.Validate(_aiStack!, _packageIdleSource!.Form);
        _packageIdles!.Restore(saved.Collection);
        _idleReplays.Restore(saved.Cooldowns.ToDictionary(value => value.Idle, value => value.Remaining));
        _packageIdleError = saved.Error;
    }
}
