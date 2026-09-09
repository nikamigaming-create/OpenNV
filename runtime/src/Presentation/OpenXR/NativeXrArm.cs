using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.OpenXR;

/// <summary>One anatomical arm on the player's existing source skeleton.</summary>
internal sealed class NativeXrArm
{
    private readonly Skeleton3D _skeleton;
    private readonly int _upper, _forearm, _hand, _foreTwist, _upperTwist;
    private readonly Transform3D _gripFromHand;
    private readonly Transform3D? _aimFromHand;
    private readonly (int Bone, int Input, Transform3D Open, Transform3D Closed, Transform3D Held)[] _fingers;
    private readonly float _upperLength, _forearmLength;
    private readonly Vector3 _restPole;
    private readonly float[] _amounts = new float[3];
    private Transform3D? _lastTrackedGrip;
    private Basis? _lastTrackedAim;
    private float _reachError, _shoulderShift;
    private float _targetReachError;
    internal float WristErrorMeters { get; private set; }
    private bool _tracked;
    internal bool Left { get; }
    internal Transform3D HandTarget(Transform3D grip, Basis? aim = null)
        => aim is { } basis && _aimFromHand is { } attachment ? new Transform3D(basis, grip.Origin) * attachment : grip * _gripFromHand;
    internal Transform3D GripFromHand(Transform3D hand, bool aiming = false)
        => hand * (aiming ? _aimFromHand ?? _gripFromHand : _gripFromHand).AffineInverse();
    internal Transform3D SourceGrip(bool aiming = false) => GripFromHand(_skeleton.GetBoneGlobalPose(_hand), aiming);
    internal bool CanReach(Transform3D hand)
        => ReachLimit(hand).Error <= .001f;
    internal Transform3D ConstrainHandTarget(Transform3D hand)
    {
        var constrained = ReachLimit(hand);
        _targetReachError = constrained.Error;
        return constrained.Pose;
    }
    private (Transform3D Pose, float Error) ReachLimit(Transform3D hand)
    {
        var local = _skeleton.GlobalTransform.AffineInverse() * hand;
        var shoulder = ShoulderFrame * _skeleton.GetBoneGlobalRest(_upper).Origin;
        var offset = local.Origin - shoulder;
        var distance = offset.Length();
        var reach = Math.Clamp(distance, MathF.Abs(_upperLength - _forearmLength) + .001f, _upperLength + _forearmLength + .15f - .001f);
        local.Origin = shoulder + (distance > .00001f ? offset / distance : Vector3.Down) * reach;
        return (_skeleton.GlobalTransform * local, MathF.Abs(distance - reach));
    }
    internal object State => new
    {
        side = Left ? "left" : "right",
        tracked = _tracked,
        grip = _amounts[0],
        trigger = _amounts[1],
        thumb = _amounts[2],
        reachErrorMeters = Math.Max(_reachError, _targetReachError),
        wristErrorMeters = WristErrorMeters,
        shoulderShiftMeters = _shoulderShift,
        calibration = "openxr-grip-palmar-normal-v3;weapon-source-muzzle-to-tracked-aim-v2",
        lossPolicy = "hold-last-valid-grip; input-disabled"
    };

    internal NativeXrArm(RuntimeNativeNifSkeleton source, bool left, Transform3D[] closed, Basis? sourceAim = null)
    {
        Left = left; _skeleton = source.Node;
        var prefix = left ? "Bip01 L " : "Bip01 R ";
        _upper = source.BoneIndex(prefix + "UpperArm");
        _forearm = source.BoneIndex(prefix + "Forearm");
        _hand = source.BoneIndex(prefix + "Hand");
        _foreTwist = source.BoneIndex(prefix + "ForeTwist");
        _upperTwist = source.BoneIndex(left ? "Bip01 LUpArmTwistBone" : "Bip01 RUpArmTwistBone");
        if (_skeleton.GetBoneParent(_foreTwist) != _forearm || _skeleton.GetBoneParent(_upperTwist) != _upper)
            throw new InvalidDataException("Source arm twist helpers do not belong to their anatomical segments.");
        var shoulder = _skeleton.GetBoneGlobalRest(_upper).Origin;
        var elbow = _skeleton.GetBoneGlobalRest(_forearm).Origin;
        var wrist = _skeleton.GetBoneGlobalRest(_hand);
        _upperLength = shoulder.DistanceTo(elbow); _forearmLength = elbow.DistanceTo(wrist.Origin);
        _restPole = (elbow - shoulder).Normalized();
        var middle = _skeleton.GetBoneGlobalRest(source.BoneIndex(prefix + "Finger2")).Origin;
        var index = _skeleton.GetBoneGlobalRest(source.BoneIndex(prefix + "Finger1")).Origin;
        var little = _skeleton.GetBoneGlobalRest(source.BoneIndex(prefix + "Finger4")).Origin;
        var skeletonFromGrip = GripFrame(wrist.Origin, middle, index, little, left);
        _gripFromHand = skeletonFromGrip.AffineInverse() * wrist;
        if (sourceAim is { } aim)
        {
            var palmInHand = wrist.AffineInverse() * skeletonFromGrip.Origin;
            wrist = _skeleton.GetBoneGlobalPose(_hand);
            skeletonFromGrip = new(aim.Orthonormalized(), wrist * palmInHand);
            _aimFromHand = skeletonFromGrip.AffineInverse() * wrist;
        }
        _fingers = Enumerable.Range(0, _skeleton.GetBoneCount()).Select(bone =>
        {
            var name = _skeleton.GetBoneName(bone).ToString();
            var input = !name.StartsWith(prefix, StringComparison.Ordinal) ? -1 :
                name[prefix.Length..].StartsWith("Thumb", StringComparison.Ordinal) ? 2 :
                name[prefix.Length..].StartsWith("Finger1", StringComparison.Ordinal) ? 1 :
                name[prefix.Length..].StartsWith("Finger", StringComparison.Ordinal) ? 0 : -1;
            return (Bone: bone, Input: input, Open: _skeleton.GetBoneRest(bone), Closed: closed[bone], Held: _skeleton.GetBonePose(bone));
        }).Where(value => value.Input >= 0).ToArray();
    }

    internal static Transform3D GripFrame(Vector3 wrist, Vector3 middle, Vector3 index, Vector3 little, bool left)
    {
        // OpenXR grip -Z runs across the curled fingers, little to index.
        // +X points away from the left palm and into the right palm. The
        // metacarpal direction is not grip-forward (nor controller aim).
        var along = (middle - wrist).Normalized();
        var across = (index - little).Normalized();
        // The normal must point out of the palm, not out of the back of the
        // hand. Reversing it rolls both anatomical hands 180 degrees around
        // grip Z even though their wrist positions still match perfectly.
        var palm = along.Cross(across).Normalized() * (left ? 1 : -1);
        var x = palm * (left ? 1 : -1);
        var z = -across;
        var y = z.Cross(x).Normalized();
        x = y.Cross(z).Normalized();
        if (x.LengthSquared() < .99f || y.LengthSquared() < .99f || z.LengthSquared() < .99f)
            throw new InvalidDataException("Source hand has no independent palm and knuckle axes.");
        return new(new Basis(x, y, z), (wrist + middle) / 2);
    }

    internal void Publish(Transform3D worldFromGrip, bool tracked, double delta, float grip, float trigger, bool thumb, bool weaponHeld,
        Basis? worldFromAim = null, Transform3D? contactHand = null)
    {
        _tracked = tracked;
        if (tracked) { _lastTrackedGrip = worldFromGrip; _lastTrackedAim = worldFromAim; }
        if (_lastTrackedGrip is not { } retained) return;
        var attachment = _gripFromHand;
        if (weaponHeld && _aimFromHand is { } aimFromHand && _lastTrackedAim is { } aim)
        {
            // The held hand/weapon still has one chain. Aim supplies orientation;
            // the physical grip supplies the palm position, never the ray origin.
            retained.Basis = aim;
            attachment = aimFromHand;
        }
        var target = _skeleton.GlobalTransform.AffineInverse() * (contactHand ?? retained * attachment);
        // The prepared torso owns the clavicle. Keep every arm rest reference
        // in that same frame so skin across the shoulder stays connected.
        var shoulderFrame = ShoulderFrame;
        var shoulderPose = shoulderFrame * _skeleton.GetBoneGlobalRest(_upper);
        var elbowPose = shoulderFrame * _skeleton.GetBoneGlobalRest(_forearm);
        var wristPose = shoulderFrame * _skeleton.GetBoneGlobalRest(_hand);
        var shoulder = shoulderPose.Origin;
        var direction = target.Origin - shoulder;
        var distance = direction.Length();
        if (distance < .0001f) return;
        direction /= distance;
        // Explicit, bounded shoulder protraction; source limb lengths remain
        // unchanged. Excess reach is reported instead of silently stretching.
        _shoulderShift = Math.Clamp(distance - (_upperLength + _forearmLength - .001f), 0, .15f);
        shoulder += direction * _shoulderShift;
        distance -= _shoulderShift;
        var reach = Math.Clamp(distance, MathF.Abs(_upperLength - _forearmLength) + .001f, _upperLength + _forearmLength - .001f);
        _reachError = MathF.Abs(distance - reach);
        var wrist = shoulder + direction * reach;
        var restPole = shoulderFrame.Basis * _restPole;
        var pole = restPole - direction * restPole.Dot(direction);
        if (pole.LengthSquared() < .0001f) pole = Vector3.Back - direction * Vector3.Back.Dot(direction);
        pole = pole.Normalized();
        var along = (_upperLength * _upperLength - _forearmLength * _forearmLength + reach * reach) / (2 * reach);
        var height = MathF.Sqrt(Math.Max(0, _upperLength * _upperLength - along * along));
        var elbow = shoulder + direction * along + pole * height;
        var sourceUpper = (elbowPose.Origin - shoulderPose.Origin).Normalized();
        var sourceLower = (wristPose.Origin - elbowPose.Origin).Normalized();
        var solvedUpper = (elbow - shoulder).Normalized();
        var forearmAxis = (wrist - elbow).Normalized();
        // Carry the source elbow bend plane through the solve. Independent
        // shortest-arc rotations introduce a different roll on either side of
        // the elbow and tear skin weighted across those bones.
        var upperRotation = BendFrame(solvedUpper, forearmAxis) * BendFrame(sourceUpper, sourceLower).Inverse();
        var lowerRotation = new Basis(new Quaternion(upperRotation * sourceLower, forearmAxis)) * upperRotation;
        var difference = (target.Basis.Orthonormalized() * (lowerRotation * wristPose.Basis).Orthonormalized().Inverse()).GetRotationQuaternion();
        var projected = forearmAxis * new Vector3(difference.X, difference.Y, difference.Z).Dot(forearmAxis);
        var twist = new Quaternion(projected.X, projected.Y, projected.Z, difference.W);
        var wristRotation = twist.LengthSquared() > .00001f ? new Basis(twist.Normalized()) * lowerRotation : lowerRotation;
        SetGlobalPose(_upper, new(upperRotation * shoulderPose.Basis, shoulder));
        SetGlobalPose(_forearm, new(lowerRotation * elbowPose.Basis, elbow));
        // Authored helper weights distribute roll toward the shoulder/wrist.
        // Keep pronation off the elbow's main forearm bone; rolling that whole
        // segment collapses vertices shared with the upper arm.
        var shoulderSwing = new Basis(new Quaternion(sourceUpper, solvedUpper));
        var upperHelper = shoulderFrame * _skeleton.GetBoneGlobalRest(_upperTwist);
        SetGlobalPose(_upperTwist, new(shoulderSwing.Slerp(upperRotation, .5f) * upperHelper.Basis,
            shoulder + upperRotation * (upperHelper.Origin - shoulderPose.Origin)));
        var foreHelper = shoulderFrame * _skeleton.GetBoneGlobalRest(_foreTwist);
        SetGlobalPose(_foreTwist, new(wristRotation * foreHelper.Basis,
            elbow + lowerRotation * (foreHelper.Origin - elbowPose.Origin)));
        SetGlobalPose(_hand, new(target.Basis, wrist));
        WristErrorMeters = (_skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_hand)).Origin
            .DistanceTo((_skeleton.GlobalTransform * target).Origin);
        var blend = 1 - MathF.Exp(-30 * (float)delta);
        if (tracked)
        {
            _amounts[0] = Mathf.Lerp(_amounts[0], grip, blend);
            _amounts[1] = Mathf.Lerp(_amounts[1], trigger, blend);
            _amounts[2] = Mathf.Lerp(_amounts[2], thumb ? 1 : 0, blend);
        }
        foreach (var finger in _fingers)
        {
            var closed = weaponHeld ? finger.Held : finger.Closed;
            var amount = weaponHeld && finger.Input == 0 ? 1 : _amounts[finger.Input];
            _skeleton.SetBonePose(finger.Bone, finger.Open.InterpolateWith(closed, amount));
        }
    }

    private static Basis BendFrame(Vector3 upper, Vector3 lower)
    {
        var normal = upper.Cross(lower).Normalized();
        if (normal.LengthSquared() < .99f) throw new InvalidDataException("Source arm bend plane is degenerate.");
        return new(upper, normal.Cross(upper).Normalized(), normal);
    }

    private Transform3D ShoulderFrame
    {
        get
        {
            var parent = _skeleton.GetBoneParent(_upper);
            return _skeleton.GetBoneGlobalPose(parent) * _skeleton.GetBoneGlobalRest(parent).AffineInverse();
        }
    }

    private void SetGlobalPose(int bone, Transform3D pose)
    {
        var parent = _skeleton.GetBoneParent(bone);
        _skeleton.SetBonePose(bone, parent < 0 ? pose : _skeleton.GetBoneGlobalPose(parent).AffineInverse() * pose);
    }
}
