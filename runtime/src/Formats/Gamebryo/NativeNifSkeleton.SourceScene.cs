using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNativeNifSkeleton
{
    private FalloutActorLogicalScene? _logicalScene;
    private string[]? _sourceBoneNames;
    private FalloutNifSourcePoseBlend? _sourcePoseBlend;
    internal FalloutActorLogicalScene? LogicalScene => _logicalScene;
    internal FalloutNifSourcePoseBlend SourcePoseBlend => _sourcePoseBlend ??= new(_boneIndices.Count);
    internal IReadOnlyList<RuntimeNativeNifGeometryMaterial> ReadGeometryMaterialTargets()
    {
        var result = new List<RuntimeNativeNifGeometryMaterial>();
        foreach (var attachment in _attachments.Values)
            for (var surface = 0; surface < attachment.Mesh.Mesh.GetSurfaceCount(); surface++)
                result.Add(new(Source.Sha256, attachment.Source.Block.Index,
                    attachment.Mesh.GetActiveMaterial(surface) ?? throw new NotSupportedException("source-skeleton-geometry-material-target-missing")));
        return result;
    }
    internal void RefuseUnownedLogicalPose(string boundary)
    {
        if (_logicalScene is not { } scene) return;
        scene.RetainUnownedPose(boundary);
        throw new NotSupportedException(boundary);
    }
    internal void BindLogicalScene(FalloutActorLogicalScene scene)
    {
        if (_logicalScene is not null || scene.SkeletonSha256 != Source.Sha256)
            throw new InvalidDataException("Skeleton logical scene changed its actual source instance or bound twice.");
        _sourceBoneNames = new string[_boneIndices.Count];
        foreach (var (name, index) in _boneIndices) _sourceBoneNames[index] = name;
        _logicalScene = scene;
    }
    internal void PublishSourceBone(int bone, FalloutNifAnimationSample sample)
    {
        var scene = _logicalScene ?? throw new InvalidOperationException("Source pose has no actual logical scene consumer.");
        scene.PublishBoneSample(_sourceBoneNames![bone], sample);
        // These stores only adapt the same C# result for presentation. Native
        // pose caches and matrix decomposition never feed it back into C#.
        if (sample.Translation is { } position)
            Node.SetBonePosePosition(bone, GamebryoCoordinate.ConvertVector(new(position.X, position.Y, position.Z)) * UnitsToMetres);
        if (sample.Rotation is { } rotation) Node.SetBonePoseRotation(bone, new(rotation.X, rotation.Z, -rotation.Y, rotation.W));
        if (sample.Scale is { } scale) Node.SetBonePoseScale(bone, Vector3.One * scale);
    }
    internal void BorrowRestoredSourcePoses()
    {
        var scene = _logicalScene ?? throw new InvalidOperationException("Cold renderer lost its actual logical source scene.");
        var byName = scene.Objects.Where(node => node.Key.Asset == 0 && node.IsNode).ToDictionary(node => node.Name, StringComparer.Ordinal);
        foreach (var (name, index) in _boneIndices) Node.SetBonePose(index, Convert(byName[name].Local));
    }
}
