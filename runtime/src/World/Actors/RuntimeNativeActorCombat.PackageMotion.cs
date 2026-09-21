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
    internal bool PackageMovementReady => _context?.Player() is { CollisionResident: true } &&
        _context.Resident(_actor.GlobalPosition) && _world.IsEnabled(_state.Reference) &&
        !Dead && !_state.Unconscious && !_state.Restrained;
    internal RuntimeNativePlayer? PackagePlayer => _context?.Player();
    internal FalloutFormKey? PackagePlayerCell => _context?.PlayerCell?.Invoke();
    internal FalloutActorPackageMotion? PackageMotion => _state.PackageMotion;
    internal uint PackageRandom(uint bound) => _state.SoundRandom.NextBounded(bound);
    internal void SetPatrolProgress(FalloutPatrolProgress progress)
    {
        if (_state.PackageMotion is { } motion) _state.PackageMotion = motion with { Patrol = progress };
    }
    internal void FacePackageDirection(Vector3 direction, double delta) => TurnToward(_actor.GlobalPosition + direction, delta);
    internal Vector3 ProjectPackageDestination(Vector3 authored)
    {
        PrepareMovement();
        var route = _context!.Route(authored, authored);
        if (route.Length == 0 || route[^1].DistanceTo(authored) > _radius * 6)
            throw new NotSupportedException("Patrol marker has no nearby authored navigation floor.");
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
        if (_state.Engagement is not null || _state.PackageMotion is not { } motion) return;
        motion.Validate();
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
        PreparePackageWeapon(weaponDrawn);
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
        MoveActor(moving ? clip.RootDisplacement(seconds, next) : Vector3.Zero, delta);
        foreach (var key in clip.Events.Crossed(seconds, next, includeStart)) _packageSounds!.Dispatch(key);
        _skeleton.Node.ResetBonePoses();
        var root = _skeleton.BoneIndex(clip.Animation.Sequence.TargetName);
        if (root >= 0) _skeleton.Node.SetBonePose(root, Transform3D.Identity);
        clip.Animation.ApplySourceTime(clip.Time(next));
        if (weaponDrawn) { _packageAim?.Animation.ApplySourceTime(_packageAim.Time(next)); _packageGrip?.Animation.ApplySourceTime(_packageGrip.Time(next)); }
        var position = _actor.GlobalPosition; var rotation = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        _state.PackageMotion = new(package.FormKey, hash, clip.Path, clip.Hash, next, false,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
            retained?.Package == package.FormKey ? retained.Patrol : null);
        PackageOwnsPose = true;
    }
}
