using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit
{
    private async Task ExerciseAttackRangeCold(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutFormKey key, FalloutFormKey targetKey, RuntimeNativeActorCombat attacker,
        List<CharacterBody3D> actors, Func<FalloutFormKey, Vector3, FalloutReferenceWorld?, RuntimeNativeActorCombat> create)
    {
        if (actors.Count != 2) throw new InvalidOperationException("Attack range/cold fixture requires one source pair.");
        var before = world.Health(targetKey).Current;
        for (var frame = 0; frame < 600 && world.Get(key).Engagement?.Action != "attack"; frame++)
            await Step(attacker);
        var attack = world.Get(key).Engagement;
        if (attack is not { Action: "attack", StartPending: false, Animation: not null } ||
            Observe(attacker).GetProperty("attacks").GetInt64() != 0 || world.Health(targetKey).Current != before)
            throw new InvalidOperationException("Source attack has no unconsumed windup for range/cold verification.");

        // Freeze the fixture's combat producer while actual finite native
        // voices finish, as the manual-save preparation owner does. Never
        // pretend an elapsed interval is a native completion receipt.
        for (var frame = 0; frame < 180 && (!world.Get(key).AnimationSoundsCaptureReady ||
            !world.Get(targetKey).AnimationSoundsCaptureReady); frame++) await Frames(1);
        if (!world.Get(key).AnimationSoundsCaptureReady || !world.Get(targetKey).AnimationSoundsCaptureReady)
            throw new InvalidOperationException("Source attack audio did not become capturable during its frozen windup.");

        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(world.Get(key).Capture()))!;
        var savedTarget = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(world.Get(targetKey).Capture()))!;
        var actorTransform = actors[0].GlobalTransform;
        var targetTransform = actors[1].GlobalTransform;
        var coldStart = actors.Count;
        using (var coldWorld = new FalloutReferenceWorld(records))
        {
            coldWorld.Restore([saved, savedTarget]);
            // Keep the two worlds' physical query shapes apart. Only the fresh
            // source skins participate in the cold fixture's contact query.
            actors[0].GlobalPosition += Vector3.Right * 100;
            actors[1].GlobalPosition += Vector3.Right * 100;
            try
            {
                var cold = create(key, actorTransform.Origin - Vector3.Up * .05f, coldWorld);
                _ = create(targetKey, targetTransform.Origin - Vector3.Up * .05f, coldWorld);
                await Frames(3);
                cold._PhysicsProcess(0);
                if (cold.EngagementError is { } error) throw new InvalidOperationException(error);
                var resumed = coldWorld.Get(key).Engagement;
                if (resumed?.Animation != attack.Animation || resumed.AnimationHash != attack.AnimationHash ||
                    resumed.Seconds != attack.Seconds || coldWorld.Get(key).AttackRandom.State != world.Get(key).AttackRandom.State ||
                    Observe(cold).GetProperty("attacks").GetInt64() != 0)
                    throw new InvalidOperationException("Fresh native attack rerolled, rewound or replayed its saved event prefix.");
                for (var frame = 0; frame < 600 && coldWorld.Health(targetKey).Current == before; frame++)
                    await Step(cold);
                if (coldWorld.Health(targetKey).Current >= before ||
                    Observe(cold).GetProperty("attacks").GetInt64() != 1 || world.Health(targetKey).Current != before)
                    throw new InvalidOperationException("Cold native attack failed its source hit or crossed world authority.");
            }
            finally
            {
                foreach (var actor in actors.Skip(coldStart))
                    if (GodotObject.IsInstanceValid(actor)) actor.Free();
                actors.RemoveRange(coldStart, actors.Count - coldStart);
                actors[0].GlobalTransform = actorTransform;
                actors[1].GlobalTransform = targetTransform;
            }
        }
        // A real Hit key must miss after the target moves out during windup.
        // Continue the saved original clock; do not fabricate a Hit callback.
        actors[1].GlobalPosition = actorTransform.Origin + Vector3.Back * 12;
        await Frames(3);
        for (var frame = 0; frame < 600 && Observe(attacker).GetProperty("attacks").GetInt64() == 0; frame++)
            await Step(attacker);
        var observation = Observe(attacker);
        var last = observation.GetProperty("lastAttack");
        if (observation.GetProperty("attacks").GetInt64() != 1 || world.Health(targetKey).Current != before ||
            last.GetProperty("distance").GetSingle() <= last.GetProperty("range").GetSingle() ||
            last.GetProperty("part").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("Source melee event damaged an out-of-range target or never executed.");
        actors[1].GlobalTransform = targetTransform;
        await Frames(3);
        GD.Print($"OPENNV_NATIVE_ATTACK_RANGE_COLD_PASS actor={key} target={targetKey} animation={attack.Animation} " +
            $"seconds={attack.Seconds:R} sourceHash={attack.AnimationHash} nativeCold=true randomRetained=true " +
            "eventPrefixRetained=true coldHitOnce=true crossWorldDamage=false movedOutDuringWindup=true sourceMiss=true " +
            "fixture=synthetic-floor ordinaryGameplay=separate");
    }

    private static JsonElement Observe(RuntimeNativeActorCombat actor) =>
        JsonSerializer.SerializeToElement(actor.Observation).GetProperty("engagement");

    private async Task Step(RuntimeNativeActorCombat actor)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        actor._PhysicsProcess(1d / 60);
        if (actor.EngagementError is { } error) throw new InvalidOperationException(error);
    }
}
