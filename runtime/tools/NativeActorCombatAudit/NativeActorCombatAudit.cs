using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit : Node3D
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            RuntimeLiveContentSource.Configure(args[0], RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var globals = FalloutGlobalState.Read(records);
            foreach (var hex in args.Skip(1)) await Exercise(records, content, globals, hex);
            GD.Print("OPENNV_NATIVE_ACTOR_COMBAT_AUDIT_PASS sourceContacts=true health=true ragdoll=true deathInventory=true coldRestore=true ordinaryGameplay=separate");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("OPENNV_NATIVE_ACTOR_COMBAT_AUDIT_FAIL " + error); GetTree().Quit(1); }
    }

    private async Task Exercise(FalloutPluginStack records, RuntimeLiveContentSource content, FalloutGlobalState globals, string hex)
    {
        var key = records.RuntimeFormKey(Convert.ToUInt32(hex, 16));
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(key))!.Value);
        var placed = cell.References.Single(value => value.FormKey == key);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell);
        var actor = RuntimeNativeCreature.Create(records, content, placed, world.Get(key), .0142875f);
        // This is a component physics fixture at the actual reference position.
        // The ordinary run separately uses resident source LAND and user input.
        actor.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale),
            GamebryoCoordinate.ConvertVector(new(placed.Position[0], placed.Position[1], placed.Position[2])) * .0142875f);
        AddChild(actor);
        var floor = new StaticBody3D { Position = actor.GlobalPosition - Vector3.Up * .05f };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(20, .1f, 20) } }); AddChild(floor);
        RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
        actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath, world, world.Get(key), records, content, 2, 3);
        try
        {
            await Frames(3);
            var contact = actor.FindChildren("*", "Area3D", true, false).OfType<Area3D>()
                .First(area => actor.Combat.HitPart(area) == 0);
            var health = world.Health(key).Current;
            var defense = world.Defense(key, 1, globals);
            var first = actor.Combat.Hit(contact, new(health / 4, 1, 100, 1), records.RuntimeFormKey(0x14), 1, globals);
            var expected = health - defense.Absorb(health / 4, FalloutGameSettingFloats.Read(records, "fMinDamMultiplier"));
            if (first.Dead || first.HealthAfter != expected) throw new InvalidDataException("Nonfatal source hit did not preserve life.");
            var died = actor.Combat.Hit(contact, new(health * 10, 1, 100, 1), records.RuntimeFormKey(0x14), 1, globals);
            if (!died.Died || !actor.Combat.Dead) throw new InvalidDataException("Fatal source hit did not publish death.");
            var initial = world.Get(key).Capture().Ragdoll!;
            await Frames(180);
            var fallen = world.Get(key).Capture().Ragdoll!;
            if (fallen.Bodies.Any(body => body.Transform.Any(value => !float.IsFinite(value))) ||
                !fallen.Bodies.Any(body => MathF.Abs(body.Transform[10] - initial.Bodies.Single(old => old.SourceBody == body.SourceBody).Transform[10]) > .03f))
                throw new InvalidDataException("Source ragdoll did not fall or became nonfinite.");
            var minY = fallen.Bodies.Min(body => body.Transform[10]);
            var maxY = fallen.Bodies.Max(body => body.Transform[10]);
            if (minY < floor.GlobalPosition.Y - .2f || maxY > actor.GlobalPosition.Y + 4)
                throw new InvalidDataException($"Ragdoll escaped its floor/joints: minimum={minY}, maximum={maxY}, floor={floor.GlobalPosition.Y}.");
            var inventory = world.Inventory(key, 1, globals).Contents;
            var before = inventory.Items.Sum(item => item.Count);
            var player = new FalloutPlayerInventory();
            foreach (var item in inventory.Items.ToArray()) inventory.TransferTo(player, item.FormKey, item.Count);
            if (player.Items.Sum(item => item.Count) != before || inventory.Items.Count != 0) throw new InvalidDataException("Corpse loot transfer lost items.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(world.Get(key).Capture()))!;
            var authoredTransform = actor.Transform;
            actor.Free();
            using var restored = new FalloutReferenceWorld(records); restored.Restore([saved]); restored.LoadCell(cell);
            var cold = RuntimeNativeCreature.Create(records, content, placed, restored.Get(key), .0142875f);
            cold.Transform = authoredTransform;
            RuntimeNativeActorContacts.Configure(cold, cold.Skeleton, 2);
            GamebryoReferenceEnableRuntime.Apply(cold, false);
            cold.Combat = RuntimeNativeActorCombat.Attach(cold, cold.Skeleton, cold.Appearance.SkeletonPath, restored, restored.Get(key), records, content, 2, 3);
            // Production builds the whole cell off-tree before mounting it.
            // Exercise that ordering, including the parent's Ready traversal.
            var coldCell = new Node3D(); coldCell.AddChild(cold); AddChild(coldCell);
            try
            {
                var initialized = new TaskCompletionSource();
                Callable.From(() => initialized.SetResult()).CallDeferred();
                await initialized.Task;
                if (cold.Combat.Error is not null) throw new InvalidDataException(cold.Combat.Error);
                var bodies = cold.FindChildren("*", "RigidBody3D", true, false).OfType<RigidBody3D>().ToArray();
                if (bodies.Any(body => body.CollisionLayer != 0 || body.CollisionMask != 0))
                    throw new InvalidDataException("Deferred death re-enabled a disabled reference's collision.");
                GamebryoReferenceEnableRuntime.Apply(cold, true);
                if (bodies.Length != saved.Ragdoll!.Bodies.Count || bodies.Any(body => !body.IsInsideTree() || body.CollisionLayer != 2))
                    throw new InvalidDataException("Cold corpse lacks live queryable source bodies.");
                if (cold.FindChildren("*", "Area3D", true, false).OfType<Area3D>().Any(area => area.CollisionLayer != 0))
                    throw new InvalidDataException("Reference enable resurrected living contacts on a corpse.");
                var after = restored.Get(key).Capture();
                if (!restored.IsDead(key) || restored.Inventory(key, 1, globals).Contents.Items.Count != 0 ||
                    !after.Ragdoll!.Bodies.Zip(saved.Ragdoll!.Bodies).All(pair => pair.First.Transform.Zip(pair.Second.Transform).All(v => MathF.Abs(v.First - v.Second) < .0002f)))
                    throw new InvalidDataException("Cold corpse lost pose, resurrected, or rerolled its inventory.");
                await Frames(3);
                var center = bodies[0].GlobalPosition;
                using var query = PhysicsRayQueryParameters3D.Create(center + Vector3.Up * 2, center - Vector3.Up, 2);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
                if (hit.Count == 0 || RuntimeNativeActorCombat.Find(hit["collider"].AsGodotObject() as Node) != cold.Combat)
                    throw new InvalidDataException("Cold corpse cannot be targeted through the normal physics query.");
                GD.Print($"OPENNV_ACTOR_COMBAT_SOURCE_PASS reference={key} health={health} bodies={fallen.Bodies.Count} lootTransferred={before} floor={floor.GlobalPosition.Y} range={minY}..{maxY}");
            }
            finally { coldCell.Free(); }
        }
        finally { if (IsInstanceValid(actor)) actor.Free(); floor.Free(); }
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }
}
