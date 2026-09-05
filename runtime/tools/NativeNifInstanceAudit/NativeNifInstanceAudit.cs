using System.Text;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit : Node
{
    public override void _Ready()
    {
        try
        {
            Exercise(Synthetic(), 0.02f, "synthetic");
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
                GD.Print($"OPENNV_SOURCE_ACTOR_BUILD_PASS actor={appearance.Npc} parts={actor.Parts.Count} surfaces={actor.Parts.Sum(part => part.Surfaces)} dynamicFaceGen=true");
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

    private static byte[] Synthetic()
    {
        var blocks = new (string Type, byte[] Bytes)[]
        {
            ("NiNode", Bytes(writer =>
            {
                writer.Write(0); writer.Write(0); writer.Write(1); writer.Write((ushort)14); writer.Write((ushort)0);
                foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
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
