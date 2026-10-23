using System.Reflection;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private static void PlayerNoteAdapters()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-note-adapters-");
        var driver = new RuntimeNativeOpeningStageDriver();
        var player = new RuntimeNativePlayer();
        var speech = new RuntimeNativeSpeech();
        var conversation = new RuntimeNativeConversation();
        var npc = new RuntimeNativeNpc();
        var creature = new RuntimeNativeCreature();
        NativeOwnedPipBoyMenu? pipBoy = null;
        try
        {
            var bytes = new byte[28];
            byte[] RemovalScript(uint id, string command, bool bindPlayer = true)
            {
                var header = new byte[20]; header[16] = 1;
                return Record("SCPT", id, Field("SCHR", header),
                    Field("SCRO", BitConverter.GetBytes(bindPlayer ? 0x14u : 4u)),
                    Field("SCRO", BitConverter.GetBytes(4u)), Field("SCRO", BitConverter.GetBytes(2u)),
                    Field("SCTX", Encoding.ASCII.GetBytes("begin GameMode\n" + command + "\nend\0")));
            }
            BitConverter.GetBytes((ushort)382).CopyTo(bytes, 8);
            BitConverter.GetBytes(1u).CopyTo(bytes, 12);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Contact.esm"),
                Record("TES4", 0, Field("HEDR", new byte[12]))
                    .Concat(Record("NOTE", 1, Field("EDID", Encoding.ASCII.GetBytes("FixtureNote\0")), Field("FULL", Encoding.ASCII.GetBytes("First\0"))))
                    .Concat(Record("NOTE", 4, Field("EDID", Encoding.ASCII.GetBytes("OtherNote\0")), Field("FULL", Encoding.ASCII.GetBytes("Second\0"))))
                    .Concat(Record("NOTE", 6, Field("EDID", Encoding.ASCII.GetBytes("ThirdNote\0")), Field("FULL", Encoding.ASCII.GetBytes("Second\0"))))
                    .Concat(Record("GMST", 7, Field("EDID", Encoding.ASCII.GetBytes("sComputersAddedNote\0")), Field("DATA", Encoding.ASCII.GetBytes("Note Added: %s\0"))))
                    .Concat(Record("ARMO", 5, Field("EDID", Encoding.ASCII.GetBytes("PipBoy\0"))))
                    .Concat(Record("GLOB", 2, Field("EDID", Encoding.ASCII.GetBytes("FixtureGlobal\0")),
                        Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(7f))))
                    .Concat(Record("INFO", 3, Field("CTDA", bytes)))
                    .Concat(Record("QUST", 8, Field("EDID", Encoding.ASCII.GetBytes("NoteCommandOwner\0")), Field("DATA", [0, 0])))
                    .Concat(RemovalScript(9, "player.RemoveNote OtherNote"))
                    .Concat(RemovalScript(10, "FixtureGlobal.RemoveNote OtherNote"))
                    .Concat(RemovalScript(11, "player.RemoveNote FixtureGlobal"))
                    .Concat(RemovalScript(12, "player.RemoveNote OtherNote", bindPlayer: false)).ToArray());
            using var records = FalloutPluginStack.Load(directory.FullName, ["Contact.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var inventory = new FalloutPlayerInventory();
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), inventory,
                defaultProcessingDelay: .1f, references: world);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = typeof(RuntimeNativeOpeningStageDriver);
            void Bind(string name, object value) => type.GetField(name, flags)!.SetValue(driver, value);
            Bind("_scripts", scripts); Bind("_pluginStack", records); Bind("_quests", quests); Bind("_inventory", inventory);
            var globals = new FalloutGlobalState(records.EffectiveRecords("GLOB").Select(FalloutGlobal.Read));
            Bind("_globals", globals);
            Bind("_playerSkills", new FalloutPlayerSkills(records,
                () => throw new InvalidOperationException("Note query unexpectedly read SPECIAL."), _ => false, () => [],
                globals, inventory, records.RuntimeFormKey(0x14),
                () => throw new InvalidOperationException("Note query unexpectedly read race."), () => false));
            Func<FalloutCondition, float> Context(string name) =>
                (Func<FalloutCondition, float>)type.GetMethod(name, flags)!.Invoke(driver, null)!;
            speech.Configure(records, RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip,
                quest => quests.Stage(quest), Context("SpeechConditionContext"), quests: quests, references: world);
            conversation.Configure(records, quests, player, speech, Context("ConversationConditionContext"),
                (_, _, _) => throw new InvalidOperationException("Unexpected result during note condition audit."));
            typeof(RuntimeNativeNpc).GetField("_aiWorld", flags)!.SetValue(npc, world);
            typeof(RuntimeNativeCreature).GetField("_aiWorld", flags)!.SetValue(creature, world);
            var unloaded = new FalloutUnloadedActorPackages(records, world, quests, null, null,
                (_, _) => throw new InvalidOperationException("Unexpected package dispatch."), () => 1);
            var unloadedQuery = typeof(FalloutUnloadedActorPackages).GetMethod("Evaluate", flags)!
                .CreateDelegate<Func<FalloutFormKey, FalloutCondition, float>>(unloaded);
            var adapters = new[]
            {
                (Name: "conversation", Query: (Func<FalloutCondition, float>)typeof(RuntimeNativeConversation)
                    .GetField("_runtimeConditions", flags)!.GetValue(conversation)!),
                (Name: "speech", Query: (Func<FalloutCondition, float>)typeof(RuntimeNativeSpeech)
                    .GetField("_conditionContext", flags)!.GetValue(speech)!),
                (Name: "recipe", Query: type.GetMethod("EvaluateRecipeCondition", flags)!
                    .CreateDelegate<Func<FalloutCondition, float>>(driver)),
                (Name: "npc", Query: (Func<FalloutCondition, float>)npc.EvaluateAiCondition),
                (Name: "creature", Query: (Func<FalloutCondition, float>)creature.PackageCondition),
                (Name: "unloaded", Query: (Func<FalloutCondition, float>)(value => unloadedQuery(records.RuntimeFormKey(0x14), value)))
            };
            var condition = FalloutCondition.Read(records.GetEffective(Key(3))).Single();
            var identity = new FalloutDialogueSpeaker(Key(0x20), Key(0x20), Key(0x21), "fixture", null, false);
            foreach (var (name, query) in adapters)
            {
                world.PlayerNotes.Restore(new([]));
                Require(query(condition) == 0, $"Actual {name} adapter lost fresh empty membership.");
                world.PlayerNotes.AddMembership(Key(1));
                var before = JsonSerializer.Serialize(world.PlayerNotes.Capture());
                var dialogue = new FalloutDialogueConditions(records, quests, Key(0x20), identity, query);
                foreach (var runOn in new uint[] { 0, 1, 2 })
                    Require(dialogue.Evaluate(condition with { RunOn = runOn }) == 1,
                        $"Actual {name} adapter did not read collected notes independently of actor context.");
                Require(inventory.Item(Key(1)) is null, "Note query fabricated an inventory item.");
                world.PlayerNotes.Restore(JsonSerializer.Deserialize<FalloutPlayerNotesSnapshot>(before));
                Require(query(condition) == 1, "Native adapter lost cold-restored membership.");
                RejectReputationAdapter(() => query(condition with { Argument1 = 2 }));
                RejectReputationAdapter(() => query(condition with { RunOn = 3 }));
                RejectReputationAdapter(() => query(condition with { RunOn = 2, Reference = 2 }));
                Require(JsonSerializer.Serialize(world.PlayerNotes.Capture()) == before, "Refused native query mutated membership.");
                world.PlayerNotes.Restore(null);
                RejectReputationAdapter(() => query(condition));
            }
            world.PlayerNotes.Restore(new([Key(1)]));
            var pipBoyState = new FalloutPipBoyState(records, inventory);
            Bind("_pipBoy", pipBoyState);
            pipBoy = new(records, pipBoyState, inventory, quests, world,
                () => throw new InvalidOperationException("Unexpected vitals query."),
                () => throw new InvalidOperationException("Unexpected SPECIAL query."), "fixture", [], [], [], null, Vector3.Zero, 0);
            IReadOnlyList<FalloutFormKey> UiNotes() => (IReadOnlyList<FalloutFormKey>)typeof(NativeOwnedPipBoyMenu)
                .GetProperty("CollectedNotes", flags)!.GetValue(pipBoy)!;
            Require(UiNotes().SequenceEqual(new[] { Key(1) }) && inventory.Items.Count == 0,
                "Actual Pip-Boy membership binding still derived notes from inventory.");
            driver.ApplyReferenceEffect(new(FalloutReferenceEffectKind.AddNote, Key(0x14), Key(1)));
            Require(inventory.Notifications.Capture().Pending.Count == 0, "Duplicate AddNote replayed feedback.");
            driver.ApplyReferenceEffect(new(FalloutReferenceEffectKind.AddNote, Key(0x14), Key(4)));
            Require(world.PlayerNotes.Contains(Key(4)) && inventory.Notifications.Capture().Pending is
                [{ Event.Kind: FalloutHudEventKind.NoteAdded, Event.Text: "Note Added: Second" }],
                "Real AddNote effect did not publish collected membership and its source-formatted notice.");
            var commands = new FalloutInventoryCommands(records, world, inventory, () => 1);
            commands.Execute(new(FalloutInventoryCommandKind.Add, records.RuntimeFormKey(0x14), Key(1), Count: 2));
            commands.Execute(new(FalloutInventoryCommandKind.Add, records.RuntimeFormKey(0x14), Key(6), Count: 2));
            Require(inventory.Items.Count == 0 && world.PlayerNotes.Notes.SequenceEqual(new[] { Key(6), Key(4), Key(1) }) &&
                inventory.Notifications.Capture().Pending.Count == 1,
                "Acquisition counted NOTE items or failed identical-text feedback deduplication.");
            var notifications = JsonSerializer.Serialize(inventory.Notifications.Capture());
            void ExecuteQualifiedRemoval(uint scriptId)
            {
                var script = records.GetEffective(Key(scriptId));
                var program = FalloutGameModeProgram.Read(FalloutDialogueTopic.ScriptText(
                    script.ReadSubrecords().Single(field => field.Signature == "SCTX").Data.Span));
                var executor = new FalloutReferenceScripts(records, world, quests,
                    RuntimeCoordinator.CreateNativeReferenceEventHost(new((_, _) => false,
                        _ => throw new InvalidOperationException("Unexpected default effect in note removal.")), driver));
                executor.ExecuteProgram(records.GetEffective(Key(8)), script, program, 0);
            }
            // Retain an existing Node identity in the playback-owner slot. This
            // tests noninterference, not actual voice playback or audio parity.
            Bind("_pipBoyNoteOwner", speech);
            pipBoyState.SelectNote(Key(4));
            var revision = pipBoyState.Revision;
            driver.ApplyReferenceEffect(new(FalloutReferenceEffectKind.RemoveNote, Key(0x14), Key(1)));
            Require(pipBoyState.SelectedNote == Key(4) && pipBoyState.Revision == revision &&
                !world.PlayerNotes.Contains(Key(1)), "Unrelated removal invalidated the selected note.");
            world.PlayerNotes.Restore(new([Key(4), Key(6), Key(4)]));
            ExecuteQualifiedRemoval(9);
            Require(pipBoyState.SelectedNote is null && pipBoyState.NoteSelectionInitialized &&
                pipBoyState.Revision == revision + 1 && world.PlayerNotes.Notes.SequenceEqual(new[] { Key(6), Key(4) }) &&
                UiNotes().SequenceEqual(world.PlayerNotes.Notes), "Native removal did not clear selection before first-entry removal.");
            Require(type.GetField("_pipBoyNoteOwner", flags)!.GetValue(driver) == speech &&
                JsonSerializer.Serialize(inventory.Notifications.Capture()) == notifications,
                "Removal touched playback ownership or invented an ItemRemoved HUD notice.");
            world.PlayerNotes.Restore(new([])); pipBoyState.SelectNote(Key(4));
            driver.ApplyReferenceEffect(new(FalloutReferenceEffectKind.RemoveNote, Key(0x14), Key(4)));
            Require(pipBoyState.SelectedNote is null, "Absent membership failed to invalidate its matching selection.");
            pipBoyState.SelectNote(Key(6));
            pipBoy.Free(); pipBoy = null;
            revision = pipBoyState.Revision;
            driver.ApplyReferenceEffect(new(FalloutReferenceEffectKind.RemoveNote, Key(0x14), Key(6)));
            Require(pipBoyState.SelectedNote is null && !pipBoyState.NoteSelectionInitialized && pipBoyState.Revision == revision,
                "Retired menu retained selection or was invalidated as a live owner.");
            pipBoy = new(records, pipBoyState, inventory, quests, world,
                () => throw new InvalidOperationException("Unexpected vitals query."),
                () => throw new InvalidOperationException("Unexpected SPECIAL query."), "fixture", [], [], [], null, Vector3.Zero, 0);
            Require(!pipBoyState.NoteSelectionInitialized, "A new menu inherited the retired menu's cleared selection.");
            pipBoyState.SelectNote(Key(6));
            var replaced = pipBoy;
            pipBoy = new(records, pipBoyState, inventory, quests, world,
                () => throw new InvalidOperationException("Unexpected vitals query."),
                () => throw new InvalidOperationException("Unexpected SPECIAL query."), "fixture", [], [], [], null, Vector3.Zero, 0);
            pipBoyState.SelectNote(Key(4)); replaced.Free();
            Require(pipBoyState.SelectedNote == Key(4), "Retiring a previous menu cleared the replacement owner's selection.");
            foreach (var invalidScript in new uint[] { 10, 11, 12 })
            {
                RejectReputationAdapter(() => ExecuteQualifiedRemoval(invalidScript));
                Require(pipBoyState.SelectedNote == Key(4), "Bad compiled receiver/NOTE cleared actual menu selection.");
            }
            RejectReputationAdapter(() => driver.ApplyReferenceEffect(new(FalloutReferenceEffectKind.RemoveNote, Key(0x14), Key(2))));
            Require(pipBoyState.SelectedNote == Key(4), "Invalid NOTE type cleared UI state before admission.");
            pipBoyState.Reset();
            Require(pipBoyState.SelectedNote == Key(4), "Navigation reset changed a live menu's independent note selection.");
            pipBoyState.SelectNote(Key(6)); world.PlayerNotes.Restore(null);
            RejectReputationAdapter(() => driver.ApplyReferenceEffect(new(FalloutReferenceEffectKind.RemoveNote, Key(0x14), Key(6))));
            Require(pipBoyState.SelectedNote == Key(6), "Unknown history partially cleared selected-note state.");
            Console.WriteLine("OPENNV_PLAYER_NOTE_ADAPTER_PASS conversation=true speech=true recipe=true npc=true creature=true " +
                "unloaded=true pipBoyMembership=true cold=true unknownRefused=true addNoteAndAddItem=true duplicateFeedbackSuppressed=true " +
                "removeNote=true qualifiedRemovalThroughNativeHost=true invalidReceiverAndNote=true exactSelectionInvalidation=true retiredAndReopened=true playbackOwnerUnchanged=true placedPickup=unadmitted pixels=unverified campaign=false");
        }
        finally
        {
            pipBoy?.Free(); creature.Free(); npc.Free(); conversation.Free(); speech.Free(); player.Free(); driver.Free();
            File.Delete(Path.Combine(directory.FullName, "Contact.esm")); directory.Delete();
        }
    }
}
