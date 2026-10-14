using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private RuntimeNativeNifLogicalSceneMaterials? _logicalMaterials;
    internal object? LogicalMaterialState => _logicalMaterials?.State;
    internal FalloutActorLogicalScene? LogicalScene => Actor.SourceModel.LogicalScene;
    internal FalloutActorLogicalScene BindLogicalScene(FalloutActorLogicalSceneSnapshot? saved = null)
    {
        if (!_first || Error is not null || _idle is null)
            throw new NotSupportedException("Logical first-person publication requires its actual returned source role and animation selection.");
        var scene = Actor.SourceModel.ConstructLogicalScene(saved);
        RequireCompleteLogicalAttachmentGraph(scene);
        Skeleton.BindLogicalScene(scene);
        if (saved is not null) Skeleton.BorrowRestoredSourcePoses();
        else
        {
            scene.ResetBonePoses(_idle.Sequence.TargetName);
            var count = CurrentSourceLayers();
            RuntimeNativeNifAnimation.ApplyLogicalLayers(_animationLayers.AsSpan(0, count), Skeleton);
        }
        var targets = Skeleton.ReadGeometryMaterialTargets().Select(binding => new RuntimeNativeNifSceneMaterialTarget(0, binding))
            .Concat(Actor.Parts.SelectMany((part, index) => part.GeometryMaterials.Select(binding => new RuntimeNativeNifSceneMaterialTarget(index + 1, binding))));
        _logicalMaterials = new(scene, targets, () => ReferenceEquals(Actor.SourceModel.LogicalScene, scene) &&
            Godot.GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion());
        AddChild(_logicalMaterials);
        return scene;
    }
    private int CurrentSourceLayers()
    {
        var count = 0;
        if (_idle is not null) _animationLayers[count++] = (_idle, SourceTime(_idle, _seconds));
        if (_movement is not null) _animationLayers[count++] = (_movement, SourceTime(_movement, _movementSeconds));
        if (_active is not null && _active != _idle) _animationLayers[count++] = (_active, SourceTime(_active, _seconds));
        if (_drawn && _grip is not null) _animationLayers[count++] = (_grip, SourceTime(_grip, _seconds));
        if (_actionClip is not null) _animationLayers[count++] = (_actionClip, SourceTime(_actionClip, _actionSeconds));
        return count;
    }
    internal void RetireLogicalScene()
    {
        NativeNifSceneRetirement.Drain("Player logical scene",
            [() => _logicalMaterials?.Retire(), () => Actor.SourceModel.LogicalScene?.Retire()]);
    }
    private void RequireCompleteLogicalAttachmentGraph(FalloutActorLogicalScene scene)
    {
        if (Weapon is null && _equippedObject is null && _weaponRoot is null && _weaponNodes.Count == 0) return;
        if (Weapon is not { } weapon || _equippedObject is not { } assembly ||
            !ReferenceEquals(assembly.Weapon, weapon) || !ReferenceEquals(assembly.Root, _weaponRoot) ||
            !Godot.GodotObject.IsInstanceValid(assembly.Root) || !Godot.GodotObject.IsInstanceValid(assembly.Attachment))
        {
            const string lost = "source-first-person-logical-weapon-assembly-identity-unbound";
            scene.RetainUnownedPose(lost); throw new NotSupportedException(lost);
        }
        // This actual returned source weapon and its anatomical attachments are
        // outside the current logical skeleton/appearance graph. Retain that
        // denominator gap before publishing the graph as the Player field.
        var boundary = "source-first-person-logical-equipped-weapon-attachment-publication-unowned:" +
            weapon.Form + "/" + weapon.Model?.ModelPath + "/native-root=" + assembly.Root.GetInstanceId();
        scene.RetainUnownedPose(boundary); throw new NotSupportedException(boundary);
    }
}
