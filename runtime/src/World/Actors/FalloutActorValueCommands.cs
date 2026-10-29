namespace OpenNV.Runtime.World.Actors;

internal enum FalloutActorValueOperation { SetBase, ModifyPermanent, ForceCurrent, Damage, Restore }

// One alias table and pool update rule serve player and reference command hosts.
// Source decoding and consumers of effective values remain separate owners.
internal static class FalloutActorValueCommands
{
    internal static FalloutActorValueOperation? Operation(string name) => name.ToLowerInvariant() switch
    {
        "setav" or "setactorvalue" => FalloutActorValueOperation.SetBase,
        "modav" or "modactorvalue" => FalloutActorValueOperation.ModifyPermanent,
        "forceav" or "forceactorvalue" => FalloutActorValueOperation.ForceCurrent,
        "damageav" or "damageactorvalue" => FalloutActorValueOperation.Damage,
        "restoreav" or "restoreactorvalue" => FalloutActorValueOperation.Restore,
        _ => null,
    };

    // Native command declarations use signed Integer for Set/Mod/Force and
    // Float for Damage/Restore. Integer extraction truncates toward zero;
    // float extraction rounds once to Float32. Keep normalized integers in
    // Double until the value owner writes Float32, including Int32.MaxValue.
    internal static double Argument(string name, double value)
    {
        var operation = Operation(name) ?? throw new NotSupportedException($"Actor-value command {name} is unbound.");
        if (!double.IsFinite(value)) throw new InvalidDataException("Actor-value command argument is non-finite.");
        if (operation is FalloutActorValueOperation.Damage or FalloutActorValueOperation.Restore)
        {
            var amount = (float)value;
            if (!float.IsFinite(amount)) throw new InvalidDataException("Actor-value command argument exceeds Float32 storage.");
            if (amount < 0) throw new NotSupportedException("Negative damage/restore requires the value's damage-direction contract.");
            return amount;
        }
        return Integer(value);
    }

    internal static int Integer(double value)
    {
        if (!double.IsFinite(value)) throw new InvalidDataException("Actor-value integer argument is non-finite.");
        var integer = Math.Truncate(value);
        if (integer < int.MinValue || integer > int.MaxValue)
            throw new NotSupportedException("Actor-value command integer conversion exceeds signed Int32 storage.");
        return (int)integer;
    }

    internal static FalloutActorValue Apply(FalloutActorValue previous, FalloutActorValueOperation operation,
        float amount, float current)
    {
        if (!float.IsFinite(amount) || !float.IsFinite(current))
            throw new InvalidDataException("Actor-value command amount or effective value is non-finite.");
        if (operation is FalloutActorValueOperation.Damage or FalloutActorValueOperation.Restore && amount < 0)
            throw new InvalidDataException("Damage/restore amount must be nonnegative.");
        var result = operation switch
        {
            FalloutActorValueOperation.SetBase => previous with { Base = amount },
            FalloutActorValueOperation.ModifyPermanent => previous with { Permanent = previous.Permanent + amount },
            FalloutActorValueOperation.ForceCurrent => previous with { Permanent = previous.Permanent + (amount - current) },
            FalloutActorValueOperation.Damage => previous with { Damage = previous.Damage - amount },
            FalloutActorValueOperation.Restore => previous with { Damage = Math.Min(0, previous.Damage + amount) },
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        return result.IsFinite ? result : throw new InvalidDataException("Actor-value pool update exceeds finite Float32 storage.");
    }
}
