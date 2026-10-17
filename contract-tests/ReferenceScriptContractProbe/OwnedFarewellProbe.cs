using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedFarewellProbe
{
    internal static void Run(string root, IReadOnlySet<FalloutFormKey> previousConversation)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey));
        var quests = new FalloutQuestState(records);
        var globals = FalloutGlobalState.Read(records);
        var inventory = new FalloutPlayerInventory();
        var pipBoy = new FalloutPipBoyState(records, inventory);
        var questScripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), inventory, globals, references: world);
        questScripts.SaidInfos.UnionWith(previousConversation);
        var player = records.RuntimeFormKey(0x14);
        var speaker = FalloutDialogueTopic.Find(records, "ACHR", "DocMitchellREF");
        var opening = FalloutDialogueTopic.Find(records, "QUST", "VCG01").FormKey;
        var timer = FalloutDialogueTopic.Find(records, "QUST", "VGenericTimer").FormKey;
        var door = FalloutDialogueTopic.Find(records, "REFR", "GSDocMitchellHouseIntDoorREF").FormKey;
        var hardcore = FalloutDialogueTopic.Find(records, "REFR", "VCG01CasualHardcoreMessageREF").FormKey;
        quests.SetRunning(opening, true); quests.EnterStage(opening, 115);
        // This fixture begins after the couch objective's ordinary script
        // completion and the exit objective published by the stand-up stage.
        quests.ApplyObjective(opening, 40, false, true);
        quests.ApplyObjective(opening, 50, true, true);
        world.Get(door).Destroyed = true;
        var controls = FalloutPlayerControlState.AllEnabled;
        FalloutReferenceScripts? scripts = null;
        FalloutQuestStages? stages = null;
        void Apply(FalloutReferenceScriptEffect effect)
        {
            switch (effect.Kind)
            {
                case FalloutReferenceEffectKind.AddNote:
                    if (inventory.Item(effect.Target!.Value) is null) inventory.Add(records, effect.Target.Value, 1, 1, true, globals);
                    break;
                case FalloutReferenceEffectKind.AddItem when effect.Target == player:
                    inventory.Add(records, effect.Argument!.Value, effect.Value, 1, effect.Enable, globals); break;
                case FalloutReferenceEffectKind.EquipItem when effect.Target == player:
                    inventory.Equip(records, effect.Argument!.Value); break;
                case FalloutReferenceEffectKind.PipBoyReset: pipBoy.Reset(); break;
                case FalloutReferenceEffectKind.ScriptActivate:
                    if (!effect.Enable) throw new InvalidDataException("Fixture expects script activation admission.");
                    var result = scripts!.Activate(effect.Target!.Value, effect.Argument!.Value);
                    if (result.Error is not null) throw new InvalidDataException(result.Error);
                    break;
                case FalloutReferenceEffectKind.Message:
                    var owner = records.GetEffective(effect.Source);
                    questScripts.ShowMessage(effect.Target!.Value, FalloutScriptLocals.AttachedScript(records, owner)?.FormKey,
                        owner.Signature == "QUST" ? null : owner.FormKey);
                    break;
                case FalloutReferenceEffectKind.PlayerControls:
                    controls = new FalloutPlayerControlCommand(effect.Enable, effect.Controls!).Apply(controls); break;
                case FalloutReferenceEffectKind.Hardcore: questScripts.Session.Hardcore = effect.Enable; break;
                case FalloutReferenceEffectKind.AutoDisplayObjectives: questScripts.Session.AutoDisplayObjectives = effect.Enable; break;
                case FalloutReferenceEffectKind.Achievement: questScripts.Session.AddAchievement(effect.Value); break;
                case FalloutReferenceEffectKind.SetStage: stages!.Enter(effect.Target!.Value, effect.Stage); break;
                case FalloutReferenceEffectKind.ReferenceEnable: break; // World state was already published; no presentation is attached.
                default: throw new NotSupportedException($"Farewell fixture effect {effect.Kind} is unbound.");
            }
        }
        scripts = new(records, world, quests, new((_, _) => false, Apply, questScripts.MessageResults.Take,
            IsPlayerTagSkill: name => name == "Guns", Globals: globals));
        stages = new(records, quests, scripts.StageSteps, quests.Evaluate);
        questScripts.Host = new((quest, stage) => () => stages.Enter(quest, stage), _ => throw new NotSupportedException("Fixture actor value"), scripts.ExecuteProgram);
        var baseNpc = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
        var context = new FalloutDialogueConditions(records, quests, speaker.FormKey, FalloutNpcAppearanceResolver.Resolve(records, baseNpc), condition =>
            condition.RunOn == 1 && condition.Function == 70 ? condition.Argument1 == 0 ? 1 : 0 :
            throw new NotSupportedException($"Farewell condition {condition.Function}/{condition.RunOn}"));
        var infos = new HashSet<FalloutFormKey>();
        var conversation = new FalloutConversation(records, quests, context.Evaluate, (info, begin) =>
        { infos.Add(info.Record.FormKey); scripts.ExecuteResult(info, speaker.FormKey, begin); }, questScripts.SaidInfos);
        conversation.Start(baseNpc, FalloutDialogueTopic.Find(records, "DIAL", "GREETING").FormKey);
        for (var index = 0; index < 80 && conversation.Phase != "closed"; ++index)
        {
            if (conversation.Phase == "speaking") conversation.CompleteResponse();
            else conversation.Choose(conversation.Choices[0].Topic);
        }
        if (conversation.Phase != "closed" || conversation.Error is not null || !pipBoy.Available || pipBoy.Revision != 1 ||
            world.Get(door).Destroyed || !quests.IsRunning(timer) || !controls.PipBoy || inventory.Items.All(item => item.RecordType != "WEAP"))
            throw new InvalidDataException("Owned farewell handoff differs: " + System.Text.Json.JsonSerializer.Serialize(new
            { conversation.Phase, conversation.Error, infos, pipBoy.Available, pipBoy.Revision, world.Get(door).Destroyed,
                timerRunning = quests.IsRunning(timer), controls.PipBoy, items = inventory.Items.Select(item => item.EditorId).ToArray() }));
        if (!questScripts.TryTakeMessage(out var message) || message!.Request!.Caller != hardcore)
            throw new InvalidDataException("Hardcore activation did not own its message result.");
        questScripts.MessageResults.Select(message.Request, 1);
        var hardcoreResult = scripts.Dispatch(hardcore, "GameMode", elapsedSeconds: 1d / 60);
        if (hardcoreResult.Error is not null || !questScripts.Session.Hardcore || !world.Get(hardcore).DeletePending)
            throw new InvalidDataException("Hardcore answer did not execute and retain its source tombstone.");
        for (var frame = 0; frame < 600 && quests.IsRunning(timer); ++frame) questScripts.Advance(1d / 60);
        if (quests.IsRunning(timer) || quests.Stage(opening) != 200 || !quests.IsCompleted(opening) || quests.IsRunning(opening) ||
            quests.Stage(FalloutDialogueTopic.Find(records, "QUST", "VCG02").FormKey) != 5 ||
            quests.Stage(FalloutDialogueTopic.Find(records, "QUST", "VMQ01").FormKey) != 10)
        {
            var state = questScripts.Capture().Instances.Single(instance => instance.Quest == timer);
            throw new InvalidDataException("Original post-farewell quest timer did not complete: " + state.Error);
        }
        var scriptSnapshot = System.Text.Json.JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(System.Text.Json.JsonSerializer.Serialize(questScripts.Capture()))!;
        var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
        using var coldWorld = new FalloutReferenceWorld(records); coldWorld.Restore(world.Capture());
        var coldScripts = new FalloutQuestScripts(records, coldQuests, new HashSet<FalloutFormKey>(), new(), globals, references: coldWorld);
        coldScripts.Restore(scriptSnapshot);
        if (!coldScripts.SaidInfos.SetEquals(questScripts.SaidInfos) || !coldScripts.Session.Hardcore || coldQuests.ActiveQuest != quests.ActiveQuest ||
            System.Text.Json.JsonSerializer.Serialize(coldScripts.Capture()) != System.Text.Json.JsonSerializer.Serialize(scriptSnapshot))
            throw new InvalidDataException("Cold dialogue, hardcore, active quest or script continuation differs.");
        var cell = world.Get(hardcore).Cell;
        world.UnloadCell(cell);
        world.LoadCell(FalloutCellSceneReader.Read(records, cell));
        if (!world.Get(hardcore).Deleted || world.Get(hardcore).DeletePending || world.CanActivate(hardcore) || world.IsEnabled(hardcore) ||
            world.SetEnabled(hardcore, true)) throw new InvalidDataException("Deleted reference returned after its cell reloaded.");
        Console.WriteLine($"OPENNV_OWNED_FAREWELL_PASS sourceInfos={infos.Count} realGrants=true pipBoyState=true doorState=true hardcoreAnswer=true sourceTimer=true subsequentQuests=true coldHistory=true deletedLifetime=true ordinaryInputAndPresentation=unverified");
    }
}
