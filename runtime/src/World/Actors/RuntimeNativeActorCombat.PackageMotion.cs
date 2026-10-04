using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private NativeActorCombatAnimation? _packageWalk, _packageRun, _packageIdle;
    private NativeOwnedAnimationSoundPlayer? _packageSounds;
    private readonly Dictionary<FalloutFormKey, string> _packageHashes = [];
    internal bool PackageOwnsPose { get; private set; }
    internal bool PackageMoving { get; private set; }
    internal bool PackageMovementReady => _context is not null &&
        _context.Resident(_actor.GlobalPosition) && _world.IsEnabled(_state.Reference) &&
        !Dead && !_state.Unconscious && !_state.Restrained;
    internal bool PackageRouteRestorable => _routeSearch is null && _pursuitPath.Length > 0;
    internal FalloutTravelRouteFailure? PackageRouteFailure => _routeSearch is null && _routeDoor is null &&
        _pursuitPath.Length == 0 && _routeError is { } error && _routeFailures > 0
            ? new(error, _routeFailures, Math.Max(0, _routeClock)) : null;
    internal RuntimeNativePlayer? PackagePlayer => _context?.Player();
    internal FalloutFormKey? PackagePlayerCell => _context?.PlayerCell?.Invoke();
    internal FalloutActorPackageMotion? PackageMotion => _state.PackageMotion;
    internal uint PackageRandom(uint bound) => _state.SoundRandom.NextBounded(bound);
    internal void SetPatrolProgress(FalloutPatrolProgress progress)
    {
        if (_state.PackageMotion is { } motion) _state.PackageMotion = motion with { Patrol = progress };
    }
    internal void SetEscortProgress(FalloutEscortProgress progress)
    {
        if (_state.PackageMotion is { } motion) _state.PackageMotion = motion with { Escort = progress };
    }
    internal void SetEditorTravelProgress(FalloutEditorTravelProgress progress)
    {
        if (_state.PackageMotion is { } motion) _state.PackageMotion = motion with { EditorTravel = progress };
    }
    internal void SetTravelProgress(FalloutTravelProgress progress)
    {
        if (_state.PackageMotion is not { } motion) throw new NotSupportedException("Travel has no observed package motion.");
        if (progress.NavigationSha256 is not null)
            progress = progress with
            {
                RouteWaypoints = _pursuitPath.Select(point => new[] { point.X, point.Y, point.Z }).ToArray(),
                RouteCursor = _pursuitCursor,
                RouteFailure = progress.Complete ? null : PackageRouteFailure
            };
        _state.PackageMotion = motion with { Travel = progress };
    }
    internal void CompleteDialoguePackage()
    {
        if (_state.PackageMotion is not { } motion) throw new NotSupportedException("Dialogue completion has no observed package motion.");
        _state.PackageMotion = motion with { DialogueCompleted = true };
    }
    internal void FacePackageDirection(Vector3 direction, double delta) => TurnToward(_actor.GlobalPosition + direction, delta);
    internal Vector3 ProjectPackageDestination(Vector3 authored)
    {
        PrepareMovement();
        var route = _context!.Route(authored, authored);
        if (route.Length == 0 || route[^1].DistanceTo(authored) > _radius * 6)
            throw new NotSupportedException("Package destination has no nearby authored navigation floor.");
        return route[^1];
    }

    internal void StopPackageMotion()
    {
        PackageOwnsPose = false;
        PackageMoving = false;
        _packageWeapon?.Root.Hide();
        if (!OwnsPose && _mover is not null) _mover.Velocity = Vector3.Up * _mover.Velocity.Y;
    }

    internal void RestorePackageMotion()
    {
        if (_state.Engagement is not null) return;
        if (_state.FurnitureContinuation is not null) return;
        if (_state.SelectionFailure is not null) return;
        if (_state.DialogueContinuation is not null) return;
        // NPC binding can retry before its body enters the tree. Its registered
        // pre-begin owner already restored the actual pose; a retained motion
        // from the retired package must not overwrite it during Ready.
        if (_state.CanCapturePackageBindingFailure?.Invoke() == true) return;
        if (_state.PackageBindingFailure is { } stopped)
        {
            stopped.Validate();
            _actor.GlobalTransform = new(new Basis(new Vector3(stopped.Basis[0], stopped.Basis[1], stopped.Basis[2]),
                new Vector3(stopped.Basis[3], stopped.Basis[4], stopped.Basis[5]),
                new Vector3(stopped.Basis[6], stopped.Basis[7], stopped.Basis[8])),
                new(stopped.Position[0], stopped.Position[1], stopped.Position[2]));
            return;
        }
        if (_state.PackageMotion is not { } motion) return;
        if (_state.PackageAssignment is { } assignment && assignment.Package != motion.Package)
        {
            GD.Print($"OPENNV_NATIVE_PACKAGE_MOTION_STALE reference={_state.Reference} saved={motion.Package} current={assignment.Package} restored=false");
            _state.PackageMotion = null;
            return;
        }
        if (_actor is RuntimeNativeNpc { CurrentFurniture: not null }) return;
        motion.Validate();
        if (motion.Travel is { NavigationSha256: not null } travel)
        {
            _pursuitPath = travel.RouteWaypoints!.Select(point => new Vector3(point[0], point[1], point[2])).ToArray();
            _pursuitCursor = travel.RouteCursor;
            _routeTarget = new(travel.RouteTarget![0], travel.RouteTarget[1], travel.RouteTarget[2]);
            _routeClock = travel.RouteFailure?.RetrySeconds ?? 0;
            _routeError = travel.RouteFailure?.Error;
            _routeFailures = travel.RouteFailure?.Failures ?? 0;
            _routeStall = 0;
            _waypointDistance = float.PositiveInfinity;
        }
        _actor.GlobalTransform = new(new Basis(new Quaternion(motion.Rotation[0], motion.Rotation[1],
            motion.Rotation[2], motion.Rotation[3])).Scaled(_actor.Scale),
            new(motion.Position[0], motion.Position[1], motion.Position[2]));
    }

    internal void AdvancePackageMotion(FalloutPluginRecord package, Vector3 target, float distance,
        bool running, double delta, bool weaponDrawn = false, bool requireArrivalHeight = false)
    {
        StopPackageMotion();
        if (OwnsPose || !PackageMovementReady) return;
        PrepareMovement();
        if (_packageIdle is null)
        {
            var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
            _packageIdle = new(SelectPath(directory, "mtidle", "locomotion/mtidle"), _content, _skeleton, null, true);
            var gender = _actor is RuntimeNativeNpc npc && npc.Appearance.Female ? "female" : "male";
            _packageWalk = new(SelectPath(directory, "mtforward", $"locomotion/{gender}/mtforward", "locomotion/mtforward"), _content, _skeleton, null, true);
            _packageRun = new(SelectPath(directory, "mtfastforward", $"locomotion/{gender}/mtfastforward", "locomotion/mtfastforward", "mtforward", $"locomotion/{gender}/mtforward", "locomotion/mtforward"),
                _content, _skeleton, null, true);
            _packageSounds = new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom);
            _actor.AddChild(_packageSounds);
        }
        PreparePackageWeapon(package, weaponDrawn);
        if (!_packageHashes.TryGetValue(package.FormKey, out var hash))
            _packageHashes.Add(package.FormKey, hash = Convert.ToHexString(SHA256.HashData(package.ReadData())));
        var offset = target - _actor.GlobalPosition;
        var moving = (requireArrivalHeight ? offset.Length() : new Vector2(offset.X, offset.Z).Length()) > distance;
        var destination = moving ? PursuitTarget(target, delta, distance) : null;
        moving &= destination.HasValue;
        PackageMoving = moving;
        var clip = moving ? running ? _packageRun! : _packageWalk! : _packageIdle;
        var retained = _state.PackageMotion;
        var same = retained?.Package == package.FormKey && retained.Animation.Equals(clip.Path, StringComparison.OrdinalIgnoreCase);
        if (same && (!retained!.PackageSha256.Equals(hash, StringComparison.OrdinalIgnoreCase) ||
            !retained.AnimationSha256.Equals(clip.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Saved package animation differs from its winning source.");
        var seconds = same ? retained!.Seconds : 0;
        var includeStart = !same || retained!.StartPending;
        var next = seconds + delta;
        if (destination is { } waypoint) TurnToward(waypoint, delta);
        Activity.SetMovement(running && moving, sneaking: false);
        MoveActor(moving ? clip.RootDisplacement(seconds, next) : Vector3.Zero, delta, destination);
        foreach (var key in clip.Events.Crossed(seconds, next, includeStart)) _packageSounds!.Dispatch(key);
        _skeleton.Node.ResetBonePoses();
        var root = _skeleton.BoneIndex(clip.Animation.Sequence.TargetName);
        if (root >= 0) _skeleton.Node.SetBonePose(root, Transform3D.Identity);
        clip.Animation.ApplySourceTime(clip.Time(next));
        if (weaponDrawn) { _packageAim?.Animation.ApplySourceTime(_packageAim.Time(next)); _packageGrip?.Animation.ApplySourceTime(_packageGrip.Time(next)); }
        var position = _actor.GlobalPosition; var rotation = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        _state.PackageMotion = new(package.FormKey, hash, clip.Path, clip.Hash, next, false,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
            retained?.Package == package.FormKey ? retained.Patrol : null,
            retained?.Package == package.FormKey ? retained.Escort : null,
            retained?.Package == package.FormKey ? retained.EditorTravel : null,
            retained?.Package == package.FormKey && retained.DialogueCompleted,
            retained?.Package == package.FormKey ? retained.Travel : null,
            retained?.Package == package.FormKey ? retained.Guard : null);
        PackageOwnsPose = true;
    }
}
