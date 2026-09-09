using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.OpenXR;

// Position constraints preserve the authored segment lengths. The tracked head
// supplies the final orientation; unreachable poses stay measurable, not stretched.
internal sealed class NativeXrSpine
{
    private readonly Skeleton3D _skeleton;
    private readonly int[] _bones;
    private readonly Transform3D[] _rest;
    private readonly Vector3[] _points;
    private readonly float[] _lengths;
    private readonly float _reach;
    internal float HeadErrorMeters { get; private set; }

    internal float PelvisHeightCorrection(Transform3D worldHead)
    {
        var spine = _skeleton.GlobalTransform * _rest[0].Origin;
        var distance = _rest[0].Origin.DistanceTo(_rest[^1].Origin) * _skeleton.GlobalBasis.Scale.X;
        var difference = worldHead.Origin - spine;
        var vertical = MathF.Sqrt(Math.Max(0, distance * distance - difference.X * difference.X - difference.Z * difference.Z));
        return worldHead.Origin.Y - vertical - spine.Y;
    }

    internal NativeXrSpine(RuntimeNativeNifSkeleton source)
    {
        _skeleton = source.Node;
        var chain = new List<int>();
        var root = source.BoneIndex("Bip01 Spine");
        for (var bone = source.BoneIndex("Bip01 Head"); bone != root; bone = _skeleton.GetBoneParent(bone))
        {
            if (bone < 0) throw new InvalidDataException("Head has no source spine ancestor.");
            chain.Add(bone);
        }
        chain.Add(root); chain.Reverse(); _bones = chain.ToArray();
        _rest = _bones.Select(_skeleton.GetBoneGlobalRest).ToArray();
        _points = new Vector3[_bones.Length];
        _lengths = Enumerable.Range(0, _bones.Length - 1).Select(index => _rest[index].Origin.DistanceTo(_rest[index + 1].Origin)).ToArray();
        _reach = _lengths.Sum();
        if (_lengths.Any(length => length < .00001f)) throw new NotSupportedException("Spine has a zero-length source segment.");
    }

    internal void Publish(Transform3D worldHead)
    {
        var target = _skeleton.GlobalTransform.AffineInverse() * worldHead;
        for (var index = 0; index < _points.Length; index++) _points[index] = _rest[index].Origin;
        var root = _points[0];
        if (root.DistanceTo(target.Origin) >= _reach)
        {
            var direction = (target.Origin - root).Normalized();
            for (var index = 1; index < _points.Length; index++) _points[index] = _points[index - 1] + direction * _lengths[index - 1];
        }
        else
            for (var iteration = 0; iteration < 24; iteration++)
            {
                _points[^1] = target.Origin;
                for (var index = _points.Length - 2; index >= 0; index--)
                    _points[index] = _points[index + 1] + Direction(_points[index] - _points[index + 1], -Vector3.Up) * _lengths[index];
                _points[0] = root;
                for (var index = 1; index < _points.Length; index++)
                    _points[index] = _points[index - 1] + Direction(_points[index] - _points[index - 1], Vector3.Up) * _lengths[index - 1];
                if (_points[^1].DistanceTo(target.Origin) < .0001f) break;
            }
        for (var index = 0; index < _bones.Length; index++)
        {
            var basis = target.Basis;
            if (index < _lengths.Length)
            {
                var from = (_rest[index + 1].Origin - _rest[index].Origin).Normalized();
                var to = (_points[index + 1] - _points[index]).Normalized();
                basis = new Basis(new Quaternion(from, to)) * _rest[index].Basis;
            }
            var parent = _skeleton.GetBoneParent(_bones[index]);
            _skeleton.SetBonePose(_bones[index], _skeleton.GetBoneGlobalPose(parent).AffineInverse() * new Transform3D(basis, _points[index]));
        }
        HeadErrorMeters = (_skeleton.GlobalTransform * _points[^1]).DistanceTo(worldHead.Origin);
    }
    private static Vector3 Direction(Vector3 value, Vector3 fallback) => value.LengthSquared() > .00000001f ? value.Normalized() : fallback;
}
