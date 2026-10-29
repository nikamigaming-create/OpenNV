using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class SharedConditionContracts
{
    private static void MutableState(FalloutPluginStack records, FalloutReferenceWorld world, FalloutQuestState quests)
    {
        var lists = world.FormLists;
        var playerReference = records.RuntimeFormKey(FalloutEngineForms.PlayerReference);
        Require(!records.TryGetEffective(playerReference, out _), "Fixture player must be an engine identity, not a placed record.");
        lists.Add(Key(0x79), playerReference);
        Require(lists.Entries(Key(0x79)).Count == 1 && lists.Entries(Key(0x79))[0] == playerReference,
            "Engine PlayerRef was mistaken for an unowned dynamic reference or retained an empty-head sentinel.");
        Require(FalloutFormList.Read(records, Key(0x79)).Members.Count == 0, "Source-empty list was represented by a serialized null member.");
        lists.Add(Key(0x79), Key(0x20));
        Require(lists.Entries(Key(0x79)).Count == 2 && lists.Entries(Key(0x79))[0] == Key(0x20),
            "Adding to an empty list retained an extra sentinel entry.");
        var inventory = new FalloutPlayerInventory();
        Require(FalloutInventoryItemCounts.Count(records, inventory, Key(0x79), lists) == 0 &&
            FalloutInventoryItemCounts.Count(records, inventory, playerReference, lists) == 0 &&
            !FalloutEquippedItems.Any(records, Key(0x79), _ => throw new InvalidDataException("Non-item reached equipment owner."), lists) &&
            !FalloutEquippedItems.Any(records, playerReference, _ => true, lists) &&
            FalloutReferenceIdentity.IsInList(records, Key(0x200), Key(0x79), lists) &&
            !FalloutReferenceIdentity.IsInList(records, playerReference, Key(0x79), lists),
            "Intrinsic PlayerRef list membership was treated as an inventory item, source lookup or base identity.");
        Reject(() => lists.Add(Key(0x78), Key(0x20)));
        lists.Add(Key(0x77), Key(0x20));
        lists.Add(Key(0x77), Key(0x21));
        var revision = lists.Revision;
        lists.Add(Key(0x77), Key(0x20));
        lists.Add(Key(0x77), Key(0x71));
        Require(lists.Revision == revision && lists.Entries(Key(0x77)).SequenceEqual(new FalloutFormKey?[] { Key(0x21), Key(0x20), Key(0x71) }),
            "Native duplicate addition reordered the current list or changed its persistent prefix.");
        Require(FalloutFormList.Read(records, Key(0x77)).Members.SequenceEqual(new FalloutFormKey?[] { Key(0x71) }),
            "A runtime command modified the immutable winning list.");
        Require(FalloutReferenceIdentity.IsInList(records, Key(0x200), Key(0x77), lists) &&
            FalloutEquippedItems.Any(records, Key(0x77), form => form == Key(0x71), lists),
            "Membership/equipment consumers retained a source-only view.");
        var ammo = new FalloutTypedFormList(records, Key(0x76), "AMMO", lists);
        Require(ammo.Count == 1 && ammo[0] == Key(0x74), "Typed list did not resolve its source baseline.");
        lists.Add(Key(0x76), Key(0x75));
        Require(ammo.SequenceEqual(new[] { Key(0x75), Key(0x74) }), "Retained resource view missed a later runtime list mutation.");
        var captured = JsonSerializer.Deserialize<FalloutFormListsSnapshot>(JsonSerializer.Serialize(lists.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.FormLists.Restore(captured);
        Require(cold.FormLists.Entries(Key(0x77)).SequenceEqual(lists.Entries(Key(0x77))) &&
            cold.FormLists.Entries(Key(0x76)).SequenceEqual(lists.Entries(Key(0x76))) &&
            cold.FormLists.Entries(Key(0x79)).Contains(playerReference), "Cold form-list order or engine player identity changed.");
        Reject(() => cold.FormLists.Restore(captured with { Lists = captured.Lists.Select((entry, index) =>
            index == 0 ? entry with { Sha256 = new string('0', 64) } : entry).ToArray() }));
        Require(cold.FormLists.Entries(Key(0x77)).SequenceEqual(lists.Entries(Key(0x77))),
            "Rejected form-list restore partially replaced current state.");
        Reject(() => lists.Add(Key(0x77), Key(0xffffff)));
        Reject(() => lists.Add(Key(0x71), Key(0x20)));

        var source = FalloutActorAiData.Read(records, Key(0x20));
        Require(ReferenceEquals(source, FalloutActorAiData.Read(records, Key(0x23))) &&
            ReferenceEquals(source, FalloutActorAiData.Read(records, Key(0x20))),
            "Immutable AI metadata cache changed identity or ignored the resolved template owner.");
        foreach (var id in new[] { FalloutActorValueId.Aggression, FalloutActorValueId.Confidence,
            FalloutActorValueId.Energy, FalloutActorValueId.Responsibility, FalloutActorValueId.Mood, FalloutActorValueId.Assistance })
            Require(world.ActorValue(Key(0x201), FalloutActorValueNames.Name(id)) == source.Value(id),
                "Creature AI-data value did not use the shared winning source owner.");
        world.ChangeActorValue(Key(0x200), "Confidence", "setav", 0);
        world.ChangeActorValue(Key(0x200), "Confidence", "forceav", 0);
        Require(!world.ActorThreat(Key(0x200)).Initiates(1), "Confidence mutation did not affect the production AI decision owner.");
        world.ChangeActorValue(Key(0x200), "Confidence", "forceav", 2);
        Require(world.ActorThreat(Key(0x200)).Initiates(1) && source.Confidence == 1,
            "Live AI decision failed to observe a modifier or modified the cached source declaration.");
        world.RestoreActorValue(Key(0x200), "Mood", 100);
        world.ChangeActorValue(Key(0x200), "Mood", "forceav", 6);
        world.ChangeActorValue(Key(0x200), "Mood", "modav", 2);
        world.ChangeActorValue(Key(0x200), "Mood", "damageav", 1);
        Require(world.ActorValue(Key(0x200), "Mood", FalloutActorValueRead.Base) == 6 &&
            world.ActorValue(Key(0x200), "Mood", FalloutActorValueRead.Permanent) == 8 && world.ActorValue(Key(0x200), "Mood") == 7,
            "Reference base/permanent/damage pools disagreed.");
        world.ChangeActorValue(Key(0x200), "Mood", "forceav", 12);
        world.RestoreActorValue(Key(0x200), "Mood", 100);
        Require(world.ActorValue(Key(0x200), "Mood") == 13, "Force/restore lost the existing damage pool ordering.");
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
        Require(cold.ActorValue(Key(0x200), "Mood") == 13 && cold.ActorThreat(Key(0x200)).Confidence == 2,
            "Cold reference restore lost behavioral value pools.");

        var player = new FalloutPlayerActorValues(records);
        player.BindConstantModifiers((_, _) => []); // Fixture player has no SPLO declarations.
        player.Change("Confidence", "setav", 3);
        player.Change("Confidence", "modav", 1);
        player.Change("Confidence", "damageav", 2);
        var confidence = (int)FalloutActorValueId.Confidence;
        Require(player.ReadBase(confidence) == 3 && player.ReadPermanent(confidence) == 4 && player.ReadCurrent(confidence) == 2,
            "Player behavior pools did not share the production update rules.");
        player.Change("Confidence", "forceav", 5);
        player.Change("Confidence", "restoreav", 1);
        var playerSaved = JsonSerializer.Deserialize<FalloutPlayerActorValuesSnapshot>(JsonSerializer.Serialize(player.Capture()))!;
        var restored = new FalloutPlayerActorValues(records, playerSaved);
        restored.BindConstantModifiers((_, _) => []);
        Require(restored.ReadCurrent(confidence) == 6 && restored.ReadBase(confidence) == 3,
            "Cold player restore changed the force/damage/restore result.");
        Reject(() => new FalloutPlayerActorValues(records, playerSaved with { AiSha256 = new string('0', 64) }));
        Reject(() => player.Change("Mood", "damageav", -1));
        Reject(() => world.ChangeActorValue(Key(0x200), "Mood", "setav", float.NaN));
        Console.WriteLine("OPENNV_MUTABLE_SCRIPT_STATE_PASS nativeListOrder=true duplicateNoOp=true sourceImmutable=true " +
            "retainedConsumerView=true coldLists=true aiSourceCache=true aiDecisionEffects=true referencePools=true playerPools=true coldPools=true");
    }
}
