using Godot;
using OpenNV.Runtime.World.Actors;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class FurnitureContracts
{
    private static byte[] CreatureFoodDefaults(uint list)
    {
        var data = new byte[84];
        BitConverter.GetBytes(list).CopyTo(data, 18 * sizeof(uint));
        return data;
    }

    private static byte[] CreatureEatFixture(uint id, uint flags = 0x82006, sbyte hour = 22, int duration = 10, bool secondary = false, bool conflictingSecondary = false)
    {
        var data = new byte[12]; BitConverter.GetBytes(flags).CopyTo(data, 0); data[4] = 3;
        BitConverter.GetBytes((ushort)0x200).CopyTo(data, 8);
        return Record("PACK", id, Field("EDID", Text("CreatureEat" + id)), Field("PKDT", data),
            Field("PLDT", Join(BitConverter.GetBytes(2), new byte[4], BitConverter.GetBytes(256))),
            Field("PTDT", Join(BitConverter.GetBytes(2), BitConverter.GetBytes(18), new byte[8])),
            Field("PSDT", Join(new byte[] { 255, 255, 0, unchecked((byte)hour) }, BitConverter.GetBytes(duration))),
            secondary ? Field("PLD2", Join(BitConverter.GetBytes(0), BitConverter.GetBytes(0x901u), BitConverter.GetBytes(3000))) : [],
            secondary ? Field("PLD2", Join(BitConverter.GetBytes(0), BitConverter.GetBytes(0x901u), BitConverter.GetBytes(conflictingSecondary ? 3001 : 3000))) : []);
    }

    private static void CreatureEatContracts(FalloutPluginStack records, FalloutActorPackageMotion npcMotion)
    {
        var center = new Vector3(10, 20, 30);
        Require(NativePackageRouteReceipt.AlreadyWithinRegion(center, center, 3, true, true) &&
            NativePackageRouteReceipt.AlreadyWithinRegion(center + Vector3.Right, center, 3, true, true) &&
            !NativePackageRouteReceipt.AlreadyWithinRegion(center, center, 3, false, true) &&
            !NativePackageRouteReceipt.AlreadyWithinRegion(center + Vector3.Up * 4, center, 3, true, true) &&
            !NativePackageRouteReceipt.AlreadyWithinRegion(center + Vector3.Right * 4, center, 3, true, true),
            "Initial source-region occupancy required a fabricated route or ignored native floor/height/radius.");
        var record = records.GetEffective(new("Find.esp", 0x413));
        var source = FalloutEatPackage.Read(record, creature: true);
        Require(source.Creature && source.Running && source.Flags == 0x82006 && source.SpecificFlags == 0x200 &&
            source.Location is { Type: 2, Radius: 256 } && source.SearchLocation is { Type: 0, Radius: 3000, Form: not null }, "Creature Eat lost source permissions or travel scope.");
        Reject(() => FalloutEatPackage.Read(record));
        Reject(() => FalloutEatPackage.Read(records.GetEffective(new("Find.esp", 0x414)), creature: true));
        Reject(() => FalloutEatPackage.Read(records.GetEffective(new("Find.esp", 0x416)), creature: true));
        var calls = 0;
        var item = FalloutCreatureEatItem.Select(records, source, bound =>
        {
            Require(bound == 2, "Creature Eat changed the winning FLST choice range.");
            ++calls; return 0;
        });
        Require(calls == 1 && item.Source == new FalloutFormKey("Seats.esm", 0x740) &&
            item.Item == new FalloutFormKey("Seats.esm", 0x716) && item.Index == 0,
            "Creature Eat ignored winning list order or repeated its integer selection.");
        item.Validate(records, source);
        Reject(() => (item with { Index = 1 }).Validate(records, source));
        Reject(() => (item with { ItemSha256 = new string('0', 64) }).Validate(records, source));
        Require(!source.TimedMustCompleteExpired(record, 8.99f, 22) && source.TimedMustCompleteExpired(record, 9, 22),
            "Timed Eat used calendar modulo24 or waited for a consumed meal after the source deadline.");
        var relative = records.GetEffective(new("Find.esp", 0x415));
        var relativeSource = FalloutEatPackage.Read(relative, creature: true);
        Require(!relativeSource.TimedMustCompleteExpired(relative, 12.9f, 10.9f) &&
            relativeSource.TimedMustCompleteExpired(relative, 13, 10.9f), "Unspecified start did not retain/truncate actual assignment start.");
        var progress = new FalloutActorEatPackageProgress(new("Seats.esm", 0x800),
            Creature: new(22, Arrived: true, UsedItem: item, RetrySeconds: -.01f));
        var motion = npcMotion with { Package = record.FormKey, PackageSha256 = record.ReadDataHash(), Eat = null, EatPackage = progress };
        motion.Validate();
        Reject(() => (motion with { Eat = npcMotion.Eat }).Validate());
        Reject(() => (progress with { Creature = progress.Creature! with { Arrived = false } }).Validate());
        var cold = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(motion))!;
        cold.Validate(); cold.EatPackage!.Creature!.UsedItem!.Validate(records, source);
        Require(cold.EatPackage == progress && cold.Eat is null && calls == 1,
            "Cold Creature Eat lost its exact used item/clock or invented ingestion/reselection.");
        Console.WriteLine("OPENNV_CREATURE_EAT_CONTRACT_PASS source=true winningList=true retainedChoice=true timedMustComplete=true cold=true inventoryTransaction=false physicalPlayback=separate");
    }
}
