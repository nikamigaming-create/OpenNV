using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class CaravanSourceContracts
{
    internal static void Run()
    {
        ManualRules();
        BoardRules();
        MatchRules();
        CollectionSchema();
        var noticeCode = new byte[64];
        noticeCode[0] = 0xb9; U32(1).CopyTo(noticeCode, 1); noticeCode[5] = 0xe8;
        noticeCode[10] = 0xd9; noticeCode[11] = 0x05; U32(2).CopyTo(noticeCode, 12);
        noticeCode[16] = 0x68; U32(3).CopyTo(noticeCode, 17);
        noticeCode[21] = 0x68; U32(4).CopyTo(noticeCode, 22);
        string? Literal(uint pointer) => pointer switch { 3 => "icon.dds", 4 => "UIPopUpFixture", _ => null };
        var notice = FalloutExecutableStringTable.ReadCaravanMinimumNotice(noticeCode, Literal,
            new Dictionary<uint, string> { [1] = "sCardCountText" }, address => address == 2 ? 2 : float.NaN);
        if (notice.Setting != "sCardCountText" || notice.Icon != "icon.dds" || notice.Seconds != 2 || notice.SoundEditorId != "UIPopUpFixture")
            throw new InvalidDataException("Caravan notice did not retain the bound source declaration.");
        Reject(() => FalloutExecutableStringTable.ReadCaravanMinimumNotice(noticeCode, Literal,
            new Dictionary<uint, string> { [1] = "sCardCountText" }, _ => float.NaN));
        var menuCode = new byte[512];
        byte[] openingCall = [0x6a, 0, 0x51, 0xd9, 0x45, 0xfc, 0xd9, 0x1c, 0x24,
            0x8b, 0x4d, 0xf8, 0x51, 0x8b, 0x55, 0xec, 0x52, 0x8b, 0x45, 0xf4, 0x50, 0xe8];
        openingCall.CopyTo(menuCode, 0); BitConverter.GetBytes(300 - 26).CopyTo(menuCode, 22);
        menuCode[300] = 0x68; U32(99).CopyTo(menuCode, 301);
        string? MenuLiteral(uint value) => value == 99 ? "Data\\Menus\\fixture.xml" : null;
        if (FalloutExecutableStringTable.ReadCaravanMenuPath(menuCode, 0, MenuLiteral) != "Menus/fixture.xml")
            throw new InvalidDataException("Caravan menu path did not follow the source command opening call.");
        menuCode[21] = 0;
        Reject(() => FalloutExecutableStringTable.ReadCaravanMenuPath(menuCode, 0, MenuLiteral));
        var tokens = FalloutMenuTokenStream.Read(Encoding.UTF8.GetBytes("<rect name='a'><!--x--><include src=\"one.xml\"/></hotrect>"));
        if (tokens.Count != 3 || tokens[0].Kind != FalloutMenuTokenKind.Open || tokens[0].Value != "rect" ||
            tokens[1].Value != "include" || !tokens[1].SelfClosing || tokens[1].Attributes?["src"] != "one.xml" ||
            tokens[2].Kind != FalloutMenuTokenKind.Close || tokens[2].Value != "hotrect" ||
            tokens[0].Offset >= tokens[1].Offset || tokens[1].Offset >= tokens[2].Offset)
            throw new InvalidDataException("Menu token stream lost source ordering or independent close names.");
        Reject(() => FalloutMenuTokenStream.Read(Encoding.UTF8.GetBytes("<rect name=bad>")));
        Reject(() => FalloutMenuTokenStream.Read(Encoding.UTF8.GetBytes("<rect><!-- unfinished")));
        var menuSources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["menus/root.xml"] = Encoding.UTF8.GetBytes("<rect><include src='a.xml'/><include src='a.xml'/></rect>"),
            ["menus/prefabs/a.xml"] = Encoding.UTF8.GetBytes("<hotrect></rect>"),
            ["menus/prefabs/cycle.xml"] = Encoding.UTF8.GetBytes("<include src='cycle.xml'/>"),
        };
        var sequence = FalloutMenuSourceProgram.Read("menus/root.xml", path => menuSources[path]);
        if (sequence.Count(value => value.Kind == FalloutMenuSourceEventKind.EnterSource && value.Source.EndsWith("a.xml")) != 2 ||
            sequence.Count(value => value.Kind == FalloutMenuSourceEventKind.LeaveSource && value.Source.EndsWith("a.xml")) != 2 ||
            sequence.Count(value => value.Token is { Kind: FalloutMenuTokenKind.Close, Value: "rect" } && value.Source.EndsWith("a.xml")) != 2)
            throw new InvalidDataException("Menu include sequence lost repeated source events or mismatched close tokens.");
        Reject(() => FalloutMenuSourceProgram.Read("menus/prefabs/cycle.xml", path => menuSources[path]));
        var directory = Directory.CreateTempSubdirectory("opennv-caravan-source-").FullName;
        try
        {
            byte[] Card(uint id, uint script, bool badExtent = false) => Record("CCRD", id,
                Field("EDID", Text("Card" + id)), Field("OBND", new byte[12]), Field("FULL", Text("Card")),
                Field("MODL", Text("card.nif")), Field("ICON", Text("card.dds")), Field("SCRI", U32(script)),
                Field("TX00", Text("face.dds")), Field("TX01", Text("back.dds")),
                Field("INTV", U32(2)), Field("INTV", badExtent ? [1] : U32(13)), Field("DATA", U32(7)));
            File.WriteAllBytes(Path.Combine(directory, "Fixture.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 7, Field("ACBS", new byte[24])),
                Record("SCPT", 0x10, Field("SCTX", Text("scn ItemEvent\nbegin OnAdd\nif GetContainer != player\nreturn\nendif\nAddCardToPlayer\nRemoveMe\nend\nbegin OnAdd player\nreturn\nend")),
                    Field("SCRO", U32(0x14))),
                Record("SCPT", 0x11, Field("SCTX", Text("scn ItemFailure\nbegin OnAdd\nAddCardToPlayer\nUnownedEffect\nend"))),
                Record("SCPT", 0x12, Field("SCTX", Text("scn RetainedItem\nbegin OnAdd\nAddCardToPlayer\nend"))),
                Card(0x20, 0x10), Card(0x21, 0x10), Card(0x22, 0x10, badExtent: true), Card(0x23, 0x20),
                Card(0x24, 0x11), Card(0x25, 0x12),
                Record("CDCK", 0x30, Field("EDID", Text("Deck")), Field("FULL", Text("Deck")),
                    Field("CARD", U32(0x21)), Field("CARD", U32(0x20)), Field("CARD", U32(0x21)), Field("DATA", U32(84))),
                Record("CDCK", 0x31, Field("EDID", Text("BadDeck")), Field("FULL", Text("BadDeck")),
                    Field("CARD", U32(0x10)), Field("DATA", U32(1))),
                Record("QUST", 0x40, Field("SCRO", U32(0x30)), Field("SCRO", U32(0x900))),
                Record("CELL", 0x800, Field("DATA", [1])), Group(0x800,
                    Join(Record("REFR", 0x900, Field("NAME", U32(0x20)), Field("DATA", new byte[24]), Field("EDID", Text("CardReference"))),
                        Record("REFR", 0x901, Field("NAME", U32(0x24)), Field("DATA", new byte[24])),
                        Record("REFR", 0x902, Field("NAME", U32(0x25)), Field("DATA", new byte[24]))))));
            using var stack = FalloutPluginStack.Load(directory, ["Fixture.esm"]);
            FalloutFormKey Key(uint id) => new("Fixture.esm", id);
            var card = FalloutCaravanCard.Read(stack, stack.GetEffective(Key(0x20)));
            if (card.FirstIntv != 2 || card.SecondIntv != 13 || card.Data != 7 || card.Script != Key(0x10) || card.ObjectBounds.Length != 12)
                throw new InvalidDataException("Caravan card lost raw source fields or source script.");
            var queen = FalloutCaravanCardAdapter.Interpret(card, 7);
            if (queen.Identity.Form != Key(0x20) || queen.Identity.Instance != 7 || queen.Kind != FalloutCaravanMatch.Kind.Queen ||
                queen.Suit != FalloutCaravanRules.Suit.Spades)
                throw new InvalidDataException("Caravan interpretation lost source identity or evidenced rank and suit.");
            var joker = FalloutCaravanCardAdapter.Interpret(card with { FirstIntv = 5, SecondIntv = 15 }, 8);
            if (joker.Kind != FalloutCaravanMatch.Kind.Joker || joker.Suit is not null)
                throw new InvalidDataException("Caravan joker did not preserve its unsuited source type.");
            Reject(() => FalloutCaravanCardAdapter.Interpret(card with { SecondIntv = 11 }, 0));
            Reject(() => FalloutCaravanCardAdapter.Interpret(card with { FirstIntv = 6 }, 0));
            var recipient = Key(0x14); var delivered = new List<string>();
            var values = new FalloutScriptValueContext(name => name.Equals("player", StringComparison.OrdinalIgnoreCase)
                    ? FalloutScriptValue.Form(0x14) : throw new NotSupportedException($"Unowned item value {name}."),
                (_, _) => throw new NotSupportedException("Unowned item local mutation."),
                IsForm: name => name.Equals("player", StringComparison.OrdinalIgnoreCase));
            var itemExecution = new FalloutInventoryScriptExecution(values,
                name => name.Equals("GetContainer", StringComparison.OrdinalIgnoreCase)
                    ? FalloutScriptFunction.Typed([], _ => FalloutScriptValue.Form(stack.RuntimeFormId(recipient)), readOnly: true) : null,
                (name, _) => delivered.Add(name.ToLowerInvariant()));
            if (FalloutInventoryScriptEvents.Dispatch(stack, stack.GetEffective(Key(0x20)), "OnAdd", recipient, itemExecution) != 2 ||
                !delivered.SequenceEqual(["addcardtoplayer", "removeme"]))
                throw new InvalidDataException("Inventory event lost source blocks, condition or command order.");
            recipient = Key(0x900); delivered.Clear();
            if (FalloutInventoryScriptEvents.Dispatch(stack, stack.GetEffective(Key(0x20)), "OnAdd", recipient, itemExecution) != 1 || delivered.Count != 0)
                throw new InvalidDataException("Inventory event ignored its recipient filter or executed a returned branch.");
            var deck = FalloutCaravanDeck.Read(stack, stack.GetEffective(Key(0x30)));
            if (!deck.Cards.SequenceEqual([Key(0x21), Key(0x20), Key(0x21)]) || deck.Data != 84)
                throw new InvalidDataException("Caravan deck lost source order, duplicates or raw data.");
            long instance = 0;
            var interpretedDeck = FalloutCaravanCardAdapter.InterpretDeck(stack, deck, _ => instance++);
            if (!interpretedDeck.Select(value => value.Identity.Form).SequenceEqual(deck.Cards) ||
                interpretedDeck.Select(value => value.Identity).Distinct().Count() != 3)
                throw new InvalidDataException("Caravan deck interpretation lost order or duplicate source instances.");
            Reject(() => FalloutCaravanCardAdapter.InterpretDeck(stack, deck, _ => 0));
            var collection = new FalloutPlayerCaravanCards(stack);
            if (!collection.Add(Key(0x20)) || !collection.Add(Key(0x21)) || collection.Add(Key(0x20)) ||
                !collection.First.SequenceEqual([Key(0x21), Key(0x20)]))
                throw new InvalidDataException("Caravan collection did not retain source add order or deduplicate.");
            collection.Restore(new([Key(0x20)], [Key(0x21)]));
            if (collection.Add(Key(0x21)) || collection.Capture().Second.Single() != Key(0x21))
                throw new InvalidDataException("Caravan add duplicated a card in the second source list.");
            collection.Restore(new(Enumerable.Repeat(Key(0x20), 20).ToArray(), Enumerable.Repeat(Key(0x21), 9).ToArray()));
            if (collection.MeetsMatchMinimum()) throw new InvalidDataException("A 29-card collection met the match minimum.");
            collection.Restore(new(Enumerable.Repeat(Key(0x20), 20).ToArray(), Enumerable.Repeat(Key(0x21), 10).ToArray()));
            if (!collection.MeetsMatchMinimum()) throw new InvalidDataException("Both source card lists did not contribute to the match minimum.");
            var prior = collection.Capture();
            Reject(() => collection.Restore(new([Key(0x20)], [Key(0x30)])));
            if (!collection.Capture().First.SequenceEqual(prior.First) || !collection.Capture().Second.SequenceEqual(prior.Second))
                throw new InvalidDataException("Rejected Caravan collection restore changed live membership.");
            collection.Restore(null);
            Reject(() => collection.Add(Key(0x20)));
            Reject(() => collection.MeetsMatchMinimum());
            var request = FalloutCaravanCommandRequest.Read(stack, Key(0x20), stack.GetEffective(Key(0x30)), 1, .25);
            if (request.Opponent != Key(0x20) || request.Deck.Form != Key(0x30) || request.AiType != 1 || request.BetPercentage != .25f)
                throw new InvalidDataException("Caravan command lost its resolved request arguments.");
            Reject(() => FalloutCaravanCommandRequest.Read(stack, Key(0x20), stack.GetEffective(Key(0x30)), .5, .25));
            Reject(() => FalloutCaravanCommandRequest.Read(stack, Key(0x20), stack.GetEffective(Key(0x30)), 1, double.NaN));
            Reject(() => FalloutCaravanCommandRequest.Read(stack, Key(0x20), stack.GetEffective(Key(0x20)), 1, .25));
            using var world = new FalloutReferenceWorld(stack);
            world.LoadCell(FalloutCellSceneReader.Read(stack, Key(0x800)));
            var pickupInventory = new FalloutPlayerInventory();
            using (var unknown = new FalloutReferenceWorld(stack))
            {
                unknown.LoadCell(FalloutCellSceneReader.Read(stack, Key(0x800)));
                unknown.PlayerCaravanCards.Restore(null);
                var unknownInventory = new FalloutPlayerInventory();
                var unknownRevision = unknown.PlayerCaravanCards.Revision;
                Reject(() => unknown.Take(Key(0x900), unknownInventory, 1, null));
                if (unknown.Get(Key(0x900)).Taken || unknownInventory.Item(Key(0x20)) is not null ||
                    unknown.PlayerCaravanCards.Known || unknown.PlayerCaravanCards.Revision != unknownRevision)
                    throw new InvalidDataException("Unknown card history was changed by a refused pickup.");
            }
            using (var ambiguous = new FalloutReferenceWorld(stack))
            {
                ambiguous.LoadCell(FalloutCellSceneReader.Read(stack, Key(0x800)));
                var inventoryWithItem = new FalloutPlayerInventory();
                inventoryWithItem.Add(stack, Key(0x20), 1, 1, true);
                Reject(() => ambiguous.Take(Key(0x900), inventoryWithItem, 1, null));
                if (ambiguous.Get(Key(0x900)).Taken || inventoryWithItem.Item(Key(0x20))?.Count != 1 ||
                    ambiguous.PlayerCaravanCards.First.Count != 0)
                    throw new InvalidDataException("Ambiguous incoming item changed existing inventory or world state.");
            }
            using (var stale = new FalloutReferenceWorld(stack))
            {
                stale.LoadCell(FalloutCellSceneReader.Read(stack, Key(0x800)));
                var inventory = new FalloutPlayerInventory();
                inventory.PrepareChange = (_, _) => new FalloutInventoryChangeLease(() => stale.Get(Key(0x902)).Enabled = false, () => { });
                try { stale.Take(Key(0x902), inventory, 1, null); throw new InvalidDataException("Stale scripted pickup was accepted."); }
                catch (InvalidOperationException error) when (error.Message == "Item state changed during pickup.") { }
                if (stale.Get(Key(0x902)).Taken || stale.Get(Key(0x902)).Enabled != false || inventory.Item(Key(0x25)) is not null ||
                    stale.PlayerCaravanCards.First.Count != 0)
                    throw new InvalidDataException("Scripted pickup published a stale disabled item or its staged event.");
            }
            using (var staleOwner = new FalloutReferenceWorld(stack))
            {
                staleOwner.LoadCell(FalloutCellSceneReader.Read(stack, Key(0x800)));
                var inventory = new FalloutPlayerInventory();
                inventory.PrepareChange = (_, _) => new FalloutInventoryChangeLease(() => staleOwner.SetOwnership(Key(0x902), Key(7)), () => { });
                try { staleOwner.Take(Key(0x902), inventory, 1, null); throw new InvalidDataException("Stale scripted ownership was accepted."); }
                catch (InvalidOperationException error) when (error.Message == "Item state changed during pickup.") { }
                if (staleOwner.Get(Key(0x902)).Taken || inventory.Item(Key(0x25)) is not null ||
                    staleOwner.PlayerCaravanCards.First.Count != 0 || staleOwner.Ownership(Key(0x902)).Owner != Key(7))
                    throw new InvalidDataException("Scripted pickup published stale ownership or reverted the independent owner change.");
            }
            var priorRevision = world.PlayerCaravanCards.Revision;
            Reject(() => world.Take(Key(0x901), pickupInventory, 1, null));
            if (world.Get(Key(0x901)).Taken || pickupInventory.Item(Key(0x24)) is not null ||
                world.PlayerCaravanCards.Revision != priorRevision || world.PlayerCaravanCards.First.Count != 0)
                throw new InvalidDataException("Rejected item event published a partial pickup or collection effect.");
            world.Take(Key(0x900), pickupInventory, 1, null);
            if (!world.Get(Key(0x900)).Taken || pickupInventory.Item(Key(0x20)) is not null ||
                !world.PlayerCaravanCards.First.SequenceEqual([Key(0x20)]))
                throw new InvalidDataException("Placed pickup did not execute and publish its source event.");
            using (var cold = new FalloutReferenceWorld(stack))
            {
                cold.Restore(world.Capture());
                cold.PlayerCaravanCards.Restore(world.PlayerCaravanCards.Capture());
                cold.LoadCell(FalloutCellSceneReader.Read(stack, Key(0x800)));
                if (!cold.Get(Key(0x900)).Taken || !cold.PlayerCaravanCards.First.SequenceEqual([Key(0x20)]))
                    throw new InvalidDataException("Cold reference and collection restore lost the item event outcome.");
            }
            IReadOnlyList<string>? received = null;
            var executor = new FalloutReferenceScripts(stack, world, new(stack), new((_, _) => false, _ => { },
                Command: (_, _, _, args) => { received = args.ToArray(); throw new NotSupportedException("Match owner is not bound."); }));
            var quest = stack.GetEffective(Key(0x40));
            Reject(() => executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "ShowCaravanMenu Deck 1 .25"));
            if (received is null || !received.SequenceEqual(["Deck", "1", "0.25"]))
                throw new InvalidDataException("Caravan source dispatch did not pass resolved argument values to its owner.");
            executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "CardReference.AddCardToPlayer\nCardReference.addcard");
            if (!world.PlayerCaravanCards.First.SequenceEqual([Key(0x20)]))
                throw new InvalidDataException("AddCardToPlayer did not publish the referenced source card exactly once.");
            FalloutFormKey? removedReference = null, removedDestination = null;
            var inventoryExecutor = new FalloutReferenceScripts(stack, world, new(stack), new((_, _) => false, _ => { },
                InventoryContainer: reference => reference == Key(0x900) ? Key(0x900) : null,
                RemoveInventoryInstance: (reference, destination) => { removedReference = reference; removedDestination = destination; }));
            inventoryExecutor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(),
                "if CardReference.GetContainer == CardReference\nCardReference.RemoveMe CardReference\nendif");
            if (removedReference != Key(0x900) || removedDestination != Key(0x900))
                throw new InvalidDataException("Inventory-instance query and removal did not preserve source identities.");
            Reject(() => executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "CardReference.RemoveMe"));
            Reject(() => FalloutCaravanCard.Read(stack, stack.GetEffective(Key(0x22))));
            Reject(() => FalloutCaravanCard.Read(stack, stack.GetEffective(Key(0x23))));
            Reject(() => FalloutCaravanDeck.Read(stack, stack.GetEffective(Key(0x31))));
            Console.WriteLine("OPENNV_CARAVAN_SOURCE_CONTRACT_PASS cardRaw=true deckOrder=true duplicates=true wrongExtentRefused=true wrongSourceRefused=true rulesAndRetail=unverified");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void CollectionSchema()
    {
        var prior = JsonSerializer.Deserialize<FalloutNativeCampaignState>(
            "{\"Schema\":\"opennv-native-fnv-campaign-save/v61\",\"References\":[]}")!;
        var upgraded = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(prior);
        if (upgraded.Schema != FalloutNativeCampaignSave.ExpectedSchema || upgraded.PlayerCaravanCards is not null)
            throw new InvalidDataException("Legacy save admission invented Caravan collection history.");
        var v62 = upgraded with { Schema = FalloutNativeCampaignSave.BeforeExecutableNoticeSchema,
            PlayerCaravanCards = new([new("Fixture.esm", 0x20)], []) };
        if (FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(v62).PlayerCaravanCards?.First.Count != 1)
            throw new InvalidDataException("Previous save schema lost its known Caravan collection.");
        var notifications = new FalloutHudNotifications();
        notifications.Publish([new(FalloutHudEventKind.ExecutableNotice, new("Fixture.esm", 0x900), 0, Text: "sCardCountText")]);
        var withNotice = upgraded with { Scripts = new FalloutQuestScriptsSnapshot([], [], notifications.Capture()) };
        var noticeCold = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(withNotice))!;
        var previous = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(noticeCold with
            { Schema = FalloutNativeCampaignSave.BeforePackageStartLocationSchema });
        if (previous.Schema != FalloutNativeCampaignSave.ExpectedSchema || previous.Scripts?.Notifications?.Pending.Count != 1)
            throw new InvalidDataException("Previous save schema lost executable notifications during package-start location admission.");
        var restored = new FalloutHudNotifications();
        restored.Restore(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(noticeCold).Scripts!.Notifications!);
        if (restored.Capture().Pending.Single().Event.Text != "sCardCountText")
            throw new InvalidDataException("Cold save lost the source executable notice.");
        Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(withNotice with
            { Schema = FalloutNativeCampaignSave.BeforeExecutableNoticeSchema }));
        Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(prior with { PlayerCaravanCards = new([], []) }));
        var unknown = upgraded with { PlayerCaravanCards = new([], [], Known: false) };
        var cold = JsonSerializer.Deserialize<FalloutNativeCampaignState>(JsonSerializer.Serialize(unknown))!;
        if (FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(cold).PlayerCaravanCards is not { Known: false, First.Count: 0, Second.Count: 0 })
            throw new InvalidDataException("Campaign save lost unknown Caravan collection history.");
        Reject(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(unknown with
            { PlayerCaravanCards = new([new("Fixture.esm", 0x20)], [], Known: false) }));
    }
    private static void MatchRules()
    {
        FalloutCaravanMatch.Card Card(int player, int index) => new(
            new(new FalloutFormKey("Fixture.esm", (uint)(1 + player * 100 + index)), index),
            FalloutCaravanMatch.Kind.Number, FalloutCaravanRules.Suit.Hearts, 2 + index % 8);
        var first = Enumerable.Range(0, 30).Select(index => Card(0, index)).ToArray();
        first[29] = first[29] with { Kind = FalloutCaravanMatch.Kind.Joker, Suit = null, Number = 0 };
        var second = Enumerable.Range(0, 30).Select(index => Card(1, index)).ToArray();
        var match = new FalloutCaravanMatch(first, second, firstPlayer: 0, drawDuringOpening: false);
        if (match.Hand(0).Count != 8 || match.Remaining(0) != 22)
            throw new InvalidDataException("Caravan deal did not partition the stock into an eight-card hand.");
        for (var lane = 0; lane < 3; ++lane)
        {
            match.PlayNumber(first[lane].Identity, lane);
            match.PlayNumber(second[lane].Identity, lane);
        }
        if (match.CurrentPhase != FalloutCaravanMatch.Phase.Playing || match.CurrentPlayer != 0 || match.Hand(0).Count != 5)
            throw new InvalidDataException("Caravan opening did not retain both hands and three piles.");
        match.PlayNumber(first[3].Identity, 0);
        if (match.Hand(0).Count != 5 || match.Remaining(0) != 21 || match.CurrentPlayer != 1)
            throw new InvalidDataException("Caravan play did not draw and transfer turn ownership.");
        match.Discard(second[3].Identity);
        if (match.Discarded(1).Single().Identity != second[3].Identity || match.CurrentPlayer != 0)
            throw new InvalidDataException("Caravan discard lost its card or turn.");
        match.Disband(0);
        if (match.Discarded(0).Count != 2 || match.Lane(0, 0).Entries.Count != 0 || match.CurrentPlayer != 1)
            throw new InvalidDataException("Caravan disband did not transfer its cards to discard.");
        try { match.PlayNumber(first[4].Identity, 1); throw new InvalidDataException("Opponent hand card was accepted."); }
        catch (InvalidOperationException) { }
        if (match.CurrentPlayer != 1 || match.Hand(1).Count != 5)
            throw new InvalidDataException("Rejected action changed Caravan turn or hand.");
        first[3] = first[3] with { Kind = FalloutCaravanMatch.Kind.Jack, Number = 0 };
        first[4] = first[4] with { Kind = FalloutCaravanMatch.Kind.Joker, Suit = null, Number = 0 };
        var modifiers = new FalloutCaravanMatch(first, second, 0, drawDuringOpening: false);
        for (var lane = 0; lane < 3; ++lane)
        {
            modifiers.PlayNumber(first[lane].Identity, lane);
            modifiers.PlayNumber(second[lane].Identity, lane);
        }
        modifiers.PlayModifier(first[3].Identity, 1, 0, 0);
        if (modifiers.Discarded(0).Single().Identity != first[3].Identity ||
            modifiers.Discarded(1).Single().Identity != second[0].Identity)
            throw new InvalidDataException("Jack did not return the played card and opponent target to their owners.");
        modifiers.Discard(second[3].Identity);
        modifiers.PlayModifier(first[4].Identity, 0, 1, 0);
        if (!modifiers.Discarded(1).Any(card => card.Identity == second[1].Identity) ||
            modifiers.Lane(0, 1).Entries[0].Modifiers.Single().Identity != first[4].Identity)
            throw new InvalidDataException("Joker did not remove the opposing match or stay attached to its target.");
        var snapshot = modifiers.Capture();
        var encoded = JsonSerializer.Serialize(snapshot);
        var cold = FalloutCaravanMatch.Restore(JsonSerializer.Deserialize<FalloutCaravanMatch.Snapshot>(encoded)!);
        if (JsonSerializer.Serialize(cold.Capture()) != encoded)
            throw new InvalidDataException("Cold Caravan match lost card zones, lane order or turn state.");
        var nextCard = cold.Hand(cold.CurrentPlayer)[0].Identity;
        modifiers.Discard(nextCard); cold.Discard(nextCard);
        if (JsonSerializer.Serialize(cold.Capture()) != JsonSerializer.Serialize(modifiers.Capture()))
            throw new InvalidDataException("Cold Caravan continuation changed its next transition.");
        Reject(() => FalloutCaravanMatch.Restore(snapshot with { FirstStock = snapshot.FirstStock.Append(snapshot.FirstHand[0]).ToArray() }));
        Reject(() => FalloutCaravanMatch.Restore(snapshot with { Winner = FalloutCaravanRules.Sale.First }));
    }
    private static void BoardRules()
    {
        var board = new FalloutCaravanBoard();
        FalloutCaravanBoard.CardIdentity Id(int value) => new(new FalloutFormKey("Fixture.esm", (uint)value), value);
        var hearts = FalloutCaravanRules.Suit.Hearts;
        board.PlayNumber(0, 0, Id(1), new(7, hearts));
        board.PlayNumber(0, 0, Id(2), new(9, FalloutCaravanRules.Suit.Spades));
        board.PlayModifier(0, 0, 0, new(Id(3), FalloutCaravanBoard.ModifierKind.King, hearts));
        board.PlayModifier(0, 0, 1, new(Id(4), FalloutCaravanBoard.ModifierKind.Queen, hearts));
        board.PlayNumber(0, 0, Id(5), new(6, FalloutCaravanRules.Suit.Clubs));
        if (board.Total(0, 0) != 29 || board.GetLane(0, 0).Direction != FalloutCaravanRules.Direction.Descending ||
            board.GetLane(0, 0).Entries[0].Modifiers[0].Identity != Id(3))
            throw new InvalidDataException("Caravan board lost its ordered cards, modifiers or direction.");
        try { board.PlayNumber(1, 0, Id(1), new(2, hearts)); throw new InvalidDataException("Duplicate card instance was accepted."); }
        catch (InvalidOperationException) { }
        if (board.GetLane(1, 0).Entries.Count != 0) throw new InvalidDataException("Rejected card changed the board.");
        var removed = board.Disband(0, 0);
        if (!removed.SequenceEqual([Id(1), Id(3), Id(2), Id(4), Id(5)]) || board.Total(0, 0) != 0 ||
            board.GetLane(0, 0).Direction != FalloutCaravanRules.Direction.Unset || board.GetLane(0, 0).Suit is not null)
            throw new InvalidDataException("Disband did not retire the complete ordered caravan and its modifiers.");
        Reject(() => board.Disband(0, 0));
        board.PlayNumber(0, 1, Id(6), new(3, hearts));
        board.PlayModifier(0, 1, 0, new(Id(7), FalloutCaravanBoard.ModifierKind.Queen, FalloutCaravanRules.Suit.Spades));
        if (board.GetLane(0, 1).Direction != FalloutCaravanRules.Direction.Unset ||
            board.GetLane(0, 1).Suit != FalloutCaravanRules.Suit.Spades)
            throw new InvalidDataException("One-card queen lost its suit or invented a direction.");
        board.PlayNumber(0, 1, Id(8), new(8, FalloutCaravanRules.Suit.Clubs));
        board.PlayModifier(0, 1, 0, new(Id(9), FalloutCaravanBoard.ModifierKind.Queen, hearts));
        if (board.GetLane(0, 1).Direction != FalloutCaravanRules.Direction.Ascending ||
            board.GetLane(0, 1).Suit != FalloutCaravanRules.Suit.Clubs)
            throw new InvalidDataException("A queen on an earlier number changed the current caravan direction or suit.");
        board.PlayNumber(0, 2, Id(10), new(6, hearts));
        board.PlayNumber(1, 0, Id(11), new(6, FalloutCaravanRules.Suit.Spades));
        board.PlayModifier(1, 0, 0, new(Id(12), FalloutCaravanBoard.ModifierKind.King, hearts));
        var jokerRemoved = board.ApplyJoker(Id(13), new(0, 2, 0));
        if (!jokerRemoved.SequenceEqual([Id(11), Id(12)]) || board.GetLane(1, 0).Entries.Count != 0 ||
            board.GetLane(0, 2).Entries[0].Modifiers.Single().Identity != Id(13))
            throw new InvalidDataException("Joker failed to remove matching numbers and retain its target.");
        var jackRemoved = board.ApplyJack(new(0, 2, 0));
        if (!jackRemoved.SequenceEqual([Id(10), Id(13)]) || board.GetLane(0, 2).Entries.Count != 0)
            throw new InvalidDataException("Jack failed to remove its target and attached joker.");
    }
    private static void ManualRules()
    {
        var seven = new FalloutCaravanRules.NumberCard(7, FalloutCaravanRules.Suit.Hearts);
        var sixHearts = new FalloutCaravanRules.NumberCard(6, FalloutCaravanRules.Suit.Hearts);
        var sixClubs = new FalloutCaravanRules.NumberCard(6, FalloutCaravanRules.Suit.Clubs);
        var sameValue = new FalloutCaravanRules.NumberCard(7, FalloutCaravanRules.Suit.Clubs);
        if (!FalloutCaravanRules.CanAppend(seven, sixHearts, FalloutCaravanRules.Direction.Ascending, FalloutCaravanRules.Suit.Hearts) ||
            FalloutCaravanRules.CanAppend(seven, sixClubs, FalloutCaravanRules.Direction.Ascending, FalloutCaravanRules.Suit.Hearts) ||
            FalloutCaravanRules.CanAppend(seven, sameValue, FalloutCaravanRules.Direction.Unset, FalloutCaravanRules.Suit.Clubs) ||
            FalloutCaravanRules.Total([new(seven, 2), new(sixClubs, 0)]) != 34 ||
            FalloutCaravanRules.ResolveSale(21, 27) != FalloutCaravanRules.Sale.First ||
            FalloutCaravanRules.ResolveSale(26, 26) != FalloutCaravanRules.Sale.Unresolved ||
            FalloutCaravanRules.ResolveWinner([(26, 25), (21, 20), (19, 24)]) != FalloutCaravanRules.Sale.First ||
            FalloutCaravanRules.ResolveWinner([(26, 25), (21, 21), (19, 24)]) != FalloutCaravanRules.Sale.Unresolved)
            throw new InvalidDataException("Caravan manual-rule primitives disagree with their bounded contract.");
        Reject(() => FalloutCaravanRules.ResolveWinner([(21, 20)]));
        Reject(() => FalloutCaravanRules.Total([new(new(0, FalloutCaravanRules.Suit.Clubs), 0)]));
        var queen = FalloutCaravanRules.Queen(FalloutCaravanRules.Direction.Ascending, FalloutCaravanRules.Suit.Spades);
        if (queen != (FalloutCaravanRules.Direction.Descending, FalloutCaravanRules.Suit.Spades))
            throw new InvalidDataException("Queen did not reverse the direction and change suit.");
        if (FalloutCaravanRules.Queen(FalloutCaravanRules.Direction.Unset, FalloutCaravanRules.Suit.Spades) !=
            (FalloutCaravanRules.Direction.Unset, FalloutCaravanRules.Suit.Spades))
            throw new InvalidDataException("Queen invented a direction on a one-card caravan.");
        FalloutCaravanRules.Position a = new(0, 0, 0), b = new(0, 1, 0), c = new(1, 2, 1), d = new(1, 0, 0);
        FalloutCaravanRules.BoardNumber[] board = [
            new(a, new(1, FalloutCaravanRules.Suit.Hearts)), new(b, new(6, FalloutCaravanRules.Suit.Hearts)),
            new(c, new(6, FalloutCaravanRules.Suit.Clubs)), new(d, new(1, FalloutCaravanRules.Suit.Clubs))];
        if (!FalloutCaravanRules.JokerRemovals(board, a).SequenceEqual([b]) ||
            !FalloutCaravanRules.JokerRemovals(board, b).SequenceEqual([c]))
            throw new InvalidDataException("Joker did not retain its target and select the matching cards on both sides.");
        Reject(() => FalloutCaravanRules.JokerRemovals(board, new(1, 1, 0)));
        Reject(() => FalloutCaravanRules.JokerRemovals([board[0], board[0]], a));
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid Caravan source was accepted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Group(uint cell, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 6); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
}
