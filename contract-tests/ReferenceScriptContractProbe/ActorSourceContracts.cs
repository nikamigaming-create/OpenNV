using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class ActorSourceContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            RandomDialogueContracts(directory);
            File.WriteAllBytes(Path.Combine(directory, "Actors.esm"), Join(Header(),
                Creature(0x800, 0, 0, "creatures/test/skeleton.nif", "body.nif", 1.5f,
                    Field("CNTO", Join(BitConverter.GetBytes(0x840u), BitConverter.GetBytes(3)))),
                Creature(0x801, 65, 0x800, "creatures/local/skeleton.nif", "local.nif", .75f),
                Creature(0x802, 66, 0x802, "creatures/test/skeleton.nif", "body.nif", 1),
                Record("NPC_", 7, Field("EDID", Text("PlayerBase"))),
                Creature(0x803, 0, 0, "creatures/test/skeleton.nif", "../escape.nif", 1),
                Creature(0x804, 256, 0x800, "creatures/test/skeleton.nif", "body.nif", 1),
                Creature(0x805, 66, 0x820, "creatures/local/skeleton.nif", "local.nif", 1),
                Creature(0x806, 66, 0x821, "creatures/local/skeleton.nif", "local.nif", 1),
                ImmobileCreature(0x808),
                Creature(0x80a, 0, 0, "creatures/test/skeleton.nif", "body.nif", 1,
                    Field("PKID", BitConverter.GetBytes(0x8f5u)), Field("PKID", BitConverter.GetBytes(0x8f6u))),
                Creature(0x809, 64, 0x808, "creatures/local/skeleton.nif", "local.nif", 1),
                ActorList(0x820, 0, (1, 0x800), (1, 0x804), (10, 0x804)),
                ActorList(0x821, 100, (1, 0x800)),
                Record("MISC", 0x840, Field("EDID", Text("CreatureLoot")), Field("DATA", new byte[8])),
                Record("FACT", 0x8e0), Record("FACT", 0x8e1),
                Record("PERK", 0x8e2, Field("PRKE", [2, 0, 0]), Field("DATA", [0, 3, 2]),
                    Field("PRKC", [1]), Field("CTDA", Condition(35, 0)), Field("EPFT", [1]),
                    Field("EPFD", BitConverter.GetBytes(2f)), Field("PRKF", [])),
                Record("CSTY", 0x8e3, Field("CSSD", new byte[64])),
                Cell(0x881, Record("REFR", 0x908, Field("NAME", BitConverter.GetBytes(0x844u)),
                    Field("DATA", new float[] { 10, 20, 30, 0, 0, 1 }.SelectMany(BitConverter.GetBytes).ToArray()))),
                NavigationFloor(0xb100, 0x881, 5),
                Record("STAT", 0x844), Record("ACTI", 0x845, Field("SCRI", BitConverter.GetBytes(0x891u))),
                Record("TACT", 0x846, Field("VNAM", BitConverter.GetBytes(0x850u)), Field("SNAM", BitConverter.GetBytes(0x847u)),
                    Field("SCRI", BitConverter.GetBytes(0x892u))),
                Record("SOUN", 0x847), Record("TACT", 0x848, Field("VNAM", BitConverter.GetBytes(0x840u))),
                Record("TACT", 0x849),
                Cell(0x880, Join(Record("ACRE", 0x900, Field("EDID", Text("BoundCreature")), Field("NAME", BitConverter.GetBytes(0x801u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x901, Field("NAME", BitConverter.GetBytes(0x804u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x902, Field("NAME", BitConverter.GetBytes(0x804u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x906, Field("NAME", BitConverter.GetBytes(0x805u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x907, Field("NAME", BitConverter.GetBytes(0x806u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x909, Field("NAME", BitConverter.GetBytes(0x809u)), Field("DATA", new byte[24])),
                    Record("ACRE", 0x90b, Field("NAME", BitConverter.GetBytes(0x80au)), Field("DATA", new byte[24])),
                    Marker(0x903, "SourceMapMarker"), Marker(0x905, "OtherMapMarker"),
                    Record("REFR", 0x904, Field("NAME", BitConverter.GetBytes(0x845u)), Field("DATA", new byte[24])),
                    Record("REFR", 0x90a, Field("NAME", BitConverter.GetBytes(0x846u)), Field("DATA", new byte[24])))),
                Record("SCPT", 0x890, Field("SCTX", Text("begin OnActivate\nif GetUnconscious\nSetUnconscious 0\nelse\nSetUnconscious 1\nendif\nend"))),
                Record("SCPT", 0x891, Field("SCRO", BitConverter.GetBytes(0x903u)),
                    Field("SCRO", BitConverter.GetBytes(0x905u)),
                    Field("SCRO", BitConverter.GetBytes(0x840u)),
                    Field("SCRO", BitConverter.GetBytes(0x14u)),
                    Field("SCTX", Text("begin OnActivate\nif GetActionRef == Player && player.GetIsID 7 == 1 && GetIsID 0x845 == 1\nShowMap SourceMapMarker\nif SourceMapMarker.GetMapMarkerVisible == 1\nShowMap SourceMapMarker 1\nendif\nendif\nend"))),
                Record("SCPT", 0x892, Field("SCRO", BitConverter.GetBytes(0x900u)),
                    Field("SCTX", Text("begin OnActivate\nSetTalkingActivatorActor BoundCreature\nend"))),
                Record("VTYP", 0x850, Field("EDID", Text("TestVoice"))),
                Record("PACK", 0x8f3, Field("EDID", Text("FirstActorPackage"))),
                Record("PACK", 0x8f4, Field("EDID", Text("OtherActorPackage"))),
                Record("QUST", 0x871, Field("DATA", new byte[8])),
                Record("PACK", 0x8f5, Field("EDID", Text("ConditionalActorPackage")),
                    Field("PKDT", [0, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0]),
                    Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]),
                    Field("CTDA", UnloadedActorPackageContracts.StageCondition(0x871))),
                Record("PACK", 0x8f6, Field("EDID", Text("FallbackActorPackage")),
                    Field("PKDT", [0, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0]),
                    Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0])),
                Record("WRLD", 0x8f0, Field("ICON", Text("interface/worldmap/owned.dds")),
                    Field("MNAM", Join(BitConverter.GetBytes(2048), BitConverter.GetBytes(1024),
                        BitConverter.GetBytes((short)-20), BitConverter.GetBytes((short)30),
                        BitConverter.GetBytes((short)40), BitConverter.GetBytes((short)-10)))),
                Record("WRLD", 0x8f2, Field("WNAM", BitConverter.GetBytes(0x8f0u)), Field("PNAM", BitConverter.GetBytes((ushort)4)),
                    Field("ONAM", new float[] { 2, -20, 30 }.SelectMany(BitConverter.GetBytes).ToArray())),
                Group(1, 0x8f0, Record("CELL", 0x882, Field("DATA", [0]), Field("XCLC", new byte[8]))),
                Group(1, 0x8f2, Record("CELL", 0x883, Field("DATA", [0]), Field("XCLC", new byte[8]))),
                Record("REGN", 0x8f1, Field("WNAM", BitConverter.GetBytes(0x8f0u)),
                    Field("RPLD", new float[] { 0, 0, 10, 0, 0, 10 }.SelectMany(BitConverter.GetBytes).ToArray())),
                Record("INFO", 0x860, Field("DATA", [0, 0, 0, 0]), Field("QSTI", BitConverter.GetBytes(0x870u)),
                    Field("TRDT", Response()), Field("NAM1", Text("Source response")))));
            // Override keeps the original voice identity; it is not a new line
            // under Patch.esp merely because another plugin wins the INFO.
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Actors.esm"),
                Record("INFO", 0x860, Field("DATA", [0, 0, 0, 0]), Field("QSTI", BitConverter.GetBytes(0x870u)),
                    Field("TRDT", Response()), Field("NAM1", Text("Winning response")))));
            using var records = FalloutPluginStack.Load(directory, ["Actors.esm", "Patch.esp"]);
            FalloutFormKey Key(uint id) => new("Actors.esm", id);
            var map = FalloutWorldMap.Read(records, Key(0x8f0));
            Check(map.Width == 2048 && map.Height == 1024 && map.Project(-20 * 4096, 30 * 4096) == System.Numerics.Vector2.Zero &&
                map.Project(40 * 4096, -10 * 4096) == System.Numerics.Vector2.One &&
                map.Project(10 * 4096, 10 * 4096) == new System.Numerics.Vector2(.5f, .5f),
                "Signed world-map bounds or Y orientation changed.");
            Check(FalloutWorldMap.Read(records, Key(0x8f2)).World == Key(0x8f0) &&
                FalloutWorldMap.Position(records, Key(0x8f2), 15 * 4096, -10 * 4096) == new System.Numerics.Vector2(.5f, .5f),
                "Child-world map inheritance/scale/offset changed.");
            Check(FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), Key(0x8f0), 1, 1) &&
                !FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), Key(0x8f0), 9, 9) &&
                !FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), null, 1, 1) &&
                !FalloutExteriorClimate.ContainsRegion(records, Key(0x8f1), Key(0x8f2), 1, 1),
                "Player region membership ignored the source polygon or world.");
            var appearance = FalloutCreatureAppearanceResolver.Resolve(records, Key(0x801), Key(0x900));
            Check(appearance.ModelOwner == Key(0x800) && appearance.StatsOwner == Key(0x801) && appearance.BaseScale == .75f &&
                appearance.Models.Single() == "meshes/creatures/test/body.nif", "Independent CREA template groups or part directory changed.");
            Reject(() => FalloutCreatureAppearanceResolver.Resolve(records, Key(0x802)));
            Reject(() => FalloutCreatureAppearanceResolver.Resolve(records, Key(0x803)));
            Reject(() => FalloutCreatureAppearanceResolver.Resolve(records, Key(0x800), Key(0x900)));
            Check(FalloutCreatureAppearanceResolver.SelectIdle(appearance, ["meshes/creatures/test/mtidle.kf"]).EndsWith("/mtidle.kf"), "Source idle not selected.");
            Reject(() => FalloutCreatureAppearanceResolver.SelectIdle(appearance, []));
            Reject(() => FalloutCreatureAppearanceResolver.SelectIdle(appearance,
                ["meshes/creatures/test/mtidle.kf", "meshes/creatures/test/locomotion/mtidle.kf"]));
            var speaker = FalloutDialogueSpeaker.Read(records, Key(0x801));
            var activatorSpeaker = FalloutDialogueSpeaker.Read(records, Key(0x846));
            Check(activatorSpeaker.RecordType == "TACT" && activatorSpeaker.Actor == Key(0x846) &&
                activatorSpeaker.TraitsOwner == Key(0x846) && activatorSpeaker.VoiceType == Key(0x850) && activatorSpeaker.Race is null &&
                FalloutDialogueSpeaker.AllowsPlayerDialogue(records, Key(0x846)), "Talking activator invented an actor/template/faction owner.");
            Reject(() => FalloutDialogueSpeaker.Read(records, Key(0x848)));
            Reject(() => FalloutDialogueSpeaker.Read(records, Key(0x849)));
            var activatorConditions = new FalloutDialogueConditions(records, new FalloutQuestState(records), Key(0x90a), activatorSpeaker,
                factions: _ => throw new InvalidDataException("Talking activator queried actor factions."));
            var activatorCondition = new FalloutCondition(records.GetEffective(Key(0x860)), 0, 1, 72, 0x846, 0, 0, 0);
            Check(activatorConditions.Evaluate(activatorCondition) == 1 &&
                activatorConditions.Evaluate(activatorCondition with { Function = 427, Argument1 = 0x850 }) == 1,
                "Talking activator dialogue lost its source base or VNAM voice identity.");
            Reject(() => activatorConditions.Evaluate(activatorCondition with { Function = 70, Argument1 = 0 }));
            using (var activatorWorld = new FalloutReferenceWorld(records))
            using (var activatorCold = new FalloutReferenceWorld(records))
            {
                activatorWorld.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
                var activatorScripts = new FalloutReferenceScripts(records, activatorWorld, new FalloutQuestState(records),
                    new((_, _) => false, _ => throw new InvalidDataException("Talking activator binding invented a presentation effect.")));
                Check(activatorScripts.Activate(Key(0x90a), Key(0x14)).Error is null &&
                    activatorWorld.DialogueSubject(Key(0x90a)) == Key(0x900) && activatorWorld.DialogueIdentity(Key(0x90a)).Actor == Key(0x801),
                    "Source SetTalkingActivatorActor did not bind the actual reference's dialogue identity.");
                activatorWorld.Get(Key(0x90a)).TalkedToPlayer = true;
                activatorCold.Restore(activatorWorld.Capture());
                Check(activatorCold.Get(Key(0x90a)).TalkedToPlayer && activatorCold.DialogueSubject(Key(0x90a)) == Key(0x900),
                    "Talking activator lost shared conversation history/binding across cold restoration.");
                activatorCold.SetTalkingActivatorActor(Key(0x90a), null);
                Check(activatorCold.DialogueIdentity(Key(0x90a)).Actor == Key(0x846), "Clearing the binding did not restore the actual activator voice.");
                Reject(() => activatorCold.SetTalkingActivatorActor(Key(0x904), Key(0x900)));
                Reject(() => activatorCold.SetTalkingActivatorActor(Key(0x90a), Key(0x904)));
            }
            Console.WriteLine("OPENNV_TALKING_ACTIVATOR_IDENTITY_PASS sourceVoice=true nonActor=true sharedColdHistory=true unknownQueries=visible");
            using var world = new FalloutReferenceWorld(records);
            Check(FalloutActorValue.UserSlot(62) == "variable01" && FalloutActorValue.UserSlot(66) == "variable05" &&
                FalloutActorValue.UserSlot(71) == "variable10", "Numeric user values changed their engine slots.");
            Reject(() => FalloutActorValue.UserSlot(61));
            Reject(() => FalloutActorValue.UserSlot(72));
            using (var spatial = new FalloutReferenceWorld(records))
            {
                var player = new FalloutReferencePlacement(Key(0x880), [0, 0, 0], [0, 0, 0]);
                Check(spatial.InSameCell(Key(0x900), Key(0x14), player, .01f) &&
                    !spatial.InSameCell(Key(0x908), Key(0x14), player, .01f), "Interior spatial membership ignored the live cell.");
                spatial.SetPlacement(Key(0x900), new(Key(0x882), [-1, 0, 0], [0, 0, 0]));
                player = new(Key(0x882), [-4096, 0, 0], [0, 0, 0]);
                Check(spatial.InSameCell(Key(0x900), Key(0x14), player, .01f), "Negative exterior coordinates used truncation.");
                Check(!spatial.InSameCell(Key(0x900), Key(0x14), player with { Position = [0, 0, 0] }, .01f),
                    "Adjacent exterior grids were treated as one cell.");
                Check(!spatial.InSameCell(Key(0x900), Key(0x14), player with { Cell = Key(0x883) }, .01f),
                    "Different worldspaces shared spatial membership.");
            }
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
            DialoguePackageQueryContracts.Run(directory, records, world, speaker);
            UnloadedActorPackageContracts.Run(records);
            var choices = new HashSet<FalloutFormKey>();
            for (ulong seed = 0; seed < 32; seed++)
            {
                var selection = new FalloutActorTemplateSelection(1, seed);
                selection.ResolveAll(records, Key(0x805));
                var selected = FalloutCreatureAppearanceResolver.Resolve(records, Key(0x805), selection: selection);
                Check(selected.ModelOwner == selected.StatsOwner, "Template groups rerolled the same leveled actor.");
                choices.Add(selected.ModelOwner);
                var savedSelection = new FalloutActorTemplateSelection(JsonSerializer.Deserialize<FalloutActorTemplateSnapshot>(JsonSerializer.Serialize(selection.Capture()))!);
                savedSelection.ResolveAll(records, Key(0x805));
                Check(FalloutCreatureAppearanceResolver.Resolve(records, Key(0x805), selection: savedSelection).ModelOwner == selected.ModelOwner,
                    "Cold template selection changed a source actor.");
            }
            Check(choices.SetEquals([Key(0x800), Key(0x804)]), "Reference-owned random selection did not admit every eligible candidate.");
            var highLevel = new FalloutActorTemplateSelection(10, 0);
            highLevel.ResolveAll(records, Key(0x805));
            Check(FalloutCreatureAppearanceResolver.Resolve(records, Key(0x805), selection: highLevel).ModelOwner == Key(0x804),
                "Highest eligible actor level did not exclude lower entries.");
            var retainedSelection = world.InitializeActorTemplates(Key(0x906), 1);
            Check(ReferenceEquals(retainedSelection, world.InitializeActorTemplates(Key(0x906), 10)), "Revisiting a reference rerolled its encounter.");
            Check(world.InitializeActorTemplates(Key(0x907), 1).Absent && !world.CanActivate(Key(0x907)),
                "A source chance-none result admitted an actor.");
            using (var selectionRestore = new FalloutReferenceWorld(records))
            {
                selectionRestore.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
                Check(selectionRestore.Get(Key(0x907)).Templates!.Absent &&
                    JsonSerializer.Serialize(selectionRestore.Get(Key(0x906)).Templates!.Capture()) == JsonSerializer.Serialize(retainedSelection.Capture()),
                    "World save did not preserve actor and chance-none choices.");
            }
            var firstInventory = world.Inventory(Key(0x901), 1).Contents;
            var peerInventory = world.Inventory(Key(0x902), 1).Contents;
            Check(firstInventory.Item(Key(0x840))?.Count == 3 && peerInventory.Item(Key(0x840))?.Count == 3 &&
                world.Inventory(Key(0x900), 1).Contents.Items.Count == 0, "CREA inventory ignored its independent template flag.");
            firstInventory.Remove(Key(0x840), 1, silent: true);
            world.ChangeFaction(Key(0x901), Key(0x8e0), 1, changeBase: false);
            Check(world.ActorFactions(Key(0x901))[Key(0x8e0)] == 1 && !world.ActorFactions(Key(0x902)).ContainsKey(Key(0x8e0)),
                "Reference faction membership leaked to another actor of the same base.");
            world.ChangeFaction(Key(0x901), Key(0x8e1), 2, changeBase: true);
            world.ChangeFaction(Key(0x901), Key(0x8e1), -1, changeBase: false);
            Check(world.ActorFactions(Key(0x901))[Key(0x8e1)] == -1 && world.ActorFactions(Key(0x902))[Key(0x8e1)] == 2,
                "Base faction rank or reference removal lost its independent scope.");
            world.ChangePerk(Key(0x901), Key(0x8e2), true);
            world.SetActorFlag(Key(0x901), true, friendlyHits: true);
            world.SetCombatStyle(Key(0x901), Key(0x8e3));
            Check(world.AcquiredPerks(Key(0x901)).Contains(Key(0x8e2)) && !world.AcquiredPerks(Key(0x902)).Contains(Key(0x8e2)),
                "Acquired perks leaked between references.");
            Reject(() => world.PerkEntries(Key(0x901)).Single().RequireActorConditionScope());
            var savedOverrides = JsonSerializer.Deserialize<OpenNV.Runtime.Gameplay.State.FalloutActorOverrides[]>(
                JsonSerializer.Serialize(world.CaptureActorOverrides()))!;
            using (var coldOverrides = new FalloutReferenceWorld(records))
            {
                coldOverrides.Restore(world.Capture()); coldOverrides.RestoreActorOverrides(savedOverrides);
                Check(coldOverrides.IgnoresFriendlyHits(Key(0x901)) && coldOverrides.ActorFactions(Key(0x902))[Key(0x8e1)] == 2 &&
                    coldOverrides.AcquiredPerks(Key(0x901)).Contains(Key(0x8e2)) && coldOverrides.CombatStyle(Key(0x901))?.Form == Key(0x8e3),
                    "Cold restoration lost companion effects or base faction scope.");
                Reject(() => coldOverrides.RestoreActorOverrides([savedOverrides[0] with { SourceSha256 = new string('0', 64) }]));
                Check(coldOverrides.IgnoresFriendlyHits(Key(0x901)), "Rejected actor restoration partially replaced authoritative state.");
            }
            world.ChangePerk(Key(0x901), Key(0x8e2), false);
            Check(!world.AcquiredPerks(Key(0x901)).Contains(Key(0x8e2)), "Perk removal did not reach reference state.");
            var sourceScene = FalloutCellSceneReader.Read(records, Key(0x880));
            var destinationScene = FalloutCellSceneReader.Read(records, Key(0x881));
            world.MoveTo(Key(0x901), Key(0x908), 2, 3, 4);
            world.SetRestrained(Key(0x901), true);
            world.SetPlayerTeammate(Key(0x901), true);
            var sourceAfterMove = world.ComposeResidency(sourceScene);
            var destinationAfterMove = world.ComposeResidency(destinationScene);
            world.ReplaceResidentCell(sourceAfterMove);
            world.LoadCell(destinationAfterMove);
            Check(world.Get(Key(0x901)).Cell == Key(0x880) && world.Placement(Key(0x901)).Cell == Key(0x881) &&
                world.Placement(Key(0x901)).Position.SequenceEqual(new float[] { 12, 23, 5 }) &&
                !sourceAfterMove.References.Any(reference => reference.FormKey == Key(0x901)) &&
                destinationAfterMove.References.Count(reference => reference.FormKey == Key(0x901)) == 1 &&
                world.IsResident(Key(0x901)), "MoveTo lost source identity, offsets or destination residency.");
            using (var inventoryRestore = new FalloutReferenceWorld(records))
            {
                inventoryRestore.Restore(world.Capture());
                Check(inventoryRestore.Inventory(Key(0x901), 1).Contents.Item(Key(0x840))?.Count == 2 &&
                    inventoryRestore.Inventory(Key(0x902), 1).Contents.Item(Key(0x840))?.Count == 3,
                    "Creature inventory changes leaked to a peer or were lost in the save.");
                Check(inventoryRestore.Get(Key(0x901)) is { Restrained: true, PlayerTeammate: true } &&
                    inventoryRestore.ComposeResidency(destinationScene).References.Single(reference => reference.FormKey == Key(0x901))
                        .Position.SequenceEqual(new float[] { 12, 23, 5 }), "Cold save lost moved actor placement or companion flags.");
            }
            Reject(() => world.MoveTo(Key(0x901), Key(0x908), float.NaN));
            Check(world.Placement(Key(0x901)).Position.SequenceEqual(new float[] { 12, 23, 5 }), "Rejected movement mutated placement.");
            Reject(() => world.MoveTo(Key(0x901), Key(0x903)));
            Check(world.Placement(Key(0x901)).Position.SequenceEqual(new float[] { 12, 23, 5 }), "Missing navigation partially moved an actor.");
            world.MoveTo(Key(0x903), Key(0x908), 2, 3, 4);
            Check(world.Placement(Key(0x903)).Position.SequenceEqual(new float[] { 12, 23, 34 }), "A non-actor MoveTo lost its exact offset.");
            world.MoveTo(Key(0x909), Key(0x908), 2, 3, 4);
            Check(world.Placement(Key(0x909)).Position.SequenceEqual(new float[] { 12, 23, 34 }), "Inherited Immobile model flags did not preserve an exact MoveTo.");
            var quests = new FalloutQuestState(records);
            FalloutFormKey? queriedHealth = null;
            var healthConditions = new FalloutDialogueConditions(records, quests, Key(0x900), speaker,
                healthPercentage: actor => { queriedHealth = actor; return .25f; });
            var healthCondition = new FalloutCondition(records.GetEffective(Key(0x860)), 0, .5f, 431, 0, 0, 0, 0);
            foreach (var (runOn, reference, expected) in new[]
            {
                (0u, 0u, Key(0x900)), (1u, 0u, Key(0x14)), (2u, 0x901u, Key(0x901))
            })
                Check(healthConditions.Evaluate(healthCondition with { RunOn = runOn, Reference = reference }) == .25f &&
                    queriedHealth == expected, "Dialogue health query selected the wrong actor context.");
            Reject(() => healthConditions.Evaluate(healthCondition with { RunOn = 2 }));
            Reject(() => healthConditions.Evaluate(healthCondition with { RunOn = 3 }));
            var actorConditions = new FalloutDialogueConditions(records, quests, Key(0x900), speaker,
                actorValue: (actor, value) => actor == Key(0x901) && value == 27 ? 62.5f : throw new InvalidDataException("Wrong actor/value."));
            Check(actorConditions.Evaluate(healthCondition with { Function = 14, RunOn = 2, Reference = 0x901, Argument1 = 27 }) == 62.5f,
                "Explicit-reference actor value did not reach its authoritative owner.");
            Reject(() => actorConditions.Evaluate(healthCondition with { Function = 14, RunOn = 2, Argument1 = 27 }));
            var listenerIdentity = FalloutDialogueSpeaker.Read(records, Key(0x804));
            var npcListener = new FalloutDialogueConditions(records, quests, Key(0x900), speaker,
                runtime: _ => throw new InvalidOperationException("NPC listener fell through to the player context."),
                healthPercentage: actor => actor == Key(0x901) ? .75f : throw new InvalidDataException("Wrong listener health owner."),
                actorValue: (actor, value) => actor == Key(0x901) && value == 27 ? 31 : throw new InvalidDataException("Wrong listener AV owner."),
                listener: Key(0x901), listenerIdentity: listenerIdentity);
            Check(npcListener.Evaluate(healthCondition with { RunOn = 1 }) == .75f &&
                npcListener.Evaluate(healthCondition with { Function = 14, RunOn = 1, Argument1 = 27 }) == 31 &&
                npcListener.Evaluate(healthCondition with { Function = 72, RunOn = 1, Argument1 = 0x804 }) == 1 &&
                npcListener.Evaluate(healthCondition with { Function = 72, RunOn = 1, Argument1 = 0x801 }) == 0,
                "NPC-directed speech selected the player or speaker as its listener.");
            Reject(() => npcListener.Evaluate(healthCondition with { Function = 999, RunOn = 1 }));
            var female = false;
            var playerConditions = new FalloutDialogueConditions(records, quests, Key(0x900), speaker, playerFemale: () => female);
            Check(playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 1, Argument1 = 7 }) == 1 &&
                playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 1, Argument1 = 0x804 }) == 0 &&
                playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 0, Argument1 = 0x801 }) == 1 &&
                playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 2, Reference = 0x901, Argument1 = 0x804 }) == 1 &&
                playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 2, Reference = 0x903, Argument1 = 0x844 }) == 1 &&
                playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 2, Reference = 0x14, Argument1 = 7 }) == 1,
                "Dialogue GetIsID lost player, speaker, listener or explicit source base ownership.");
            Reject(() => playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 2 }));
            Reject(() => playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 2, Reference = 0x840 }));
            Reject(() => playerConditions.Evaluate(healthCondition with { Function = 72, RunOn = 3, Argument1 = 7 }));
            var playerSex = healthCondition with { Function = 131, Argument1 = 0 };
            Check(playerConditions.Evaluate(playerSex) == 1 && playerConditions.Evaluate(playerSex with { Argument1 = 1 }) == 0,
                "GetPCIsSex read speaker identity instead of the shared player state.");
            female = true;
            Check(playerConditions.Evaluate(playerSex) == 0 && playerConditions.Evaluate(playerSex with { Argument1 = 1, RunOn = 2 }) == 1,
                "GetPCIsSex retained stale gender or changed ownership with the actor scope.");
            Reject(() => playerConditions.Evaluate(playerSex with { Argument1 = 2 }));
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => { }));
            ReferenceIdentityQueryContracts.Verify(records, world);
            Check(scripts.Activate(Key(0x904), Key(0x900)).Error is null && world.MapMarkerVisibility(Key(0x903)) == 0,
                "GetActionRef confused a non-player activator with the compiled Player reference.");
            Check(world.MapMarkerVisibility(Key(0x903)) == 0 && scripts.Activate(Key(0x904), Key(0x14)).Error is null &&
                world.MapMarkerVisibility(Key(0x903)) == 2 && world.MapMarkerVisibility(Key(0x905)) == 0,
                "ShowMap/query did not preserve ordered visibility, travel admission and marker isolation.");
            world.ShowMap(Key(0x903));
            Check(world.MapMarkerVisibility(Key(0x903)) == 2, "Revealing a known map marker revoked fast travel.");
            using (var markerRestore = new FalloutReferenceWorld(records))
            {
                markerRestore.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!);
                Check(markerRestore.MapMarkerVisibility(Key(0x903)) == 2 && markerRestore.MapMarkerVisibility(Key(0x905)) == 0,
                    "Map marker discovery did not survive a cold save.");
            }
            Reject(() => world.ShowMap(Key(0x900)));
            Check(!world.IsUnconscious(Key(0x900)), "Fresh creature acquired unconscious state.");
            scripts.Activate(Key(0x900), Key(0x14));
            Check(world.IsUnconscious(Key(0x900)), "Source unconscious command did not reach the reference owner.");
            using var restoredWorld = new FalloutReferenceWorld(records);
            restoredWorld.Restore([world.Get(Key(0x900)).Capture()]);
            Check(restoredWorld.IsUnconscious(Key(0x900)), "Unconscious state was lost on restoration.");
            scripts.Activate(Key(0x900), Key(0x14));
            Check(!world.IsUnconscious(Key(0x900)), "Source unconscious condition/clear did not execute.");
            Check(speaker.TraitsOwner == Key(0x800) && speaker.VoiceType == Key(0x850) && speaker.Race is null,
                "Creature dialogue did not inherit its own voice traits.");
            var info = FalloutDialogueTopic.Decode(records.GetEffective(Key(0x860)));
            var serviceInfo = info with { Conditions = [Condition(610, 4), Condition(71, 1)] };
            float Context(FalloutCondition condition) => condition.Function == 71 ? 0 : throw new NotSupportedException("Unrelated casino query.");
            Check(!FalloutDialogueTopic.Eligible(serviceInfo, speaker.Actor, new HashSet<FalloutFormKey>(), _ => 0, Context),
                "A false actor faction restriction evaluated another speaker's service dialogue.");
            Reject(() => FalloutDialogueTopic.Eligible(serviceInfo with { Conditions = [Condition(610, 4, 1), Condition(71, 1)] },
                speaker.Actor, new HashSet<FalloutFormKey>(), _ => 0, Context));
            string[] paths = ["sound/voice/Actors.esm/TestVoice/quest_topic_00000860_1.wav",
                "sound/voice/Actors.esm/OtherVoice/quest_topic_00000860_1.ogg",
                "sound/voice/Other.esm/TestVoice/quest_topic_00000860_1.ogg",
                "sound/voice/Actors.esm/TestVoice/quest_topic_00000860_2.ogg"];
            var binding = new FalloutDialogueVoiceIndex(paths).Resolve(speaker, info, 0);
            Check(binding.AudioPath == paths[0] && binding.WinningPlugin == "Patch.esp" && binding.LipPath.EndsWith("_1.lip"),
                "Voice crossed plugin/speaker/response ownership or rejected WAV.");
            Reject(() => new FalloutDialogueVoiceIndex(paths.Skip(1)).Resolve(speaker, info, 0));
            Reject(() => new FalloutDialogueVoiceIndex(paths.Append(paths[0].Replace(".wav", ".ogg"))).Resolve(speaker, info, 0));
            Reject(() => new FalloutDialogueVoiceIndex(paths).Resolve(speaker, info with { Speaker = Key(0x800) }, 0));
            var clock = new FalloutActorAnimationState(); var hash = new string('a', 64);
            clock.Bind("meshes/creatures/test/mtidle.kf", hash); clock.Advance(.73);
            var snapshot = JsonSerializer.Deserialize<FalloutActorAnimationSnapshot>(JsonSerializer.Serialize(clock.Capture()))!;
            var restored = new FalloutActorAnimationState(); restored.Restore(snapshot);
            restored.Advance(.41); clock.Advance(.41);
            Check(clock.Capture() == restored.Capture() && !restored.StartPending, "Cold actor clock changed or replayed start events.");
            Reject(() => restored.Bind(snapshot.Resource, new string('b', 64)));
            Reject(() => restored.Restore(snapshot with { ElapsedSeconds = double.NaN }));
            Reject(() => restored.Restore(snapshot with { StartPending = true }));
            var firstIdle = new FalloutActorAnimationState(); firstIdle.Bind(snapshot.Resource, hash);
            var secondIdle = new FalloutActorAnimationState(); secondIdle.Bind(snapshot.Resource, hash);
            firstIdle.StartAmbientLoop(Key(0x900), 2.5); secondIdle.StartAmbientLoop(Key(0x901), 2.5);
            Check(firstIdle.ElapsedSeconds != secondIdle.ElapsedSeconds && firstIdle.ElapsedSeconds is >= 0 and < 2.5,
                "Independent creature idles entered in lockstep or outside their source loop.");
            var idleSave = firstIdle.Capture()!; var coldIdle = new FalloutActorAnimationState(); coldIdle.Restore(idleSave);
            coldIdle.StartAmbientLoop(Key(0x900), 2.5);
            Check(coldIdle.Capture() == idleSave, "Cold idle restarted or changed its persisted phase.");
            coldIdle.Change("meshes/characters/test/chairsit.kf", new string('c', 64));
            Check(coldIdle.ElapsedSeconds == 0 && coldIdle.StartPending, "An explicit base animation change kept its previous clip's clock.");
            coldIdle.StartAmbientLoop(Key(0x900), 7);
            var seatedSave = coldIdle.Capture()!; var seatedCold = new FalloutActorAnimationState(); seatedCold.Restore(seatedSave);
            seatedCold.StartAmbientLoop(Key(0x900), 7);
            Check(seatedCold.Capture() == seatedSave, "A changed furniture loop lost its own persisted phase.");
            Console.WriteLine("OPENNV_ACTOR_SOURCE_PASS templates=independent voice=speaker-info-response override=original-identity ambiguous=rejected clock=cold-exact dialogueIdentity=player-speaker-listener-explicit scriptIdentity=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void RandomDialogueContracts(string directory)
    {
        var path = Path.Combine(directory, "Random.esm");
        byte[] Info(uint id, byte flags, bool conditional = false)
        {
            var condition = Condition(59, 1);
            return Record("INFO", id, Field("DATA", [1, 0, flags, 0]), Field("QSTI", BitConverter.GetBytes(0x40u)),
                Field("TRDT", Response()), Field("NAM1", Text("Synthetic source line")),
                conditional ? Field("CTDA", condition) : []);
        }
        File.WriteAllBytes(path, Join(Header(), Record("QUST", 0x40, Field("EDID", Text("SyntheticQuest"))),
            Record("DIAL", 0x100, Field("EDID", Text("SyntheticRandom"))),
            Group(7, 0x100, Join(Info(0x101, 4), Info(0x102, 2, true), Info(0x103, 2), Info(0x104, 34), Info(0x105, 34))),
            Record("DIAL", 0x200, Field("EDID", Text("SyntheticBoundary"))),
            Group(7, 0x200, Join(Info(0x201, 2), Info(0x202, 0, true), Info(0x203, 34))),
            Record("DIAL", 0x300, Field("EDID", Text("SyntheticUnknownFlag"))), Group(7, 0x300, Info(0x301, 8))));
        using var records = FalloutPluginStack.Load(directory, ["Random.esm"]);
        FalloutFormKey Key(uint id) => new("Random.esm", id);
        var topic = FalloutDialogueTopic.Read(records, Key(0x100));
        var said = new HashSet<FalloutFormKey>();
        var draws = 0;
        uint ChooseLast(uint count) { ++draws; Check(count == 2, "Random End lost its eligible pool boundary."); return count - 1; }
        Check(topic.Select(Key(0x14), said, _ => 0, _ => 0, random: ChooseLast)!.Record.FormKey == Key(0x101) && draws == 0,
            "Nonrandom first response consumed a random draw or lost SayOnce precedence.");
        said.Add(Key(0x101));
        Check(topic.Select(Key(0x14), said, _ => 0, _ => 0, random: ChooseLast)!.Record.FormKey == Key(0x104) && draws == 1,
            "Random selection crossed the first eligible Random End or admitted a false condition.");
        Check(topic.Select(Key(0x14), said, _ => 0, _ => 0, random: _ => 0)!.Record.FormKey == Key(0x103),
            "Random selection lost a valid earlier source response.");
        Reject(() => topic.Select(Key(0x14), said, _ => 0, _ => 0));
        Reject(() => topic.Select(Key(0x14), said, _ => 0, _ => 0, random: count => count));
        var boundary = FalloutDialogueTopic.Read(records, Key(0x200));
        Check(boundary.Select(Key(0x14), said, _ => 0, _ => 1, random: count =>
        {
            Check(count == 1, "An eligible nonrandom INFO entered the random pool."); return 0;
        })!.Record.FormKey == Key(0x201), "Nonrandom source boundary did not close the prior pool.");
        Check(boundary.Select(Key(0x14), said, _ => 0, _ => 0, random: count =>
        {
            Check(count == 2, "An ineligible INFO incorrectly ended the pool."); return 1;
        })!.Record.FormKey == Key(0x203), "Skipped source conditions broke the later random boundary.");
        Reject(() => FalloutDialogueTopic.Read(records, Key(0x300)).Select(Key(0x14), said, _ => 0, random: _ => 0));
        var values = new FalloutScriptValueStore(); _ = values.RandomBounded(7);
        var cold = new FalloutScriptValueStore(); cold.Restore(values.Capture());
        Check(Enumerable.Range(0, 32).All(_ => values.RandomBounded(5) == cold.RandomBounded(5)), "Dialogue selection lost the shared RNG's cold sequence.");
        Console.WriteLine("OPENNV_DIALOGUE_RANDOM_CONTRACT_PASS sourceOrder=true conditions=true sayOnce=true boundaries=true invalidRng=true unknownFlags=true coldRandom=true retailSequence=unverified");
    }

    private static byte[] Creature(uint id, ushort flags, uint template, string skeleton, string part, float scale, params byte[][] extra)
    {
        var acbs = new byte[24]; BinaryPrimitives.WriteUInt16LittleEndian(acbs.AsSpan(22), flags);
        return Record("CREA", id, Field("ACBS", acbs), Field("TPLT", BitConverter.GetBytes(template)), Field("MODL", Text(skeleton)),
            Field("NIFZ", Text(part)), Field("BNAM", BitConverter.GetBytes(scale)), Field("VTCK", BitConverter.GetBytes(0x850u)),
            Field("SCRI", BitConverter.GetBytes(0x890u)), Join(extra));
    }
    private static byte[] NavigationFloor(uint id, uint cell, float height)
    {
        var triangle = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(4), 2);
        for (var edge = 0; edge < 3; edge++) BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(6 + edge * 2), -1);
        return Record("NAVM", id, Field("NVER", BitConverter.GetBytes(11u)),
            Field("DATA", new uint[] { cell, 3, 1, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray()),
            Field("NVVX", new float[] { -100, -100, height, 200, -100, height, -100, 200, height }.SelectMany(BitConverter.GetBytes).ToArray()),
            Field("NVTR", triangle));
    }
    private static byte[] ImmobileCreature(uint id)
    {
        var record = Creature(id, 0, 0, "creatures/test/skeleton.nif", "body.nif", 1);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(30), 0x00800000);
        return record;
    }
    private static byte[] ActorList(uint id, byte chance, params (ushort Level, uint Actor)[] entries) =>
        Record("LVLC", id, Field("LVLD", [chance]), Field("LVLF", [0]), Join(entries.Select(entry =>
        {
            var data = new byte[12]; BinaryPrimitives.WriteUInt16LittleEndian(data, entry.Level);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), entry.Actor);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 1); return Field("LVLO", data);
        }).ToArray()));
    private static byte[] Cell(uint id, byte[] reference)
    {
        var group = new byte[24 + reference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id);
        BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); reference.CopyTo(group, 24);
        return Join(Record("CELL", id, Field("EDID", Text("ActorCell")), Field("DATA", [1])), group);
    }
    private static byte[] Group(int type, uint label, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), label);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), type); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Marker(uint id, string name) => Record("REFR", id, Field("EDID", Text(name)),
        Field("NAME", BitConverter.GetBytes(0x844u)), Field("DATA", new byte[24]), Field("XMRK", []),
        Field("FNAM", [0]), Field("TNAM", [5, 0xa7]), Field("FULL", Text("Source location")));
    private static byte[] Response() { var bytes = new byte[24]; bytes[12] = 1; return bytes; }
    private static byte[] Condition(ushort function, float comparison, byte flags = 0)
    {
        var bytes = new byte[28]; bytes[0] = flags;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), comparison);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), function); return bytes;
    }
    private static byte[] Header(string? master = null)
    {
        var hedr = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(hedr, 1.34f);
        return master is null ? Record("TES4", 0, Field("HEDR", hedr)) : Record("TES4", 0,
            Field("HEDR", hedr), Field("MAST", Text(master)), Field("DATA", new byte[8]));
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported actor source binding was accepted.");
    }
}
