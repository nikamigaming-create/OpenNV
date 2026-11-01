using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerVitals
{
    private FalloutBodyPartData? _scriptAnatomy;

    // False means another existing value owner must handle this name. A known
    // health/anatomy name never falls through to SPECIAL or a placed PlayerRef.
    internal bool TryChangeActorValue(string name, string operation, double argument)
    {
        var limb = FalloutBodyPartData.LimbActorValue(name);
        var health = name.Equals("health", StringComparison.OrdinalIgnoreCase);
        if (!health && limb is null) return false;
        var kind = FalloutActorValueCommands.Operation(operation) ??
            throw new NotSupportedException($"Actor-value command {operation} is unbound.");
        var amount = (float)FalloutActorValueCommands.Argument(operation, argument);
        if (kind is not (FalloutActorValueOperation.Damage or FalloutActorValueOperation.Restore))
            throw new NotSupportedException("Player health/anatomy base/permanent mutation has no retained pool owner.");
        var current = State;
        if (health)
        {
            if (kind == FalloutActorValueOperation.Damage) Publish(current.Damage(amount));
            else if (current.ExactHitPoints > 0)
            {
                var remaining = Math.Min(current.MaximumHitPoints, current.ExactHitPoints + amount);
                var displayed = checked((int)MathF.Ceiling(remaining));
                Publish(current with { HitPoints = displayed, HitPointFraction = displayed - remaining });
            }
            return true;
        }
        // The existing humanoid health and ingestible owners use this engine
        // BPTD identity. Its owned fields still supply anatomy and thresholds.
        var anatomy = _scriptAnatomy ??= FalloutBodyPartData.Read(_records.GetEffective(_records.RuntimeFormKey(0x1d)));
        var changed = FalloutAnatomicalActorValues.Change(anatomy, limb!.Value, current.MaximumHitPoints,
            current.LimbDamage, kind, amount);
        Publish(current with { LimbDamage = changed });
        return true;
    }
}
