using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.OpenXR;

// Retains source animated foot targets in the grounded player frame while the
// tracked pelvis changes height/position. Source segment lengths never stretch.
internal sealed class NativeXrLeg
{
    private readonly Skeleton3D _skeleton;
    private readonly int _thigh, _calf, _foot;
    private readonly float _upperLength, _lowerLength;
    internal float FootErrorMeters { get; private set; }
    internal NativeXrLeg(RuntimeNativeNifSkeleton source, bool left)
    {
        _skeleton = source.Node;
        var prefix = left ? "Bip01 L " : "Bip01 R ";
        _thigh = source.BoneIndex(prefix + "Thigh"); _calf = source.BoneIndex(prefix + "Calf"); _foot = source.BoneIndex(prefix + "Foot");
        _upperLength = _skeleton.GetBoneGlobalRest(_thigh).Origin.DistanceTo(_skeleton.GetBoneGlobalRest(_calf).Origin);
        _lowerLength = _skeleton.GetBoneGlobalRest(_calf).Origin.DistanceTo(_skeleton.GetBoneGlobalRest(_foot).Origin);
    }
    internal Transform3D FootInActor => _skeleton.Transform * _skeleton.GetBoneGlobalPose(_foot);
    internal float RequiredPelvisDrop(Transform3D worldFoot)
    {
        var hip = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_thigh).Origin;
        var offset = hip - worldFoot.Origin;
        var reach = (_upperLength + _lowerLength - .001f) * _skeleton.GlobalBasis.Scale.X;
        var horizontalSquared = offset.X * offset.X + offset.Z * offset.Z;
        if (horizontalSquared >= reach * reach) return 0; // Report lateral reach through the foot solve.
        return Math.Max(0, offset.Y - MathF.Sqrt(reach * reach - horizontalSquared));
    }
    internal void Publish(Transform3D worldFoot)
    {
        var target = _skeleton.GlobalTransform.AffineInverse() * worldFoot;
        var thigh = _skeleton.GetBoneGlobalPose(_thigh);
        var calf = _skeleton.GetBoneGlobalPose(_calf);
        var foot = _skeleton.GetBoneGlobalPose(_foot);
        var difference = target.Origin - thigh.Origin;
        var length = difference.Length();
        if (length < .0001f) throw new InvalidOperationException("Tracked leg target coincides with its hip.");
        var direction = difference / length;
        var reach = Math.Clamp(length, MathF.Abs(_upperLength - _lowerLength) + .001f, _upperLength + _lowerLength - .001f);
        var pole = Vector3.Forward.Slide(direction);
        if (pole.LengthSquared() < .0001f) pole = Vector3.Down.Slide(direction);
        pole = pole.Normalized();
        var along = (_upperLength * _upperLength - _lowerLength * _lowerLength + reach * reach) / (2 * reach);
        var height = MathF.Sqrt(Math.Max(0, _upperLength * _upperLength - along * along));
        var knee = thigh.Origin + direction * along + pole * height;
        var ankle = thigh.Origin + direction * reach;
        var sourceUpper = (calf.Origin - thigh.Origin).Normalized();
        var sourceLower = (foot.Origin - calf.Origin).Normalized();
        var solvedUpper = (knee - thigh.Origin).Normalized();
        var solvedLower = (ankle - knee).Normalized();
        var rotation = Frame(solvedUpper, solvedLower) * Frame(sourceUpper, sourceLower).Inverse();
        var lowerRotation = new Basis(new Quaternion(rotation * sourceLower, solvedLower)) * rotation;
        Set(_thigh, new(rotation * thigh.Basis, thigh.Origin));
        Set(_calf, new(lowerRotation * calf.Basis, knee));
        Set(_foot, new(target.Basis, ankle));
        FootErrorMeters = MathF.Abs(length - reach) * _skeleton.GlobalBasis.Scale.X;
    }
    private static Basis Frame(Vector3 upper, Vector3 lower)
    {
        var normal = upper.Cross(lower);
        if (normal.LengthSquared() < .00001f) normal = upper.Cross(Vector3.Forward);
        if (normal.LengthSquared() < .00001f) normal = upper.Cross(Vector3.Right);
        normal = normal.Normalized();
        return new(upper, normal.Cross(upper).Normalized(), normal);
    }
    private void Set(int bone, Transform3D pose)
    {
        var parent = _skeleton.GetBoneParent(bone);
        _skeleton.SetBonePose(bone, _skeleton.GetBoneGlobalPose(parent).AffineInverse() * pose);
    }
}
