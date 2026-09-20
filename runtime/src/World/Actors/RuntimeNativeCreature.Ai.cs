using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutPluginStack? _aiRecords;
    private FalloutQuestState? _aiQuests;
    private FalloutReferenceWorld? _aiWorld;
    private FalloutReferenceInstance? _aiState;
    private FalloutPluginRecord? _aiPackage;
    private FalloutFollowPackage? _followPackage;
    private FalloutDialoguePackage? _dialoguePackage;
    private FalloutPackageEvents? _packageEvents;
    private bool _dialogueRequested;
    private bool _evaluateRequested = true;
    private double _packageClock;
    private string? _aiError;
    internal Action<FalloutDialoguePackage, Action>? BeginPackageDialogue { get; set; }
    internal object AiState => new
    {
        package = _aiPackage?.FormKey.ToString(),
        follow = _followPackage,
        dialogue = _dialoguePackage,
        dialogueRequested = _dialogueRequested,
        motion = _aiState?.PackageMotion,
        error = _aiError
    };

    internal void ConfigureAi(FalloutPluginStack records, FalloutQuestState quests, FalloutReferenceWorld world)
    {
        _aiRecords = records; _aiQuests = quests; _aiWorld = world;
        _aiState = world.Get(Appearance.Reference!.Value);
        _packageEvents = new((package, kind) =>
        {
            // Preserve source event order; reached behavior without an owner
            // stays an explicit divergence instead of silently succeeding.
            package.EventPrograms.GetValueOrDefault(kind)?.RequireEmptyScript();
            if (package.Events.GetValueOrDefault(kind) is not null)
                throw new NotSupportedException("Creature package event idle requires its animation owner.");
        });
    }

    internal void EvaluatePackages(bool reset)
    {
        if (_aiRecords is null) throw new NotSupportedException("Creature has no package owner.");
        _aiError = null;
        // Script evaluation may precede native enable/materialization in the
        // same source event. Evaluate on the next resident physics step.
        _evaluateRequested = true;
    }

    private float PackageCondition(FalloutCondition condition) => condition.Function switch
    {
        58 or 59 or 79 or 546 => _aiQuests!.Evaluate(condition),
        35 when condition.Argument1 == 0 => Combat!.PackagePlayer?.ModalInput == true ? 1 : 0,
        50 => _aiState!.TalkedToPlayer ? 1 : 0,
        53 => (float)_aiWorld!.ReadVariable(_aiQuests!, condition.FormArgument1, condition.Argument2),
        63 => Activity.Attacked ? 1 : 0,
        72 => Appearance.Creature == condition.FormArgument1 ? 1 : 0,
        91 => Activity.Alerted ? 1 : 0,
        101 => Activity.WeaponDrawn ? 1 : 0,
        244 => _aiState!.Restrained ? 1 : 0,
        286 => Activity.Sneaking ? 1 : 0,
        287 => Activity.Running ? 1 : 0,
        289 => Activity.InCombat ? 1 : 0,
        _ => throw new NotSupportedException($"Creature package condition {condition.Owner.FormKey}/{condition.Function} is unbound.")
    };

    private void SelectPackage()
    {
        var selected = FalloutAiPackages.Select(_aiRecords!, Appearance.Creature, PackageCondition, _aiState!.Templates);
        if (_aiPackage?.FormKey == selected?.FormKey) return;
        var source = selected is null ? null : FalloutScriptPackage.Read(selected);
        FalloutFollowPackage? follow = null;
        FalloutDialoguePackage? dialogue = null;
        if (source is not null)
        {
            if (source.Procedure == 1) follow = FalloutFollowPackage.Read(selected!);
            else if (source.Procedure == 15)
            {
                dialogue = FalloutDialoguePackage.Read(selected!);
                if (source.LocationType is not null || selected!.ReadSubrecords().Any(field => field.Signature == "PLD2") || dialogue.Type != 0)
                    throw new NotSupportedException("Creature dialogue start/end location or SayTo procedure is unbound.");
            }
            else throw new NotSupportedException($"Creature package {source.Form} procedure {source.Procedure} is unbound.");
        }
        _packageEvents!.Change(source);
        _aiPackage = selected; _followPackage = follow; _dialoguePackage = dialogue; _dialogueRequested = false;
        GD.Print($"OPENNV_CREATURE_PACKAGE reference={Appearance.Reference} package={source?.Form} procedure={source?.Procedure}");
    }

    private Vector3 TargetPosition(FalloutFormKey target)
    {
        if (target == _aiRecords!.RuntimeFormKey(0x14))
            return (Combat!.PackagePlayer ?? throw new InvalidOperationException("Package player is not resident.")).GlobalPosition;
        var node = GetParent().GetChildren().OfType<Node3D>().SingleOrDefault(node =>
            node is RuntimeNativeCreature creature && creature.Appearance.Reference == target ||
            node is RuntimeNativeNpc npc && npc.Appearance.Reference == target);
        return node?.GlobalPosition ?? throw new NotSupportedException($"Package target {target} is not resident.");
    }

    public override void _PhysicsProcess(double delta)
    {
        Combat?.StopPackageMotion();
        if (_aiRecords is null || _aiError is not null || Combat is null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try
        {
            _packageClock -= delta;
            if (_evaluateRequested || _packageClock <= 0)
            {
                _evaluateRequested = false; _packageClock = 10;
                SelectPackage();
            }
            if (_followPackage is { } follow)
                Combat.AdvancePackageMotion(_aiPackage!, TargetPosition(follow.Target), follow.Distance * Skeleton.UnitsToMetres,
                    Combat.PackagePlayer?.Activity.Running == true, delta);
            else if (_dialoguePackage is { } dialogue && !_dialogueRequested)
            {
                var target = TargetPosition(dialogue.Target);
                var distance = dialogue.ActivationDistance * Skeleton.UnitsToMetres;
                if (GlobalPosition.DistanceTo(target) > distance)
                    Combat.AdvancePackageMotion(_aiPackage!, target, distance, false, delta);
                else if (Combat.PackagePlayer?.ModalInput != true)
                {
                    (BeginPackageDialogue ?? throw new NotSupportedException("Creature dialogue has no conversation owner."))
                        (dialogue, () => { _packageEvents!.Complete(); _evaluateRequested = true; });
                    _dialogueRequested = true;
                }
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or FileNotFoundException)
        {
            _aiError = error.Message;
            GD.PushError($"OPENNV_CREATURE_AI_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }
}
