using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    // Isolated composition, with explicit known note prerequisites. No campaign
    // save, physical activation, complete CELL presentation or retail claim.
    private async Task OwnedJukeboxComposition(string game)
    {
        RuntimeLiveContentSource.Configure(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var reference = records.GetEffective(new("OldWorldBlues.esm", 0x13573));
        var script = FalloutScriptLocals.AttachedScript(records, reference)!;
        var fields = script.ReadSubrecords().ToArray();
        static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        Require(Hash(script.ReadData()) == "B4AAF962DCA7516D4FEBBA2517255C645C67AA960A8FBE2F8C138FB7A1A35932" &&
            Hash(fields.Single(value => value.Signature == "SCTX").Data.Span) == "A3EF5E56BA8AB1E0D18C0820233E3C826466BDB267B70B75B004F171BA755947" &&
            Hash(fields.Single(value => value.Signature == "SCDA").Data.Span) == "55064873D9B8BEC087A5C82961D654E6B53A5AD1A98BC0B8A821F95E464E4889",
            "Native jukebox original source/compiled binding identity changed.");
        var bindings = new FalloutScriptBindings(records, reference, script, fields);
        var player = bindings.Reference("player");
        var note = bindings.Form("NVDLC03HQBuddyJukeboxNOTE").FormKey;
        var paired = bindings.Reference("NVDLC03SinkJukeBoxTAREF");
        var challenge = bindings.Form("NVDLC03AchievementMakingFriends").FormKey;
        var unlocked = bindings.Variable("NVDLC03DialogueHQBuddies.bJukeboxUnlocked");
        var jukebox = bindings.Variable("NVDLC03HQBuddy.iJuke");
        var firstEnabled = bindings.Variable("NVDLC03SinkJukeBoxTAREF.bJukeboxFirstEnabled");
        var cell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(reference)!.Value);
        cell = cell with { References = cell.References.Where(value => value.FormKey == reference.FormKey).ToArray() };
        Require(cell.References.Count == 1, "Native jukebox actual CELL lacks the selected reference.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
        foreach (var count in new[] { -1, 0, 2 })
        {
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(cell);
            world.PlayerNotes.Restore(count < 0 ? null : new(Enumerable.Repeat(note, count).ToArray()));
            var quests = new FalloutQuestState(records);
            var inventory = new FalloutPlayerInventory();
            var native = new RuntimeNativeQuestScripts(records, quests, new HashSet<FalloutFormKey>(), inventory, references: world);
            var driver = new RuntimeNativeOpeningStageDriver();
            try
            {
                AddChild(native); native.SetProcess(false);
                Bind(driver, native.Scripts, quests, inventory);
                var executor = Executor(world, quests, native, driver);
                var beforeProgress = native.Scripts.Challenges.State(challenge).Progress;
                var result = executor.Activate(reference.FormKey, player);
                if (count < 0)
                {
                    Require(result.Error?.Contains("no authoritative collected-note membership", StringComparison.Ordinal) == true &&
                        !world.PlayerNotes.Known && quests.Capture().Count == 0 &&
                        native.Capture().Messages.Count == 0 && inventory.Notifications.Capture().LastOrdinal == 0 &&
                        native.Scripts.Challenges.State(challenge).Progress == beforeProgress,
                        "Unknown history did not refuse before native effects.");
                    var stopped = JsonSerializer.Serialize(world.Capture());
                    Require(executor.Activate(reference.FormKey, player).Error == result.Error &&
                        JsonSerializer.Serialize(world.Capture()) == stopped, "Warm unknown-history failure was cleared/replayed.");
                    using var failedCold = new FalloutReferenceWorld(records);
                    failedCold.Restore(RoundTrip(world.Capture())); failedCold.PlayerNotes.Restore(RoundTrip(world.PlayerNotes.Capture()));
                    failedCold.LoadCell(cell);
                    var failedQuests = new FalloutQuestState(records); failedQuests.Restore(RoundTrip(quests.Capture()));
                    var failedInventory = new FalloutPlayerInventory();
                    var failedNative = new RuntimeNativeQuestScripts(records, failedQuests, new HashSet<FalloutFormKey>(), failedInventory, references: failedCold);
                    var failedDriver = new RuntimeNativeOpeningStageDriver();
                    try
                    {
                        AddChild(failedNative); failedNative.SetProcess(false);
                        failedNative.Scripts.Restore(RoundTrip(native.Capture()));
                        Bind(failedDriver, failedNative.Scripts, failedQuests, failedInventory);
                        Require(Executor(failedCold, failedQuests, failedNative, failedDriver).Activate(reference.FormKey, player).Error == result.Error &&
                            JsonSerializer.Serialize(failedCold.Capture()) == stopped && !failedCold.PlayerNotes.Known &&
                            failedNative.Capture().Messages.Count == 0 && failedInventory.Notifications.Capture().LastOrdinal == 0,
                            "Cold unknown-history fault was cleared/replayed through native host.");
                    }
                    finally { failedNative.Free(); failedDriver.Free(); }
                    GD.Print("OPENNV_NATIVE_JUKEBOX_FAILURE_PASS unknownHistory=true refusedBeforeEffects=true warmColdRetained=true");
                    continue;
                }
                Require(result.Error is null && result.Blocks == 1, result.Error ?? "Native jukebox invocation did not finish.");
                var removed = count != 0;
                Require(world.PlayerNotes.Notes.Count == Math.Max(0, count - 1) &&
                    quests.Variable(unlocked.Owner, unlocked.Index) == (removed ? 1 : 0) &&
                    quests.Variable(jukebox.Owner, jukebox.Index) == (removed ? 1 : 0) &&
                    world.Get(reference.FormKey).Destroyed == removed &&
                    world.Get(firstEnabled.Owner).Read(firstEnabled.Index) == (removed ? 1 : 0) &&
                    native.Scripts.Challenges.State(challenge).Progress == beforeProgress + (removed ? 1 : 0),
                    "Native jukebox removal/quest/destruction/challenge/paired-variable outcomes differ.");
                Require(!removed || world.Get(paired).EnableRequest is { Enabled: true },
                    "Authored paired-reference enable was not queued.");
                var worldState = RoundTrip(world.Capture());
                var questState = RoundTrip(quests.Capture());
                var noteState = RoundTrip(world.PlayerNotes.Capture());
                var scriptState = RoundTrip(native.Capture());
                var message = FalloutSourceMessage.Read(bindings.Form(removed ? "NVDLC03HQBuddyUnlockMSG" : "NVDLC03HQBuddyLockedMSG"));
                Require(message.Modal ? scriptState.Messages.Count == 1 && scriptState.Messages[0].Caller == reference.FormKey :
                    scriptState.Messages.Count == 0 && scriptState.Notifications!.Pending.Any(value =>
                        value.Event.Kind == FalloutHudEventKind.Message && value.Event.Source == message.Form),
                    "Production message subtype/request ownership differs from its source.");
                await PresentAndAccept(native, driver, message, scriptState.Messages.SingleOrDefault());
                var after = native.Scripts.Capture();
                Require(after.Messages.Count == 0 && (!message.Modal || after.MessageResults!.Caller is null),
                    "Accepted modal source button was not consumed exactly once.");

                using var cold = new FalloutReferenceWorld(records);
                cold.Restore(worldState); cold.PlayerNotes.Restore(noteState); cold.LoadCell(cell);
                var coldQuests = new FalloutQuestState(records); coldQuests.Restore(questState);
                var coldInventory = new FalloutPlayerInventory();
                var coldNative = new RuntimeNativeQuestScripts(records, coldQuests, new HashSet<FalloutFormKey>(), coldInventory, references: cold);
                var coldDriver = new RuntimeNativeOpeningStageDriver();
                try
                {
                    AddChild(coldNative); coldNative.SetProcess(false);
                    coldNative.Scripts.Restore(scriptState);
                    Bind(coldDriver, coldNative.Scripts, coldQuests, coldInventory);
                    var coldBefore = JsonSerializer.Serialize(cold.Capture());
                    var challengeBefore = JsonSerializer.Serialize(coldNative.Scripts.Challenges.Capture());
                    await PresentAndAccept(coldNative, coldDriver, message, scriptState.Messages.SingleOrDefault());
                    Require(JsonSerializer.Serialize(cold.Capture()) == coldBefore &&
                        JsonSerializer.Serialize(coldNative.Scripts.Challenges.Capture()) == challengeBefore &&
                        cold.PlayerNotes.Notes.SequenceEqual(noteState.Notes),
                        "Cold message presentation replayed source mutation/removal/challenge effects.");
                    var coldAfter = RoundTrip(coldNative.Capture());
                    Require(coldAfter.Messages.Count == 0 && (!message.Modal || coldAfter.MessageResults!.Caller is null),
                        "Cold accepted message was republished or left consumable.");
                }
                finally { coldNative.Free(); coldDriver.Free(); GetTree().Paused = false; }
                GD.Print($"OPENNV_NATIVE_JUKEBOX_COMPOSITION_CASE count={count} originalOnActivate=true completeInvocation=true " +
                    $"noteRemaining={world.PlayerNotes.Notes.Count} questAndDestroyed={removed} challengeAndEnable={removed} " +
                    $"sourceModal={message.Modal} realModalMenuOrTimedHud=true coldMessageNoEffectReplay=true pairedPresentation=nonresident");
            }
            finally { native.Free(); driver.Free(); GetTree().Paused = false; }
        }
        Require(Hash(script.ReadData()) == "B4AAF962DCA7516D4FEBBA2517255C645C67AA960A8FBE2F8C138FB7A1A35932", "Owned script changed.");
        GD.Print("OPENNV_NATIVE_JUKEBOX_COMPOSITION_PASS controlledPrerequisites=true realProductionMessageHost=true " +
            "fullOriginalScript=true absentAndDuplicate=true coldNoReplay=true physicalInputCampaignFullCellAndRetail=unverified recording=false");

        void Bind(RuntimeNativeOpeningStageDriver driver, FalloutQuestScripts scripts, FalloutQuestState quests, FalloutPlayerInventory inventory)
        {
            var type = typeof(RuntimeNativeOpeningStageDriver);
            foreach (var (name, value) in new (string, object)[] { ("_pluginStack", records), ("_scripts", scripts), ("_quests", quests), ("_inventory", inventory) })
                type.GetField(name, flags)!.SetValue(driver, value);
        }
        FalloutReferenceScripts Executor(FalloutReferenceWorld world, FalloutQuestState quests, RuntimeNativeQuestScripts native,
            RuntimeNativeOpeningStageDriver driver) => new(records, world, quests,
                RuntimeCoordinator.CreateNativeReferenceEventHost(new((_, _) => false,
                    _ => throw new InvalidOperationException("Unexpected default activation.")), driver, native.Scripts.Challenges));
        async Task PresentAndAccept(RuntimeNativeQuestScripts native, RuntimeNativeOpeningStageDriver driver,
            FalloutSourceMessage message, FalloutMessageRequest? request)
        {
            native.EvaluateMessageCondition = driver.EvaluateMessageCondition;
            if (message.Modal)
            {
                native.ActivateWorld(loaded: false);
                native._Process(0); // Queue-to-menu through the real production owner.
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!message.Modal)
            {
                var hud = native.FindChild("HUDMainMenuMessages", true, false) as NativeOwnedHudMessages;
                Require(native.StartupError is null && hud is { Error: null } && !GetTree().Paused &&
                    native.Scripts.Capture().Notifications!.Current is { } current &&
                    current.Event.Kind == FalloutHudEventKind.Message && current.Event.Source == message.Form &&
                    JsonSerializer.SerializeToElement(hud.State).GetProperty("text").GetString() == message.Text,
                    hud?.Error ?? native.StartupError ?? "Source timed message did not reach its real HUD owner.");
                return;
            }
            Require(native.StartupError is null && GetTree().Paused, native.StartupError ?? "Message did not own pause.");
            var menu = native.FindChild("MessageMenu", true, false) as NativeOwnedMessageMenu ??
                throw new InvalidDataException("Production message menu was not constructed.");
            var button = menu.GetChildren().OfType<NativeBitmapMenuButton>().First();
            var choice = (int)button.GetMeta("opennv_source_choice_id");
            button.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!native.Scripts.MessageResults.Select(request!, choice), "A repeated old request still accepted input.");
            Require(driver.TakeMessageButton(reference.FormKey) == choice && driver.TakeMessageButton(reference.FormKey) == -1 && !GetTree().Paused,
                "Production button ownership/pause/consumptive result changed.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
