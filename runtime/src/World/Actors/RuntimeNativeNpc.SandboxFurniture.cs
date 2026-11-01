using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc : INativeSandboxActivityOwner
{
    private bool _sandboxFurniturePackageExit;
    private PackageSelection? _furniturePendingSelection;

    bool INativeSandboxActivityOwner.Supports(FalloutSandboxActivity activity) =>
        activity is FalloutSandboxActivity.Furniture or FalloutSandboxActivity.Sleep or FalloutSandboxActivity.IdleMarker;

    bool INativeSandboxActivityOwner.ReleaseForReselection(FalloutSandboxActivityProgress activity)
    {
        if (activity.Activity == FalloutSandboxActivity.IdleMarker) return ReleaseSandboxMarkerForReselection();
        if (!_furnitureSandboxActivity) return true;
        if (_reservedFurniture != activity.Target)
            throw new InvalidOperationException("Sandbox furniture reservation differs from its selected activity.");
        // Sandbox owns no package idle collection or queued collection request.
        // A current explicit/response IDLE and finite entry/exit retain their
        // real owner until it settles; elapsed game time alone cannot retire it.
        if (_packageIdles is not null)
            throw new InvalidOperationException("Sandbox furniture acquired another package's idle collection.");
        if (_furnitureApproaching || _sitting is 2 or 4 || _idlePlayback is not null || _responseIdleActive) return false;
        if (_sitting != 1) throw new InvalidOperationException("Sandbox furniture has no physical procedure phase.");
        _sandboxFurniturePackageExit = false;
        Combat!.ObserveSandboxPhase(1);
        _furnitureOccupied = Transform;
        _sitting = 4;
        StartFurnitureAnimation();
        return false;
    }

    void INativeSandboxActivityOwner.Advance(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutSandboxActivityProgress activity, double delta)
    {
        if (activity.Activity == FalloutSandboxActivity.IdleMarker)
        { AdvanceSandboxMarker(package, source, activity, delta); return; }
        if (_idlePlayback is not null || _responseIdleActive) return;
        var nativePhase = Combat!.CurrentSandboxActivity?.NativePhase;
        if (nativePhase == 1 && _furnitureSandboxActivity && _sitting == 4) return;
        if (nativePhase is null && _furnitureSandboxActivity && _sitting == 1)
        {
            activity = Combat.ObserveSandboxPhase(3);
            nativePhase = 3;
        }
        if (nativePhase is null or 1)
        {
            if (!IdleRequestsReady || _idleRetirementPending) return;
            activity = Combat.ObserveSandboxPhase(2);
        }
        else if (nativePhase == 2 && _furnitureSandboxActivity && _sitting is 1 or 2 or 4)
            activity = Combat.ObserveSandboxPhase(3);
        var target = activity.Target ?? throw new InvalidDataException("Furniture activity has no selected reference.");
        if (!_furnitureSandboxActivity)
        {
            var selected = _aiWorld!.PlacedSource(target);
            if (_aiStack!.GetEffective(selected.Reference.Base).Signature != "FURN")
                throw new InvalidDataException("Furniture activity target is not a winning FURN reference.");
            if (!TryBeginFurniturePackage(package, selected.Reference, _aiStack.GetEffective(selected.Reference.Base),
                initializing: false, sandboxActivity: true))
                return; // Contention retains the selected target, clock and RNG.
        }
        if (_reservedFurniture != target) throw new InvalidOperationException("Furniture activity changed its selected target.");
        if (_furnitureApproaching) AdvanceFurnitureApproach(delta);
        if (_sitting is 1 or 2 or 4) ObserveSandboxFurniturePose(activity);
    }

    private void InterruptSandboxFurniture()
    {
        if (!_furnitureSandboxActivity) return;
        // Combat/death takes the body immediately; it cannot wait on a chair
        // exit KF. Release the reservation while retaining Sandbox's selection
        // and clock for its eventual ordinary resume.
        _requestedSelection ??= _furniturePendingSelection;
        _furniturePendingSelection = null;
        ClearFurniture();
        // The combat owner keeps the published skeleton and current transform.
        // Bind the next legitimate standing base without publishing over it, so
        // retirement/reconstruction cannot bind a chair clock to locomotion.
        PlayLocomotion(moving: false, publishPose: false);
        if (_aiReferenceState?.PackageMotion?.Sandbox is { } activity)
            ObserveSandboxFurniturePose(activity);
        _sandboxWander = null;
    }

    private void ObserveSandboxFurniturePose(FalloutSandboxActivityProgress activity)
    {
        var motion = _aiReferenceState!.PackageMotion ?? throw new InvalidOperationException("Sandbox furniture has no motion observation.");
        var rotation = GlobalBasis.Orthonormalized().GetRotationQuaternion();
        var position = GlobalPosition;
        _aiReferenceState.PackageMotion = motion with
        {
            Animation = _baseResource,
            AnimationSha256 = _baseHash,
            Seconds = _baseClock.ElapsedSeconds,
            StartPending = false,
            Position = [position.X, position.Y, position.Z],
            Rotation = [rotation.X, rotation.Y, rotation.Z, rotation.W],
            Sandbox = Combat?.CurrentSandboxActivity ?? activity,
            Wander = null,
            Eat = null
        };
    }
}
