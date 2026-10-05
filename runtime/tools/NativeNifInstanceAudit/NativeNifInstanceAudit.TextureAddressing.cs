using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseTextureAddressing()
    {
        var source = new FalloutNifNoLightingProperty(new(0, "BSShaderNoLightingProperty", 0, 0),
            "", [], -1, 1, 33, 0x82000000, 1, 1, 0, "", 1, 0, 1, 0);
        for (uint mode = 0; mode < 4; mode++)
        {
            using var material = NativeNifEffectMaterial.Build(source with { TextureClampMode = mode }, null, null, null, false);
            if (material.GetMeta("opennv_nif_texture_clamp_mode").AsUInt32() != mode ||
                material.GetShaderParameter("source_has_texture").AsBool())
                throw new InvalidDataException("No-lighting sampler lost its source mode or invented a texture.");
            NativeNifTextureTransform.Configure(material, new(new(.25f, -.5f), new(2, 3), .7f, 1, new(.5f, .5f)));
            NativeNifTextureTransform.Apply(material, 0, 1.5f);
            NativeNifTextureTransform.Apply(material, 1, -2.25f);
            if (material.GetShaderParameter("source_uv_offset").AsVector2() != new Vector2(1.5f, -2.25f) ||
                material.GetShaderParameter("source_uv_scale").AsVector2() != new Vector2(2, 3) ||
                material.GetShaderParameter("source_uv_center").AsVector2() != new Vector2(.5f, .5f))
                throw new InvalidDataException("Independent sampler axes interfered with authored UV animation.");
        }
        GD.Print("OPENNV_NIF_TEXTURE_ADDRESSING_NATIVE_PASS modes=0,1,2,3 textureTransform=true pixels=unverified");
    }

    private void ExerciseOwnedModels(string game, string mod, string root, string[] arguments)
    {
        var split = Array.IndexOf(arguments, "--dependencies");
        if (split < 1) throw new ArgumentException("Owned model audit needs model paths then --dependencies.");
        var setup = new FalloutModStackSelection([new(mod, root, arguments[(split + 1)..])]).Resolve(game);
        using var content = setup.OpenSource();
        foreach (var model in arguments[..split])
        {
            if (!content.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
            var nif = FalloutNifFile.Read(bytes);
            foreach (var block in nif.Blocks) _ = nif.ReadObject(block.Index);
            GD.Print($"OPENNV_OWNED_NIF_DECODE_PASS source={identity} sha256={nif.Sha256} stream={nif.UserVersion2} blocks={nif.Blocks.Count}");
            foreach (var manager in nif.Blocks.Where(block => block.TypeName == "NiControllerManager")
                .Select(block => (FalloutNifControllerManager)nif.ReadObject(block.Index)))
                GD.Print(JsonSerializer.Serialize(new
                {
                    kind = "owned-nif-manager",
                    source = identity,
                    nif.Sha256,
                    controller = manager.Block.Index,
                    manager.Cumulative,
                    manager.ObjectPalette,
                    sequences = manager.Sequences.Select(reference => (FalloutNifControllerSequence)nif.ReadObject(reference))
                        .Select(sequence => new
                        {
                            block = sequence.Block.Index,
                            sequence.Name,
                            sequence.Manager,
                            sequence.TargetName,
                            sequence.CycleType,
                            sequence.Frequency,
                            sequence.StartTime,
                            sequence.StopTime,
                            sequence.Weight,
                            sequence.TextKeys,
                            sequence.ControlledBlocks
                        })
                }));
            var phantoms = nif.Blocks.Where(block => block.TypeName == "bhkSimpleShapePhantom").ToArray();
            RuntimeNativeNifScene scene;
            try { scene = RuntimeNativeNifMeshBuilder.Build(nif, .0142875f, contentSource: content); }
            catch (NotSupportedException failure) when (phantoms.Length != 0 && failure.Message.Contains("requires a native overlap/contact owner", StringComparison.Ordinal))
            {
                GD.Print($"OPENNV_OWNED_NIF_PRESENTATION_UNBOUND source={identity} phantoms={phantoms.Length} error={failure.Message} debugMarkerDraw=false solidFloorInvented=false");
                continue;
            }
            try
            {
                AddChild(scene.Root);
                var meshes = scene.Root.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToArray();
                var effects = meshes.Where(mesh => mesh.Mesh is not null).SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount())
                    .Select(index => mesh.Mesh.SurfaceGetMaterial(index))).OfType<ShaderMaterial>()
                    .Where(material => material.ResourceName == NativeNifEffectMaterial.ResourceIdentity).ToArray();
                var samplerModes = effects.Select(effect => effect.GetMeta("opennv_nif_texture_clamp_mode").AsUInt32()).ToArray();
                var channels = 0;
                foreach (var player in scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>())
                {
                    if (player.ActiveSourceSequence is not (>= 0 and var selected)) continue;
                    var sequence = nif.ReadControllerSequence(selected);
                    foreach (var fraction in new[] { .271f, .713f })
                    {
                        var time = sequence.StartTime + (sequence.StopTime - sequence.StartTime) * fraction;
                        player.SeekSourceTime(time);
                        foreach (var link in sequence.ControlledBlocks.Where(link => link.ControllerType == "NiTextureTransformController"))
                        {
                            var control = (FalloutNifTextureTransformController)nif.ReadObject(link.Controller);
                            var sampler = new FalloutNifFloatAnimation(nif, link.Interpolator);
                            var geometry = nif.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
                                .Select(block => nif.ReadGeometry(block.Index)).Single(value => value.Name == link.NodeName);
                            var mesh = meshes.Single(value => value.GetMeta("opennv_nif_geometry_block").AsInt32() == geometry.Block.Index);
                            var material = mesh.Mesh.SurfaceGetMaterial(0) as ShaderMaterial ??
                                throw new InvalidDataException("Owned texture controller lost its actual shader target.");
                            var parameter = control.Operation == 2 ? "source_uv_rotation" : control.Operation < 2 ? "source_uv_offset" : "source_uv_scale";
                            var actual = control.Operation == 2 ? material.GetShaderParameter(parameter).AsSingle() :
                                control.Operation is 0 or 3 ? material.GetShaderParameter(parameter).AsVector2().X : material.GetShaderParameter(parameter).AsVector2().Y;
                            if (Math.Abs(actual - sampler.Sample(time)) > .000001f)
                                throw new InvalidDataException("Owned texture animation diverged from its source scalar clock.");
                            channels++;
                        }
                    }
                }
                GD.Print($"OPENNV_OWNED_NIF_PRESENTATION_PASS source={identity} nodes={scene.Nodes} surfaces={scene.Surfaces} collisionBodies={scene.CollisionBodies} collisionShapes={scene.CollisionShapes} samplerModes={string.Join(',', samplerModes)} textureChannelSamples={channels} finalPixels=unverified");
            }
            finally { scene.Root.Free(); }
        }
    }
}
