using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class ScriptDeathContracts
{
    private static void RunFatigueKnockoutPersistence(FalloutPluginStack records)
    {
        Require(FalloutActorKnockdownSource.Allows(records, Key(2)) &&
            !FalloutActorKnockdownSource.Allows(records, Key(9)) && !FalloutActorKnockdownSource.Allows(records, Key(10)),
            "Source knockout admission lost NoKnockdowns or immobile-creature flags.");
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x80)));
        var actor = world.Get(Key(0x91));
        _ = world.Health(actor.Reference);
        actor.ActorValues["fatigue"] = new(20, Damage: -25);
        actor.KnockedDown = true;
        actor.Ragdoll = new(new string('A', 64),
            [new(0, [1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 3, 4], [0, 0, 0], [0, 0, 0], true)]);
        foreach (var phase in new[] { FalloutFatigueKnockoutPhase.Falling, FalloutFatigueKnockoutPhase.Unconscious })
        {
            actor.FatigueKnockout = new(phase);
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(Snapshot(world)))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(snapshots);
            var retained = cold.Get(actor.Reference);
            Require(retained.FatigueKnockout == actor.FatigueKnockout && retained.KnockedDown &&
                retained.ActorValues["fatigue"].Current == -5 && retained.Ragdoll!.Bodies[0].Sleeping &&
                retained.Injury is { Dead: false, DeathEventPending: false, DeathInventoryGranted: false } && retained.DeathCount == 0 &&
                cold.GetKnockedState(actor.Reference) == (phase == FalloutFatigueKnockoutPhase.Unconscious ? 1 : 0),
                "Cold fatigue knockout lost its physical phase, pool, unconscious query or nonlethal state.");
            var snapshot = snapshots.Single(value => value.Reference == actor.Reference);
            Reject(() => FalloutReferenceSnapshot.Validate([snapshot with { KnockedDown = false }]));
            Reject(() => FalloutReferenceSnapshot.Validate([snapshot with { Ragdoll = null }]));
            Reject(() => FalloutReferenceSnapshot.Validate([snapshot with { FatigueKnockout = new(FalloutFatigueKnockoutPhase.GettingUp) }]));
            using var invalid = new FalloutReferenceWorld(records);
            Reject(() => invalid.Restore(snapshots.Select(value => value.Reference == actor.Reference ?
                value with { ActorValues = value.ActorValues!.Where(pair => pair.Key != "fatigue").ToDictionary(pair => pair.Key, pair => pair.Value) } : value).ToArray()));
        }
        Console.WriteLine("OPENNV_FATIGUE_KNOCKOUT_STATE_PASS coldPhysicalPhases=true poolRetained=true noDeathEvents=true invalidOwnersRejected=true nativeRagdollGetUp=unverified");
    }
}
