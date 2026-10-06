using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

public partial class NativeActorPerformanceAudit : Node
{
    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            if (args is ["--owned-stopped-idle", var stoppedGame, var stoppedMod, var stoppedRoot, var stoppedSave,
                var stoppedActor, var stoppedQuest, var stoppedStage, var stoppedPackage, var stoppedForm, var stoppedSounds, .. var stoppedDependencies])
            {
                await OwnedStoppedIdle(stoppedGame, stoppedMod, stoppedRoot, stoppedSave, stoppedActor, stoppedQuest,
                    short.Parse(stoppedStage, System.Globalization.CultureInfo.InvariantCulture), stoppedPackage, stoppedForm, stoppedSounds.Split(','), stoppedDependencies);
                GetTree().Quit(); return;
            }
            if (args is ["--owned-actor-finite-retirement", var retireGame, var retireMod, var retireRoot,
                var retireSave, var retireActor, var retireSound, .. var retireDependencies])
            {
                await OwnedEndedSounds(retireGame, retireMod, retireRoot, retireSave, retireActor, retireSound, retireDependencies,
                    actorRetirement: true);
                GetTree().Quit(); return;
            }
            if (args is ["--owned-finite-sound-retirement", var finiteGame, var finiteMod, var finiteRoot,
                var finiteSave, var finiteActor, var finiteSound, .. var finiteDependencies])
            {
                await OwnedEndedSounds(finiteGame, finiteMod, finiteRoot, finiteSave, finiteActor, finiteSound, finiteDependencies, finiteRetirement: true);
                GetTree().Quit(); return;
            }
            if (args is ["--owned-ended-sounds", var soundGame, var soundMod, var soundRoot,
                var soundSave, var soundActor, var soundForm, .. var soundDependencies])
            {
                await OwnedEndedSounds(soundGame, soundMod, soundRoot, soundSave, soundActor, soundForm, soundDependencies);
                GetTree().Quit(); return;
            }
            if (args is ["--owned-attack-variants", var attackGame, var attackMod, var attackRoot,
                var attackCheckpoint, var attackActor, .. var attackDependencies])
            {
                await OwnedAttackVariants(attackGame, attackMod, attackRoot, attackCheckpoint, attackActor, attackDependencies);
                GetTree().Quit(); return;
            }
            if (args is ["--owned-route-lifecycle", var routeGame, var routeMod, var routeRoot,
                var routeCheckpoint, var routeActor, .. var routeDependencies])
            {
                await OwnedAttackVariants(routeGame, routeMod, routeRoot, routeCheckpoint, routeActor, routeDependencies,
                    routeLifecycle: true);
                GetTree().Quit(); return;
            }
            if (args is [var corpseMode, var corpseGame, var corpseMod, var corpseRoot,
                var corpsePath, var corpseActor, var corpseAttacker, .. var corpseDependencies] &&
                corpseMode is "--saved-stopped-corpse" or "--saved-pending-corpse")
            {
                await SavedStoppedCorpse(corpseGame, corpseMod, corpseRoot, corpsePath, corpseActor, corpseAttacker, corpseDependencies,
                    pendingSelection: corpseMode == "--saved-pending-corpse");
                GetTree().Quit(); return;
            }
            if (args is ["--saved-actor-checkpoint", var savedGame, var savedMod, var savedRoot,
                var savedPath, var savedActors, .. var savedDependencies])
            {
                SavedActorCheckpoint(savedGame, savedMod, savedRoot, savedPath, savedActors.Split(','), savedDependencies);
                GetTree().Quit(); return;
            }
            if (args is ["--npc-checkpoint", var checkpointGame, var checkpointMod, var checkpointRoot,
                var checkpointActor, var checkpointQuest, var checkpointStage, var checkpointOwner, .. var checkpointDependencies])
            {
                NpcCheckpoint(checkpointGame, checkpointMod, checkpointRoot, checkpointActor, checkpointQuest,
                    short.Parse(checkpointStage, System.Globalization.CultureInfo.InvariantCulture), checkpointOwner, checkpointDependencies);
                GetTree().Quit(); return;
            }
            if (args is ["--package-binding-cold", var bindingGame, var bindingMod, var bindingRoot,
                var bindingActor, var bindingQuest, var bindingStages, var bindingPackages, .. var bindingDependencies])
            {
                PackageEvaluation(bindingGame, bindingMod, bindingRoot, bindingActor, bindingQuest,
                    bindingStages.Split(',').Select(value => short.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                    bindingDependencies, stoppedFailure: true, expectedPackageId: bindingPackages);
                GetTree().Quit(); return;
            }
            if (args is ["--guard-approach", var guardGame, var guardMod, var guardRoot, var guardActor,
                var guardQuest, var guardStage, .. var guardDependencies])
            {
                await CreatureTravel(guardGame, guardMod, guardRoot, guardActor, guardQuest,
                    short.Parse(guardStage, System.Globalization.CultureInfo.InvariantCulture), 0, guardDependencies,
                    guardApproach: true);
                GetTree().Quit(); return;
            }
            if (args is [var evaluationMode, var evaluationRoot, var evaluationMod, var evaluationModRoot,
                var evaluationActor, var evaluationQuest, var evaluationStage, .. var evaluationDependencies] &&
                evaluationMode is "--package-evaluation" or "--package-failure-cold")
            {
                PackageEvaluation(evaluationRoot, evaluationMod, evaluationModRoot, evaluationActor, evaluationQuest,
                    evaluationStage.Split(',').Select(value => short.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray(), evaluationDependencies,
                    evaluationMode == "--package-failure-cold");
                GetTree().Quit(); return;
            }
            if (args is [var creatureTravelMode, var creatureTravelGame, var creatureTravelMod, var creatureTravelRoot,
                var creatureTravelActor, var creatureTravelQuest, var creatureTravelStage, var creatureTravelExpected, .. var creatureTravelDependencies] &&
                creatureTravelMode is "--creature-travel" or "--creature-travel-arrival")
            {
                await CreatureTravel(creatureTravelGame, creatureTravelMod, creatureTravelRoot, creatureTravelActor, creatureTravelQuest,
                    short.Parse(creatureTravelStage, System.Globalization.CultureInfo.InvariantCulture),
                    short.Parse(creatureTravelExpected, System.Globalization.CultureInfo.InvariantCulture), creatureTravelDependencies,
                    creatureTravelMode == "--creature-travel-arrival");
                GetTree().Quit(); return;
            }
            if (args is ["--creature-stack", var creatureGame, var creatureMod, var creatureModRoot,
                var creatureReferences, .. var creatureDependencies])
            {
                ExerciseCreatureAssembly(creatureGame, creatureMod, creatureModRoot,
                    creatureReferences.Split(','), creatureDependencies);
                GetTree().Quit(); return;
            }
            if (args is ["--dialogue-package", var dialogueRoot, var dialogueMod, var dialogueModRoot, var dialogueActor,
                var dialogueQuest, var dialogueStage, .. var dialogueDependencies])
            {
                await DialoguePackage(dialogueRoot, dialogueMod, dialogueModRoot, dialogueActor, dialogueQuest,
                    short.Parse(dialogueStage, System.Globalization.CultureInfo.InvariantCulture), dialogueDependencies);
                GetTree().Quit(); return;
            }
            if (args is ["--editor-travel", var editorRoot, var editorMod, var editorModRoot, var editorActor,
                var editorQuest, var editorStage, .. var editorDependencies])
            {
                await EditorTravel(editorRoot, editorMod, editorModRoot, editorActor, editorQuest,
                    short.Parse(editorStage, System.Globalization.CultureInfo.InvariantCulture), editorDependencies);
                GetTree().Quit(); return;
            }
            if (args is [var markerMode, var markerRoot, var markerMod, var markerModRoot, var markerActor,
                var markerQuest, var markerStage, .. var markerDependencies] &&
                markerMode is "--marker-travel-cold" or "--marker-travel-failure-cold" or "--marker-travel-room-cold")
            {
                await MarkerTravelCold(markerRoot, markerMod, markerModRoot, markerActor, markerQuest,
                    short.Parse(markerStage, System.Globalization.CultureInfo.InvariantCulture), markerDependencies,
                    failedRoute: markerMode == "--marker-travel-failure-cold",
                    ownedRoom: markerMode == "--marker-travel-room-cold");
                GetTree().Quit(); return;
            }
            if (args is ["--appearance-stack", var appearanceGame, var appearanceMod, var appearanceModRoot,
                var appearanceReferences, .. var appearanceDependencies])
            {
                var setup = new FalloutModStackSelection([new(appearanceMod, appearanceModRoot, appearanceDependencies)])
                    .Resolve(appearanceGame);
                using var appearanceContent = setup.OpenSource();
                ExerciseAppearances(appearanceContent, appearanceReferences.Split(','));
                GetTree().Quit(); return;
            }
            if (args is ["--escort-package", var escortRoot, var escortMod, var escortModRoot, var escortActor,
                var escortQuest, var escortStage, .. var escortDependencies])
            {
                await EscortPackage(escortRoot, escortMod, escortModRoot, escortActor, escortQuest,
                    short.Parse(escortStage, System.Globalization.CultureInfo.InvariantCulture), escortDependencies);
                GetTree().Quit();
                return;
            }
            if (args is ["--reference-package-events", var eventRoot, var eventMod, var eventModRoot,
                var eventActor, var eventQuest, var eventStage, var eventExpectedStage, .. var eventDependencies])
            {
                ReferencePackageEvents(eventRoot, eventMod, eventModRoot, eventActor, eventQuest,
                    short.Parse(eventStage, System.Globalization.CultureInfo.InvariantCulture),
                    short.Parse(eventExpectedStage, System.Globalization.CultureInfo.InvariantCulture), eventDependencies);
                GetTree().Quit();
                return;
            }
            if (args is [var travelMode, var travelRoot, var travelMod, var travelModRoot, var travelActor,
                var travelQuest, var travelStage, .. var travelDependencies] &&
                travelMode is "--package-travel" or "--travel-interruption")
            {
                PackageResults(travelRoot, travelMod, travelModRoot, travelActor, travelQuest,
                    short.Parse(travelStage, System.Globalization.CultureInfo.InvariantCulture), null, travelDependencies,
                    travelMode == "--travel-interruption");
                GetTree().Quit();
                return;
            }
            if (args is ["--package-results", var resultRoot, var resultMod, var resultModRoot, var resultActor,
                var resultQuest, var resultStage, var expectedStage, .. var resultDependencies])
            {
                PackageResults(resultRoot, resultMod, resultModRoot, resultActor, resultQuest,
                    short.Parse(resultStage, System.Globalization.CultureInfo.InvariantCulture),
                    short.Parse(expectedStage, System.Globalization.CultureInfo.InvariantCulture), resultDependencies);
                GetTree().Quit();
                return;
            }
            if (args is ["--package-arrival", var baseRoot, var mod, var modRoot, var arrivalCell,
                var arrivalActors, var arrivalQuest, var arrivalStages, .. var dependencies])
            {
                ExercisePackageArrival(baseRoot, mod, modRoot, arrivalCell, arrivalActors, arrivalQuest, arrivalStages, dependencies);
                GetTree().Quit();
                return;
            }
            if (args is ["--patrol-idles", var patrolRoot, var patrolReference, var patrolPackage])
            {
                ExercisePatrolIdles(patrolRoot, patrolReference, patrolPackage);
                GetTree().Quit();
                return;
            }
            if (args is ["--appearance", var appearanceRoot, .. var references])
            {
                ExerciseAppearances(appearanceRoot, references);
                GetTree().Quit();
                return;
            }
            if (args is ["--idle", var idleRoot, var idleCell, var idleActor, var idleName])
            {
                RuntimeLiveContentSource.Configure(idleRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
                using var source = RuntimeLiveContentSource.Current!;
                using var stack = FalloutPluginStack.Load(source.PluginSources);
                var scene = FalloutCellSceneReader.Read(stack, FalloutDialogueTopic.Find(stack, "CELL", idleCell).FormKey);
                var idleReference = scene.References.Single(reference => reference.EditorId == idleActor);
                var idleSubject = RuntimeNativeNpc.Create(stack, source, idleReference, 0.0142875f, (_, _, _, _) => new StandardMaterial3D());
                AddChild(idleSubject);
                var idle = FalloutActorIdleSource.Resolve(stack, idleName);
                GD.Print($"OPENNV_IDLE_PREFLIGHT source={idle.Form} objects={string.Join(',', idle.Objects.Select(value => value.ModelPath))}");
                idleSubject.PlayIdle(stack, idleName);
                var idleBefore = BonePoses(idleSubject);
                idleSubject._Process(.7);
                if (idleSubject.AnimationError is not null || BonePoses(idleSubject).SequenceEqual(idleBefore)) throw new InvalidDataException("Source idle did not move the actor: " + idleSubject.AnimationError);
                GD.Print($"OPENNV_IDLE_PREFLIGHT_PASS idle={idle.Form} objects={idle.Objects.Count} animated=true ordinaryPresentation=unverified");
                idleSubject.Free(); GetTree().Quit(); return;
            }
            if (args is ["--furniture", var furnitureRoot, var furnitureCell, var furnitureActor, var furnitureQuest, var furnitureStage])
            {
                ExerciseFurniture(furnitureRoot, furnitureCell, furnitureActor, furnitureQuest, furnitureStage);
                GetTree().Quit();
                return;
            }
            if (args.Length is not (4 or 6))
                throw new ArgumentException("Expected owned Data root, CELL FormID, actor reference FormID, and dialogue topic EDID.");
            var (dataRoot, cellHex, referenceHex, topicId) = (args[0], args[1], args[2], args[3]);
            RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            using var records = FalloutPluginStack.Load(content.PluginSources);
            var cell = FalloutCellSceneReader.Read(records, records.RuntimeFormKey(Convert.ToUInt32(cellHex, 16)));
            var reference = cell.References.Single(value => value.FormKey == records.RuntimeFormKey(Convert.ToUInt32(referenceHex, 16)));
            var actor = RuntimeNativeNpc.Create(records, content, reference, 0.0142875f,
                (_, _, _, _) => new StandardMaterial3D());
            AddChild(actor);
            var quests = new FalloutQuestState(records);
            actor.ConfigureAi(records, quests, cell, Placement);
            actor.EvaluatePackages(false);
            actor._Process(0.1);
            if (actor.AnimationError is not null || actor.PackageIdleError is not null || actor.ActiveIdleOwner != "package-idle")
                throw new InvalidOperationException($"Package idle was not bound: {actor.PackageIdleError ?? actor.AnimationError}");
            var before = BonePoses(actor);
            actor._Process(1.5);
            if (BonePoses(actor).SequenceEqual(before)) throw new InvalidOperationException("Package idle did not animate the actor.");
            foreach (var idle in FalloutDialogueTopic.Read(records, topicId).Infos.SelectMany(info => info.Responses)
                .Select(response => response.SpeakerAnimation).OfType<FalloutFormKey>().Distinct())
            {
                var source = FalloutActorIdleSource.Resolve(records, records.GetEffective(idle));
                if (!content.TryRead(source.AnimationPath, null, out var bytes, out _)) throw new FileNotFoundException(source.AnimationPath);
                var nif = FalloutNifFile.Read(bytes);
                var sequence = nif.Roots.Select(nif.ReadObject).OfType<FalloutNifControllerSequence>().Single();
                actor.BeginResponseAnimation(records, idle);
                if (actor.ActiveIdle != idle) throw new InvalidOperationException("Response did not bind its declared IDLE.");
                before = BonePoses(actor);
                actor._Process(0.7);
                if (actor.AnimationError is not null || BonePoses(actor).SequenceEqual(before))
                    throw new InvalidOperationException($"Response idle {idle} did not publish motion: {actor.AnimationError}");
                actor._Process((sequence.StopTime - sequence.StartTime) / sequence.Frequency);
                if (actor.AnimationError is not null) throw new InvalidOperationException(actor.AnimationError);
                if (actor.ActiveIdle is not null) throw new InvalidOperationException("Finite response retained ownership after completion.");
                actor.EndResponseAnimation();
                actor._Process(0.1);
                if (actor.AnimationError is not null || actor.PackageIdleError is not null || actor.ActiveIdleOwner != "package-idle")
                    throw new InvalidOperationException($"Package did not resume after its response override: {actor.PackageIdleError ?? actor.AnimationError}");
                GD.Print($"OPENNV_ACTOR_RESPONSE_ANIMATION_PASS idle={idle} sequence={sequence.Name} complete=source-clock pixels=unverified");
            }
            var family = actor.Appearance.SkeletonPath[..actor.Appearance.SkeletonPath.LastIndexOf('/')];
            if (!content.TryRead(family + "/locomotion/mtidle.kf", null, out var neutralBytes, out var neutralIdentity))
                throw new FileNotFoundException("Owned neutral preview KF is absent.");
            var neutral = FalloutNifFile.Read(neutralBytes);
            var preview = RuntimeNativeNpc.Create(records, content, reference, 0.0142875f, (_, _, _, _) => new StandardMaterial3D());
            AddChild(preview);
            preview.PlayBaseSequence(neutral, neutral.Roots.Select(neutral.ReadControllerSequence).Single(), neutralIdentity);
            var observedBlink = false;
            var blinkWindow = FalloutFaceBlinkSettings.Read(records);
            for (var frame = 0; frame < 60 * (blinkWindow.DelayMaximum + blinkWindow.DownSeconds + blinkWindow.UpSeconds + 1); frame++)
            {
                preview._Process(1.0 / 60);
                if (preview.AnimationError is not null) throw new InvalidOperationException(preview.AnimationError);
                observedBlink |= preview.FaceWeight("BlinkLeft") > 0 && preview.FaceWeight("BlinkRight") > 0;
            }
            if (!observedBlink) throw new InvalidOperationException("Neutral preview animation did not publish bilateral TRI blinking.");
            preview.Free();
            GD.Print("OPENNV_ACTOR_BLINK_PASS source=owned-settings targets=owned-tri chronology=source-facegen-queue pixels=unverified");
            if (args.Length == 6)
            {
                var occupiedPosition = actor.Position;
                var priorPackage = actor.CurrentPackage;
                quests.EnterStage(FalloutDialogueTopic.Find(records, "QUST", args[4]).FormKey,
                    short.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture));
                actor.EvaluatePackages(true);
                actor._Process(0);
                if (actor.AiError is not null || actor.SittingState != 4 || actor.Position != occupiedPosition)
                    throw new InvalidOperationException($"Source package change did not begin a stationary furniture exit: {actor.AiError}");
                var moving = false;
                var priorPosition = actor.Position;
                for (var frame = 0; frame < 60 * 120; frame++)
                {
                    actor._Process(1.0 / 60);
                    if (actor.AiError is not null || actor.AnimationError is not null)
                        throw new InvalidOperationException(actor.AiError ?? actor.AnimationError);
                    moving |= actor.Position != priorPosition;
                    if (actor.Position.DistanceTo(priorPosition) > 0.25f)
                        throw new InvalidOperationException("Actor transition jumped more than 25 cm in one audit frame.");
                    priorPosition = actor.Position;
                    if (actor.SittingState == 0 && !actor.Traveling) break;
                }
                if (!moving || actor.SittingState != 0 || actor.Traveling || actor.CurrentPackage == priorPackage)
                    throw new InvalidOperationException("Source exit and travel failed to reach the newly selected package.");
                GD.Print($"OPENNV_ACTOR_EXIT_TRAVEL_PASS package={actor.CurrentPackage} position={actor.Position} navigation=owned-navm clock=owned-kf pixels=unverified");
            }
            GD.Print("OPENNV_ACTOR_PERFORMANCE_AUDIT_PASS packageIdle=advancing responseIdles=advancing lifecycle=finite-release materials=diagnostic pixels=unverified");
            actor.Free();
            GetTree().Quit();
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private static Transform3D[] BonePoses(RuntimeNativeNpc actor) => Enumerable.Range(0, actor.Skeleton.Node.GetBoneCount())
        .Select(actor.Skeleton.Node.GetBonePose).ToArray();

    private static Transform3D Placement(FalloutPlacedReference reference) => new(
        GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
        GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * 0.0142875f);
}
