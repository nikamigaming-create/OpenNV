using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class IngestibleContracts
{
    private static byte[] InstantScriptFixture()
    {
        var weapon = new byte[15]; UInt(weapon, 4, 200);
        var dnam = new byte[204]; UInt(dnam, 0, 3); BitConverter.GetBytes(1f).CopyTo(dnam, 4);
        byte[] ScriptItem(uint item, uint magic, uint script, bool failure = false, bool update = false)
        {
            var effect = new byte[72]; UInt(effect, 0, 0x2070); UInt(effect, 8, script);
            UInt(effect, 16, uint.MaxValue); UInt(effect, 64, 1); UInt(effect, 68, uint.MaxValue);
            var header = new byte[20]; UInt(header, 4, 1); UInt(header, 8, 1); UInt(header, 12, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), 0x100);
            var local = new byte[24]; UInt(local, 0, 1);
            var source = "float result\nbegin ScriptEffectStart\nset result to player.GetWeaponHealthPerc\n" +
                "if player.GetAV Repair < 25\nset result to result + 10\nelse\nset result to result + 15\nendif\n" +
                "player.SetWeaponHealthPerc result\n" + (failure ? "player.UnownedInstantCommand 1\n" : "") + "end\n" +
                (update ? "begin ScriptEffectUpdate\nplayer.SetWeaponHealthPerc 10\nend\n" : "");
            return Join(Item(item, Effect(magic, 0, 0)), Record("MGEF", magic, Field("DATA", effect)),
                Record("SCPT", script, Field("EDID", Text("InstantSource" + script)), Field("SCHR", header),
                    Field("SCDA", [0]), Field("SLSD", local), Field("SCVR", Text("result")),
                    Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text(source))));
        }
        return Join(Record("WEAP", 0x300, Field("EDID", Text("InstantWeapon")), Field("DATA", weapon),
                Field("ETYP", BitConverter.GetBytes(6)), Field("DNAM", dnam)),
            ScriptItem(0x301, 0x311, 0x321), ScriptItem(0x302, 0x312, 0x322, failure: true),
            ScriptItem(0x303, 0x313, 0x323, update: true));
    }

    private static void InstantScriptTransactions(FalloutPluginStack records, Func<FalloutPlayerVitals> vitals,
        FalloutBodyPartData bodyParts)
    {
        FalloutFormKey Key(uint id) => new("Test.esm", id);
        var inventory = new FalloutPlayerInventory(81);
        inventory.Add(records, Key(0x300), 1, 1, true, extra: new(1, Condition: .2f, Instance: Guid.NewGuid()));
        inventory.Equip(records, Key(0x300));
        foreach (var id in new[] { 0x301u, 0x302u, 0x303u }) inventory.Add(records, Key(id), 3, 1, true);
        var health = vitals();
        float repair = 15;
        FalloutPlayerIngestibles Owner(FalloutPlayerInventory items, FalloutPlayerVitals hp) =>
            new(records, items, hp, bodyParts, value => value == 39 ? repair : throw new NotSupportedException("fixture AV"),
                _ => false, () => false);
        var owner = Owner(inventory, health);
        string Inventory() => JsonSerializer.Serialize(inventory.Capture());
        string Effects() => JsonSerializer.Serialize(owner.Capture());
        var before = Inventory(); var effects = Effects(); var revision = inventory.Revision;
        Reject(() => owner.Prepare(Key(0x303)));
        Reject(() => owner.Prepare(Key(0x302)).Commit());
        Check(Inventory() == before && Effects() == effects && inventory.Revision == revision,
            "Unowned script command/timeline retained consumed item, staged condition, locals, receipt or RNG.");
        inventory.PrepareChange = (_, _) => new(() => throw new InvalidOperationException("fixture publication refusal"), () => { });
        Reject(() => owner.Prepare(Key(0x301)).Commit());
        Check(Inventory() == before && Effects() == effects && inventory.Revision == revision,
            "Refused instant script retained inventory/effect publication.");
        inventory.PrepareChange = null;
        var originalVitals = health.State;
        var independentVitals = originalVitals with { HitPoints = originalVitals.HitPoints - 1 };
        inventory.PrepareChange = (_, _) => { health.Publish(independentVitals); return null; };
        Reject(() => owner.Prepare(Key(0x301)).Commit());
        Check(Inventory() == before && Effects() == effects && inventory.Revision == revision &&
            ReferenceEquals(health.State, independentVitals),
            "Prepare callback invalidation was overwritten by stale Aid state or retained a scripted consumption.");
        health.Publish(originalVitals);
        inventory.PrepareChange = (_, _) => new(() => { health.Publish(independentVitals); repair = 30; },
            () => { health.Publish(originalVitals); repair = 15; });
        Reject(() => owner.Prepare(Key(0x301)).Commit());
        Check(Inventory() == before && Effects() == effects && inventory.Revision == revision &&
            ReferenceEquals(health.State, originalVitals) && repair == 15,
            "External lease mutation bypassed effect-boundary validation or lost its own rollback ownership.");
        inventory.PrepareChange = null;
        owner.Prepare(Key(0x301)).Commit();
        Check(inventory.Item(Key(0x301))!.Count == 2 && inventory.Revision == revision + 1 &&
            Math.Abs(inventory.EquippedWeaponHealthPercentage(records) - 30) < .0001f &&
            owner.Capture().ScriptReceipts is { Count: 1 },
            "Source Start script did not atomically consume one item, use actual AV branch or persist completed local state.");
        var saved = JsonSerializer.Deserialize<FalloutIngestiblesSnapshot>(Effects())!;
        var grant = inventory.Capture(); var coldItems = new FalloutPlayerInventory();
        coldItems.Restore(grant.Inventory, grant.EquippedRuntimeFormIds.ToArray(), grant.InventoryRandomState);
        var cold = Owner(coldItems, vitals()); cold.Restore(saved);
        Check(Math.Abs(coldItems.EquippedWeaponHealthPercentage(records) - 30) < .0001f &&
            JsonSerializer.Serialize(cold.Capture()) == Effects(), "Cold restore replayed completed Start or dropped its source/local receipt.");
        var receipt = saved.ScriptReceipts!.Single();
        Reject(() => Owner(coldItems, vitals()).Restore(saved with
            { ScriptReceipts = [receipt with { ScriptHash = new string('0', 64) }] }));
        owner.Prepare(Key(0x301)).Commit();
        Check(inventory.Item(Key(0x301))!.Count == 1 && owner.Capture().ScriptReceipts!.Count == 2 &&
            Math.Abs(inventory.EquippedWeaponHealthPercentage(records) - 40) < .0001f,
            "A second consumption reused first-instance locals or failed to invoke a new Start generation.");
        var staleInput = owner.Prepare(Key(0x301)); var inputBefore = Inventory(); var inputEffects = Effects();
        repair = 30; Reject(() => staleInput.Commit()); repair = 15;
        Check(Inventory() == inputBefore && Effects() == inputEffects, "Changed source AV input retained script effects.");
        var stale = owner.Prepare(Key(0x301)); inventory.Add(records, Key(1), 1, 1, true);
        var afterAdd = Inventory(); var afterEffects = Effects();
        Reject(() => stale.Commit());
        Check(Inventory() == afterAdd && Effects() == afterEffects, "Stale script consumption published effects.");
        inventory.SetEquippedWeaponHealthPercentage(records, 500);
        Check(inventory.EquippedWeaponHealthPercentage(records) == 99.5f,
            "Weapon percentage repair lost the native maximum-health-minus-one ceiling.");
        var beforeDamage = Inventory(); Reject(() => inventory.SetEquippedWeaponHealthPercentage(records, 5));
        Check(Inventory() == beforeDamage, "Unowned weapon-damage setter mutated current equipment.");
        Console.WriteLine("OPENNV_INGESTIBLE_SCRIPT_CONTRACT_PASS sourceStart=true staged=true atomic=true repeated=true coldNoReplay=true unknownRefused=true");
    }
}
