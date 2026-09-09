using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedReferenceInteractionProbe
{
    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var scene = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
        world.LoadCell(scene);
        FalloutFormKey Reference(string baseName) => scene.References.Single(reference => scene.BaseObjects[reference.Base].EditorId == baseName).FormKey;
        var trigger = Reference("VCG01VigorTesterTrigger");
        var tester = Reference("VCG01VigorTester");
        var exit = Reference("GSDocMitchellExitTrigger");
        var quest = FalloutDialogueTopic.Find(records, "QUST", "VCG01").FormKey;
        var player = records.RuntimeFormKey(0x14);
        var quests = new FalloutQuestState(records);
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
        {
            effects.Add(effect);
            if (effect.Kind == FalloutReferenceEffectKind.SetStage) quests.EnterStage(effect.Target!.Value, effect.Stage);
        }));
        void Require(bool accepted, string message) { if (!accepted) throw new InvalidDataException(message); }
        void Dispatch(FalloutFormKey reference, string name)
        {
            var result = scripts.Dispatch(reference, name, player);
            Require(result.Error is null, result.Error ?? "Reference event failed.");
        }
        quests.EnterStage(quest, 55);
        Dispatch(trigger, "OnTriggerEnter");
        Require(quests.StageDone(quest, 60), "Owned trigger did not execute its stage guard/result.");
        var effectCount = effects.Count;
        Dispatch(trigger, "OnTriggerEnter");
        Require(effects.Count == effectCount, "Owned trigger repeated a completed stage result.");
        Dispatch(trigger, "OnTriggerLeave");
        Require(effects.Count(effect => effect.Kind == FalloutReferenceEffectKind.Message) == 2, "Owned trigger lost its leave tutorial messages.");
        Dispatch(trigger, "OnTriggerLeave");
        Require(effects.Count(effect => effect.Kind == FalloutReferenceEffectKind.Message) == 2, "Owned reference-local guard repeated tutorial messages.");
        Require(scripts.Activate(tester, player).Error is null && effects.All(effect => effect.Kind != FalloutReferenceEffectKind.SpecialMenu),
            "Owned activation ignored its objective prerequisite.");
        quests.ApplyObjective(quest, 30, true, true);
        Require(scripts.Activate(tester, player).Error is null && effects[^2] is { Kind: FalloutReferenceEffectKind.SpecialMenu, Value: 40 } &&
            effects[^1] is { Kind: FalloutReferenceEffectKind.SetStage, Stage: 65 }, "Owned activation lost menu-before-stage execution.");
        quests.EnterStage(quest, 110);
        Dispatch(exit, "OnTriggerEnter");
        Require(quests.Stage(quest) == 115, "Unrelated owned trigger did not use the same stage owner.");
        var cave = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "SLGoodspringsCaveINT").FormKey);
        world.LoadCell(cave);
        var caveTriggerScript = FalloutDialogueTopic.Find(records, "SCPT", "VERT1CoyoteDenGSTriggerScript").FormKey;
        var caveTrigger = cave.References.Single(reference => world.Get(reference.FormKey).Script?.Record.FormKey == caveTriggerScript).FormKey;
        var caveBox = world.Get(FalloutDialogueTopic.Find(records, "REFR", "VERT1GSCoyoteDenCodeBox").FormKey);
        var midway = caveBox.Script!.Locals["bPlayerMidway"];
        Dispatch(caveTrigger, "OnTriggerEnter");
        Require(caveBox.Read(midway) == 1, "Second-cell trigger did not write another reference's local state.");
        caveBox.Write(midway, 0);
        Dispatch(caveTrigger, "OnTriggerEnter");
        Require(caveBox.Read(midway) == 1, "Second-cell trigger retained an artificial one-shot guard.");
        var saved = world.Capture();
        var doctor = FalloutDialogueTopic.Find(records, "ACHR", "DocMitchellREF").FormKey;
        Require(scripts.Dispatch(doctor, "GameMode", elapsedSeconds: .1).Error is null && world.ActorValue(doctor, "Variable01") == 0,
            "The owned actor script has no initial user-defined actor-value state.");
        world.ChangeActorValue(doctor, "Variable01", "setav", 3);
        world.ChangeActorValue(doctor, "Variable01", "modav", 2);
        Require(world.ActorValue(doctor, "Variable01") == 5, "Actor value modifiers replaced the source base.");
        world.ChangeActorValue(doctor, "Variable01", "forceav", 7);
        using var actorCold = new FalloutReferenceWorld(records); actorCold.Restore(world.Capture());
        Require(actorCold.ActorValue(doctor, "Variable01") == 7 && actorCold.Get(doctor).ActorValues["variable01"].Base == 3,
            "Cold actor state lost its value or separate modifier pool.");
        using var coldWorld = new FalloutReferenceWorld(records);
        coldWorld.Restore(saved);
        coldWorld.LoadCell(scene);
        coldWorld.LoadCell(cave);
        var coldScripts = new FalloutReferenceScripts(records, coldWorld, quests, new((_, _) => false, effects.Add));
        effectCount = effects.Count;
        Require(coldScripts.Dispatch(trigger, "OnTriggerLeave", player).Error is null && effects.Count == effectCount &&
            coldWorld.Get(caveBox.Reference).Read(midway) == 1, "Cold reference state repeated a source message or lost cross-cell locals.");
        var primitives = scene.References.Select(reference => FalloutReferencePrimitive.Read(records.GetEffective(reference.FormKey)))
            .Where(primitive => primitive is not null).ToArray();
        Require(primitives.Length > 0 && primitives.All(primitive => primitive!.X > 0), "Owned primitive bounds were lost.");
        Console.WriteLine($"OPENNV_OWNED_REFERENCE_INTERACTION_PASS cells=2 primitives={primitives.Length} stageGuard=true leaveMessages=true localGuard=true objectiveGuard=true orderedEffects=true crossReference=true reentry=true coldLocals=true parity=unverified");
    }
}
