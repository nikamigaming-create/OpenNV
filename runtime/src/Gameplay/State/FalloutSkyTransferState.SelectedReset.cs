using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutSkyTransferState
{
    private static readonly IReadOnlyList<FalloutSkyResetStep> NewVegasResetSteps = Array.AsReadOnly(
        new[] { FalloutSkyResetStep.DirtyStore, FalloutSkyResetStep.OverrideNull, FalloutSkyResetStep.PreviousNull,
            FalloutSkyResetStep.CurrentNull, FalloutSkyResetStep.TransitionFlagClear, FalloutSkyResetStep.TransitionZero,
            FalloutSkyResetStep.Clouds, FalloutSkyResetStep.Precipitation, FalloutSkyResetStep.ImageInstances });
    private static readonly IReadOnlyList<FalloutSkyResetStep> StandaloneResetSteps = Array.AsReadOnly(
        new[] { FalloutSkyResetStep.OverrideNull, FalloutSkyResetStep.PreviousNull, FalloutSkyResetStep.CurrentNull,
            FalloutSkyResetStep.CombinedFlagsStore, FalloutSkyResetStep.TransitionZero,
            FalloutSkyResetStep.Clouds, FalloutSkyResetStep.Precipitation, FalloutSkyResetStep.ImageInstances });

    internal static IReadOnlyList<FalloutSkyResetStep> ResetSteps(FalloutSkyTransferDeclaration source)
    {
        source.Validate(); return source.IsStandalone ? StandaloneResetSteps : NewVegasResetSteps;
    }

    private uint SelectedResetFlags(FalloutSkyResetContext context) =>
        (Flags & ~8u) | 1u | (context.Origin == FalloutSkyResetOrigin.ClimateSelection ? 0x3f00u : 0u);

    private static void ValidateSelectedResetOrigin(FalloutSkyResetContext context)
    {
        if (context.Origin == FalloutSkyResetOrigin.PlayerTransfer)
        {
            if (context.Main is null || context.Main == Guid.Empty || context.Request is null || context.Request == Guid.Empty)
                throw new InvalidDataException("Sky transfer receipt lost its actual Main and pending request.");
        }
        else if (context.Origin != FalloutSkyResetOrigin.ClimateSelection || context.Main is not null || context.Request is not null)
            throw new InvalidDataException("Sky reset receipt introduced another caller or a fabricated transfer association.");
    }

    private void ApplySelectedResetStep(FalloutSkyResetStep step, uint combinedFlags,
        FalloutSkyResetContext context, Action<FalloutSkyResetStep> publishFields)
    {
        switch (step)
        {
            case FalloutSkyResetStep.DirtyStore: Flags |= 1; break;
            case FalloutSkyResetStep.OverrideNull: OverrideWeather = null; publishFields(step); break;
            case FalloutSkyResetStep.PreviousNull: PreviousWeather = null; publishFields(step); break;
            case FalloutSkyResetStep.CurrentNull: CurrentWeather = null; publishFields(step); break;
            case FalloutSkyResetStep.TransitionFlagClear: Flags &= ~8u; break;
            case FalloutSkyResetStep.CombinedFlagsStore:
                if (!Source.IsStandalone) throw new InvalidDataException("Sky reset used another selected flags-store transport.");
                Flags = combinedFlags; break;
            case FalloutSkyResetStep.TransitionZero: TransitionBits = 0; break;
            case FalloutSkyResetStep.Clouds: ResetChild(_cloudBinding, _clouds, "Clouds", context); break;
            case FalloutSkyResetStep.Precipitation: ResetChild(_precipitationBinding, _precipitation, "Precipitation", context); break;
            case FalloutSkyResetStep.ImageInstances: UpdateResetImages(); break;
            default: throw new InvalidDataException("Sky reset has no selected source operation for this step.");
        }
    }
}
