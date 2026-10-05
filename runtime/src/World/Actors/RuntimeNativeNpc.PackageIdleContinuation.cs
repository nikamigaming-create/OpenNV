using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private string? _idleAnimationResource, _idleAnimationSha256;

    private bool CanCaptureFurnitureIdleAnimation() => _sitting == 1 && _idleOwner == "package-idle" &&
        _idleForm is not null && _animation is not null && _idlePlayback is { Complete: false } && _idleData is not null &&
        _baseAnimation is not null &&
        _idleRevision is > 0 and < long.MaxValue && _idleAnimationResource is not null && _idleAnimationSha256 is not null &&
        _animationObjects.Count == 0 && Combat?.AnimationWeapon is null && _animationSounds?.CanCaptureSilent != false &&
        _packageIdles?.Capture() is { Cursor: > 0, SelectionCount: > 0, WaitSeconds: 0, Complete: false };

    private FalloutActorPackageIdleAnimation CaptureFurnitureIdleAnimation()
    {
        if (!CanCaptureFurnitureIdleAnimation())
            throw new NotSupportedException("Furniture collection animation has an unowned overlay, object or sound continuation.");
        var saved = new FalloutActorPackageIdleAnimation(_idleForm!.Value,
            FalloutActorFurnitureContinuation.RecordHash(_aiStack!.GetEffective(_idleForm.Value)),
            _idleAnimationResource!, _idleAnimationSha256!, _idlePlayback!.Capture(), _idleRevision, CaptureFurnitureResidualPose());
        saved.Validate(_aiStack, _packageIdleSource!.Form);
        return saved;
    }

    private void RestoreFurnitureIdleAnimation(FalloutActorPackageIdleAnimation saved)
    {
        saved.Validate(_aiStack!, _packageIdleSource!.Form);
        PlayIdle(_aiStack!, saved.Idle, "package-idle", saved);
    }

    private FalloutActorResidualPose CaptureFurnitureResidualPose()
    {
        var coverage = RuntimeNativeNifAnimation.TransformCoverage(_baseAnimation!, _animation!);
        var node = Skeleton.Node;
        var bones = new List<FalloutActorResidualBonePose>();
        for (var index = 0; index < coverage.Length; ++index)
        {
            var missing = FalloutNifTransformComponents.All & ~coverage[index];
            if (missing == FalloutNifTransformComponents.None) continue;
            var p = node.GetBonePosePosition(index); var q = node.GetBonePoseRotation(index); var s = node.GetBonePoseScale(index);
            bones.Add(new(index, node.GetBoneName(index).ToString(),
                (missing & FalloutNifTransformComponents.Translation) == 0 ? null : [p.X, p.Y, p.Z],
                (missing & FalloutNifTransformComponents.Rotation) == 0 ? null : [q.X, q.Y, q.Z, q.W],
                (missing & FalloutNifTransformComponents.Scale) == 0 ? null : [s.X, s.Y, s.Z]));
        }
        return new(Appearance.SkeletonPath, Skeleton.Source.Sha256, bones);
    }

    private void RestoreFurnitureResidualPose(FalloutActorResidualPose saved, RuntimeNativeNifAnimation overlay)
    {
        var coverage = RuntimeNativeNifAnimation.TransformCoverage(_baseAnimation ??
            throw new InvalidDataException("Saved residual pose has no furniture base layer."), overlay);
        var node = Skeleton.Node;
        saved.ValidateBinding(Appearance.SkeletonPath, Skeleton.Source.Sha256,
            coverage.Select((mask, index) => (node.GetBoneName(index).ToString(), mask)).ToArray());
        foreach (var bone in saved.Bones)
        {
            if (bone.Position is { } p) node.SetBonePosePosition(bone.Index, new(p[0], p[1], p[2]));
            if (bone.Rotation is { } q) node.SetBonePoseRotation(bone.Index, new(q[0], q[1], q[2], q[3]));
            if (bone.Scale is { } s) node.SetBonePoseScale(bone.Index, new(s[0], s[1], s[2]));
        }
    }
}
