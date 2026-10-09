namespace OpenNV.Runtime.Content;

internal enum FalloutSkillPointRounding { Floor, Ceiling, Truncate }
internal enum FalloutPermanentIntelligenceInteger { Floor }
internal enum FalloutSkillPointArithmetic { Real, SignedInteger32 }

// The selected actor-value descriptor owns the Float32 getter bounds. These
// precede integer conversion and are independent of the later rate operand.
internal sealed record FalloutPermanentIntelligenceGetter(float Minimum, float Maximum,
    FalloutPermanentIntelligenceInteger Conversion)
{
    internal void Validate()
    {
        if (!float.IsFinite(Minimum) || !float.IsFinite(Maximum) || Minimum > Maximum ||
            Math.Floor((double)Minimum) < int.MinValue || Math.Floor((double)Maximum) > int.MaxValue ||
            !Enum.IsDefined(Conversion))
            throw new InvalidDataException("Permanent Intelligence getter declaration is invalid.");
    }
    internal int Read(float permanent)
    {
        Validate();
        if (!float.IsFinite(permanent)) throw new InvalidDataException("Permanent Intelligence must be finite.");
        var bounded = Math.Clamp(permanent, Minimum, Maximum);
        return Conversion switch
        {
            FalloutPermanentIntelligenceInteger.Floor => checked((int)Math.Floor((double)bounded)),
            _ => throw new NotSupportedException("Permanent Intelligence has no admitted integer getter."),
        };
    }
}

// A setting is consumed only when the selected executable/dependency contract
// declares that operand. The presence of a GMST cannot select a formula.
internal sealed record FalloutSkillPointOperand(string? Setting, int Constant)
{
    internal static FalloutSkillPointOperand Literal(int value) => new(null, value);
    internal static FalloutSkillPointOperand GameSetting(string name) => new(name, 0);
    internal int Resolve(Func<string, int> settings) => Setting is null ? Constant : settings(Setting);
    internal void Validate()
    {
        if (Setting is not null && (Constant != 0 || Setting.Length < 2 || Setting[0] != 'i' ||
            Setting.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_')))
            throw new InvalidDataException("Skill-point operand has an invalid integer setting identity.");
    }
}

internal sealed record FalloutSkillPointCadence(int IntelligencePeriod, int IntelligenceRemainder,
    int LevelPeriod, int LevelRemainder, int Bonus)
{
    internal void Validate()
    {
        if (IntelligencePeriod <= 0 || IntelligenceRemainder < 0 || IntelligenceRemainder >= IntelligencePeriod ||
            LevelPeriod <= 0 || LevelRemainder < 0 || LevelRemainder >= LevelPeriod)
            throw new InvalidDataException("Skill-point cadence has an invalid modular declaration.");
    }
    internal int Resolve(int intelligence, int level) =>
        intelligence % IntelligencePeriod == IntelligenceRemainder && level % LevelPeriod == LevelRemainder ? Bonus : 0;
}

// Source-neutral affine integer-Intelligence rate, with an independently
// declared rounding and optional modular gained-level adjustment.
internal sealed record FalloutSkillPointRate(FalloutSkillPointOperand Base,
    FalloutSkillPointOperand IntelligenceMultiplier, int IntelligenceOffset, int Divisor,
    int? MinimumTerm, int? MaximumTerm, FalloutSkillPointRounding Rounding,
    FalloutSkillPointCadence? Cadence = null, FalloutSkillPointArithmetic Arithmetic = FalloutSkillPointArithmetic.Real)
{
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Base); ArgumentNullException.ThrowIfNull(IntelligenceMultiplier);
        Base.Validate(); IntelligenceMultiplier.Validate(); Cadence?.Validate();
        if (Divisor <= 0 || MinimumTerm is { } minimum && MaximumTerm is { } maximum && minimum > maximum ||
            !Enum.IsDefined(Rounding) || !Enum.IsDefined(Arithmetic) ||
            Arithmetic == FalloutSkillPointArithmetic.SignedInteger32 &&
                (Divisor != 1 || Rounding != FalloutSkillPointRounding.Truncate))
            throw new InvalidDataException("Skill-point rate declaration is invalid.");
    }

    internal int Points(int integerIntelligence, int gainedLevel, int skillPointBase, int multiplier)
    {
        Validate();
        if (gainedLevel <= 1) throw new InvalidDataException("Skill points require a gained player level.");
        // The rate applies its offset once, then bounds that term. An actor-
        // value getter's independent bounds must never move after this offset.
        long term = Arithmetic == FalloutSkillPointArithmetic.SignedInteger32
            ? unchecked(integerIntelligence + IntelligenceOffset) : (long)integerIntelligence + IntelligenceOffset;
        if (MinimumTerm is { } minimum) term = Math.Max(term, minimum);
        if (MaximumTerm is { } maximum) term = Math.Min(term, maximum);
        var cadence = Cadence?.Resolve(checked((int)term), gainedLevel) ?? 0;
        if (Arithmetic == FalloutSkillPointArithmetic.SignedInteger32)
            return unchecked((int)term * multiplier + skillPointBase + cadence);
        var scaled = (double)term * multiplier / Divisor;
        var rounded = Rounding switch
        {
            FalloutSkillPointRounding.Floor => Math.Floor(scaled),
            FalloutSkillPointRounding.Ceiling => Math.Ceiling(scaled),
            FalloutSkillPointRounding.Truncate => Math.Truncate(scaled),
            _ => throw new NotSupportedException("Skill-point rounding has no admitted owner."),
        };
        var result = skillPointBase + rounded + cadence;
        if (!double.IsFinite(result) || result < int.MinValue || result > int.MaxValue)
            throw new NotSupportedException("Skill-point arithmetic exceeds its admitted signed menu storage.");
        return checked((int)result);
    }
}
