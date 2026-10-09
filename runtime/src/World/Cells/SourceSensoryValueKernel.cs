namespace OpenNV.Runtime.World.Cells;

// Private first-party arithmetic proposal. This does not admit a source process,
// discover effect absence, initialize pools, or own mutation/cache persistence.
internal enum SourceSensoryValueDomain { Low, MiddleLow, MiddleHigh, High, Player }
internal sealed record SourceSensoryCache(bool Dirty, float Value);
internal sealed record SourceSensoryNumericInput(int ActorValue, SourceSensoryValueDomain? Domain,
    float? Base, float? FirstModifier, float? SecondModifier, float? ThirdModifier = null,
    SourceSensoryCache? Cache = null);

internal static class SourceSensoryValueKernel
{
    // NPC order is Reference, LowProcess, MiddleProcess. Player order is the
    // source pool indices 0,1,2; their semantic category names are a separate join.
    internal static float RawFloat(SourceSensoryNumericInput input)
    {
        RequireSlot(input.ActorValue);
        var domain = input.Domain ?? throw new NotSupportedException("Sensory value has no source process domain.");
        if (!Enum.IsDefined(domain)) throw new InvalidDataException("Sensory process domain is invalid.");
        if (domain == SourceSensoryValueDomain.High)
        {
            var cache = input.Cache ?? throw new NotSupportedException("High sensory value has no cache receipt.");
            _ = Required(cache.Value);
            // A clean source cache is authoritative, including its older value.
            // The external owner must validate that exact cache/domain receipt.
            if (!cache.Dirty) return cache.Value;
        }
        var basis = Required(input.Base); var first = Required(input.FirstModifier); var second = Required(input.SecondModifier);
        if (domain == SourceSensoryValueDomain.Player)
            return Stored((double)basis + first + second + Required(input.ThirdModifier));
        var low = Stored((double)basis + first + second);
        if (domain == SourceSensoryValueDomain.Low) return low;
        return Stored((double)low + Required(input.ThirdModifier));
    }

    internal static int RawInteger(SourceSensoryNumericInput input)
    {
        RequireSlot(input.ActorValue);
        var domain = input.Domain ?? throw new NotSupportedException("Sensory value has no source process domain.");
        if (!Enum.IsDefined(domain)) throw new InvalidDataException("Sensory process domain is invalid.");
        if (domain == SourceSensoryValueDomain.High) return Integer(Math.Floor(RawFloat(input)));
        var basis = Integer(Math.Floor(Required(input.Base)));
        var first = Required(input.FirstModifier); var second = Required(input.SecondModifier);
        if (domain == SourceSensoryValueDomain.Player)
            return checked(basis + Integer(Math.Truncate(first)) + Integer(Math.Truncate(second)) +
                Integer(Math.Truncate(Required(input.ThirdModifier))));
        var low = Integer(Math.Truncate((double)basis + first + second));
        return domain == SourceSensoryValueDomain.Low ? low : checked(low + Integer(Math.Truncate(Required(input.ThirdModifier))));
    }

    internal static int Visual(SourceSensoryNumericInput input)
    {
        if (input.ActorValue is not (6 or 42)) throw new InvalidDataException("Visual sensory input requires Perception or Sneak.");
        return Integer(Math.Truncate(Bounded(input)));
    }

    internal static int HearingSneak(SourceSensoryNumericInput input)
    {
        if (input.ActorValue != 42) throw new InvalidDataException("Hearing sensory input requires Sneak.");
        return Integer(Math.Floor(Bounded(input)));
    }

    internal static int TargetSneak(SourceSensoryNumericInput target, bool teammate, SourceSensoryNumericInput? player)
    {
        if (target.ActorValue != 42) throw new InvalidDataException("Target sensory input requires Sneak.");
        var value = Visual(target);
        if (!teammate) return value;
        if (player is null || player.ActorValue != 42 || player.Domain != SourceSensoryValueDomain.Player)
            throw new NotSupportedException("Teammate Sneak has no authoritative player value.");
        return Math.Max(value, Visual(player));
    }

    private static float Bounded(SourceSensoryNumericInput input) => input.ActorValue == 6
        ? Math.Clamp(RawFloat(input), 1, 10) : Math.Clamp(RawFloat(input), 0, 100);
    private static void RequireSlot(int value)
    {
        if (value is not (6 or 42 or 48 or 49)) throw new NotSupportedException("Sensory numeric input has no certified metadata.");
    }
    private static float Required(float? value) => value is { } actual && float.IsFinite(actual)
        ? actual : throw new NotSupportedException("Sensory numeric input is absent or non-finite.");
    private static float Stored(double value)
    {
        var stored = (float)value;
        return float.IsFinite(stored) ? stored : throw new InvalidDataException("Sensory value exceeds Float32 storage.");
    }
    private static int Integer(double value) => double.IsFinite(value) && value >= int.MinValue && value <= int.MaxValue
        ? checked((int)value) : throw new InvalidDataException("Sensory value exceeds integer storage.");
}
