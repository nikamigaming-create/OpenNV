using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

// A post-animation head rotation. Rest-space axes come from the owned rig;
// source animation is restored before evaluating the following KF publication.
internal sealed class NativeHeadTrackingPose
{
    private readonly Skeleton3D _skeleton;
    private readonly int _bone;
    private readonly int _parent;
    private readonly Vector3 _headForward;
    private readonly Vector3 _parentForward;
    private readonly FalloutBodyPartLook _part;
    private readonly FalloutLookSettings _settings;
    private readonly float _unitsToMetres;
    private Quaternion? _previous;
    private Quaternion? _authored;
    private bool _active;
    private bool _clamped;
    private bool _inRange;
    private bool _overridden;
    private float _step;
    private Vector3? _target;

    internal NativeHeadTrackingPose(Skeleton3D skeleton, int bone, FalloutBodyPartLook part,
        FalloutLookSettings settings, float unitsToMetres)
    {
        _skeleton = skeleton; _bone = bone; _part = part; _settings = settings; _unitsToMetres = unitsToMetres;
        _parent = skeleton.GetBoneParent(bone);
        if (_parent < 0) throw new InvalidDataException("Head-tracking bone has no source parent.");
        _headForward = (skeleton.GetBoneGlobalRest(bone).Basis.Inverse() * Vector3.Forward).Normalized();
        _parentForward = (skeleton.GetBoneGlobalRest(_parent).Basis.Inverse() * Vector3.Forward).Normalized();
    }

    internal Vector3 WorldPosition => _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_bone).Origin;
    internal object State => new
    {
        bone = _skeleton.GetBoneName(_bone).ToString(),
        parent = _skeleton.GetBoneName(_parent).ToString(),
        headForward = Values(_headForward),
        parentForward = Values(_parentForward),
        target = _target is { } target ? Values(target) : null,
        active = _active,
        clamped = _clamped,
        inRange = _inRange,
        animationOverride = _overridden,
        stepRadians = _step,
        rotation = _previous is { } rotation ? new[] { rotation.X, rotation.Y, rotation.Z, rotation.W } : null,
    };

    internal void RestoreAuthoredPose()
    {
        if (_authored is not { } pose) return;
        _skeleton.SetBonePoseRotation(_bone, pose);
        _authored = null;
    }

    internal void Publish(Vector3? targetWorld, float animationOverride)
    {
        if (!float.IsFinite(animationOverride)) throw new InvalidDataException("Head animation override is not finite.");
        var authored = _skeleton.GetBonePoseRotation(_bone).Normalized();
        _target = targetWorld;
        _clamped = false; _inRange = false; _step = 0;
        // Engine interpretation of the declared float channel, not a fitted
        // actor/sequence name. Authored values at/above 90 suppress Look IK.
        _overridden = animationOverride >= 90;
        var desired = authored;
        if (!_overridden && targetWorld is { } target)
        {
            var head = _skeleton.GetBoneGlobalPose(_bone);
            var difference = _skeleton.GlobalTransform.AffineInverse() * target - head.Origin;
            var distance = (target - WorldPosition).Length() / _unitsToMetres;
            _inRange = distance >= _settings.MinimumDistance && distance <= _settings.MaximumDistance && distance > 0;
            if (_inRange)
            {
                var parent = _skeleton.GetBoneGlobalPose(_parent).Basis.Orthonormalized();
                var direction = ClampDirection(parent * _parentForward, difference.Normalized(), _part.ConeRadians, out _clamped);
                var rotation = head.Basis.Orthonormalized().GetRotationQuaternion();
                var arc = new Quaternion(rotation * _headForward, direction);
                desired = (parent.GetRotationQuaternion().Inverse() * arc * rotation).Normalized();
            }
        }
        var tracking = !_overridden && _inRange;
        if (!tracking && !_active) { _previous = authored; return; }
        var previous = _previous ?? authored;
        var differenceAngle = previous.AngleTo(desired);
        if (!tracking && differenceAngle < Mathf.DegToRad(_settings.EasingStopDegrees))
        {
            _active = false; _previous = authored; return;
        }
        var maximum = Mathf.DegToRad(tracking ? _settings.MaximumStepDegrees : _settings.EasingStepDegrees);
        var published = differenceAngle > maximum && differenceAngle > 0 ? previous.Slerp(desired, maximum / differenceAngle).Normalized() : desired;
        _step = previous.AngleTo(published);
        _authored = authored;
        _skeleton.SetBonePoseRotation(_bone, published);
        _previous = published;
        _active = true;
    }

    internal static Vector3 ClampDirection(Vector3 axis, Vector3 direction, float cone, out bool clamped)
    {
        var dot = Mathf.Clamp(axis.Dot(direction), -1, 1);
        clamped = dot < MathF.Cos(cone);
        if (!clamped) return direction;
        var cross = axis.Cross(direction);
        if (cross.LengthSquared() <= float.Epsilon)
            throw new NotSupportedException("Antipodal head target has no verified cone-plane owner.");
        return (new Quaternion(cross.Normalized(), cone) * axis).Normalized();
    }

    private static float[] Values(Vector3 value) => [value.X, value.Y, value.Z];
}
