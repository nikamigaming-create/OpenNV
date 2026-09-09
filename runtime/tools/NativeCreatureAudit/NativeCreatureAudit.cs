using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeCreatureAudit : Node3D
{
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args.Length < 2) throw new ArgumentException("Expected owned Data and one or more ACRE runtime hex identities.");
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var source = RuntimeLiveContentSource.Current!;
            using var stack = FalloutPluginStack.Load(source.PluginSources);
            using var world = new FalloutReferenceWorld(stack);
            var failed = 0;
            foreach (var hex in args.Skip(1))
            {
                try { Exercise(stack, source, world, hex); }
                catch (Exception error) { GD.PushError($"OPENNV_CREATURE_AUDIT_FAIL reference={hex} {error}"); failed++; }
            }
            GetTree().Quit(failed == 0 ? 0 : 1);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Exercise(FalloutPluginStack stack, RuntimeLiveContentSource source, FalloutReferenceWorld world, string hex)
    {
        var key = stack.RuntimeFormKey(Convert.ToUInt32(hex, 16));
        var record = stack.GetEffective(key);
        var cell = FalloutCellSceneReader.Read(stack, FalloutCellSceneReader.ParentCell(record)!.Value);
        var reference = cell.References.Single(item => item.FormKey == key);
        var actor = RuntimeNativeCreature.Create(stack, source, reference, world.Get(key), .0142875f);
        AddChild(actor); actor.SetProcess(false);
        try
        {
            actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(
                new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * .0142875f);
            var placement = actor.Transform;
            var first = Poses(actor);
            actor._Process(.73);
            if (actor.Error is not null || first.SequenceEqual(Poses(actor)) || actor.Transform != placement)
                throw new InvalidDataException("Source creature did not animate independently of authored placement: " + actor.Error);
            RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 1);
            var expected = Poses(actor);
            var expectedMaterials = MaterialValues(actor);
            var snapshot = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(world.Get(key).Capture()))!;
            using var restored = new FalloutReferenceWorld(stack);
            restored.Restore([snapshot]);
            var second = RuntimeNativeCreature.Create(stack, source, reference, restored.Get(key), .0142875f);
            AddChild(second); second.SetProcess(false);
            try
            {
                if (!expected.SequenceEqual(Poses(second)))
                    throw new InvalidDataException("Cold restoration changed the creature pose.");
                if (!expectedMaterials.SequenceEqual(MaterialValues(second)))
                    throw new InvalidDataException("Cold restoration changed the creature's KF material channels.");
                second._Process(.41);
                if (!expected.SequenceEqual(Poses(actor)) || expected.SequenceEqual(Poses(second)))
                    throw new InvalidDataException("Creature instances share a mutable pose or clock.");
                if (!expectedMaterials.SequenceEqual(MaterialValues(actor)))
                    throw new InvalidDataException("Creature instances share animated material state.");
                actor._Process(.41);
                if (!Poses(actor).SequenceEqual(Poses(second))) throw new InvalidDataException("Restored animation continuation differs.");
                if (!MaterialValues(actor).SequenceEqual(MaterialValues(second)))
                    throw new InvalidDataException("Restored material animation continuation differs.");
            }
            finally { second.Free(); }
            GD.Print($"OPENNV_CREATURE_AUDIT_PASS reference={hex} bones={actor.Skeleton.Node.GetBoneCount()} parts={actor.Parts.Count} " +
                $"surfaces={actor.Parts.Sum(part => part.Surfaces)} baseScale={actor.Appearance.BaseScale:R} sourceIdle={world.Get(key).Animation.Resource} " +
                "pose=source-KF contacts=source-shapes independent=true coldContinuation=exact ordinaryPixels=unverified");
        }
        finally { actor.Free(); }
    }

    private static Transform3D[] Poses(RuntimeNativeCreature actor) => Enumerable.Range(0, actor.Skeleton.Node.GetBoneCount())
        .Select(actor.Skeleton.Node.GetBonePose).ToArray();

    private static Variant[] MaterialValues(RuntimeNativeCreature actor) => actor.FindChildren("*", "", true, false)
        .OfType<MeshInstance3D>().Where(mesh => mesh.Mesh is not null)
        .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(mesh.GetActiveMaterial))
        .OfType<ShaderMaterial>().SelectMany(material => new[] { "source_uv_offset", "source_uv_scale", "source_uv_rotation",
            "source_color_multiplier", "source_emissive_color", "emissive_color", "base_factor" }.Select(name => material.GetShaderParameter(name))).ToArray();
}
