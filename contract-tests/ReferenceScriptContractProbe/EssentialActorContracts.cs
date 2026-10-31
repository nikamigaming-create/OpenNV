using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class ScriptDeathContracts
{
    private static FalloutPluginStack LoadEssentialFixture(string directory, bool protection) => FalloutPluginStack.Load(
        [new("Death.esm", Path.Combine(directory, "Death.esm"))], false, out _,
        FalloutInstallationSettings.ReadIniLayers(() => [new("bEssentialTakeNoDamage:GamePlay", FalloutIniCollection.Main, protection ? 1u : 0u)],
            [], "synthetic-essential-settings"));

    private static void RunEssentialOwners(FalloutPluginStack records, string directory)
    {
        var cell = FalloutCellSceneReader.Read(records, Key(0x80));
        var sourceHash = SHA256.HashData(records.GetEffective(Key(4)).ReadData());
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(cell);
        var hardcore = false;
        world.BindHardcore(() => hardcore);
        var actor = world.Get(Key(0x93));
        Require(world.IsEssential(actor.Reference) && !world.IsActorRefEssential(actor.Reference), "Base essentiality became a reference flag.");
        world.ActorBaseFlags.SetEssential(actor.Base, false);
        Require(!world.IsEssential(actor.Reference), "Explicit false base override did not override its source flag.");
        world.SetActorRefEssential(actor.Reference, true);
        Require(world.IsEssential(actor.Reference), "Reference essentiality was lost to a false base override.");
        world.SetActorRefEssential(actor.Reference, false);
        actor.PlayerTeammate = true;
        Require(world.IsEssential(actor.Reference), "A non-hardcore teammate lost essential protection.");
        hardcore = true;
        Require(!world.IsEssential(actor.Reference), "Hardcore was ignored by teammate essentiality.");
        actor.PlayerTeammate = false;
        world.ActorBaseFlags.SetEssential(actor.Base, true);
        var flags = JsonSerializer.Deserialize<FalloutActorBaseFlagsSnapshot>(JsonSerializer.Serialize(world.ActorBaseFlags.Capture()))!;
        var restoredFlags = new FalloutActorBaseFlagState(records);
        restoredFlags.Restore(flags);
        Require(restoredFlags.Essential(actor.Base, false) && sourceHash.SequenceEqual(SHA256.HashData(records.GetEffective(actor.Base).ReadData())),
            "Essential writes changed source bytes or lost cold state.");
        Reject(() => restoredFlags.Restore(new([flags.Changes[0] with { Sha256 = new string('0', 64) }])));
        Require(restoredFlags.Essential(actor.Base, false), "A failed source validation partially changed essential flags.");

        var hit = world.DamageActor(actor.Reference, Key(0x91), 0, 5, 0, 1);
        Require(hit.HealthAfter == 45 && actor.EssentialKnockout is null, "Essential actors incorrectly ignored nonlethal damage.");
        hit = world.DamageActor(actor.Reference, Key(0x91), 0, 100, 0, 1);
        Require(!hit.Dead && world.Health(actor.Reference).Current == 50 && actor.EssentialKnockout is { RemainingSeconds: 2, RecoveryRequested: false } &&
            actor.Injury is { Dead: false, DeathEventPending: false, DeathInventoryGranted: false } && actor.Inventory is null && actor.DeathCount == 0,
            "Essential lethal damage created death, loot, history or failed to retain a living knockout.");
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(Snapshot(world)); cold.LoadCell(cell);
        var retained = cold.Get(actor.Reference);
        Require(retained.EssentialKnockout == actor.EssentialKnockout && cold.Health(actor.Reference).Current == 50,
            "Cold restoration lost a knockout waiting for native physical admission.");
        cold.AdvanceEssentialKnockout(actor.Reference, 20);
        Require(retained.EssentialKnockout!.RemainingSeconds == 2, "The clock advanced before physical admission.");
        // The native presenter owns this admission. This headless contract
        // exercises the shared clock after admission, not ragdoll/get-up proof.
        retained.KnockedDown = true;
        cold.AdvanceEssentialKnockout(actor.Reference, .5f);
        retained.Unconscious = true;
        cold.AdvanceEssentialKnockout(actor.Reference, 20);
        retained.Unconscious = false; retained.Restrained = true;
        cold.AdvanceEssentialKnockout(actor.Reference, 20);
        Require(retained.EssentialKnockout.RemainingSeconds == 1.5f, "Script unconsciousness/restraint did not hold the essential clock.");
        retained.Restrained = false;
        retained.ActorValues["health"] = cold.Health(actor.Reference) with { Damage = -10 };
        retained.ActorValues["fatigue"] = new(20, Damage: -5);
        cold.AdvanceEssentialKnockout(actor.Reference, 1.5f);
        Require(retained.EssentialKnockout is { RemainingSeconds: 0, RecoveryRequested: true } &&
            cold.Health(actor.Reference).Current == 50 && retained.ActorValues["fatigue"].Damage == 0,
            "Essential expiry failed to request recovery and restore retained damage pools.");
        retained.ActorValues["health"] = cold.Health(actor.Reference) with { Damage = -7 };
        cold.AdvanceEssentialKnockout(actor.Reference, 10);
        Require(cold.Health(actor.Reference).Current == 43, "A consumed essential recovery healed twice.");

        using var unprotectedRecords = LoadEssentialFixture(directory, false);
        using var unprotected = new FalloutReferenceWorld(unprotectedRecords);
        unprotected.LoadCell(FalloutCellSceneReader.Read(unprotectedRecords, Key(0x80)));
        Require(unprotected.KillActor(actor.Reference, null, 1) && unprotected.IsDead(actor.Reference) &&
            unprotected.Get(actor.Reference).EssentialKnockout is null && unprotected.GetDeadCount(actor.Base) == 1,
            "Disabled essential protection prevented the ordinary death transition.");
        Console.WriteLine("OPENNV_ESSENTIAL_STATE_PASS baseAndReferenceFlags=true hardcore=true immutableSource=true coldFlags=true invalidRestoreAtomic=true " +
            "nonlethalDamage=true lethalKnockout=true noDeathEffects=true coldPendingAdmission=true clockHolds=true expiryOnce=true protectionDisabled=true nativeGetUp=unverified");
    }
}
