using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNativeNifSkeleton
{
    private sealed record LogicalVisualAttachment(Attachment Native, FalloutNifSceneObject Source,
        FalloutNifSceneMorphField? Morph);
    private LogicalVisualAttachment[]? _logicalVisualAttachments;
    private int[]? _logicalParentBones;
    private bool[]? _logicalVisibleBones;

    private void BindLogicalVisualFields(FalloutActorLogicalScene scene)
    {
        var objects = scene.Objects.Where(node => node.Key.Asset == 0).ToDictionary(node => node.Key.Block);
        var bones = _logicalBones!; var indices = new Dictionary<FalloutNifSceneKey, int>();
        for (var index = 0; index < bones.Length; ++index) indices.Add(bones[index].Key, index);
        _logicalParentBones = new int[bones.Length]; _logicalVisibleBones = new bool[bones.Length];
        for (var index = 0; index < bones.Length; ++index)
        {
            _logicalParentBones[index] = bones[index].Parent is { } parent ? indices[parent.Key] : -1;
            if (_logicalParentBones[index] >= index)
                throw new InvalidDataException("Logical skeleton visibility lost source parent-before-child order.");
        }
        _logicalVisualAttachments = new LogicalVisualAttachment[_attachments.Count]; var slot = 0;
        foreach (var attachment in _attachments.Values)
        {
            var node = objects[attachment.Source.Block.Index];
            if (!node.IsGeometry || node.Name != attachment.Source.Name || node.Key.Asset != 0 ||
                node.Parent?.Key != bones[attachment.ParentBone].Key)
                throw new InvalidDataException("Logical visual borrower changed its actual source geometry or parent.");
            FalloutNifSceneMorphField? morph = null;
            if (attachment.Morph is { } declared)
            {
                morph = scene.RequireMorphField(node.Key);
                if (morph.Controller.Block != declared.Controller.Block.Index || morph.Data.Block != declared.Data.Block.Index ||
                    morph.Weights.Count != declared.Data.Morphs.Length)
                    throw new InvalidDataException("Native morph borrower lost the same source controller/data extent.");
            }
            _logicalVisualAttachments[slot++] = new(attachment, node, morph);
        }
    }
    internal void BorrowCurrentSourceVisualFields()
    {
        var scene = _logicalScene ?? throw new InvalidOperationException("Visual borrower has no actual logical scene.");
        scene.RequireCurrentPoseProjection();
        for (var index = 0; index < _logicalBones!.Length; ++index)
        {
            var parent = _logicalParentBones![index];
            var visible = (_logicalBones[index].Flags & FalloutActorLogicalScene.AppCulledFlag) == 0 &&
                (parent < 0 || _logicalVisibleBones![parent]);
            _logicalVisibleBones![index] = visible;
            _visibility[_sourceBoneNames![index]] = (_logicalBones[index].Flags & FalloutActorLogicalScene.AppCulledFlag) == 0;
        }
        foreach (var attachment in _logicalVisualAttachments!)
        {
            var visible = (attachment.Source.Flags & FalloutActorLogicalScene.AppCulledFlag) == 0;
            _visibility[attachment.Source.Name] = visible;
            attachment.Native.Parent.Visible = visible && _logicalVisibleBones![attachment.Native.ParentBone];
            if (attachment.Morph is not { } morph) continue;
            for (var index = 0; index < morph.Weights.Count; ++index)
            {
                var value = morph.Weights[index].Value;
                _morphWeights[(attachment.Source.Name, index)] = value;
                if (index > 0) attachment.Native.Mesh.SetBlendShapeValue(index - 1, value);
            }
        }
    }
    private Action<float>? BindLogicalVisualChannel(FalloutNifFile source, FalloutNifControllerLink link)
    {
        if (_logicalScene is not { } scene || link.ControllerType is not ("NiVisController" or "NiGeomMorpherController"))
            return null;
        if (!HasSourceTarget(link.NodeName)) return null;
        var block = _boneIndices.TryGetValue(link.NodeName, out var bone) ? _logicalBones![bone].Key.Block :
            _attachments[link.NodeName].Source.Block.Index;
        var apply = link.ControllerType == "NiVisController" ? scene.BindVisibilityAnimation(new(0, block), source, link) :
            scene.BindMorphAnimation(new(0, block), source, link);
        return time => { apply(time); BorrowCurrentSourceVisualFields(); };
    }
}
