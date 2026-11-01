using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

// Anatomical AV units are percentages of the source BPTD health threshold,
// not raw HP and not independent generic modifier pools.
internal static class FalloutAnatomicalActorValues
{
    internal static IReadOnlyDictionary<byte, float>? Change(FalloutBodyPartData anatomy, int actorValue,
        float maximumHealth, IReadOnlyDictionary<byte, float>? damage,
        FalloutActorValueOperation operation, float amount)
    {
        if (operation is not (FalloutActorValueOperation.Damage or FalloutActorValueOperation.Restore))
            throw new NotSupportedException("Anatomical base/permanent mutations have no qualified owner.");
        if (!float.IsFinite(maximumHealth) || maximumHealth <= 0 || !float.IsFinite(amount) || amount < 0)
            throw new InvalidDataException("Anatomical mutation requires finite health and a nonnegative amount.");
        var parts = anatomy.Parts.Where(part => part.ActorValue == actorValue && part.HealthPercent > 0).ToArray();
        if (parts.Length == 0) return damage;
        if (parts.Length != 1) throw new NotSupportedException("Anatomical mutation has ambiguous source regions.");
        var part = parts[0];
        var previous = damage?.GetValueOrDefault(part.Type) ?? 0;
        var delta = amount * maximumHealth * part.HealthPercent / 10000;
        var next = operation == FalloutActorValueOperation.Restore ? Math.Max(0, previous - delta) : previous + delta;
        if (!float.IsFinite(previous) || previous < 0 || !float.IsFinite(next))
            throw new InvalidDataException("Anatomical mutation exceeds finite injury storage.");
        return new Dictionary<byte, float>(damage ?? new Dictionary<byte, float>()) { [part.Type] = next };
    }
}
