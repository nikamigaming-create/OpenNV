using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
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
        var bench = records.RuntimeFormKey(0x171b9a);
        var benchCell = FalloutCellSceneReader.Read(records, FalloutCellSceneReader.ParentCell(records.GetEffective(bench))!.Value);
        world.LoadCell(benchCell);
        FalloutFormKey? recipeCategory = null;
        var benchScripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effects.Add,
            Command: (_, bindings, command, arguments) =>
            {
                Require(command.Equals("player.showrecipemenu", StringComparison.OrdinalIgnoreCase) && arguments.Count == 1,
                    "Owned bench dispatched an unexpected command.");
                var category = bindings.Form(arguments[0]);
                Require(category.Signature == "RCCT", "Owned crafting category was not compiled into the bench script.");
                Require(!FalloutRecipeCategory.Read(records, category.FormKey).IsSubcategory,
                    "Owned bench category treated unrelated DATA bits as subcategory membership.");
                recipeCategory = category.FormKey;
            }));
        var activation = benchScripts.Activate(bench, player);
        Require(activation.Error is null && recipeCategory is not null &&
            world.Get(bench).Read(world.Get(bench).Script!.Locals["User"]) == records.RuntimeFormId(player),
            "Owned reloading bench lost GetActionRef, its reference local, or its recipe menu: " + activation.Error);
        Console.WriteLine("OPENNV_OWNED_BENCH_ACTIVATION_PASS actionReference=true referenceLocal=true compiledCategory=true ordinaryUi=separate");
        var brokenRobot = records.RuntimeFormKey(0x1572e6);
        world.InitializeSourceCorpse(brokenRobot);
        Require(world.IsDead(brokenRobot) && world.Health(brokenRobot).Current == 0,
            "An owned zero-health creature started alive before its first damage query.");
        var authored = FalloutAuthoredRagdoll.Read(records.GetEffective(brokenRobot)) ??
            throw new InvalidDataException("Owned damaged actor lost its authored ragdoll.");
        var ragdollAppearance = FalloutCreatureAppearanceResolver.Resolve(records, world.Get(brokenRobot).Base, brokenRobot);
        Require(content.TryRead(ragdollAppearance.SkeletonPath, null, out var skeletonBytes, out _), "Owned ragdoll skeleton is absent.");
        var skeleton = FalloutNifFile.Read(skeletonBytes);
        var pose = FalloutNifAuthoredRagdoll.Bind(skeleton, authored);
        Require(pose.Count == 10 && authored.BipedRotation is null && pose.GroupBy(row => row.Pose.Part).Any(group => group.Count() > 1) &&
            MathF.Abs(pose[0].Pose.Position[2]) < .01f,
            "Owned damaged pose lost repeated physical part ids or acquired a flying offset.");
        Console.WriteLine("OPENNV_OWNED_AUTHORED_RAGDOLL_PASS orderedPhysicalBindings=true duplicateParts=true localUnits=true nativePose=separate");
        world.LoadCell(FalloutCellSceneReader.Read(records, world.Get(brokenRobot).Cell));
        var robotEffects = new List<FalloutReferenceScriptEffect>();
        var robotScripts = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, robotEffects.Add, GetButtonPressed: _ => -1, IsInCombat: _ => false));
        var robotActivation = robotScripts.Activate(brokenRobot, player);
        Require(robotActivation.Error is null && robotEffects.Any(effect => effect.Kind == FalloutReferenceEffectKind.Message),
            "Owned scripted corpse activation did not reach its repair message: " + robotActivation.Error);
        var repairedRobot = records.RuntimeFormKey(0x1732d1);
        var originalRobotCell = world.Get(repairedRobot).Cell;
        Require(!world.IsEnabled(repairedRobot), "Repair fixture lost the source working actor's initial disabled state.");
        world.MoveTo(repairedRobot, brokenRobot);
        var repairScene = world.ComposeResidency(FalloutCellSceneReader.Read(records, world.Get(brokenRobot).Cell));
        world.ReplaceResidentCell(repairScene);
        Require(world.IsResident(repairedRobot) && !world.IsEnabled(repairedRobot) && world.Get(repairedRobot).Cell == originalRobotCell &&
            repairScene.References.Single(reference => reference.FormKey == repairedRobot).Position.SequenceEqual(world.Placement(brokenRobot).Position),
            "Owned MoveTo did not retain the real disabled companion in its destination cell.");
        using (var repairedRestore = new FalloutReferenceWorld(records))
        {
            repairedRestore.Restore(world.Capture());
            Require(repairedRestore.Placement(repairedRobot).Cell == world.Get(brokenRobot).Cell &&
                repairedRestore.Get(repairedRobot).Cell == originalRobotCell,
                "Cold repair placement lost source/destination cell ownership.");
        }
        Console.WriteLine("OPENNV_OWNED_REFERENCE_MOVE_PASS sourceIdentity=true disabledPreserved=true destinationResident=true coldState=true ordinaryRepair=separate");
        var repairMenu = FalloutSourceMessage.Read(records.GetEffective(robotEffects.First(effect =>
            effect.Kind == FalloutReferenceEffectKind.Message).Target!.Value)).ResolveButtons(records,
                condition => condition.Function == 53 ? (float)world.Get(condition.FormArgument1).Read(condition.Argument2) :
                    throw new NotSupportedException("Unexpected initial robot message condition."));
        Require(repairMenu.ButtonIndices!.SequenceEqual(new[] { 0, 1, 3 }),
            "Conditional message filtering renumbered its original script button indices.");
        Require(robotScripts.Dispatch(brokenRobot, "GameMode", elapsedSeconds: .1).Error is null,
            "Owned repair message waiting state failed before any selection.");
        Console.WriteLine("OPENNV_OWNED_SCRIPTED_CORPSE_ACTIVATION_PASS zeroHealth=true repairMessage=true repairOutcome=unverified");
        OwnedRepairMessageProbe.Run(records);
        OwnedIngestibleProbe.Run(records, content);
        foreach (var id in new[] { 0x08267fu, 0x0cde03u })
        {
            var reference = records.RuntimeFormKey(id);
            var actor = world.Get(reference);
            // A supplied level isolates the source template contract from
            // encounter-zone initialization, which is still rejected in play.
            var selection = new FalloutActorTemplateSelection(1, 17);
            selection.ResolveAll(records, actor.Base);
            Require(!selection.Absent && selection.Capture().Choices.Count != 0, "Owned leveled actor did not retain a source choice.");
            if (records.GetEffective(actor.Base).Signature == "CREA")
            {
                var appearance = FalloutCreatureAppearanceResolver.Resolve(records, actor.Base, reference, selection);
                Require(appearance.Models.Count > 0 && content.TryResolve(appearance.SkeletonPath, null, out _), "Owned selected creature has no source geometry.");
            }
            else
            {
                // Equipment arbitration is a separate capability. Exercise
                // the selected humanoid declaration without claiming that a
                // competing source inventory has an accepted worn outfit.
                foreach (ushort group in new ushort[] { 1, 64, 256, 512 })
                    Require(FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), group, selection).Signature == "NPC_",
                        "Owned humanoid template group changed actor type.");
            }
            var restored = new FalloutActorTemplateSelection(System.Text.Json.JsonSerializer.Deserialize<FalloutActorTemplateSnapshot>(
                System.Text.Json.JsonSerializer.Serialize(selection.Capture()))!);
            restored.ResolveAll(records, actor.Base);
            Require(System.Text.Json.JsonSerializer.Serialize(restored.Capture()) ==
                System.Text.Json.JsonSerializer.Serialize(selection.Capture()), "Owned actor selection changed on cold restore.");
        }
        Console.WriteLine("OPENNV_OWNED_ACTOR_TEMPLATE_PASS creature=true humanoid=true coherentGroups=true coldChoice=true ordinarySpawn=separate");
        Console.WriteLine($"OPENNV_OWNED_REFERENCE_INTERACTION_PASS cells=2 primitives={primitives.Length} stageGuard=true leaveMessages=true localGuard=true objectiveGuard=true orderedEffects=true crossReference=true reentry=true coldLocals=true parity=unverified");
    }
}
