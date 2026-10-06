using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class CorpseEquippedLootContracts
{
    private static FalloutFormKey Key(uint id) => new("Corpse.esm", id);

    internal static void Run(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> original)
    {
        var source = original.Single(snapshot => snapshot.Reference == Key(0x900));
        var former = new FalloutCampaignItem(Key(0x53), records.RuntimeFormId(Key(0x53)), "FixtureGun", "WEAP", 1, 0, 1,
            Variants: [new(1, .73f)]);
        var unrelated = new FalloutCampaignItem(Key(0x54), records.RuntimeFormId(Key(0x54)), "FixtureMisc", "MISC", 3, 0, 0,
            Variants: [new(3, .64f, Key(0x10))]);
        var equipment = source.CorpseEquipment!;
        var saved = original.Select(snapshot => snapshot.Reference != source.Reference ? snapshot : snapshot with
        {
            Inventory = source.Inventory! with
            {
                Contents = source.Inventory!.Contents with
                { Inventory = new([.. source.Inventory.Contents.Inventory.Items, former, unrelated], null) }
            },
            Ragdoll = source.Ragdoll! with
            {
                Bodies = source.Ragdoll!.Bodies.Select(body => body with
                { Sleeping = true, LinearVelocity = [0, 0, 0], AngularVelocity = [0, 0, 0] }).ToArray()
            },
            CorpseEquipment = equipment with
            {
                Attachments = equipment.Attachments.Select(attachment => attachment.Owner == "package"
                ? attachment with { Source = new(former.FormKey, FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(former.FormKey))) }
                : attachment).ToArray()
            }
        }).ToArray();
        AmmoThenEquippedTransfer(records, saved);
        RemoveCommand(records, saved);
        RemoveAllCommand(records, saved);
        ExchangeContract(records, saved);
        RollbackContracts(records, saved);
        RefusalContracts(records, saved);
        Console.WriteLine("OPENNV_CORPSE_EQUIPPED_LOOT_CONTRACT_PASS takeAllBatch=true removeItem=true removeAll=true " +
            "onlyTransferredEquipment=true ammoConditionOwnershipConservation=true magazineReconciled=true sourceFaultAndRagdoll=true " +
            "currentCold=true overflowAndCommitRollback=true noEarlyRetirement=true movingPendingOpaqueAndPartialRefused=true synthetic=true nativeAndRetail=unverified");
    }

    private static FalloutReferenceWorld Restore(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> saved)
    {
        var world = new FalloutReferenceWorld(records);
        world.Restore(saved);
        return world;
    }

    private static void AmmoThenEquippedTransfer(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> saved)
    {
        using var world = Restore(records, saved);
        var state = world.Get(Key(0x900)); var contents = world.Inventory(state.Reference, 1).Contents;
        var before = state.Capture(); var history = History(before);
        var player = new FalloutPlayerInventory(19);
        contents.TransferTo(player, Key(0x51), 9);
        var afterAmmo = state.Capture();
        Require(afterAmmo.CorpseEquipment!.WeaponHandling!.Magazines.Single().Loaded == 8 &&
            afterAmmo.CorpseEquipment.WeaponHandling.ShotRandomState == before.CorpseEquipment!.WeaponHandling!.ShotRandomState &&
            afterAmmo.CorpseEquipment.WeaponHandling.AttackRandomState == before.CorpseEquipment.WeaponHandling.AttackRandomState &&
            JsonSerializer.Serialize(afterAmmo.CorpseEquipment.Attachments) == JsonSerializer.Serialize(before.CorpseEquipment.Attachments) &&
            contents.Item(Key(0x50))!.Variants!.Single().Condition == .42f && contents.Item(Key(0x53))!.Variants!.Single().Condition == .73f &&
            contents.Item(Key(0x51))!.Count + player.Item(Key(0x51))!.Count == 17 && History(afterAmmo) == history,
            "A real ammo transfer changed weapon presentation/condition/RNG or manufactured loaded rounds.");
        var sourceItem = contents.Item(Key(0x50))!;
        contents.TransferTo(player, sourceItem.FormKey, sourceItem.Count);
        var afterWeapon = state.Capture();
        Require(afterWeapon.CorpseEquipment is
        {
            Attachments.Count: 1, HandlingWeapon: null, WeaponHandling: null,
            Activity.WeaponDrawn: false
        } &&
            afterWeapon.CorpseEquipment.Attachments[0].Owner == "package" &&
            afterWeapon.CorpseEquipment.Attachments[0].Source.Weapon == Key(0x53) && afterWeapon.Engagement!.WeaponHandling is null &&
            player.Item(Key(0x50))!.Variants!.SequenceEqual(sourceItem.Variants!) &&
            contents.Item(Key(0x54))!.Count == 3 && History(afterWeapon) == history,
            "Taking an actual equipped item retired unrelated cached equipment or changed a source failure/item variant.");
        Cold(records, world.Capture());
        var rest = contents.Items;
        var totals = rest.ToDictionary(item => item.FormKey, item => (player.Item(item.FormKey)?.Count ?? 0) + item.Count);
        contents.TransferItemsTo(player, rest.Select(item => new FalloutInventoryTransfer(item.FormKey, item.Count)).ToArray());
        var empty = state.Capture();
        Require(contents.Items.Count == 0 && empty.CorpseEquipment is { Attachments.Count: 0, HandlingWeapon: null, WeaponHandling: null } &&
            totals.All(pair => player.Item(pair.Key)!.Count == pair.Value) &&
            player.Item(Key(0x54))!.Variants!.Single() == new FalloutItemVariant(3, .64f, Key(0x10)) &&
            History(empty) == history, "Take All changed independent corpse history, variants or item totals.");
        var settled = JsonSerializer.Serialize(world.Capture());
        var revision = contents.Revision;
        contents.TransferItemsTo(player, []);
        Require(contents.Revision == revision && JsonSerializer.Serialize(world.Capture()) == settled,
            "Repeated empty Take All retired an owner again or altered authoritative state.");
        Cold(records, world.Capture());
    }

    private static void RemoveCommand(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> saved)
    {
        using var world = Restore(records, saved);
        var state = world.Get(Key(0x900)); _ = world.Inventory(state.Reference, 1);
        var before = state.Capture();
        var commands = new FalloutInventoryCommands(records, world, new(), () => 1,
            prepareActorChange: _ => throw new InvalidDataException("Settled corpse RemoveItem reached the old blanket equipment refusal."));
        commands.Execute(new(FalloutInventoryCommandKind.Remove, state.Reference, Key(0x53)));
        var cachedRemoved = state.Capture();
        Require(cachedRemoved.CorpseEquipment is
        {
            Attachments.Count: 1, HandlingWeapon: not null, WeaponHandling: not null,
            Activity.WeaponDrawn: true
        } && cachedRemoved.CorpseEquipment.Attachments[0].Owner == "combat" &&
            FalloutActorCorpseEquipment.SameHandling(cachedRemoved.CorpseEquipment.WeaponHandling, before.CorpseEquipment!.WeaponHandling) &&
            History(cachedRemoved) == History(before), "RemoveItem retired a different still-owned current weapon or handling stream.");
        commands.Execute(new(FalloutInventoryCommandKind.Remove, state.Reference, Key(0x50)));
        var after = state.Capture();
        Require(after.CorpseEquipment is { Attachments.Count: 0, HandlingWeapon: null, WeaponHandling: null } &&
            after.Inventory!.Contents.Inventory.Items.All(item => item.FormKey != Key(0x50) && item.FormKey != Key(0x53)) &&
            after.Inventory.Contents.Inventory.Items.Where(item => item.FormKey != Key(0x50) && item.FormKey != Key(0x53))
                .SequenceEqual(before.Inventory!.Contents.Inventory.Items.Where(item => item.FormKey != Key(0x50) && item.FormKey != Key(0x53))) &&
            History(after) == History(before), "Source RemoveItem lost ammo/unrelated items or erased original corpse faults/physical history.");
        Cold(records, world.Capture());
    }

    private static void RemoveAllCommand(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> saved)
    {
        using var world = Restore(records, saved);
        var state = world.Get(Key(0x900)); _ = world.Inventory(state.Reference, 1);
        var before = state.Capture();
        var player = new FalloutPlayerInventory(19);
        var commands = new FalloutInventoryCommands(records, world, player, () => 1,
            prepareActorChange: _ => throw new InvalidDataException("Settled corpse RemoveAll reached the old blanket refusal."));
        commands.Execute(new(FalloutInventoryCommandKind.RemoveAll, state.Reference, Destination: records.RuntimeFormKey(0x14), RetainOwnership: true));
        Require(state.Inventory!.Contents.Items.Count == 0 &&
            state.Capture().CorpseEquipment is { Attachments.Count: 0, HandlingWeapon: null, WeaponHandling: null } &&
            before.Inventory!.Contents.Inventory.Items.All(item => player.Item(item.FormKey) is { } moved &&
                moved.Count == item.Count && (moved.Variants ?? [new(moved.Count)]).SequenceEqual(item.Variants ?? [new(item.Count)])) &&
            History(state.Capture()) == History(before), "Source RemoveAll did not atomically conserve conditions/ownership and original corpse history.");
        Cold(records, world.Capture());
    }

    private static void RollbackContracts(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> saved)
    {
        using var world = Restore(records, saved);
        var state = world.Get(Key(0x900)); var contents = world.Inventory(state.Reference, 1).Contents;
        var player = new FalloutPlayerInventory(19);
        var overflow = saved.Single(snapshot => snapshot.Reference == state.Reference).Inventory!.Contents.Inventory.Items
            .Single(item => item.FormKey == Key(0x51)) with
        { Count = int.MaxValue, Variants = [new(int.MaxValue)] };
        player.Restore(new([overflow], null), [], 19);
        var before = Stamp(world, contents, player);
        Reject(() => contents.TransferItemsTo(player, [new(Key(0x50), 1), new(Key(0x51), 17)]));
        Require(Stamp(world, contents, player) == before, "Late target overflow partially transferred or retired an equipped item.");
        player.Replace(new(new([], null), [], 19));
        before = Stamp(world, contents, player);
        var finalized = 0; var publications = 0;
        player.PrepareChange = (_, _) => new(() =>
        {
            Require(contents.Item(Key(0x50)) is null && player.Item(Key(0x50)) is not null &&
                state.CorpseEquipment!.Attachments.All(attachment => attachment.Source.Weapon != Key(0x50)),
                "Native retirement committed before both actual inventory owners published the accepted transfer.");
            publications++;
            throw new InvalidDataException("Actual later owner commit failed.");
        }, () => { }, () => finalized++);
        Reject(() => contents.TransferTo(player, Key(0x50), 1));
        Require(publications == 1 && finalized == 0 && Stamp(world, contents, player) == before,
            "Failed post-publication owner commit lost inventory revisions, equipment, history or rollback.");
        player.PrepareChange = (_, _) =>
        {
            contents.Remove(Key(0x54), 1, true);
            return null;
        };
        Reject(() => contents.TransferTo(player, Key(0x50), 1));
        Require(Stamp(world, contents, player) == before, "A nested authoritative removal interleaved corpse transfer preparation.");
        player.PrepareChange = null;
        var invalidTarget = new FalloutPlayerInventory(19);
        invalidTarget.Restore(new([contents.Item(Key(0x50))! with { EditorId = "NotTheSameSourceItem" }], null), [], 19);
        var invalidBefore = Stamp(world, contents, invalidTarget);
        Reject(() => contents.TransferTo(invalidTarget, Key(0x50), 1));
        Require(Stamp(world, contents, invalidTarget) == invalidBefore, "A transfer overwrote a conflicting target item identity.");
        contents.TransferTo(player, Key(0x50), 1);
        Require(player.Item(Key(0x50))!.Count == 1 && state.CorpseEquipment!.Attachments.Count == 1,
            "A genuine retry after rollback did not retire the accepted actual equipment.");
        Cold(records, world.Capture());
    }

    private static void ExchangeContract(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> saved)
    {
        using var world = Restore(records, saved);
        var state = world.Get(Key(0x900)); var source = world.Inventory(state.Reference, 1).Contents;
        var before = state.Capture();
        var receiver = new FalloutPlayerInventory(19);
        var ammo = source.Item(Key(0x51))! with { Count = 5, Variants = [new(5)] };
        receiver.Restore(new([ammo], null), [], 19);
        source.Exchange(receiver, [new(Key(0x50), 1, true), new(Key(0x51), 2, false)]);
        Require(source.Item(Key(0x50)) is null && receiver.Item(Key(0x50))!.Variants!.Single().Condition == .42f &&
            source.Item(Key(0x51))!.Count == 19 && receiver.Item(Key(0x51))!.Count == 3 &&
            state.CorpseEquipment is { Attachments.Count: 1, HandlingWeapon: null, WeaponHandling: null } &&
            History(state.Capture()) == History(before), "The shared exchange path bypassed retirement or changed trade item/ammo/condition totals.");
        Cold(records, world.Capture());
    }

    private static void RefusalContracts(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> saved)
    {
        foreach (var scope in new[] { "moving", "pending-hit", "resident-no-owner", "opaque-native", "source-drift" })
        {
            using var world = Restore(records, saved);
            var state = world.Get(Key(0x900)); var contents = world.Inventory(state.Reference, 1).Contents;
            var player = new FalloutPlayerInventory(19);
            if (scope == "moving") state.Ragdoll = state.Ragdoll! with
            { Bodies = state.Ragdoll!.Bodies.Select(body => body with { Sleeping = false }).ToArray() };
            if (scope == "pending-hit") world.HitEvents.Mark(state.Reference, Key(0x902), Key(0x50), FalloutReferenceHitKind.Melee);
            if (scope == "resident-no-owner") world.LoadCell(FalloutCellSceneReader.Read(records, state.Cell));
            if (scope == "opaque-native") state.CorpseEquipmentCaptureBlocker = "Original opaque equipment continuation.";
            if (scope == "source-drift") state.CorpseEquipment = state.CorpseEquipment! with
            {
                Attachments = state.CorpseEquipment!.Attachments.Select(attachment => attachment with
                { Source = attachment.Source with { Sha256 = new string('0', 64) } }).ToArray()
            };
            var inventoryBefore = JsonSerializer.Serialize(contents.Capture());
            var corpseBefore = JsonSerializer.Serialize(state.CorpseEquipment);
            var revision = contents.Revision;
            Reject(() => contents.TransferTo(player, Key(0x50), 1));
            Require(contents.Revision == revision && JsonSerializer.Serialize(contents.Capture()) == inventoryBefore &&
                JsonSerializer.Serialize(state.CorpseEquipment) == corpseBefore && player.Items.Count == 0,
                "Refused " + scope + " transfer changed actual inventory or retired equipment.");
        }
        using var partialWorld = Restore(records, saved);
        var partial = partialWorld.Inventory(Key(0x900), 1).Contents;
        var before = partial.Capture();
        partial.Replace(before with
        {
            Inventory = new(before.Inventory.Items.Select(item => item.FormKey == Key(0x50)
            ? item with { Count = 2, Variants = [new(1, .42f), new(1, .73f)] } : item).ToArray(), null)
        });
        var receiver = new FalloutPlayerInventory(19);
        var stamp = Stamp(partialWorld, partial, receiver);
        Reject(() => partial.TransferTo(receiver, Key(0x50), 1));
        Require(Stamp(partialWorld, partial, receiver) == stamp, "Partial equipped-stack transfer selected an invented item instance.");
    }

    private static string History(FalloutReferenceSnapshot snapshot) => JsonSerializer.Serialize(snapshot with
    { Inventory = null, CorpseEquipment = null, Engagement = snapshot.Engagement is { } engagement ? engagement with { WeaponHandling = null } : null });

    private static string Stamp(FalloutReferenceWorld world, FalloutPlayerInventory source, FalloutPlayerInventory target) =>
        JsonSerializer.Serialize(new
        {
            World = world.Capture(),
            SourceRevision = source.Revision,
            TargetRevision = target.Revision,
            Target = target.Capture(),
            SourceHud = source.Notifications.Capture(),
            TargetHud = target.Notifications.Capture()
        });

    private static void Cold(FalloutPluginStack records, IReadOnlyList<FalloutReferenceSnapshot> snapshots)
    {
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!;
        using var cold = Restore(records, saved);
        Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshots),
            "Cold equipped-loot restoration recreated removed equipment, loaded rounds or a consumed source prefix.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or OverflowException or ArgumentOutOfRangeException) { return; }
        throw new InvalidDataException("Unowned or failed equipped-corpse transfer was admitted.");
    }
}
