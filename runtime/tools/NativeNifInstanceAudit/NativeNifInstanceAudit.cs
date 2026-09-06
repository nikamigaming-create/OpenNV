using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            Exercise(Synthetic(), 0.02f, "synthetic");
            ExercisePlaced(Synthetic(false), 0.02f);
            ExercisePlacedLights();
            ExerciseMorphBasis();
            ExerciseDdsImages();
            if (OS.GetCmdlineUserArgs() is ["--dds-gpu", ..])
            {
                await ExerciseDdsPixels(OS.GetCmdlineUserArgs()[1..]);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs() is ["--morph-gpu"])
            {
                await ExerciseMorphPixels();
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs() is ["--placement", var placementRoot, var cellHex, var observations])
            {
                ExerciseObservedPlacement(placementRoot, cellHex, observations);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs() is ["--actor", var actorRoot, var actorHex])
            {
                RuntimeLiveContentSource.Configure(actorRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
                using var content = RuntimeLiveContentSource.Current!;
                using var records = FalloutPluginStack.Load(content.PluginSources);
                var appearance = FalloutNpcAppearanceResolver.Resolve(records,
                    records.RuntimeFormKey(Convert.ToUInt32(actorHex, 16))) with
                { RuntimeFace = true };
                var actor = RuntimeNativeNpc.Create(appearance, content, 0.0142875f,
                    (npc, part, nif, geometry) => NativeNpcMaterial.Resolve(npc, part, nif, geometry, records, Colors.Black));
                AddChild(actor);
                var morphSurfaces = 0;
                foreach (var mesh in actor.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
                    .Where(mesh => mesh.Mesh is ArrayMesh array && array.GetBlendShapeCount() > 0))
                {
                    CheckMorphBasis(mesh);
                    morphSurfaces++;
                }
                GD.Print($"OPENNV_OWNED_MORPH_BASIS_PASS surfaces={morphSurfaces} basis=packed-runtime-weights pixels=unverified");
                GD.Print($"OPENNV_SOURCE_ACTOR_BUILD_PASS actor={appearance.Npc} parts={actor.Parts.Count} surfaces={actor.Parts.Sum(part => part.Surfaces)} dynamicFaceGen=true");
                actor.Free();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs() is ["--build", var buildRoot, var buildModel])
            {
                RuntimeLiveContentSource.Configure(buildRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
                using var content = RuntimeLiveContentSource.Current!;
                if (!content.TryRead(buildModel, null, out var bytes, out var identity)) throw new FileNotFoundException(buildModel);
                var scene = RuntimeNativeNifMeshBuilder.Build(bytes, 0.0142875f);
                AddChild(scene.Root);
                GD.Print($"OPENNV_NIF_SOURCE_BUILD_PASS source={identity} nodes={scene.Nodes} surfaces={scene.Surfaces} vertices={scene.Vertices}");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs() is [var dataRoot, var model])
            {
                RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
                using var source = RuntimeLiveContentSource.Current!;
                if (!source.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
                Exercise(bytes, 0.0142875f, identity);
            }
            GD.Print("OPENNV_NIF_INSTANCE_AUDIT_PASS controllers=independent targets=instance-owned prototype=unchanged");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private void Exercise(byte[] bytes, float units, string label)
    {
        var prototype = new RuntimeNativeNifPrototype(bytes, units);
        var first = prototype.Instantiate();
        var second = prototype.Instantiate();
        try
        {
            AddChild(first); AddChild(second);
            var beforePrototype = Transforms(prototype.Scene.Root);
            var beforeFirst = Transforms(first);
            var beforeSecond = Transforms(second);
            var firstPlayer = first.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var secondPlayer = second.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().Single();
            var sequence = firstPlayer.ActiveSequence ?? throw new InvalidOperationException("No automatic source animation.");
            var range = firstPlayer.SequenceRange(sequence);
            firstPlayer.SeekSourceTime(range.StartTime + (range.StopTime - range.StartTime) * 0.271);
            if (Transforms(first).SequenceEqual(beforeFirst)) throw new InvalidOperationException("Instance clock did not move its target.");
            if (!Transforms(second).SequenceEqual(beforeSecond) || !Transforms(prototype.Scene.Root).SequenceEqual(beforePrototype))
                throw new InvalidOperationException("Instance playback mutated a sibling or its prototype.");
            var afterFirst = Transforms(first);
            secondPlayer.SeekSourceTime(range.StartTime + (range.StopTime - range.StartTime) * 0.713);
            if (Transforms(second).SequenceEqual(beforeSecond) || !Transforms(first).SequenceEqual(afterFirst))
                throw new InvalidOperationException("Second instance has a missing or shared animation owner.");
            GD.Print($"OPENNV_NIF_INSTANCE_SOURCE source={label} sequence={sequence} interval={range.StartTime:R}..{range.StopTime:R}");
        }
        finally { first.Free(); second.Free(); prototype.Scene.Root.Free(); }
    }

    private static Transform3D[] Transforms(Node3D root) => root.FindChildren("*", "", true, false)
        .OfType<Node3D>().Select(node => node.Transform).ToArray();

    private void ExercisePlaced(byte[] bytes, float units)
    {
        var prototype = new RuntimeNativeNifPrototype(bytes, units);
        var standalone = prototype.Instantiate();
        var placement = new Transform3D(new Basis(Vector3.Up, 0.37f).Scaled(Vector3.One * 1.3f), new Vector3(8, 3, -2));
        var placed = prototype.InstantiatePlaced(placement);
        try
        {
            AddChild(standalone); AddChild(placed);
            var authored = prototype.Scene.Root.GetChild<Node3D>(0).Transform;
            if (authored.IsEqualApprox(Transform3D.Identity) || !standalone.GetChild<Node3D>(0).Transform.IsEqualApprox(authored))
                throw new InvalidOperationException("Standalone NIF lost its non-identity authored root.");
            if (!placed.GetChild<Node3D>(0).GlobalTransform.IsEqualApprox(placement))
                throw new InvalidOperationException("Placed NIF composed the exported root over its reference placement.");
        }
        finally { standalone.Free(); placed.Free(); prototype.Scene.Root.Free(); }
    }

    private void ExerciseObservedPlacement(string dataRoot, string cellHex, string observationPath)
    {
        const float units = 0.0142875f;
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var scene = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(Convert.ToUInt32(cellHex, 16)));
        using var observations = JsonDocument.Parse(File.ReadAllBytes(observationPath));
        var compared = 0;
        foreach (var row in observations.RootElement.EnumerateArray())
        {
            var reference = scene.References.Single(value => records.RuntimeFormId(value.FormKey) == Convert.ToUInt32(row.GetProperty("id").GetString(), 16));
            var model = scene.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Observed reference has no source model.");
            if (!content.TryRead(model, null, out var bytes, out var identity)) throw new FileNotFoundException(model);
            var prototype = new RuntimeNativeNifPrototype(bytes, units);
            var placement = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new Vector3(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new Vector3(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
            var instance = prototype.InstantiatePlaced(placement);
            try
            {
                AddChild(instance);
                var actual = instance.FindChildren("*", "", true, false).OfType<Node3D>()
                    .Where(node => node.HasMeta("opennv_nif_source_name")).ToArray();
                void Compare(JsonElement expected)
                {
                    var name = expected.GetProperty("name").GetString();
                    var node = actual.Single(value => value.GetMeta("opennv_nif_source_name").AsString() == name);
                    var values = expected.GetProperty("world").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                    if (values.Length != 13) throw new InvalidDataException("Observed NiTransform has an invalid extent.");
                    var transform = new Transform3D(GamebryoCoordinate.ConvertBasis(values[..9], values[12], "observed placed node"),
                        GamebryoCoordinate.ConvertVector(new Vector3(values[9], values[10], values[11])) * units);
                    if (!node.GlobalTransform.IsEqualApprox(transform))
                        throw new InvalidOperationException($"Native placement differs: {reference.FormKey}/{name}: expected {transform}; actual {node.GlobalTransform}.");
                    compared++;
                    if (expected.TryGetProperty("children", out var children)) foreach (var child in children.EnumerateArray()) Compare(child);
                }
                Compare(row.GetProperty("node"));
                GD.Print($"OPENNV_PLACEMENT_SOURCE_PASS reference={reference.FormKey} source={identity} transformOwner=reference observedNodes={compared}");
            }
            finally { instance.Free(); prototype.Scene.Root.Free(); }
        }
        if (compared == 0) throw new InvalidDataException("No observed nodes were compared.");
        GD.Print($"OPENNV_NATIVE_PLACEMENT_AUDIT_PASS nodes={compared} comparison=converted-transform-approximate pixels=unverified");
    }

    private static byte[] Synthetic(bool animated = true)
    {
        var blocks = new (string Type, byte[] Bytes)[]
        {
            ("NiNode", Bytes(writer =>
            {
                writer.Write(0); writer.Write(0); writer.Write(animated ? 1 : -1); writer.Write((ushort)14); writer.Write((ushort)0);
                foreach (var value in new float[] { 5, 7, 11, 0, -1, 0, 1, 0, 0, 0, 0, 1, 2 }) writer.Write(value);
                writer.Write(0); writer.Write(-1); writer.Write(0); writer.Write(0);
            })),
            ("NiTransformController", Bytes(writer =>
            {
                writer.Write(-1); writer.Write((ushort)8);
                foreach (var value in new float[] { 1, 0, 0, 1 }) writer.Write(value);
                writer.Write(0); writer.Write(2);
            })),
            ("NiTransformInterpolator", Bytes(writer =>
            {
                for (var index = 0; index < 8; index++) writer.Write(float.MinValue);
                writer.Write(3);
            })),
            ("NiTransformData", Bytes(writer =>
            {
                writer.Write(1); writer.Write(4U);
                for (var axis = 0; axis < 3; axis++)
                {
                    writer.Write(2); writer.Write(2U);
                    foreach (var time in new[] { 0f, 1f })
                    {
                        writer.Write(time); writer.Write(axis == 2 ? time * MathF.PI : 0);
                        writer.Write(0f); writer.Write(0f);
                    }
                }
                writer.Write(0); writer.Write(0);
            })),
        };
        return Bytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Length);
            foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Bytes.Length);
            writer.Write(1); writer.Write(13); writer.Write(13); writer.Write("ArbitraryNode"u8);
            writer.Write(0U);
            foreach (var block in blocks) writer.Write(block.Bytes);
            writer.Write(1U); writer.Write(0);
        });
    }

    private static byte[] Bytes(Action<BinaryWriter> emit)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        emit(writer);
        return stream.ToArray();
    }
}
