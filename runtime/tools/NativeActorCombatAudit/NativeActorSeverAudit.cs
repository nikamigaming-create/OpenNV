using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorSeverAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            CheckRebinding();
            var args = OS.GetCmdlineUserArgs();
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            foreach (var hex in args.Skip(1)) await Exercise(records, content, records.RuntimeFormKey(Convert.ToUInt32(hex, 16)));
            GD.Print("OPENNV_NATIVE_ACTOR_SEVER_AUDIT_PASS sourceJoints=true sourcePartitions=true coldRestore=true ordinaryGameplay=separate");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("OPENNV_NATIVE_ACTOR_SEVER_AUDIT_FAIL " + error); GetTree().Quit(1); }
    }

    private async Task Exercise(FalloutPluginStack records, RuntimeLiveContentSource content, FalloutFormKey key)
    {
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(key))!.Value);
        var placed = cell.References.Single(value => value.FormKey == key);
        var globals = FalloutGlobalState.Read(records);
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        var sourceParts = world.BodyParts(key).Parts.Where(part => (part.Flags & 1) != 0).ToArray();
        foreach (var part in sourceParts)
        {
            using var instance = new FalloutReferenceWorld(records); instance.LoadCell(cell);
            var actor = Create(instance);
            var floor = new StaticBody3D { Position = actor.Root.Position - Vector3.Up * .05f };
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(20, .1f, 20) } }); AddChild(floor);
            try
            {
                AddChild(actor.Root); await Frames(3);
                var health = instance.Health(key).Current;
                var contact = actor.Root.FindChildren("*", "Area3D", true, false).OfType<Area3D>().First(area => actor.Combat.HitPart(area) == 0);
                var hit = actor.Combat.Hit(contact, new(health * 20 + 1000, 1, 100, 1), records.RuntimeFormKey(0x14), 1, globals);
                if (!hit.Dead) throw new InvalidDataException("Controlled source hit failed to kill its target.");
                actor.Combat.SeverLimb(part.Type);
                CheckSkin(actor.Root, part);
                await Frames(180);
                var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(instance.Get(key).Capture()))!;
                if (saved.Injury?.SeveredParts?.SequenceEqual(new[] { part.Type }) != true || saved.Ragdoll is null)
                    throw new InvalidDataException("Severed limb state was not captured.");
                saved.Ragdoll.Validate();
                var minY = saved.Ragdoll.Bodies.Min(body => body.Transform[10]);
                var maxY = saved.Ragdoll.Bodies.Max(body => body.Transform[10]);
                if (minY < floor.Position.Y - .2 || maxY > floor.Position.Y + 4)
                    throw new InvalidDataException($"Severed body escaped its floor: {minY}..{maxY} floor={floor.Position.Y}.");
                actor.Root.Free();
                using var restored = new FalloutReferenceWorld(records); restored.Restore([saved]); restored.LoadCell(cell);
                var cold = Create(restored);
                try
                {
                    AddChild(cold.Root); await Frames(3);
                    if (cold.Combat.Error is not null) throw new InvalidDataException(cold.Combat.Error);
                    CheckSkin(cold.Root, part);
                    if (restored.Get(key).Capture().Injury!.SeveredParts!.SequenceEqual(new[] { part.Type }) != true)
                        throw new InvalidDataException("Cold corpse lost its severed part.");
                    GD.Print($"OPENNV_ACTOR_SEVER_SOURCE_PASS reference={key} part={part.Type} bone={part.Node} health={health} bodies={saved.Ragdoll.Bodies.Count} range={minY}..{maxY}");
                }
                finally { cold.Root.Free(); }
            }
            finally { if (IsInstanceValid(actor.Root)) actor.Root.Free(); floor.Free(); }
        }

        (Node3D Root, RuntimeNativeActorCombat Combat) Create(FalloutReferenceWorld owner)
        {
            Node3D root; RuntimeNativeNifSkeleton skeleton; string path;
            if (records.GetEffective(placed.Base).Signature == "NPC_")
            {
                var armor = owner.EquippedArmor(key, 1, globals);
                var npc = RuntimeNativeNpc.Create(records, content, placed, .0142875f,
                    (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.3f, .3f, .3f)), armor);
                root = npc; skeleton = npc.Skeleton; path = npc.Appearance.SkeletonPath;
            }
            else
            {
                var creature = RuntimeNativeCreature.Create(records, content, placed, owner.Get(key), .0142875f);
                root = creature; skeleton = creature.Skeleton; path = creature.Appearance.SkeletonPath;
            }
            root.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
                GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * .0142875f);
            RuntimeNativeActorContacts.Configure(root, skeleton, 2);
            var combat = RuntimeNativeActorCombat.Attach(root, skeleton, path, owner, owner.Get(key), records, content, 2, 3);
            if (root is RuntimeNativeNpc npcActor) npcActor.Combat = combat;
            else ((RuntimeNativeCreature)root).Combat = combat;
            return (root, combat);
        }
    }

    private static void CheckSkin(Node3D actor, FalloutBodyPart part)
    {
        var skeleton = actor is RuntimeNativeNpc npc ? npc.Skeleton.Node : ((RuntimeNativeCreature)actor).Skeleton.Node;
        var root = skeleton.FindBone(part.Node);
        foreach (var mesh in actor.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.Skin is not null && mesh.HasMeta("opennv_nif_body_part")))
        {
            var tag = mesh.GetMeta("opennv_nif_body_part").AsInt32();
            var detached = tag == part.Type || tag == part.Type + 100;
            for (var index = 0; index < mesh.Skin!.GetBindCount(); index++)
                if (RuntimeNativeDismemberSkin.DescendsFrom(skeleton, mesh.Skin.GetBindBone(index), root) != detached)
                    throw new InvalidDataException("Severed skin still crosses the physical cut.");
        }
    }

    private static void CheckRebinding()
    {
        var previous = new Transform3D(Basis.FromEuler(new(.4f, -.8f, .2f)), new(2, 1, -3));
        var anchor = new Transform3D(Basis.FromEuler(new(-.7f, .3f, .5f)), new(-1, 4, 2));
        var binding = new Transform3D(Basis.FromEuler(new(.1f, .2f, .3f)), new(.4f, .5f, -.2f));
        var changed = RuntimeNativeDismemberSkin.Rebind(previous, anchor, binding);
        foreach (var vertex in new[] { Vector3.Zero, Vector3.One, new Vector3(-2, .3f, 4) })
        {
            var before = previous * binding * vertex;
            var after = anchor * changed * vertex;
            if (before.DistanceTo(after) > .00001f) throw new InvalidDataException("Cut rebinding displaced a skin vertex.");
            var moved = anchor; moved.Origin += Vector3.Right * 2;
            if ((moved * changed * vertex - after).DistanceTo(Vector3.Right * 2) > .00001f)
                throw new InvalidDataException("Detached skin does not follow its own side of the cut.");
        }
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }
}
