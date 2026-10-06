using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.Bots;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private void ExerciseOwnedActivatorControl(string gameRoot, string mod, string modRoot,
        string controlSelector, string targetSelector, string[] dependencies, bool selectionOnly = false)
    {
        var installation = new FalloutModStackSelection([new(mod, modRoot, dependencies)]).Resolve(gameRoot);
        RuntimeLiveContentSource.Configure(gameRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            installation.ContentRoots.Skip(1).ToArray(), installation.ActivePlugins, installation.Settings);
        var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var controlRecord = FalloutDialogueTopic.Find(records, "REFR", controlSelector);
        var targetRecord = FalloutDialogueTopic.Find(records, "REFR", targetSelector);
        var control = controlRecord.FormKey; var target = targetRecord.FormKey;
        Require(world.GetLinkedRef(control) == target, "Selected control has a different source XLKR target.");
        Require(world.Get(control).Cell == world.Get(target).Cell, "Selected source control and ACTI belong to different cells.");
        var sourceCell = FalloutCellSceneReader.Read(records, world.Get(control).Cell);
        var references = sourceCell.References.Where(reference => reference.FormKey == control || reference.FormKey == target).ToArray();
        Require(references.Length == 2 && sourceCell.BaseObjects[world.Get(control).Base].Signature == "ACTI" &&
            sourceCell.BaseObjects[world.Get(target).Base].Signature is "ACTI" or "DOOR",
            "Selected native control requires real source ACTI and linked ACTI/DOOR placements.");
        var cell = sourceCell with { References = references };
        world.LoadCell(cell);
        Require(world.Get(control).Script is not null && world.Get(target).Script is not null,
            "Selected authored control requires the actual attached source scripts.");
        var sources = references.Select(reference => records.GetEffective(reference.FormKey))
            .Concat(references.Select(reference => records.GetEffective(reference.Base)))
            .Concat(references.Select(reference => world.Get(reference.FormKey).Script!.Record))
            .DistinctBy(record => record.FormKey).ToArray();
        var hashes = sources.Select(record => Convert.ToHexString(SHA256.HashData(record.ReadData()))).ToArray();
        var prototypes = new Dictionary<FalloutFormKey, RuntimeNativeNifPrototype>();
        var resources = new List<object>(); var roots = new List<Node3D>();
        var units = RuntimeConfiguration.Load().World.GameUnitsToMeters;
        var changes = 0;
        Transform3D Placement(FalloutPlacedReference reference) => new(
            GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
            GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * units);
        (RuntimeNativeReferencePresentation Presentation, RuntimeNativeReferenceEvents Events)
            Bind(FalloutReferenceWorld owner)
        {
            var root = new Node3D(); AddChild(root); roots.Add(root);
            var presentation = new RuntimeNativeReferencePresentation(owner, references, reference =>
            {
                if (!prototypes.TryGetValue(reference.Base, out var prototype))
                {
                    var model = sourceCell.BaseObjects[reference.Base].ModelPath ?? throw new InvalidDataException("Selected ACTI has no source model.");
                    Require(content.TryRead(model, null, out var bytes, out var identity), "Selected ACTI model is absent.");
                    prototype = new RuntimeNativeNifPrototype(bytes, units); prototypes.Add(reference.Base, prototype);
                    resources.Add(new
                    {
                        reference = reference.FormKey.ToString(),
                        model,
                        identity,
                        sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                        bytes = bytes.Length
                    });
                }
                var node = prototype.InstantiatePlaced(Placement(reference));
                node.SetMeta("opennv_reference_form_key", reference.FormKey.ToString()); root.AddChild(node);
                var controllers = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(node).ToArray();
                if (reference.FormKey == target)
                    node.AddChild(new RuntimeNativeDoorMotion(owner.Get(target), controllers, () => changes++));
                return node;
            });
            root.AddChild(presentation);
            foreach (var reference in references) _ = presentation.Resolve(reference.FormKey);
            foreach (var node in NodeTraversal.SelfAndDescendants<Node>(root))
            { node.SetProcess(false); node.SetPhysicsProcess(false); }
            var events = new RuntimeNativeReferenceEvents { ReportDivergence = error => throw new InvalidDataException(error) };
            events.SetProcess(false);
            events.Configure(records, owner, new(records), cell, root, new((_, _) => false,
                _ => throw new InvalidDataException("Owned ACTI event queue received an unexpected automatic effect.")), Placement, units, 1);
            root.AddChild(events);
            return (presentation, events);
        }
        static string Pose(Node3D node) => JsonSerializer.Serialize(NodeTraversal.SelfAndDescendants<Node3D>(node)
            .Select(value => new { path = node.GetPathTo(value).ToString(), basis = value.Transform.Basis.ToString(), position = value.Transform.Origin.ToString() }));
        static string CollisionPose(Node3D node) => JsonSerializer.Serialize(NodeTraversal.SelfAndDescendants<CollisionShape3D>(node)
            .Select(value => new { path = node.GetPathTo(value).ToString(), basis = value.GlobalTransform.Basis.ToString(), position = value.GlobalPosition.ToString() }));
        try
        {
            var live = Bind(world);
            var targetNode = live.Presentation.Nodes[target];
            var motion = targetNode.GetChildren().OfType<RuntimeNativeDoorMotion>().Single();
            var controller = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(targetNode)
                .Single(value => value.HasSequence("Open") && value.HasSequence("Close"));
            Require(NodeTraversal.SelfAndDescendants<CollisionShape3D>(targetNode).Any(), "Selected actual ACTI has no source collision.");
            var closedCollision = CollisionPose(targetNode); var closedPose = Pose(targetNode);
            var effects = new List<FalloutReferenceScriptEffect>();
            var stagesObserved = 0; var messagesObserved = 0; var defaults = 0;
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, effect =>
            {
                effects.Add(effect);
                switch (effect.Kind)
                {
                    case FalloutReferenceEffectKind.Message:
                        messagesObserved++; break;
                    case FalloutReferenceEffectKind.SetStage:
                        Require(effect.Source == control && effect.Target is { } quest && records.GetEffective(quest).Signature == "QUST",
                            "Selected control emitted an invalid stage request.");
                        stagesObserved++; break; // Stage effects are observed, not executed by this component fixture.
                    case FalloutReferenceEffectKind.ScriptActivate:
                        Require(effect.Source == control && effect.Target == target && effect.Argument == target && !effect.Enable,
                            "Selected authored control changed its linked target, action identity or OnActivate suppression.");
                        live.Events.ScriptActivate(target, target, false); defaults++; break;
                    case FalloutReferenceEffectKind.DefaultActivate:
                        live.Events.DefaultActivate(effect.Target ?? effect.Source, effect.Argument); defaults++; break;
                    default: throw new NotSupportedException($"Selected owned ACTI reaches an unowned component effect {effect.Kind}.");
                }
            }, PlayGroup: live.Presentation.PlayGroup, IsAnimPlaying: live.Presentation.IsAnimPlaying));
            var player = records.RuntimeFormKey(0x14);
            var direct = scripts.Activate(target, player);
            Require(direct.Error is null && direct.Blocks == 1 && messagesObserved == 1 && defaults == 0 &&
                world.Get(target).DoorMotion?.OpenState == 3 && Pose(targetNode) == closedPose,
                $"Actual target script did not suppress player default activation: {direct.Error}");
            BotInteractionSnapshot ObserveControl() => new(BotInteractionSourceState.StableEffects(
                RuntimeCoordinator.NativeBotSourceState(world.Get(control)),
                RuntimeCoordinator.NativeBotSourceState(world.Get(target))), null, []);
            var interaction = new BotInteractionEvidence();
            interaction.Begin(control.ToString(), ObserveControl());
            var activation = scripts.Activate(control, player);
            interaction.End(control.ToString(), ObserveControl(), activation.Error is null);
            interaction.Finish(control.ToString(), activation.Error is null);
            Require(activation.Error is null && activation.Blocks == 1 && stagesObserved == 1 && defaults == 0 &&
                live.Presentation.IsAnimPlaying(control, null), $"Actual control did not enter its authored animation: {activation.Error}");
            Require(interaction.Observe(control.ToString(), ObserveControl()) == 1,
                "The source bot did not observe the actual control's original animation-selection effect.");
            if (selectionOnly)
            {
                for (var index = 0; index < sources.Length; index++)
                    Require(Convert.ToHexString(SHA256.HashData(sources[index].ReadData())) == hashes[index],
                        "Owned control-selection evidence changed its source bytes.");
                Require(interaction.Observe(control.ToString(), ObserveControl()) == 1 &&
                    interaction.Observe(target.ToString(), ObserveControl()) == 0,
                    "Owned control-selection evidence repeated or changed its exact caller.");
                GD.Print($"OPENNV_OWNED_CONTROL_SELECTION_PASS control={control} target={target} " +
                    "actualNativeSourceSelection=true linkedSource=true exactCaller=true sourceUnchanged=true " +
                    "stageEffects=observed-only delayedRelaysAndWholeDoor=unverified fixture=true recording=false");
                return;
            }
            var waiting = scripts.Dispatch(control, "GameMode");
            Require(waiting.Error is null && defaults == 0 && !world.Get(target).DoorOpen,
                "Control opened its ACTI before the actual native animation completed.");
            foreach (var clock in NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(live.Presentation.Nodes[control])
                .Where(value => value.SourceController >= 0 && value.Playing))
            {
                var queued = clock.CaptureScriptState()?.PendingSequence;
                clock._Process(clock.FiniteEffectDuration + .01);
                if (queued is not null)
                {
                    Require(clock.ActiveSequence == queued && clock.CaptureScriptState()?.PendingSequence is null,
                        "Control did not hand off its original cycle to the queued source animation.");
                    if (clock.Playing) clock._Process(clock.FiniteEffectDuration + .01);
                }
            }
            Require(!live.Presentation.IsAnimPlaying(control, null), "Selected control has an unfinished authored native clock.");
            var completed = scripts.Dispatch(control, "GameMode");
            Require(completed.Error is null && defaults == 1 && world.Get(target).DoorMotion?.OpenState == 2,
                $"Linked ACTI self default did not enter its real opening clock: {completed.Error}");
            var effectCount = effects.Count; var controlState = JsonSerializer.Serialize(world.Get(control).Capture());
            Require(scripts.Dispatch(control, "GameMode").Error is null && effects.Count == effectCount &&
                controlState == JsonSerializer.Serialize(world.Get(control).Capture()), "Consumed control result repeated its linked activation.");
            controller._Process(controller.FiniteEffectDuration / 2); motion.Synchronize();
            Require(interaction.Observe(control.ToString(), ObserveControl()) == 1,
                "Later source animation clocks or linked-door progression published duplicate activation evidence.");
            var midpoint = Pose(targetNode); var midpointClock = JsonSerializer.Serialize(controller.CaptureScriptState());
            live.Events.ScriptActivate(target, target, false);
            Require(midpointClock == JsonSerializer.Serialize(controller.CaptureScriptState()), "Repeated self default restarted an active owned ACTI clock.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var restored = Bind(cold); var restoredNode = restored.Presentation.Nodes[target];
            var restoredController = NodeTraversal.SelfAndDescendants<RuntimeNifControllerPlayer>(restoredNode)
                .Single(value => value.HasSequence("Open") && value.HasSequence("Close"));
            var restoredMotion = restoredNode.GetChildren().OfType<RuntimeNativeDoorMotion>().Single();
            Require(cold.Get(target).DoorMotion?.OpenState == 2 && Pose(restoredNode) == midpoint &&
                midpointClock == JsonSerializer.Serialize(restoredController.CaptureScriptState()), "Cold source ACTI pose, state or clock changed.");
            restoredController._Process(restoredController.FiniteEffectDuration); restoredMotion.Synchronize();
            Require(cold.Get(target).DoorMotion?.OpenState == 1 && CollisionPose(restoredNode) != closedCollision,
                "Actual ACTI opening did not settle or move its source collision.");
            var coldScripts = new FalloutReferenceScripts(records, cold, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Cold consumed control repeated an effect."),
                PlayGroup: restored.Presentation.PlayGroup, IsAnimPlaying: restored.Presentation.IsAnimPlaying));
            Require(coldScripts.Dispatch(control, "GameMode").Error is null, "Cold completed source control resumed an unconsumed effect.");
            for (var index = 0; index < sources.Length; index++)
                Require(Convert.ToHexString(SHA256.HashData(sources[index].ReadData())) == hashes[index], "Owned ACTI source bytes changed.");
            GD.Print(JsonSerializer.Serialize(new
            {
                schema = "opennv-owned-activator-control/v1",
                runtimeMvid = typeof(RuntimeNativeDoorMotion).Module.ModuleVersionId,
                control = control.ToString(),
                target = target.ToString(),
                cell = cell.Cell.FormKey.ToString(),
                sources = sources.Select((record, index) => new
                {
                    identity = record.FormKey.ToString(),
                    signature = record.Signature,
                    winner = record.Plugin.Name,
                    masters = record.Plugin.Masters,
                    sha256 = hashes[index]
                }),
                resources,
                sourceUnchanged = true,
                stageRequestsObservedOnly = stagesObserved,
                sourcePlayerSuppression = true,
                sourceSelfActivation = true,
                sourceAnimationGate = true,
                collisionMoved = true,
                duplicateClockPreserved = true,
                coldPoseClock = true,
                coldConsumedControl = true,
                changes,
                recording = false,
                fixture = true,
                boundary = "Selected source control/linked ACTI native component only; stage effects, guards, audio, campaign save, ordinary route, exterior and matched parity remain unverified."
            }));
            GD.Print("OPENNV_OWNED_ACTIVATOR_CONTROL_PASS sourceSuppression=true authoredLinkedSelfDefault=true realMotionCollision=true coldClock=true sourceUnchanged=true stageEffects=observed-only fixture=true recording=false campaign=unverified parity=unverified");
        }
        finally
        {
            foreach (var root in roots) root.Free();
            foreach (var prototype in prototypes.Values) prototype.Scene.Root.Free();
        }
    }
}
