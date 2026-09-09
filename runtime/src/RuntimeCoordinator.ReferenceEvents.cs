using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private RuntimeNativeReferencePresentation? _nativeReferencePresentation;

    public override void _Process(double delta)
    {
        AdvanceNativeExteriorStreaming(delta);
        if (_nativeReferencePresentation is not { } presentation || presentation.Error is not null ||
            _nativeReferenceEvents?.IsProcessing() != true || GetTree().Paused) return;
        try { presentation.Advance(delta); }
        catch (Exception error) { GD.PushError($"OPENNV_NATIVE_REFERENCE_PRESENTATION_DIVERGENCE {error.Message}"); }
    }

    private void BindNativeReferenceEvents(Node3D root, FalloutCellScene cell)
    {
        foreach (var actor in root.GetChildren().OfType<RuntimeNativeNpc>())
            actor.BeginPackageDialogue = (package, completed) => (_nativeOpeningStageDriver ??
                throw new InvalidOperationException("Package dialogue has no gameplay owner."))
                .RequestPackageDialogue(actor.Appearance.Reference!.Value, package, completed);
        _nativeReferencePresentation = root.GetChildren().OfType<RuntimeNativeReferencePresentation>().Single();
        if (root.GetChildren().OfType<RuntimeNativeReferenceEvents>().SingleOrDefault() is { } existing)
        {
            _nativeReferenceEvents = existing;
            existing.SetProcess(true);
            return;
        }
        var events = new RuntimeNativeReferenceEvents { Player = _nativePlayer, Interact = ActivateNativeObject };
        events.Configure(_nativePluginStack!, _nativeReferences!, _nativeQuestState!, cell, root,
            new(events.IsCurrentFurniture, effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.DefaultActivate)
                {
                    if (effect.Argument is { } actor && _nativePluginStack!.RuntimeFormId(actor) != 0x14)
                        throw new NotSupportedException($"Default activation by {actor} has no actor interaction owner.");
                    events.DefaultActivate(effect.Target ?? effect.Source);
                }
                else (_nativeOpeningStageDriver ?? throw new InvalidOperationException("Native event effects have no gameplay host."))
                    .ApplyReferenceEffect(effect);
            }, caller => _nativeOpeningStageDriver!.TakeMessageButton(caller), actor => _nativeOpeningStageDriver!.IsTalking(actor),
                (actor, name) => _nativeOpeningStageDriver!.ActorValue(actor, name), name => _nativeOpeningStageDriver!.IsPlayerTagSkill(name), _nativeGlobals),
            ReferenceTransform, _configuration.World.GameUnitsToMeters, _configuration.Player.CollisionLayer);
        root.AddChild(events);
        _nativeReferenceEvents = events;
        foreach (var reference in events.BoundTriggers)
            _parityObservations.Observe("world/active-cell", $"{cell.Cell.FormKey}/{reference.FormKey}",
                NativeReferenceState(reference, cell.BaseObjects[reference.Base], "source-primitive-contact-owner"));
        GD.Print($"OPENNV_NATIVE_REFERENCE_EVENTS_READY cell={cell.Cell.FormKey} source=winning-reference-scripts");
    }
}
