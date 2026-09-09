using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedConversationProbe
{
    internal static HashSet<FalloutFormKey> Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey));
        var quests = new FalloutQuestState(records);
        var quest = FalloutDialogueTopic.Find(records, "QUST", "VCG01");
        var speaker = FalloutDialogueTopic.Find(records, "ACHR", "DocMitchellREF");
        var slots = FalloutScriptLocals.Read(FalloutScriptLocals.AttachedScript(records, quest)!);
        quests.EnterStage(quest.FormKey, 80);
        quests.ApplyObjective(quest.FormKey, 40, true, true);
        quests.SetVariable(quest.FormKey, slots["bGiveTest"], 1);
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            effects.Add(effect);
            if (effect.Kind == FalloutReferenceEffectKind.ReferenceEnable)
            {
                if (world.Get(effect.Target!.Value).EnableRequest?.Enabled != effect.Enable)
                    throw new InvalidOperationException("Enable result did not reach the world request queue.");
            }
            else if (effect.Kind == FalloutReferenceEffectKind.Texture)
            {
                if (!content.TryRead("textures/" + effect.TexturePath!.Replace('\\', '/') + ".dds", null, out _, out _))
                    throw new InvalidOperationException("Authored texture result has no winning owned resource.");
            }
            else if (effect.Kind == FalloutReferenceEffectKind.SetStage) quests.EnterStage(effect.Target!.Value, effect.Stage);
            else throw new NotSupportedException($"Owned conversation effect {effect.Kind} is not exercised.");
        }));
        var speakerBase = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
        var context = new FalloutDialogueConditions(records, quests, speaker.FormKey, FalloutNpcAppearanceResolver.Resolve(records, speakerBase, speaker.FormKey));
        var said = new HashSet<FalloutFormKey>();
        var conversation = new FalloutConversation(records, quests, context.Evaluate, (info, begin) => scripts.ExecuteResult(info, speaker.FormKey, begin), said);
        conversation.Start(speakerBase, FalloutDialogueTopic.Find(records, "DIAL", "GREETING").FormKey);
        if (quests.Variable(quest.FormKey, slots["bBeganTest"]) != 1) throw new InvalidOperationException("Authored conversation begin script did not run.");
        var selections = 0;
        var fade = FalloutReferenceFadeSettings.Read(FalloutInstallationSettings.Read(content));
        while (conversation.Phase != "closed" && selections < 64)
        {
            world.AdvanceEnableChanges(1d / 60, fade, _ => false);
            while (conversation.Phase == "speaking") conversation.CompleteResponse();
            if (conversation.Phase == "closed") break;
            if (conversation.Phase != "choices" || conversation.Choices.Count == 0) throw new InvalidOperationException("Authored questionnaire choices are absent.");
            conversation.Choose(conversation.Choices[0].Topic);
            selections++;
        }
        world.AdvanceEnableChanges(1d / 60, fade, _ => false);
        if (quests.Variable(quest.FormKey, slots["bGiveTest"]) != 0 || conversation.Error is not null || conversation.Phase != "closed")
            throw new InvalidOperationException("Authored greeting end script or choice handoff failed.");
        var calculation = FalloutDialogueTopic.Find(records, "QUST", "VCG01Test");
        var counters = FalloutScriptLocals.Read(FalloutScriptLocals.AttachedScript(records, calculation)!);
        if (selections != 14 || quests.Variable(calculation.FormKey, counters["bDoFinalCalculation"]) != 1 ||
            quests.Stage(quest.FormKey) != 85 || effects.Count(effect => effect.Kind == FalloutReferenceEffectKind.Texture) != 8 ||
            effects.Count(effect => effect.Kind == FalloutReferenceEffectKind.ReferenceEnable) != 2 ||
            world.IsEnabled(effects.Last(effect => effect.Kind == FalloutReferenceEffectKind.ReferenceEnable).Target!.Value))
            throw new InvalidOperationException("The complete authored questionnaire did not reach its calculation request, card cleanup and stage request.");
        using var coldWorld = new FalloutReferenceWorld(records);
        coldWorld.Restore(world.Capture());
        var coldQuests = new FalloutQuestState(records); coldQuests.Restore(quests.Capture());
        if (System.Text.Json.JsonSerializer.Serialize(coldWorld.Capture()) != System.Text.Json.JsonSerializer.Serialize(world.Capture()) ||
            System.Text.Json.JsonSerializer.Serialize(coldQuests.Capture()) != System.Text.Json.JsonSerializer.Serialize(quests.Capture()))
            throw new InvalidOperationException("Cold state lost questionnaire locals or reference enable state.");
        var standUp = FalloutDialogueTopic.Decode(records.GetEffective(records.RuntimeFormKey(0x1057ef)));
        var beforeResults = effects.Count;
        scripts.ExecuteResult(standUp, speaker.FormKey, true);
        if (quests.Stage(quest.FormKey) != 110 || effects.Count != beforeResults + 1 ||
            effects[^1].Kind != FalloutReferenceEffectKind.SetStage)
            throw new InvalidOperationException("The owned stand-up line did not execute its begin result before voice playback.");
        scripts.ExecuteResult(standUp, speaker.FormKey, false);
        if (effects.Count != beforeResults + 1) throw new InvalidOperationException("Begin results were replayed as end results.");
        Console.WriteLine($"OPENNV_OWNED_CONVERSATION_PASS choices={selections} begin=true end=true textures={effects.Count(effect => effect.Kind == FalloutReferenceEffectKind.Texture)} enabledChanges={effects.Count(effect => effect.Kind == FalloutReferenceEffectKind.ReferenceEnable)} ordinary-input-audio-pixels=unverified");
        return said;
    }
}
