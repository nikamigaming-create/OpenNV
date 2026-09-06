using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;

public partial class NativeActorPerformanceAudit : Node
{
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
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
