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
    internal bool PackageMovementReady => _context?.Player() is { CollisionResident: true } &&
        _context.Resident(_actor.GlobalPosition) && _world.IsEnabled(_state.Reference) &&
        !Dead && !_state.Unconscious && !_state.Restrained;
    internal RuntimeNativePlayer? PackagePlayer => _context?.Player();

    internal void StopPackageMotion()
    {
        PackageOwnsPose = false;
        if (!OwnsPose && _mover is not null) _mover.Velocity = Vector3.Zero;
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
        bool running, double delta)
    {
        StopPackageMotion();
        if (OwnsPose || !PackageMovementReady) return;
        PrepareMovement();
        if (_packageIdle is null)
        {
            var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
            _packageIdle = new(SelectPath(directory, "mtidle", "locomotion/mtidle"), _content, _skeleton, null, true);
            _packageWalk = new(SelectPath(directory, "mtforward", "locomotion/mtforward"), _content, _skeleton, null, true);
            _packageRun = new(SelectPath(directory, "mtfastforward", "locomotion/mtfastforward", "mtforward", "locomotion/mtforward"),
                _content, _skeleton, null, true);
            _packageSounds = new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom);
            _actor.AddChild(_packageSounds);
        }
        if (!_packageHashes.TryGetValue(package.FormKey, out var hash))
            _packageHashes.Add(package.FormKey, hash = Convert.ToHexString(SHA256.HashData(package.ReadData())));
        var offset = target - _actor.GlobalPosition;
        var moving = new Vector2(offset.X, offset.Z).Length() > distance;
        var clip = moving ? running ? _packageRun! : _packageWalk! : _packageIdle;
        var retained = _state.PackageMotion;
        var same = retained?.Package == package.FormKey && retained.Animation.Equals(clip.Path, StringComparison.OrdinalIgnoreCase);
        if (same && (!retained!.PackageSha256.Equals(hash, StringComparison.OrdinalIgnoreCase) ||
            !retained.AnimationSha256.Equals(clip.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Saved package animation differs from its winning source.");
        var seconds = same ? retained!.Seconds : 0;
        var includeStart = !same || retained!.StartPending;
        var next = seconds + delta;
        if (moving) TurnToward(PursuitTarget(target, delta), delta);
        Activity.SetMovement(running && moving, sneaking: false);
        MoveActor(moving ? clip.RootDisplacement(seconds, next) : Vector3.Zero, delta);
        foreach (var key in clip.Events.Crossed(seconds, next, includeStart)) _packageSounds!.Dispatch(key);
        _skeleton.Node.ResetBonePoses();
        var root = _skeleton.BoneIndex(clip.Animation.Sequence.TargetName);
        if (root >= 0) _skeleton.Node.SetBonePose(root, Transform3D.Identity);
        clip.Animation.ApplySourceTime(clip.Time(next));
        var position = _actor.GlobalPosition; var rotation = _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion();
        _state.PackageMotion = new(package.FormKey, hash, clip.Path, clip.Hash, next, false,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W]);
        PackageOwnsPose = true;
    }
}
