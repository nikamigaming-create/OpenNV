using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

public partial class NativePlayerBodyAudit
{
    private static void CheckSourceHeadVisibility(RuntimeNativePlayerActor body)
    {
        var actor = body.Actor;
        var heads = NativePlayerSelfVisibility.HeadGeometry(actor).ToHashSet();
        if (heads.Count == 0) throw new InvalidOperationException("The source body has no head geometry to test.");
        var meshes = actor.FindChildren("*", "", true, false).OfType<GeometryInstance3D>().ToArray();
        var before = meshes.ToDictionary(mesh => mesh, mesh => (mesh.Layers, mesh.Visible, mesh.CastShadow));
        var parts = actor.Parts.Select(part => (part.Root, part.Root.Visible)).ToArray();
        var contacts = body.BodyContacts.Select(contact => contact.GetInstanceId()).ToArray();
        var bones = body.Skeleton.Node.GetBoneCount();
        NativePlayerSelfVisibility.Apply(actor);
        foreach (var mesh in meshes)
        {
            var source = before[mesh];
            if (mesh.Layers != (heads.Contains(mesh) ? FalloutNpcAppearanceSelfView.SelfHeadLayer : source.Layers) ||
                mesh.Visible != source.Visible || mesh.CastShadow != source.CastShadow)
                throw new InvalidOperationException("Self-head policy changed an unrelated draw, authored visibility or shadow.");
        }
        if (parts.Any(part => part.Root.Visible != part.Visible) || body.Skeleton.Node.GetBoneCount() != bones ||
            !contacts.SequenceEqual(body.BodyContacts.Select(contact => contact.GetInstanceId())))
            throw new InvalidOperationException("Self-camera policy changed the complete actor or its physical owners.");
        var first = new Camera3D();
        var world = new Camera3D();
        try
        {
            var source = first.CullMask;
            first.CullMask = FalloutNpcAppearanceSelfView.CameraMask(source, true);
            world.CullMask = FalloutNpcAppearanceSelfView.CameraMask(source, false);
            if (heads.Any(mesh => (mesh.Layers & first.CullMask) != 0 || (mesh.Layers & world.CullMask) == 0) ||
                meshes.Where(mesh => !heads.Contains(mesh)).Any(mesh =>
                    (mesh.Layers & first.CullMask) != (before[mesh].Layers & source)))
                throw new InvalidOperationException("The camera mask leaked a source head or hid unrelated body geometry.");
            first.CullMask = FalloutNpcAppearanceSelfView.CameraMask(source, false);
            if (first.CullMask != source || heads.Any(mesh => (mesh.Layers & first.CullMask) == 0))
                throw new InvalidOperationException("Third-person view did not restore the complete source camera mask.");
        }
        finally { first.Free(); world.Free(); }

        var headPart = actor.Appearance.Models.Select((model, index) => (model, index))
            .First(value => FalloutNpcAppearanceSelfView.IsHead(value.model));
        var root = actor.Parts[headPart.index].Root;
        var role = root.GetMeta("opennv_source_part");
        var layers = meshes.Select(mesh => mesh.Layers).ToArray();
        try
        {
            root.SetMeta("opennv_source_part", "body");
            try
            {
                NativePlayerSelfVisibility.Apply(actor);
                throw new InvalidOperationException("A foreign source role was accepted by player visibility.");
            }
            catch (InvalidDataException) { }
            if (!layers.SequenceEqual(meshes.Select(mesh => mesh.Layers)))
                throw new InvalidOperationException("Source-role refusal changed a partially published body.");
        }
        finally { root.SetMeta("opennv_source_part", role); }
        var addonSurfaces = actor.Appearance.Models.Select((model, index) => (model, index))
            .Where(value => value.model.Role == "head-addon")
            .Sum(value => actor.Parts[value.index].Root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>().Count());
        GD.Print($"OPENNV_PLAYER_SELF_HEAD_VISIBILITY_PASS headSurfaces={heads.Count} headAddonSurfaces={addonSurfaces} " +
            "sourceRoles=exact firstCamera=excluded thirdAndWorld=complete authoredVisibility=retained shadows=retained contacts=retained " +
            "roleDrift=refused pixelAcceptance=separate");
    }
}
