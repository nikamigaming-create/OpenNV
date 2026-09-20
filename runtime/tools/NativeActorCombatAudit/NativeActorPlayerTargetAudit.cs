using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit
{
    private async Task ExercisePlayerTarget(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutGlobalState globals, string referenceHex)
    {
        var key = records.RuntimeFormKey(Convert.ToUInt32(referenceHex, 16));
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(key))!.Value);
        var placed = cell.References.Single(value => value.FormKey == key);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell); world.InitializeActorTemplates(key, 1, globals);
        var state = world.Get(key); state.Enabled = true;
        var actor = RuntimeNativeNpc.Create(records, content, placed, .0142875f,
            (appearance, part, nif, geometry) => NativeNpcMaterial.Resolve(appearance, part, nif, geometry, records, new Color(.3f, .3f, .3f)),
            world.EquippedArmor(key, 1, globals), state.Templates);
        // Explicit component fixture: source actor/weapon and native contacts,
        // with a synthetic floor, removable wall and stationary player capsule.
        actor.Position = new(0, .05f, 0); actor.Rotation = new(0, MathF.PI, 0);
        AddChild(actor); actor.SetProcess(false); actor.SetPhysicsProcess(false);
        var player = new RuntimeNativePlayer(); AddChild(player);
        player.Configure(RuntimeConfiguration.Load(), new(Basis.Identity, new(0, .05f, -7)));
        player.CollisionLayer = 2;
        player.SetProcess(false); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
        var floor = new StaticBody3D { Position = new(0, -.05f, 0) };
        var floorShape = new CollisionShape3D { Shape = new BoxShape3D { Size = new(40, .1f, 40) } };
        floorShape.SetMeta("opennv_havok_material", 0u);
        floor.AddChild(floorShape); AddChild(floor);
        var wall = new StaticBody3D { Position = new(0, 2, -3) };
        var wallShape = new CollisionShape3D { Shape = new BoxShape3D { Size = new(4, 4, .2f) } };
        wallShape.SetMeta("opennv_havok_material", 0u); wall.AddChild(wallShape);
        var scenery = new Node3D(); AddChild(scenery);
        // Dense world contacts inside the spread cone must not exhaust the
        // separate friendly-actor query or block a clear muzzle line.
        for (var index = 0; index < 160; index++)
        {
            var detail = new StaticBody3D { Position = new(1.6f + index % 8 * .02f, 1.4f, -5 - index / 8 * .01f) };
            detail.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.01f, .01f, .01f) } });
            scenery.AddChild(detail);
        }
        var health = 200f; var hits = 0;
        var context = new NativeActorCombatContext(() => player, () => new(1, 200, (int)health, 70, 70, 0, 100),
            (damage, _) => { health -= damage.Amount; hits++; },
            (_, to) => [new(3.5f, .05f, 0), new(3.5f, .05f, -4), to],
            _ => true, () => 1, globals, .4f, 9.81f);
        try
        {
            RuntimeNativeActorContacts.Configure(actor, actor.Skeleton, 2);
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                world, state, records, content, 2, 3, context);
            actor.Combat.SetPhysicsProcess(false);
            state.Engagement = new(records.RuntimeFormKey(0x14));
            await Step();
            if (state.Engagement?.Action != "idle" || state.Engagement.Animation?.Contains("mtidle", StringComparison.OrdinalIgnoreCase) != true)
                throw new InvalidOperationException("Turning toward a visible in-range player did not use the stationary idle.");
            AddChild(wall);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var before = actor.GlobalPosition;
            for (var frame = 0; frame < 12; frame++) await Step();
            if (hits != 0 || state.Engagement?.Action != "pursue" || actor.GlobalPosition.DistanceTo(before) < .1f)
                throw new InvalidOperationException("Occluded in-range target did not cause real pursuit without firing through the wall.");
            wall.Free();
            for (var frame = 0; frame < 900 && hits == 0; frame++) await Step();
            if (hits == 0 || health >= 200)
                throw new InvalidOperationException("Source NPC did not attack and damage its visible player target.");
            GD.Print($"OPENNV_NATIVE_PLAYER_TARGET_PASS reference={key} hits={hits} health=200->{health:R} " +
                "playerCapsuleVisible=true turningIdle=true occludedPursuit=true wallRefused=true denseWorldContacts=160 fixture=synthetic-floor-and-wall ordinaryGameplay=separate");
        }
        finally
        {
            actor.Free(); player.Free(); floor.Free(); scenery.Free();
            if (IsInstanceValid(wall)) wall.Free();
        }

        async Task Step()
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            actor.Combat!._PhysicsProcess(1d / 60);
            if (actor.Combat.EngagementError is { } error) throw new InvalidOperationException(error);
        }
    }
}
