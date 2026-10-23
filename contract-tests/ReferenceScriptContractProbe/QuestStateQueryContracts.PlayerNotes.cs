using System.Text.Json;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class QuestStateQueryContracts
{
    internal static void RunOwnedNotes(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "vDialogueEDE");
        var script = FalloutScriptLocals.AttachedScript(records, quest) ?? throw new InvalidDataException("Owned ED-E script is absent.");
        var note = FalloutDialogueTopic.Find(records, "NOTE", "HVPatrolNote01");
        var sourceHash = SHA256.HashData(script.ReadData());
        var locals = FalloutScriptLocals.Read(script);
        Require(script.ReadSubrecords().Where(field => field.Signature == "SCRO")
            .Any(field => script.Plugin.AdjustFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) == note.FormKey),
            "Actual owned NOTE is not a compiled reference of the source script.");
        foreach (var shared in new[] { false, true })
            foreach (var present in new[] { false, true })
            {
                using var world = new OpenNV.Runtime.World.Cells.FalloutReferenceWorld(records);
                var quests = new FalloutQuestState(records);
                var globals = new FalloutGlobalState(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
                var claims = records.EffectiveRecords("QUST").Select(value => value.FormKey).ToHashSet();
                var scripts = new FalloutQuestScripts(records, quests, claims, new FalloutPlayerInventory(), globals,
                    defaultProcessingDelay: 0, references: world);
                scripts.RequireQuestExecution(quest.FormKey);
                // Explicit isolated preconditions. No campaign save, acquisition
                // callback, source program replacement or recruitment is used.
                quests.SetRunning(quest.FormKey, true);
                quests.SetVariable(quest.FormKey, locals["iAprilDead"], 1);
                quests.EnterStage(quest.FormKey, 100); quests.Complete(quest.FormKey);
                quests.ApplyObjective(quest.FormKey, 10, display: true, value: true);
                if (present) world.PlayerNotes.AddMembership(note.FormKey);
                var changes = 0;
                quests.ObjectiveChanged += _ => changes++;
                FalloutQuestScriptHost host = new((_, _) => throw new InvalidDataException("Unexpected owned stage effect."), _ => 0);
                if (shared)
                {
                    var executor = new OpenNV.Runtime.World.Cells.FalloutReferenceScripts(records, world, quests,
                        new((_, _) => false, _ => throw new InvalidDataException("Unexpected owned reference effect."), Globals: globals));
                    host = host with { ExecuteProgram = executor.ExecuteProgram };
                }
                scripts.AdvanceClaimed(quest.FormKey, 0, host);
                Require(scripts.Capture().Instances.Single(value => value.Quest == quest.FormKey).Error is null &&
                    quests.Variable(quest.FormKey, locals["iCounter"]) == 1, "Owned note query refused or lost its source prefix.");
                Require(quests.ObjectiveCompleted(quest.FormKey, 10) == present &&
                    quests.ObjectiveCompleted(quest.FormKey, 12) == present &&
                    quests.ObjectiveCompleted(quest.FormKey, 13) == present &&
                    quests.ObjectiveDisplayed(quest.FormKey, 15) == present && changes == (present ? 6 : 0),
                    "Actual original GameMode did not select the authored objective branch from collected-note membership.");
                var beforeObjectives = JsonSerializer.Serialize(quests.Capture().Single(value => value.Quest == quest.FormKey).Objectives);
                using var coldWorld = new OpenNV.Runtime.World.Cells.FalloutReferenceWorld(records);
                coldWorld.PlayerNotes.Restore(RoundTrip(world.PlayerNotes.Capture()));
                var coldQuests = new FalloutQuestState(records);
                coldQuests.Restore(RoundTrip(quests.Capture().ToArray()));
                var coldScripts = new FalloutQuestScripts(records, coldQuests, claims, new FalloutPlayerInventory(), globals,
                    defaultProcessingDelay: 0, references: coldWorld);
                coldScripts.RequireQuestExecution(quest.FormKey);
                coldScripts.Restore(RoundTrip(scripts.Capture()));
                Require(JsonSerializer.Serialize(coldScripts.Capture()) == JsonSerializer.Serialize(scripts.Capture()) &&
                    JsonSerializer.Serialize(coldQuests.Capture()) == JsonSerializer.Serialize(quests.Capture()),
                    "Owned cold reconstruction changed the script or objective prefix.");
                var coldExecutor = new OpenNV.Runtime.World.Cells.FalloutReferenceScripts(records, coldWorld, coldQuests,
                    new((_, _) => false, _ => throw new InvalidDataException("Unexpected cold effect."), Globals: globals));
                var coldHost = shared ? host with { ExecuteProgram = coldExecutor.ExecuteProgram } : host;
                coldScripts.AdvanceClaimed(quest.FormKey, 0, coldHost);
                Require(coldScripts.Capture().Instances.Single(value => value.Quest == quest.FormKey).Error is null &&
                    JsonSerializer.Serialize(coldQuests.Capture().Single(value => value.Quest == quest.FormKey).Objectives) == beforeObjectives,
                    "Owned cold tick replayed completed objective effects or lost note state.");
            }
        Require(SHA256.HashData(script.ReadData()).SequenceEqual(sourceHash), "Owned script bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_PLAYER_NOTE_QUERY_PASS quest={quest.FormKey} note={note.FormKey} " +
            $"sourceSha256={Convert.ToHexString(sourceHash)} fullOriginalGameMode=true fallbackAndShared=true " +
            "absentAndPresent=true authoredObjectiveChanges=true cold=true controlledFixtureInputs=true " +
            "nativeAcquisition=false ordinaryRecruitment=false campaign=false retailParity=unverified");
    }

    internal static void RunNotes(string? selected = null)
    {
        foreach (var shared in selected == "fallback" ? new[] { false } : selected == "shared" ? new[] { true } : new[] { false, true })
        {
            foreach (var acquire in new[] { "AddNote CollectedNote", "player.AddItem CollectedNote 2 1" })
            {
                using var acquisition = new Fixture("set prefix to prefix + 1\n" + acquire +
                    "\nset result to GetHasNote CollectedNote\nset suffix to suffix + 1", shared, allowNoteEffects: true);
                acquisition.Tick(); acquisition.Expect(1, 1);
                acquisition.Tick(); acquisition.Expect(1, 2);
                Require(acquisition.World.PlayerNotes.Notes.Count == 1 && acquisition.Inventory.Items.Count == 0 &&
                    acquisition.Inventory.Notifications.Capture().Pending.Count == 1,
                    "Both command executors must acquire one note and suppress duplicate feedback independently of inventory/count/silent.");
            }
            using var zero = new Fixture("set prefix to 1\nplayer.AddItem CollectedNote 0\nset suffix to 1", shared, allowNoteEffects: true);
            zero.World.PlayerNotes.Restore(null);
            zero.Tick();
            Require(zero.Error is null && zero.Value(1) == 1 && zero.Value(3) == 1 && !zero.World.PlayerNotes.Known &&
                zero.Inventory.Items.Count == 0 && zero.Inventory.Notifications.Capture().Pending.Count == 0,
                "Zero-count NOTE acquisition resolved unknown membership or emitted an effect.");
            using var fixture = new Fixture("set prefix to prefix + 1\nset result to GetHasNote CollectedNote\nset suffix to suffix + 1", shared);
            fixture.Tick(); fixture.Expect(0, 1);
            fixture.World.PlayerNotes.AddMembership(fixture.Note);
            var revision = fixture.World.PlayerNotes.Revision;
            fixture.Tick(); fixture.Expect(1, 2);
            Require(fixture.World.PlayerNotes.Revision == revision, "GetHasNote mutated collected-note state.");
            fixture.World.PlayerNotes.RemoveMembership(fixture.Note);
            fixture.Tick(); fixture.Expect(0, 3);
            fixture.World.PlayerNotes.AddMembership(fixture.Note);
            fixture.Tick(); fixture.Expect(1, 4);
            using var cold = new Fixture(fixture.Body, shared, initialize: false);
            cold.World.PlayerNotes.Restore(RoundTrip(fixture.World.PlayerNotes.Capture()));
            cold.Quests.Restore(RoundTrip(fixture.Quests.Capture().ToArray()));
            cold.Scripts.Restore(RoundTrip(fixture.Scripts.Capture()));
            cold.Tick(); cold.Expect(1, 5);
            cold.World.PlayerNotes.Restore(null);
            cold.Tick();
            Require(cold.Error?.Contains("no authoritative collected-note membership", StringComparison.Ordinal) == true &&
                cold.Value(1) == 6 && cold.Value(3) == 5, "Unknown history supplied a false answer or ran the suffix.");
            var error = cold.Error;
            cold.World.PlayerNotes.Restore(new([]));
            cold.Tick();
            Require(cold.Error == error && cold.Value(1) == 6 && cold.Value(3) == 5,
                "Newly known history cleared a latched query failure or replayed its prefix.");
            using var failedCold = new Fixture(cold.Body, shared, initialize: false);
            failedCold.World.PlayerNotes.Restore(RoundTrip(cold.World.PlayerNotes.Capture()));
            failedCold.Quests.Restore(RoundTrip(cold.Quests.Capture().ToArray()));
            failedCold.Scripts.Restore(RoundTrip(cold.Scripts.Capture()));
            failedCold.Tick();
            Require(failedCold.Error == error && failedCold.Value(1) == 6 && failedCold.Value(3) == 5,
                "Cold admission cleared a note-query fault or replayed its prefix.");
            using var skipped = new Fixture("if 0 && GetHasNote CollectedNote\nset result to 99\nendif\n" +
                "if 1 || GetHasNote MissingNote\nset result to 7\nendif\nset suffix to 1", shared);
            skipped.World.PlayerNotes.Restore(null);
            skipped.Tick();
            Require(skipped.Error is null && skipped.Value(2) == 7 && skipped.Value(3) == 1,
                "An unselected note query resolved unknown history or an unbound NOTE.");
            foreach (var target in new[] { "BadGlobal", "TargetQuest", "MissingNote", "", "CollectedNote 1" })
                Refusal("GetHasNote", target, shared);
            Refusal("GetHasNote", "CollectedNote", shared, bindNote: false);
            using var objective = new Fixture("set prefix to 1\nSetObjectiveDisplayed TargetQuest 10 1\n" +
                "SetObjectiveCompleted TargetQuest 10 1\nset suffix to 1", shared);
            objective.Tick();
            Require(objective.Error is null && objective.Quests.ObjectiveDisplayed(objective.Target, 10) &&
                objective.Quests.ObjectiveCompleted(objective.Target, 10) && objective.Value(3) == 1,
                "Objective commands did not use the existing shared state.");
            foreach (var command in new[] { "SetObjectiveCompleted TargetQuest -1 1", "SetObjectiveDisplayed TargetQuest .5 1",
                "SetObjectiveCompleted TargetQuest 4294967296 1", "SetObjectiveDisplayed TargetQuest 10 2", "SetObjectiveCompleted BadGlobal 10 1" })
            {
                using var invalid = new Fixture("set prefix to 1\n" + command + "\nset suffix to 1", shared);
                invalid.Tick();
                Require(invalid.Error is not null && invalid.Value(1) == 1 && invalid.Value(3) == 0,
                    "Invalid objective command hid a refusal or ran its suffix.");
            }

            var condition = new FalloutCondition(fixture.Records.GetEffective(fixture.Target), 0, 1, 382, 0x400, 0, 0, 0);
            foreach (var runOn in new uint[] { 0, 1, 2 })
                Require(fixture.World.EvaluatePlayerNoteCondition(condition with { RunOn = runOn }) == 1,
                    "CTDA note membership depended on the supplied actor context.");
            Require(fixture.World.EvaluatePlayerNoteCondition(condition with { Function = 383 }) is null,
                "Note condition owner consumed an unrelated condition.");
            var ownerState = JsonSerializer.Serialize(fixture.World.PlayerNotes.Capture());
            foreach (var bad in new[] { condition with { Argument1 = 0x300 }, condition with { RunOn = 3 },
                condition with { RunOn = 2, Reference = 0x300 }, condition with { RunOn = 2, Reference = 0xffff } })
                RejectNote(() => fixture.World.EvaluatePlayerNoteCondition(bad));
            Require(JsonSerializer.Serialize(fixture.World.PlayerNotes.Capture()) == ownerState, "Rejected CTDA mutated note membership.");
            var inventory = new FalloutPlayerInventory();
            Require(inventory.Item(fixture.Note) is null &&
                FalloutInventoryConditions.Evaluate(fixture.Records, inventory, _ => false, condition, fixture.World.PlayerNotes) == 1,
                "Collected-note CTDA still required a counted inventory item.");
            RejectNote(() => FalloutInventoryConditions.Evaluate(fixture.Records, inventory, _ => false, condition));
        }
        NoteSchema();
        Console.WriteLine($"OPENNV_PLAYER_NOTE_QUERY_PASS executors={selected ?? "fallback-and-shared"} compiled=true " +
            "cold=true unknownRefused=true prefixRetained=true ctda=true placedAcquisition=unadmitted gameplay=unverified");
    }

    private static void NoteSchema()
    {
        // This fixture exercises the actual schema admission owner, independently
        // of unrelated campaign fields validated by the complete save contracts.
        var prior = JsonSerializer.Deserialize<FalloutNativeCampaignState>(
            "{\"Schema\":\"opennv-native-fnv-campaign-save/v58\",\"References\":[],\"Reputations\":{\"FreshBaseline\":true,\"Values\":[]}}")!;
        var upgraded = FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(prior);
        Require(upgraded.Schema == FalloutNativeCampaignSave.ExpectedSchema && upgraded.PlayerNotes is null &&
            upgraded.Reputations?.FreshBaseline == true, "Legacy admission invented notes or lost another owner.");
        RejectNote(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(prior with { PlayerNotes = new([]) }));
        var unknown = upgraded with { PlayerNotes = new([], Known: false) };
        var cold = RoundTrip(unknown);
        Require(FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(cold).PlayerNotes is { Known: false, Notes.Count: 0 },
            "Campaign JSON/resave converted unknown note history to known empty.");
        RejectNote(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(unknown with
        { PlayerNotes = new([new("QuestQueries.esm", 0x400)], Known: false) }));
        RejectNote(() => FalloutNativeCampaignSave.AdmitPreviousOwnerSchema(unknown with
        { PlayerNotes = new([new("", 0x400)]) }));
    }

    private static void RejectNote(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Unowned note query or snapshot was accepted.");
    }
}
