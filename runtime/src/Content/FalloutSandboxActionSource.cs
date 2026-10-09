using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal enum FalloutSandboxAction
{
    Furniture = 0,
    Sleeping = 1,
    Eating = 2,
    Wandering = 3,
    IdleMarker = 4,
    Dialogue = 5,
}

internal sealed record FalloutSandboxDurationInputs(float Base, float DurationMultiplier,
    float EnergyMultiplier, float ActionEnergyMultiplier, float RangeMultiplier, byte Energy)
{
    internal void Validate()
    {
        if (!float.IsFinite(Base) || !float.IsFinite(DurationMultiplier) || !float.IsFinite(EnergyMultiplier) ||
            !float.IsFinite(ActionEnergyMultiplier) || !float.IsFinite(RangeMultiplier))
            throw new InvalidDataException("Sandbox duration settings are not finite.");
    }
}

// The two original instruction families differ at intermediate Float32 stores.
// The source image selects arithmetic; a plugin title or available setting does
// not select a consumer. Reads use the live shared GMST owner every election.
internal sealed record FalloutSandboxActionSource(string EngineSha256, bool StoreEachArithmeticResult)
{
    internal static FalloutSandboxActionSource Read(FalloutAdvancementRuntimeReceipt receipt)
    {
        receipt.Validate();
        return receipt.EngineSha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => new(receipt.EngineSha256, false),
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => new(receipt.EngineSha256, true),
            _ => throw new NotSupportedException("Selected executable has no reviewed Sandbox arithmetic and action classifier."),
        };
    }

    internal static bool Vetoed(FalloutSandboxPackage source, FalloutSandboxAction action) => action switch
    {
        FalloutSandboxAction.Furniture => source.NoFurniture,
        FalloutSandboxAction.Sleeping => source.NoSleeping,
        FalloutSandboxAction.Eating => source.NoEating,
        FalloutSandboxAction.Wandering => source.NoWandering,
        FalloutSandboxAction.IdleMarker => source.NoIdleMarkers,
        FalloutSandboxAction.Dialogue => source.NoConversation,
        _ => throw new InvalidDataException("Sandbox selected an unknown action class."),
    };

    internal static byte Energy(FalloutPluginStack records, FalloutPluginRecord actor,
        FalloutActorTemplateSelection? templates = null)
    {
        var owner = FalloutActorTemplateOwner.Resolve(records, actor, 16, templates);
        var rows = owner.ReadSubrecords().Where(row => row.Signature == "AIDT").ToArray();
        if (rows.Length != 1 || rows[0].Data.Length != 20)
            throw new InvalidDataException("Sandbox energy has no complete winning AI-data template declaration.");
        return rows[0].Data.Span[2];
    }

    internal static FalloutSandboxDurationInputs DurationInputs(FalloutPluginStack records,
        FalloutSandboxAction action, byte energy)
    {
        var suffix = action switch
        {
            FalloutSandboxAction.Furniture or FalloutSandboxAction.Sleeping => "Furniture",
            FalloutSandboxAction.Eating => "Eating",
            FalloutSandboxAction.Wandering or FalloutSandboxAction.Dialogue => "Wandering",
            FalloutSandboxAction.IdleMarker => "IdleMarker",
            _ => throw new InvalidDataException("Sandbox duration action class is unknown."),
        };
        var value = new FalloutSandboxDurationInputs(records.NumericSettings.Float("fSandboxDurationBase"),
            records.NumericSettings.Float("fSandboxDurationMult" + suffix),
            records.NumericSettings.Float("fSandboxEnergyMult"),
            records.NumericSettings.Float("fSandboxEnergyMult" + suffix),
            records.NumericSettings.Float("fSandboxDurationRangeMult"), energy);
        value.Validate(); return value;
    }

    internal float Duration(FalloutSandboxDurationInputs input, Func<float, float, float> uniform)
    {
        ArgumentNullException.ThrowIfNull(uniform); input.Validate();
        var duration = (float)((double)input.Base * input.DurationMultiplier);
        var energy = (float)((double)input.EnergyMultiplier * input.ActionEnergyMultiplier);
        var center = StoreEachArithmeticResult
            ? (float)((float)(1f + (float)(input.Energy * energy)) * duration)
            : (float)((1d + input.Energy * (double)energy) * duration);
        var lower = (float)((double)center * (StoreEachArithmeticResult ? (float)(1f - input.RangeMultiplier) : 1d - input.RangeMultiplier));
        var upper = (float)((double)center * (StoreEachArithmeticResult ? (float)(1f + input.RangeMultiplier) : 1d + input.RangeMultiplier));
        if (!float.IsFinite(lower) || !float.IsFinite(upper) || lower < 0 || upper < lower)
            throw new InvalidDataException("Sandbox source duration interval is invalid.");
        // The genuine current RNG is supplied by the actor lifetime. This owner
        // does not seed, substitute another cue stream or claim retail draws.
        var result = uniform(lower, upper);
        if (!float.IsFinite(result) || result < lower || result > upper)
            throw new InvalidDataException("Sandbox duration RNG returned outside its source interval.");
        return result;
    }

    internal static (uint Minimum, uint Maximum) RescanInterval(FalloutPluginStack records)
    {
        var minimum = unchecked((int)records.NumericSettings.IntegerBits("iMinSandboxRescanSeconds"));
        var maximum = unchecked((int)records.NumericSettings.IntegerBits("iMaxSandboxRescanSeconds"));
        if (minimum < 0 || maximum < minimum || maximum > int.MaxValue / 1000)
            throw new InvalidDataException("Sandbox source rescan millisecond interval is invalid.");
        return (checked((uint)minimum * 1000), checked((uint)maximum * 1000));
    }

    internal static uint RepeatMilliseconds(FalloutPluginStack records)
    {
        var seconds = unchecked((int)records.NumericSettings.IntegerBits("iSandBoxPreventRepeatedActionTime"));
        if (seconds < 0 || seconds > int.MaxValue / 1000)
            throw new InvalidDataException("Sandbox repeated-reference source deadline is invalid.");
        return checked((uint)seconds * 1000);
    }
}
