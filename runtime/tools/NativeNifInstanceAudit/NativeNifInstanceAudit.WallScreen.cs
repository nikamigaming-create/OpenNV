using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private void ExerciseOwnedWallScreen(string game, string mod, string root, string cellKey, string referenceKey, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game);
        RuntimeLiveContentSource.Configure(setup.BaseInstallation.InstallRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, setup.Settings);
        RuntimeNativeNifPrototype? prototype = null; Node3D? first = null; Node3D? second = null;
        try
        {
            var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            using var world = new FalloutReferenceWorld(records);
            var configuration = RuntimeConfiguration.Load();
            var units = configuration.World.GameUnitsToMeters;
            var cell = FalloutCellSceneReader.Read(records, ScreenFormKey(cellKey));
            var reference = cell.References.Single(value => value.FormKey == ScreenFormKey(referenceKey));
            var basis = cell.BaseObjects[reference.Base];
            var model = basis.ModelPath ?? throw new InvalidDataException("Selected reference has no winning model.");
            var resources = new Dictionary<string, (string Source, string Sha256, int Bytes)>(StringComparer.OrdinalIgnoreCase);
            content.ResourceReadObserver = (resourcePath, resourceIdentity, payload) => resources[resourcePath.Replace('\\', '/')] =
                (resourceIdentity, Convert.ToHexString(SHA256.HashData(payload.Span)), payload.Length);
            if (!content.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
            var nif = FalloutNifFile.Read(bytes);
            foreach (var block in nif.Blocks) _ = nif.ReadObject(block.Index);
            var sequences = nif.Blocks.Where(block => block.TypeName == "NiControllerSequence")
                .Select(block => nif.ReadControllerSequence(block.Index)).ToArray();
            var sequence = sequences.Single(value => value.ControlledBlocks.Length == 0 && value.CycleType == 0);
            var keys = ((FalloutNifTextKeyExtraData)nif.ReadObject(sequence.TextKeys)).Keys;
            RequireEmptySequence(keys.Length != 0 && keys.All(key => key.Value.Trim().Equals("start", StringComparison.OrdinalIgnoreCase) ||
                key.Value.Trim().Equals("end", StringComparison.OrdinalIgnoreCase)),
                "Selected empty sequence needs a different text-key event owner.");
            prototype = new RuntimeNativeNifPrototype(nif, units);
            var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            first = prototype.InstantiatePlaced(placement); second = prototype.InstantiatePlaced(placement);
            first.ProcessMode = second.ProcessMode = ProcessModeEnum.Disabled;
            first.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
            second.SetMeta("opennv_reference_form_key", reference.FormKey.ToString());
            AddChild(first); AddChild(second);
            RequireEmptySequence(first.GetChild<Node3D>(0).Transform == Transform3D.Identity && first.Transform == placement,
                "Owned wall screen composed its exported root over the authored reference.");
            var shapes = FalloutNifGeometryOrder.Read(nif);
            var meshes = first.FindChildren("*", "", true, false).OfType<MeshInstance3D>().ToArray();
            var sourceVertices = 0; var emittedTriangles = 0;
            foreach (var shape in shapes)
            {
                var data = nif.ReadMeshData(shape.Data);
                sourceVertices += data.Vertices.Length;
                emittedTriangles += FalloutNifTriangleWinding.ToGodotIndices(data.Triangles).Length / 3;
                var mesh = meshes.Single(value => value.GetMeta("opennv_nif_geometry_block").AsInt32() == shape.Block.Index);
                var geometry = mesh.Mesh as ArrayMesh ?? throw new InvalidDataException("Owned shape has no native mesh.");
                var actual = geometry.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                RequireEmptySequence(actual.Length == data.Vertices.Length && actual.Zip(data.Vertices).All(pair =>
                    pair.First.IsEqualApprox(GamebryoCoordinate.ConvertVector(new(pair.Second.X, pair.Second.Y, pair.Second.Z)) * units)),
                    "Owned wall-screen surface changed its source positions or units.");
            }
            RequireEmptySequence(meshes.Length == shapes.Count && prototype.Scene.Surfaces == shapes.Count &&
                prototype.Scene.Vertices == sourceVertices && prototype.Scene.Triangles == emittedTriangles,
                "Owned wall-screen assembly omitted an authored surface or invented geometry.");
            var collisionTriangles = nif.Blocks.Where(block => block.TypeName == "hkPackedNiTriStripsData")
                .Sum(block => ((FalloutNifPackedData)nif.ReadObject(block.Index)).Triangles.Length);
            RequireEmptySequence(collisionTriangles > 0 && prototype.Scene.CollisionBodies > 0 &&
                prototype.Scene.CollisionTriangles == collisionTriangles, "Owned screen lost its packed architectural collision.");
            var unlitShape = shapes.Single(shape => shape.Properties.Where(index => index >= 0)
                .Select(nif.ReadObject).OfType<FalloutNifNoLightingProperty>().Any());
            var unlit = unlitShape.Properties.Where(index => index >= 0).Select(nif.ReadObject)
                .OfType<FalloutNifNoLightingProperty>().Single();
            var sourceMaterial = unlitShape.Properties.Where(index => index >= 0).Select(nif.ReadObject)
                .OfType<FalloutNifMaterialProperty>().Single();
            var unlitMesh = meshes.Single(value => value.GetMeta("opennv_nif_geometry_block").AsInt32() == unlitShape.Block.Index);
            var material = unlitMesh.Mesh.SurfaceGetMaterial(0) as ShaderMaterial ??
                throw new InvalidDataException("Owned projector plane lost its no-lighting material.");
            var texture = material.GetShaderParameter("source_texture").AsGodotObject() as Texture2D ??
                throw new InvalidDataException("Owned projector plane has no decoded source texture.");
            var path = unlit.FileName.Replace('\\', '/');
            var emissive = new Vector4(sourceMaterial.Emissive.R * sourceMaterial.EmissiveMultiple,
                sourceMaterial.Emissive.G * sourceMaterial.EmissiveMultiple,
                sourceMaterial.Emissive.B * sourceMaterial.EmissiveMultiple, sourceMaterial.Alpha);
            RequireEmptySequence(material.ResourceName == NativeNifEffectMaterial.ResourceIdentity &&
                material.GetShaderParameter("source_has_texture").AsBool() &&
                texture.GetMeta("opennv_logical_texture").AsString().Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase) &&
                material.GetShaderParameter("source_color_multiplier").AsVector4() == emissive &&
                material.GetShaderParameter("source_emissive_multiple").AsSingle() == sourceMaterial.EmissiveMultiple &&
                material.GetMeta("opennv_nif_texture_clamp_mode").AsUInt32() == unlit.TextureClampMode,
                "Owned projector texture, source emissive or sampler changed during admission.");
            var player = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                .Single(value => value.SourceController == sequence.Manager);
            var other = second.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                .Single(value => value.SourceController == sequence.Manager);
            var original = prototype.Scene.Root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
                .Single(value => value.SourceController == sequence.Manager);
            var sounds = new NativeOwnedAnimationSoundPlayer(records, content, first, units, world.Get(reference.FormKey).SoundRandom);
            first.AddChild(sounds); player.TextKeyHandler = sounds.Dispatch;
            var pose = Transforms(first); var otherPose = Transforms(second);
            var duration = ((double)sequence.StopTime - sequence.StartTime) / sequence.Frequency;
            RequireEmptySequence(player.ActiveSequence == sequence.Name && player.SourceTimeSeconds == sequence.StartTime,
                "Owned empty sequence did not enter its authored loop.");
            player._Process(duration);
            RequireEmptySequence(player.TextKeyCount == keys.Length + keys.Count(key => key.Time == sequence.StartTime) &&
                other.TextKeyCount == 0 && original.TextKeyCount == 0 && player.UnboundTextKeys.Count == 0 && sounds.Unbound.Count == 0 &&
                Transforms(first).SequenceEqual(pose) && Transforms(second).SequenceEqual(otherPose) &&
                material.GetShaderParameter("source_color_multiplier").AsVector4() == emissive,
                "Owned empty-loop events lost their source owner, isolation or static material.");
            GD.Print("OPENNV_OWNED_WALL_SCREEN_REPORT " + JsonSerializer.Serialize(new
            {
                reference = reference.FormKey.ToString(),
                cell = cell.Cell.FormKey.ToString(),
                basis = basis.FormKey.ToString(),
                model,
                identity,
                nif.Sha256,
                nif.UserVersion2,
                blocks = nif.Blocks.Count,
                runtimeBuild = typeof(RuntimeNativeNifMeshBuilder).Module.ModuleVersionId,
                content.SaveCompatibilityId,
                configurationSha256 = configuration.Sha256,
                units,
                prototype.Scene.Surfaces,
                prototype.Scene.Vertices,
                prototype.Scene.Triangles,
                prototype.Scene.CollisionBodies,
                prototype.Scene.CollisionShapes,
                prototype.Scene.CollisionTriangles,
                sequence = sequence.Name,
                channels = sequence.ControlledBlocks.Length,
                textKeys = player.TextKeyCount,
                projectorGeometry = unlitShape.Block.Index,
                texture = path,
                textureWidth = texture.GetWidth(),
                textureHeight = texture.GetHeight(),
                resources = resources.OrderBy(row => row.Key).Select(row => new { path = row.Key, row.Value.Source, row.Value.Sha256, row.Value.Bytes }),
                sourceReadOnly = true,
                geometryUnchanged = true,
                sourceEmissive = true,
                instanceClocksIndependent = true,
                finalPixels = "unverified",
                retailParity = "unverified",
                ordinaryCheckpointLoad = "unverified",
            }));
            GD.Print("OPENNV_OWNED_WALL_SCREEN_PASS emptySequence=true actualSurfaces=true sourceTexture=true sourceEmissive=true sourcePlacement=true sourceCollision=true structuralKeys=true instanceIsolation=true pixels=unverified");
        }
        finally { first?.Free(); second?.Free(); prototype?.Scene.Root.Free(); RuntimeLiveContentSource.Clear(); }
    }

    private static FalloutFormKey ScreenFormKey(string token)
    {
        var split = token.LastIndexOf(':');
        if (split <= 0 || split == token.Length - 1) throw new ArgumentException("Use plugin:hex-form for the source cell and reference.");
        return new(token[..split], Convert.ToUInt32(token[(split + 1)..], 16));
    }
}
