using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeReferencePresentation? _nativeReferencePresentation;
    private long _nativePlacementRevision;

    public override void _Process(double delta)
    {
        if (GetTree().Paused) return;
        AdvanceNativePipBoyWorld(delta);
        AdvanceNativePlayerMoves();
        AdvanceNativeDeath();
        if (_nativeDeathPresented) return;
        AdvanceNativeExteriorStreaming(delta);
        if (_nativeReferences is { } world && world.PlacementRevision != _nativePlacementRevision &&
            _nativeCurrentCellRoot is { } root && _nativeActiveCell is { } scene && !_nativeDoorLoading)
        {
            var moved = world.MovedSince(_nativePlacementRevision);
            var cells = scene.Cell.Worldspace is null ? [] : NativeExteriorCells(root).ToArray();
            var updated = world.ComposeResidency(scene, _configuration.World.GameUnitsToMeters, cells.Length == 0 ? null : cells);
            world.ReplaceResidentCell(updated);
            _nativeActiveCell = updated;
            DiscoverNativeCellReferences(updated);
            _nativeReferencePresentation!.SetResidency(updated.References, reference => MaterializeNativeReference(root, updated, reference));
            _nativeReferenceEvents!.SetResidency(updated, root);
            foreach (var instance in moved.Where(instance => world.IsResident(instance.Reference)))
            {
                var reference = updated.References.FirstOrDefault(value => value.FormKey == instance.Reference);
                if (reference is null) continue;
                if (_nativeReferencePresentation.Nodes.GetValueOrDefault(instance.Reference) is { } node ||
                    world.IsEnabled(instance.Reference) && (node = _nativeReferencePresentation.Resolve(instance.Reference)) is not null)
                {
                    var pendingSandboxPlacement = instance.SandboxAdmissionFailure is { SourcePlacement: not null } pending ? pending : null;
                    node.Transform = ReferenceTransform(reference);
                    if (pendingSandboxPlacement is not null && node is RuntimeNativeNpc npc)
                        npc.AcknowledgeSandboxPlacement(pendingSandboxPlacement);
                    if (node is CharacterBody3D body) body.Velocity = Vector3.Zero;
                }
            }
            ObserveNativeResidentReferences(updated);
            foreach (var actor in _nativeReferencePresentation.Actors) actor.UpdateResidentScene(updated);
            _nativePlacementRevision = world.PlacementRevision;
        }
        if (_nativeReferenceEvents?.IsProcessing() != true || GetTree().Paused) return;
        _nativeReferences?.AdvanceDetection(checked((float)delta));
        if (_nativeReferencePresentation is not { } presentation || presentation.Error is not null) return;
        try
        {
            presentation.Advance(delta);
        }
        catch (Exception error) { GD.PushError($"OPENNV_NATIVE_REFERENCE_PRESENTATION_DIVERGENCE {error.Message}"); }
    }

    private void BindNativeReferenceEvents(Node3D root, FalloutCellScene cell)
    {
        foreach (var actor in root.GetChildren().OfType<RuntimeNativeNpc>())
        {
            actor.PackageSpeechBusy = () => (_nativeOpeningStageDriver ??
                throw new NotSupportedException("Package speech has no gameplay owner.")).IsDialogueBusy(actor.Appearance.Reference!.Value);
            actor.NpcDialogueActive = () => (_nativeOpeningStageDriver ??
                throw new NotSupportedException("Package speech has no gameplay owner.")).IsNpcDialogueActive(actor.Appearance.Reference!.Value);
            actor.BeginPackageDialogue = (package, completed) => (_nativeOpeningStageDriver ??
                throw new NotSupportedException("Package dialogue has no gameplay owner."))
                .RequestPackageDialogue(actor.Appearance.Reference!.Value, package, completed);
            actor.ExecutePackageEvent = (program, caller) => (_nativeOpeningStageDriver ??
                throw new NotSupportedException("Package results have no gameplay owner."))
                .ExecutePackageEvent(program, caller);
        }
        foreach (var actor in root.GetChildren().OfType<RuntimeNativeCreature>())
            actor.ExecutePackageEvent = (program, caller) => (_nativeOpeningStageDriver ??
                throw new NotSupportedException("Creature package results have no gameplay owner."))
                .ExecutePackageEvent(program, caller);
        _nativeReferencePresentation = root.GetChildren().OfType<RuntimeNativeReferencePresentation>().Single();
        _nativeReferencePresentation.SynchronizeActorAppearance = actor => SynchronizeNativeNpcAppearance(actor, _nativeActiveCell!);
        if (root.GetChildren().OfType<RuntimeNativeReferenceEvents>().SingleOrDefault() is { } existing)
        {
            _nativeReferenceEvents = existing;
            existing.Player = _nativePlayer;
            if (_nativePlayer is { } player) player.BindGrabEvents = BindNativePlayerGrab;
            existing.ActivateHitCallbacks();
            existing.SetProcess(true);
            return;
        }
        var events = new RuntimeNativeReferenceEvents
        {
            Player = _nativePlayer,
            Interact = ActivateNativeObject,
            ObservePlayerActivationBegin = BeginNativeBotActivation,
            ObservePlayerActivationEnd = EndNativeBotActivation,
            ObservePlayerActivationFinished = (reference, successful) => _botInteractions.Finish(reference.ToString(), successful)
        };
        events.Configure(_nativePluginStack!, _nativeReferences!, _nativeQuestState!, cell, root,
            CreateNativeReferenceEventHost(new((actor, furniture) =>
            {
                if (_nativePluginStack!.RuntimeFormId(actor) == 0x14 && _nativePlayer is null)
                    throw new NotSupportedException("Player furniture query requires an actual resident player.");
                return events.IsCurrentFurniture(actor, furniture);
            }, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.DefaultActivate)
                    throw new NotSupportedException("Native event effect has no gameplay host.");
                events.DefaultActivate(effect.Target ?? effect.Source, effect.Argument);
            }, Globals: _nativeGlobals, Events: _nativeQuestScripts?.Scripts.Events,
                PlayGroup: (reference, group, initialization) => _nativeReferencePresentation!.PlayGroup(reference, group, initialization),
                IsAnimPlaying: (reference, group) => _nativeReferencePresentation!.IsAnimPlaying(reference, group),
                GameTime: _nativeGameTime), _nativeOpeningStageDriver, _nativeQuestScripts?.Scripts.Challenges),
            ReferenceTransform, _configuration.World.GameUnitsToMeters, _configuration.Player.CollisionLayer);
        root.AddChild(events);
        _nativeReferenceEvents = events;
        if (_nativePlayer is { } playerOwner) playerOwner.BindGrabEvents = BindNativePlayerGrab;
        foreach (var reference in events.BoundTriggers)
            _parityObservations.Observe("world/active-cell", $"{cell.Cell.FormKey}/{reference.FormKey}",
                NativeReferenceState(reference, cell.BaseObjects[reference.Base], "source-primitive-contact-owner"));
        GD.Print($"OPENNV_NATIVE_REFERENCE_EVENTS_READY cell={cell.Cell.FormKey} source=winning-reference-scripts");
    }

    // Reference/world/clock/presentation owners do not require a player. Reached
    // gameplay operations still require their real owner; absence is a source fault.
    internal static FalloutReferenceScriptHost CreateNativeReferenceEventHost(FalloutReferenceScriptHost source,
        RuntimeNativeOpeningStageDriver? driver, FalloutChallenges? challenges = null)
    {
        RuntimeNativeOpeningStageDriver Gameplay() => driver ??
            throw new NotSupportedException("Native reference command requires an actual gameplay/player host.");
        return source with
        {
            Apply = effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.DefaultActivate)
                {
                    _ = Gameplay();
                    source.Apply(effect);
                }
                else Gameplay().ApplyReferenceEffect(effect);
            },
            GetButtonPressed = caller => Gameplay().TakeMessageButton(caller),
            IsTalking = actor => Gameplay().IsTalking(actor),
            ActorValue = (actor, name) => Gameplay().ActorValue(actor, name),
            IsPlayerTagSkill = name => Gameplay().IsPlayerTagSkill(name),
            Command = (caller, bindings, command, arguments) => Gameplay().ApplyNativeSourceCommand(caller, bindings, command, arguments),
            IsInCombat = actor => Gameplay().IsInCombat(actor),
            IsInSameCell = (caller, target) => Gameplay().IsInSameCell(caller, target),
            Distance = (caller, target) => Gameplay().ReferenceDistance(caller, target),
            IsInInterior = actor => Gameplay().IsInInterior(actor),
            PlayerLevel = () => Gameplay().Vitals.Level,
            ReadActorValue = (actor, name, kind) => Gameplay().ReadActorValue(actor, name, kind),
            ChangeActorValue = (actor, name, operation, value) => Gameplay().ChangeActorValue(actor, name, operation, value),
            Inventory = driver?.InventoryCommands,
            Challenges = driver is null ? null : challenges,
            HeadingAngle = (caller, target) => Gameplay().ReferenceHeadingAngle(caller, target),
            TagSkills = driver?.PlayerTagSkills,
        };
    }

    private void DiscoverNativeCellReferences(FalloutCellScene cell) =>
        _parityObservations.ReplaceScope("world/active-cell", cell.References.Select(reference =>
            ($"{cell.Cell.FormKey}/{reference.FormKey}", ParityCategoryFor(cell.BaseObjects[reference.Base].Signature),
                NativeReferenceState(reference, cell.BaseObjects[reference.Base], "source"))));

    private void ObserveNativeResidentReferences(FalloutCellScene cell)
    {
        foreach (var reference in cell.References)
        {
            var enabled = _nativeReferences!.IsEnabled(reference.FormKey);
            if (!enabled || _nativeReferencePresentation!.Nodes.ContainsKey(reference.FormKey))
                _parityObservations.Observe("world/active-cell", $"{cell.Cell.FormKey}/{reference.FormKey}",
                    NativeReferenceState(reference, cell.BaseObjects[reference.Base], enabled ? "resident-presentation" : "disabled"));
        }
        foreach (var reference in _nativeReferenceEvents!.BoundTriggers)
            _parityObservations.Observe("world/active-cell", $"{cell.Cell.FormKey}/{reference.FormKey}",
                NativeReferenceState(reference, cell.BaseObjects[reference.Base], "source-primitive-contact-owner"));
    }
}
