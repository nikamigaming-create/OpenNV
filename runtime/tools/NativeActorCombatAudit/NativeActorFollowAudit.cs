using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit
{
    private async Task ExerciseFollow(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutGlobalState globals, string referenceHex, string packageHex, string? movedToHex)
    {
        var key = records.RuntimeFormKey(Convert.ToUInt32(referenceHex, 16));
        var package = records.GetEffective(records.RuntimeFormKey(Convert.ToUInt32(packageHex, 16)));
        var follow = FalloutFollowPackage.Read(package);
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(key))!.Value);
        using var world = new FalloutReferenceWorld(records);
        if (movedToHex is not null)
        {
            world.MoveTo(key, records.RuntimeFormKey(Convert.ToUInt32(movedToHex, 16)));
            cell = world.ComposeResidency(FalloutCellSceneReader.Read(records, world.Placement(key).Cell));
        }
        var placed = cell.References.Single(value => value.FormKey == key);
        world.LoadCell(cell);
        world.InitializeActorTemplates(key, 1, globals);
        // Explicit component fixture. No checkpoint or ordinary-gameplay state
        // is changed; source packages, clips and the native collision owner run
        // against a synthetic floor and a stationary target.
        var state = world.Get(key); state.Enabled = true;
        var parent = state;
        while (parent.EnableParent is { } link)
        {
            parent = world.Get(link.Reference); parent.Enabled = !link.Opposite;
        }
        var actor = RuntimeNativeCreature.Create(records, content, placed, state, .0142875f);
        actor.Position = new(0, 2, 0); AddChild(actor); actor.SetPhysicsProcess(false);
        var player = new RuntimeNativePlayer { Position = new(0, 0, -12) }; AddChild(player);
        player.SetProcess(false); player.SetPhysicsProcess(false); player.SetProcessUnhandledInput(false);
        player.CollisionLayer = 2;
        player.AddChild(new CollisionShape3D
        {
            Position = Vector3.Up * .9f,
            Shape = new CapsuleShape3D { Height = 1.8f, Radius = .32f }
        });
        var floor = new StaticBody3D { Position = new(0, -.05f, -6) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(40, .1f, 40) } }); AddChild(floor);
        var obstacles = new Node3D(); AddChild(obstacles);
        void Box(Vector3 position, Vector3 size)
        {
            var body = new StaticBody3D { Position = position };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
            obstacles.AddChild(body);
        }
        Box(new(0, .15f, -1.5f), new(20, .3f, .5f));
        Box(new(0, 1.5f, -4), new(4, 3, .5f));
        var noRoute = false;
        var context = new NativeActorCombatContext(() => player, () => new(1, 200, 200, 70, 70, 0, 100),
            (_, _) => throw new InvalidOperationException("Follow fixture attacked its target."),
            (_, to) => noRoute ? [] : [to], _ => true, () => 1, globals, .4f, 9.81f);
        try
        {
            actor.Combat = RuntimeNativeActorCombat.Attach(actor, actor.Skeleton, actor.Appearance.SkeletonPath,
                world, state, records, content, 2, 3, context);
            actor.Combat.SetPhysicsProcess(false);
            // A raised MoveTo placement must settle even when the target is
            // already inside the package's stopping distance.
            for (var frame = 0; frame < 90; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat.AdvancePackageMotion(package, player.GlobalPosition, 100, true, 1d / 60);
            }
            if (!actor.IsOnFloor() || Math.Abs(actor.GlobalPosition.Y) > .02f)
                throw new InvalidOperationException($"Stationary package did not settle its raised root: {actor.GlobalPosition}.");
            var origin = actor.GlobalPosition;
            var stop = follow.Distance * actor.Skeleton.UnitsToMetres;
            var maximumSide = 0f;
            for (var frame = 0; frame < 600; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat.AdvancePackageMotion(package, player.GlobalPosition, stop, true, 1d / 60);
                maximumSide = Math.Max(maximumSide, Math.Abs(actor.GlobalPosition.X));
                if (actor.GlobalPosition.DistanceTo(player.GlobalPosition) <= stop) break;
            }
            if (actor.GlobalPosition.DistanceTo(origin) < 3 || actor.GlobalPosition.DistanceTo(player.GlobalPosition) > stop + .15f)
            {
                GD.Print(JsonSerializer.Serialize(actor.Combat.Observation));
                throw new InvalidOperationException($"Follow root motion did not reach the source distance: origin={origin}, final={actor.GlobalPosition}, stop={stop}.");
            }
            if (maximumSide < 2.2f || actor.GlobalPosition.Z > -4.5f)
                throw new InvalidOperationException("Source actor did not climb the curb and go around the wall with its actual capsule.");
            var settled = actor.GlobalPosition;
            for (var frame = 0; frame < 30; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat.AdvancePackageMotion(package, player.GlobalPosition, stop, true, 1d / 60);
            }
            if (actor.GlobalPosition.DistanceTo(settled) > .15f)
                throw new InvalidOperationException($"Follow failed to stop at its authored distance: {settled} -> {actor.GlobalPosition}.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var coldWorld = new FalloutReferenceWorld(records); coldWorld.Restore(saved); coldWorld.LoadCell(cell);
            var cold = RuntimeNativeCreature.Create(records, content, placed, coldWorld.Get(key), .0142875f);
            AddChild(cold); cold.SetPhysicsProcess(false);
            try
            {
                cold.Combat = RuntimeNativeActorCombat.Attach(cold, cold.Skeleton, cold.Appearance.SkeletonPath,
                    coldWorld, coldWorld.Get(key), records, content, 2, 3, context);
                cold.Combat.SetPhysicsProcess(false); cold.Combat.RestorePackageMotion();
                if (cold.GlobalPosition.DistanceTo(actor.GlobalPosition) > .0001f)
                    throw new InvalidOperationException("Cold follow lost its observed position.");
                var retained = coldWorld.Get(key).PackageMotion!;
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                cold.Combat.AdvancePackageMotion(package, player.GlobalPosition, stop, true, 1d / 60);
                if (coldWorld.Get(key).PackageMotion!.Seconds <= retained.Seconds)
                    throw new InvalidOperationException("Cold follow restarted its animation clock.");
            }
            finally { cold.Free(); }
            player.Position = new(0, 0, 4);
            for (var frame = 0; frame < 900 && actor.GlobalPosition.DistanceTo(player.GlobalPosition) > stop + .1f; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat.AdvancePackageMotion(package, player.GlobalPosition, stop, true, 1d / 60);
            }
            if (actor.GlobalPosition.DistanceTo(player.GlobalPosition) > stop + .15f)
                throw new InvalidOperationException($"Source actor could not follow back across the same obstructions: {actor.GlobalPosition}.");
            // An unavailable source corridor must hold still and remain
            // retryable, rather than turn into direct steering through a wall.
            noRoute = true; player.Position = new(0, 0, 12);
            for (var frame = 0; frame < 180; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat.AdvancePackageMotion(package, player.GlobalPosition, stop, true, 1d / 60);
            }
            var held = actor.GlobalPosition;
            for (var frame = 0; frame < 60; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat.AdvancePackageMotion(package, player.GlobalPosition, stop, true, 1d / 60);
            }
            if (actor.GlobalPosition.DistanceTo(held) > .02f || state.PackageMotion!.Animation.Contains("forward", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unavailable corridor did not hold the source idle.");
            noRoute = false;
            for (var frame = 0; frame < 900 && actor.GlobalPosition.DistanceTo(player.GlobalPosition) > stop + .1f; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                actor.Combat.AdvancePackageMotion(package, player.GlobalPosition, stop, true, 1d / 60);
            }
            if (actor.GlobalPosition.DistanceTo(player.GlobalPosition) > stop + .15f)
                throw new InvalidOperationException($"Available corridor did not recover after a bounded retry: {actor.GlobalPosition}.");
            GD.Print($"OPENNV_NATIVE_FOLLOW_PASS reference={key} package={package.FormKey} distance={stop:R} " +
                $"movement={origin.DistanceTo(actor.GlobalPosition):R} health={world.Health(key).Base:R} coldPose=true coldClock=true " +
                "raisedIdle=true curb=true wallDetour=true return=true unavailableIdle=true retry=true fixture=synthetic-floor-and-obstacles ordinaryRecruitment=unverified");
        }
        finally { actor.Free(); player.Free(); floor.Free(); obstacles.Free(); }
    }
}
