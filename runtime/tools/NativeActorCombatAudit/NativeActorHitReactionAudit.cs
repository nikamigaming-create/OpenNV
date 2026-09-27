using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorCombatAudit
{
    private async Task ExerciseHitReaction(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutGlobalState globals, RuntimeNativeActorCombat combat, CharacterBody3D actor,
        FalloutFormKey key, FalloutFormKey target)
    {
        var part = world.BodyParts(key).Parts.First(part => part.ActorValue == 27 && part.HealthPercent > 0);
        var contact = actor.FindChildren("*", "Area3D", true, false).OfType<Area3D>()
            .First(area => combat.HitPart(area) == part.Type);
        var health = world.Health(key).Current;
        combat.Hit(contact, new(.1f, 1, 100, 1), target, 1, globals);
        if (combat.ReactingToHit) throw new InvalidOperationException("Healthy limb fabricated a hit reaction.");
        combat._PhysicsProcess(1d / 60);
        combat.Hit(contact, new(health * .1f, 1000, 100, 1), target, 1, globals);
        if (!combat.ReactingToHit || !combat.OwnsPose) throw new InvalidOperationException("Source crippled-limb reaction was not selected: " + JsonSerializer.Serialize(combat.HitReactionObservation));
        var initial = world.Get(key).HitReaction!;
        var skeleton = actor is RuntimeNativeNpc npc ? npc.Skeleton : ((RuntimeNativeCreature)actor).Skeleton;
        var prior = Enumerable.Range(0, skeleton.Node.GetBoneCount()).Select(skeleton.Node.GetBonePose).ToArray();
        for (var frame = 0; frame < 12; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            combat._PhysicsProcess(1d / 60);
        }
        var current = world.Get(key).HitReaction ?? throw new InvalidOperationException("Reaction completed before its source interval.");
        if (current.Animation.ElapsedSeconds <= 0 || current.Animation.StartPending ||
            prior.Select((pose, index) => pose != skeleton.Node.GetBonePose(index)).All(changed => !changed))
            throw new InvalidOperationException("Selected hit reaction never published an animated pose.");
        var randomBeforeRepeat = world.Get(key).HitReactionRandom.State;
        combat.Hit(contact, new(.1f, 1, 100, 1), target, 1, globals);
        if (world.Get(key).HitReaction!.Animation.ElapsedSeconds != current.Animation.ElapsedSeconds ||
            world.Get(key).HitReactionRandom.State != randomBeforeRepeat)
            throw new InvalidOperationException("A repeated pellet restarted or rerolled the active reaction.");
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(world.Get(key).Capture()))!;
        using var coldWorld = new FalloutReferenceWorld(records);
        coldWorld.Restore([saved]);
        if (JsonSerializer.Serialize(coldWorld.Get(key).HitReaction) != JsonSerializer.Serialize(saved.HitReaction) ||
            coldWorld.Get(key).HitReactionRandom.NextBounded(100) != world.Get(key).HitReactionRandom.NextBounded(100))
            throw new InvalidOperationException("Cold reaction changed its source, phase, pose or random stream.");
        // A fresh native skin and owner must resume the saved phase without
        // replaying selection or retaining the previous skin's pose/channels.
        var context = new NativeActorCombatContext(() => null, () => new(1, 200, 200, 70, 70, 0, 100), (_, _) => { },
            (_, to) => [to], _ => true, () => 1, globals, .4f, 9.81f);
        var cell = FalloutCellSceneReader.Read(records, saved.Cell);
        var placed = cell.References.Single(value => value.FormKey == key);
        var content = RuntimeLiveContentSource.Current!;
        CharacterBody3D coldActor;
        RuntimeNativeNifSkeleton coldSkeleton;
        string path;
        if (actor is RuntimeNativeNpc)
        {
            var coldNpc = RuntimeNativeNpc.Create(records, content, placed, skeleton.UnitsToMetres,
                (appearance, piece, nif, geometry) => NativeNpcMaterial.Resolve(appearance, piece, nif, geometry, records, new Color(.3f, .3f, .3f)),
                coldWorld.EquippedArmor(key, 1, globals), selection: coldWorld.Get(key).Templates);
            coldActor = coldNpc; coldSkeleton = coldNpc.Skeleton; path = coldNpc.Appearance.SkeletonPath;
        }
        else
        {
            var coldCreature = RuntimeNativeCreature.Create(records, content, placed, coldWorld.Get(key), skeleton.UnitsToMetres);
            coldActor = coldCreature; coldSkeleton = coldCreature.Skeleton; path = coldCreature.Appearance.SkeletonPath;
        }
        AddChild(coldActor); coldActor.SetProcess(false); coldActor.SetPhysicsProcess(false);
        var cold = RuntimeNativeActorCombat.Attach(coldActor, coldSkeleton, path, coldWorld, coldWorld.Get(key), records, content, 2, 3, context);
        cold.SetPhysicsProcess(false);
        try
        {
            var expected = current.Animation.ElapsedSeconds;
            cold._PhysicsProcess(0);
            if (coldWorld.Get(key).HitReaction?.Animation.ElapsedSeconds != expected)
                throw new InvalidOperationException("Native cold playback restarted or failed its saved phase.");
            for (var frame = 0; frame < 900 && cold.ReactingToHit; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
                cold._PhysicsProcess(1d / 60);
            }
            if (cold.ReactingToHit) throw new InvalidOperationException("Native hit reaction did not complete: " + JsonSerializer.Serialize(cold.HitReactionObservation));
        }
        finally { coldActor.Free(); }
        for (var frame = 0; frame < 900 && combat.ReactingToHit; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            combat._PhysicsProcess(1d / 60);
        }
        if (combat.ReactingToHit || world.Get(key).Engagement?.Action != "pursue")
            throw new InvalidOperationException("Reaction did not return control to combat.");
        combat.Hit(contact, new(.1f, 1, 100, 1), target, 1, globals, explosionDamage: true);
        var explosionReaction = JsonSerializer.SerializeToElement(combat.HitReactionObservation);
        if (combat.ReactingToHit || explosionReaction.GetProperty("error").ValueKind != JsonValueKind.Null ||
            explosionReaction.GetProperty("last").GetProperty("hitLocation").GetInt32() != -1)
            throw new InvalidOperationException("Explosion damage fabricated anatomy or failed its normal-state conditions.");
        GD.Print($"OPENNV_NATIVE_HIT_REACTION_PASS actor={key} part={part.Type} idle={initial.Idle} animation={initial.Animation.Resource} poseChanged=true noPlayer=true coldResume=true repeatDoesNotRestart=true explosionHasNoHitLocation=true ordinaryGameplay=separate");
    }
}
