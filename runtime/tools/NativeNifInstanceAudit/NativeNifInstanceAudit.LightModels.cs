using Godot;
using OpenNV.Runtime.Content;

public partial class NativeNifInstanceAudit
{
    private static void ExercisePlacedLightModelAttachment(FalloutBaseObjectDefinition definition,
        FalloutPlacedReference reference)
    {
        var source = definition with
        {
            ModelPath = "meshes/synthetic-light.nif",
            Light = definition.Light! with { Flags = 1 }
        };
        var model = new Node3D { Name = "ActualSourceModel", Position = new(4, 2, -1) };
        model.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
        model.SetMeta("opennv_source_model", source.ModelPath!);
        model.SetMeta("opennv_source_form", source.FormKey.ToString());
        try
        {
            var sourceRoot = new Node3D { Name = "ActualNifRoot", Position = new(.5f, .25f, -.5f) };
            sourceRoot.SetMeta("opennv_nif_source_node", true);
            model.AddChild(sourceRoot);
            // Keep the live source-root transform beneath the placement wrapper.
            if (RuntimeNativePlacedLightBuilder.ModelAttachment(model) != sourceRoot)
                throw new InvalidOperationException("Model-free attachment fallback invented a node.");
            var attach = new Node3D { Name = "RenamedGodotNode", Position = new(0, 1.25f, 0) };
            attach.SetMeta("opennv_nif_source_name", "AttachLight");
            attach.SetMeta("opennv_nif_source_node", true);
            sourceRoot.AddChild(attach);
            var later = new Node3D { Name = "LaterSourceNode" };
            later.SetMeta("opennv_nif_source_name", "AttachLight");
            later.SetMeta("opennv_nif_source_node", true);
            sourceRoot.AddChild(later);
            if (RuntimeNativePlacedLightBuilder.ModelAttachment(model) != attach)
                throw new InvalidOperationException("Source first-match attachment identity was replaced by Godot names.");
            var namedGeometry = new Node3D { Name = "NamedGeometry" };
            namedGeometry.SetMeta("opennv_nif_source_name", "AttachLight");
            sourceRoot.AddChild(namedGeometry);
            sourceRoot.MoveChild(namedGeometry, 0);
            if (RuntimeNativePlacedLightBuilder.ModelAttachment(model) != sourceRoot)
                throw new InvalidOperationException("Named geometry incorrectly satisfied native GetAsNiNode.");
            namedGeometry.Free();
            var light = RuntimeNativePlacedLightBuilder.Build(reference, source, Transform3D.Identity,
                1f / 64, 1, 0, false, sourceModel: model);
            try
            {
                attach.AddChild(light);
                light._Process(.125);
                if (light.Transform != Transform3D.Identity || light.LightEnergy != 1.5f ||
                    light.OmniRange != 3.125f || !light.GetMeta("opennv_ligh_dynamic").AsBool() ||
                    light.HasMeta("opennv_ligh_animation_owner"))
                    throw new InvalidOperationException("Dynamic classification changed source energy, radius or animation ownership.");
            }
            finally { light.Free(); }
            var embedded = new Node3D();
            embedded.SetMeta("opennv_nif_unreferenced_point_light", true);
            attach.AddChild(embedded);
            try
            {
                _ = RuntimeNativePlacedLightBuilder.ModelAttachment(model);
                throw new InvalidOperationException("Unowned embedded point-light reuse was silently replaced.");
            }
            catch (NotSupportedException error) when (error.Message.Contains("embedded NiPointLight", StringComparison.Ordinal)) { }
        }
        finally { model.Free(); }
        try
        {
            var rejected = RuntimeNativePlacedLightBuilder.Build(reference, source, Transform3D.Identity,
                1f / 64, 1, 0, false);
            rejected.Free();
            throw new InvalidOperationException("A modeled LIGH was accepted without its source model owner.");
        }
        catch (NotSupportedException error) when (error.Message.Contains("actual source model owner", StringComparison.Ordinal)) { }
        GD.Print("OPENNV_LIGHT_MODEL_BINDING_PASS sourceAttachment=true firstMatch=true fallback=true dynamicClassification=true embeddedUnknownRefused=true pixels=unverified");
    }
}
