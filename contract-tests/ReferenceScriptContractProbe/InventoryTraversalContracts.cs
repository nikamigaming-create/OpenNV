using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class IngestibleContracts
{
    private static byte[] NativeInventoryOrderFixture(byte[] actor)
    {
        byte[] Entry(uint id) => Field("LVLO", Join(new byte[] { 1, 0, 0, 0 }, BitConverter.GetBytes(id), new byte[] { 1, 0, 0, 0 }));
        byte[] Count(uint id, int count) => Field("CNTO", Join(BitConverter.GetBytes(id), BitConverter.GetBytes(count)));
        return Join(
            Record("LVLI", 0x160, Field("LVLD", [0]), Field("LVLF", [4]), Entry(6), Entry(8)),
            Record("LVLI", 0x161, Field("LVLD", [0]), Field("LVLF", [4]), Entry(0x160), Entry(0x18)),
            Record("LVLI", 0x162, Field("LVLD", [0]), Field("LVLF", [2]), Entry(6), Entry(8)),
            Record("LVLI", 0x163, Field("LVLD", [0]), Field("LVLF", [4]),
                Entry(6), Field("COED", Join(new byte[8], BitConverter.GetBytes(.25f))), Entry(8),
                Entry(6), Field("COED", Join(new byte[8], BitConverter.GetBytes(.75f)))),
            Record("LVLI", 0x164, Field("LVLD", [0]), Field("LVLF", [4]),
                Entry(6), Field("COED", Join(new byte[4], BitConverter.GetBytes(1u), BitConverter.GetBytes(.5f)))),
            Record("LVLI", 0x165, Field("LVLD", [0]), Field("LVLF", [4]), Entry(6),
                Entry(6), Field("COED", Join(new byte[8], BitConverter.GetBytes(1f)))),
            Record("NPC_", 0x23, Field("ACBS", actor), Field("DATA", [100, 0, 0, 0, 5, 5, 5, 5, 5, 5, 5]),
                Field("NAM4", new byte[4]), Field("DNAM", Enumerable.Repeat((byte)50, 28).ToArray()),
                Count(0x160, 1), Count(0x18, 2), Count(0x161, 1)),
            Record("CONT", 0x24, Count(6, 1), Count(8, 1), Count(6, 1)));
    }

    private static void InventoryTraversalContracts(FalloutPluginStack records)
    {
        FalloutFormKey Key(uint id) => new("Test.esm", id);
        using var world = new FalloutReferenceWorld(records);
        var actor = Key(0x97); var inventory = world.Inventory(actor, 1).Contents;
        var order = inventory.CaptureTraversal()!;
        Check(order.SourceOwner == Key(0x23) && order.Forms.SequenceEqual(new[] { Key(0x18), Key(8), Key(6) }) &&
            inventory.Items.Select(item => item.FormKey).SequenceEqual(new[] { Key(6), Key(8), Key(0x18) }),
            "Native initial food traversal borrowed sorted UI order or lost nested temporary-container ordering.");
        Check(inventory.Item(Key(6))?.Count == 2 && inventory.Item(Key(0x18))?.Count == 3 && inventory.Item(Key(8))?.Count == 2,
            "Retaining native traversal changed leveled materialization counts.");
        var duplicated = world.Inventory(Key(0x98), 1).Contents;
        Check(duplicated.Item(Key(6))?.Count == 2 && duplicated.TraversalItems is null,
            "Duplicated direct food counts were discarded or admitted with an unowned flattened count policy.");
        var draws = 0;
        var plain = FalloutLeveledItems.Resolve(records, Key(0x161), 1, 1, _ => { ++draws; return 0; });
        var previousDraws = draws;
        var native = FalloutLeveledItems.Resolve(records, Key(0x161), 1, 1, _ => { ++draws; return 0; }, nativeContainerOrder: true);
        Check(draws == previousDraws * 2 && native.Select(item => item.Form).SequenceEqual(new[] { Key(8), Key(6), Key(0x18) }) &&
            plain.GroupBy(item => item.Form).All(group => group.Sum(item => item.Variant.Count) ==
                native.Where(item => item.Form == group.Key).Sum(item => item.Variant.Count)),
            "Native traversal reordered random draws, lost nested order or changed counts.");
        draws = 0;
        var rolled = FalloutLeveledItems.Resolve(records, Key(0x162), 3, 1, _ => { ++draws; return 0; });
        previousDraws = draws;
        var nativeRolled = FalloutLeveledItems.Resolve(records, Key(0x162), 3, 1, _ => { ++draws; return 0; }, nativeContainerOrder: true);
        Check(previousDraws == 3 && draws == 6 && rolled.Sum(item => item.Variant.Count) == nativeRolled.Sum(item => item.Variant.Count),
            "Retaining materialized order consumed another random roll or changed repeated-list counts.");
        var extras = FalloutLeveledItems.Resolve(records, Key(0x163), 1, 1, _ => 0, nativeContainerOrder: true);
        Check(extras.Select(item => item.Form).SequenceEqual(new[] { Key(6), Key(8), Key(6) }) &&
            extras.Select(item => item.Variant.Condition).SequenceEqual(new float?[] { .25f, null, .75f }),
            "Temporary native container order merged distinct COED conditions or confused absent and present extras.");
        Reject(() => FalloutLeveledItems.Resolve(records, Key(0x164), 1, 1, _ => 0, nativeContainerOrder: true));
        var presence = FalloutLeveledItems.Resolve(records, Key(0x165), 1, 1, _ => 0, nativeContainerOrder: true);
        Check(presence.Count == 2 && presence.Select(item => item.SourceExtraPresent).SequenceEqual(new[] { false, true }),
            "Native temporary expansion erased absent versus explicitly default-valued COED presence.");
        for (var index = 0; index < 3; index++)
        {
            using var consumed = new FalloutInventoryChangeLease(() => { }, () => { });
            inventory.ConsumeOne(Key(0x18), consumed);
        }
        Check(inventory.TraversalItems!.First().FormKey == Key(8), "Repeated committed consumption lost the next actual materialized food.");
        var before = JsonSerializer.Serialize(world.Get(actor).Inventory!.Capture());
        using (var refused = new FalloutInventoryChangeLease(() => throw new InvalidOperationException("fixture effect refusal"), () => { }))
            Reject(() => inventory.ConsumeOne(Key(8), refused));
        Check(before == JsonSerializer.Serialize(world.Get(actor).Inventory!.Capture()), "Failed ingestion lost inventory traversal during rollback.");
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(world.Capture());
            Check(cold.Inventory(actor, 1).Contents.TraversalItems!.Select(item => item.FormKey).SequenceEqual(new[] { Key(8), Key(6) }),
                "Cold inventory replaced retained traversal with a new roll or sorted projection.");
        }
        Reject(() => inventory.RestoreTraversal(records, order with { SourceSha256 = new string('0', 64) }));
        Reject(() => inventory.RestoreTraversal(records, order with { Forms = [Key(8), Key(8), Key(6)] }));
        var detached = order.Copy(); ((FalloutFormKey[])detached.Forms)[0] = Key(1);
        Check(inventory.CaptureTraversal()!.Forms[0] == Key(0x18), "Traversal snapshots share mutable order arrays.");
        var recipient = world.Inventory(Key(0x91), 1).Contents;
        inventory.TransferTo(recipient, Key(8), 1);
        Check(inventory.TraversalItems!.First().FormKey == Key(8) && recipient.TraversalItems is null,
            "An order-neutral source removal lost traversal or an unqualified recipient insertion fabricated it.");
        var legacy = world.Capture().Select(snapshot => snapshot.Reference == actor
            ? snapshot with { Inventory = snapshot.Inventory! with { Traversal = null } } : snapshot).ToArray();
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(legacy);
            Check(cold.Inventory(actor, 1).Contents.TraversalItems is null && cold.Inventory(actor, 1).Contents.Items.Count == 2,
                "Missing inventory provenance was fabricated or rerolled during cold restoration.");
        }
        inventory.Add(records, Key(6), 1, 1, true);
        Check(inventory.TraversalItems is not null, "An in-place count update lost valid traversal.");
        inventory.Add(records, Key(1), 1, 1, true);
        Check(inventory.TraversalItems is null, "An unqualified new insertion retained a fabricated native position.");
        Console.WriteLine("OPENNV_INVENTORY_TRAVERSAL_PASS nativeCnto=true nestedLvli=true countsAndDrawsPreserved=true repeatedConsumption=true rollback=true cold=true legacyExplicit=true newInsertionInvalidates=true");
    }
}
