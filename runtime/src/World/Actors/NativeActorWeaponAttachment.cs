using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Actors;

// Both the player and native NPCs attach the equipped source object to their
// existing skeleton. There is no second character or dropped-object physics.
internal sealed class NativeActorWeaponAttachment
{
    internal BoneAttachment3D Attachment { get; }
    internal Node3D Root { get; }
    internal Node3D[] Nodes { get; }
    internal IReadOnlySet<string> Targets { get; }
    internal IReadOnlyList<BoneAttachment3D> BodyAttachments => _bodyAttachments;
    private readonly List<BoneAttachment3D> _bodyAttachments = [];
    private readonly float _units;

    internal NativeActorWeaponAttachment(FalloutWeaponPresentation weapon, RuntimeNativeNifSkeleton skeleton,
        RuntimeLiveContentSource content)
    {
        _units = skeleton.UnitsToMetres;
        var model = weapon.Model ?? throw new NotSupportedException("This weapon is embedded in its source actor skeleton and cannot attach as an inventory model.");
        var path = model.ModelPath ?? throw new InvalidDataException("Equipped weapon has no source model.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var source = FalloutNifFile.Read(bytes);
        var targets = source.Blocks.Where(block => block.TypeName is "NiNode" or "NiTriShape" or "NiTriStrips")
            .Select(block => source.ReadObject(block.Index) is FalloutNifNode node ? node.Name : source.ReadGeometry(block.Index).Name).ToHashSet(StringComparer.Ordinal);
        Root = RuntimeNativeNifMeshBuilder.Build(source, _units, externalTransformTargets: targets, contentSource: content).Root;
        try
        {
            NativeNifCollisionBuilder.BindAnimatedAttachment(Root);
            Attachment = new() { Name = "EquippedWeapon", BoneName = "Weapon" };
            skeleton.Node.AddChild(Attachment); Attachment.AddChild(Root);
            Nodes = Root.FindChildren("*", "", true, false).OfType<Node3D>().ToArray();
            Targets = Nodes.Select(node => node.GetMeta("opennv_nif_source_name", "").AsString()).ToHashSet(StringComparer.Ordinal);
            BindBodyAttachments(source, skeleton);
            Root.VisibilityChanged += () =>
            {
                // Combat and package owners alternate their weapon models.
                // Reparented packs must follow that same model lifetime.
                foreach (var part in _bodyAttachments) part.Visible = Root.Visible;
            };
            foreach (var mesh in Nodes.OfType<MeshInstance3D>())
            {
                if (!mesh.HasMeta("opennv_nif_geometry_block")) continue;
                var geometry = source.ReadGeometry(mesh.GetMeta("opennv_nif_geometry_block").AsInt32());
                mesh.MaterialOverride = NativeNifMeshBuilder.BuildMaterial(source, geometry,
                    texturePaths: NativeNpcMaterial.Alternate(model, source, geometry));
                foreach (var property in geometry.Properties.Where(index => index >= 0).Select(source.ReadObject))
                    skeleton.MaterialChannels.Add(geometry.Name, source, property, [mesh.MaterialOverride]);
            }
        }
        catch
        {
            foreach (var part in _bodyAttachments) part.Free();
            Root.Free(); Attachment?.Free(); throw;
        }
    }

    private void BindBodyAttachments(FalloutNifFile source, RuntimeNativeNifSkeleton skeleton)
    {
        // Equipped NIF subtrees can name a different anatomical parent in
        // their export metadata (heavy-weapon packs, for example). Keep their
        // authored local transforms, but bind them to that bone rather than
        // carrying the entire model on the primary hand.
        foreach (var node in Nodes.Where(node => node.HasMeta("opennv_nif_block")))
        {
            if (source.ReadObject(node.GetMeta("opennv_nif_block").AsInt32()) is not FalloutNifNode authored) continue;
            var parents = authored.ExtraData.Where(index => index >= 0).Select(source.ReadObject)
                .OfType<FalloutNifStringExtraData>().Where(extra => extra.Name is "Prn" or "UPB" &&
                    skeleton.TryBoneIndex(extra.Value.Trim(), out _)).Select(extra => extra.Value.Trim()).Distinct().ToArray();
            if (parents.Length == 0 || parents is ["Weapon"]) continue;
            if (parents.Length != 1) throw new InvalidDataException("Equipped model has ambiguous anatomical parents.");
            var attachment = new BoneAttachment3D { Name = "EquippedBodyPart", BoneName = parents[0] };
            skeleton.Node.AddChild(attachment); _bodyAttachments.Add(attachment);
            node.Reparent(attachment, keepGlobalTransform: false);
            foreach (var mesh in node.FindChildren("*", "", true, false).OfType<GeometryInstance3D>())
                mesh.SetMeta("opennv_weapon_body_attachment", parents[0]);
        }
    }

    internal Action<float>? Bind(FalloutNifFile source, FalloutNifControllerLink link)
    {
        var nodes = Nodes.Where(node => node.GetMeta("opennv_nif_source_name", "").AsString() == link.NodeName).ToArray();
        if (nodes.Length > 1)
        {
            var rendered = nodes.Where(node => node is MeshInstance3D || node.FindChildren("*", "MeshInstance3D", true, false).Count != 0).ToArray();
            if (rendered.Length == 1) nodes = rendered;
        }
        if (nodes.Length != 1) return null;
        if (link.ControllerType == "NiVisController")
        {
            var visibility = new FalloutNifBoolAnimation(source, link.Interpolator);
            return time => nodes[0].Visible = visibility.Sample(time);
        }
        if (link.PropertyType.Length == 0 && nodes[0] is RuntimeNifParticleSystem particle &&
            link.ControllerType is "NiPSysEmitterCtlr" or "NiPSysEmitterSpeedCtlr" or "NiPSysModifierActiveCtlr")
            return particle.Bind(source, link).Apply;
        if (link.ControllerType != "NiTransformController") return null;
        var sampler = new FalloutNifAnimationSampler(source, link.Interpolator);
        return time =>
        {
            var sample = sampler.Sample(time);
            if (sample.Translation is { } position) nodes[0].Position = GamebryoCoordinate.ConvertVector(new(position.X, position.Y, position.Z)) * _units;
            if (sample.Rotation is { } rotation) nodes[0].Quaternion = new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized();
            if (sample.Scale is { } scale) nodes[0].Scale = Vector3.One * scale;
        };
    }

    internal Transform3D Socket(RuntimeNativeNifSkeleton skeleton, string name)
    {
        var source = Nodes.SingleOrDefault(node => node.GetMeta("opennv_nif_source_name", "").AsString().Equals(name, StringComparison.OrdinalIgnoreCase)) ??
            throw new NotSupportedException($"Equipped source weapon has no unique {name}.");
        var local = Transform3D.Identity;
        for (Node3D? node = source; node != Attachment; node = node.GetParent() as Node3D)
        {
            if (node is null) throw new InvalidOperationException("Weapon socket left its attachment.");
            local = node.Transform * local;
        }
        return skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(skeleton.BoneIndex("Weapon")) * local;
    }

    internal bool HasSocket(string name) => Nodes.Any(node =>
        node.GetMeta("opennv_nif_source_name", "").AsString().Equals(name, StringComparison.OrdinalIgnoreCase));

    internal Transform3D ReleaseTransform(RuntimeNativeNifSkeleton skeleton) =>
        skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(skeleton.BoneIndex("Weapon")) * Root.Transform;
}
