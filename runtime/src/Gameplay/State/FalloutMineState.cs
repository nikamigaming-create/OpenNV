using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutMinePhase { Armed, Countdown, Disabled, Detonated }

/// <summary>Authoritative mine continuation, independent of resident model/audio lifetime.</summary>
internal sealed record FalloutMineState(FalloutFormKey Projectile, string SourceSha256,
    FalloutMinePhase Phase = FalloutMinePhase.Armed, double RemainingSeconds = 0,
    FalloutFormKey? TriggerActor = null, bool IgnoresPlayer = false, string? Error = null,
    double BlinkSeconds = 0, bool? LightOn = null)
{
    internal static FalloutMineState Initial(FalloutMineSource source) => new(source.Projectile.Form, source.Sha256);

    internal void Validate()
    {
        if (Projectile.ObjectId is 0 or > FalloutFormKey.ObjectIdMask || string.IsNullOrWhiteSpace(Projectile.OwnerPlugin) ||
            SourceSha256 is not { Length: 64 } || !SourceSha256.All(Uri.IsHexDigit) ||
            !Enum.IsDefined(Phase) || !double.IsFinite(RemainingSeconds) || RemainingSeconds < 0 ||
            !double.IsFinite(BlinkSeconds) || BlinkSeconds < 0 ||
            Phase != FalloutMinePhase.Countdown && RemainingSeconds != 0 ||
            Phase == FalloutMinePhase.Countdown && TriggerActor is null ||
            Phase is FalloutMinePhase.Armed or FalloutMinePhase.Disabled && TriggerActor is not null ||
            TriggerActor is { } actor && (actor.ObjectId is 0 or > FalloutFormKey.ObjectIdMask || string.IsNullOrWhiteSpace(actor.OwnerPlugin)))
            throw new InvalidDataException("Saved mine continuation is invalid.");
    }

    internal void Validate(FalloutMineSource source)
    {
        Validate();
        if (Projectile != source.Projectile.Form || !SourceSha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved mine declaration differs from its winning source.");
        if (Phase == FalloutMinePhase.Disabled && !source.Projectile.CanBeDisabled)
            throw new InvalidDataException("Saved mine is disabled without the source capability.");
    }

    internal FalloutMineState Trigger(FalloutMineSource source, FalloutPluginStack records,
        FalloutFormKey actor, bool creature, float explosives)
    {
        Validate(source);
        if (source.Projectile.Detonates) throw new NotSupportedException("Remote explosives require their source detonation command owner.");
        if (Phase != FalloutMinePhase.Armed) throw new InvalidOperationException("Only an armed mine can begin its proximity countdown.");
        var next = this with
        {
            Phase = FalloutMinePhase.Countdown,
            RemainingSeconds = source.TriggerDelay(records, creature, explosives),
            TriggerActor = actor
        };
        next.Validate(source);
        return next;
    }

    internal FalloutMineState CommandDetonation(FalloutMineSource source, FalloutFormKey actor)
    {
        Validate(source);
        if (!source.Projectile.IsRemoteMine || Phase != FalloutMinePhase.Armed)
            throw new InvalidOperationException("Remote command requires an armed source remote explosive.");
        var next = this with { Phase = FalloutMinePhase.Countdown, RemainingSeconds = 0, TriggerActor = actor };
        next.Validate(source);
        return next;
    }

    internal FalloutMineState Advance(double simulationSeconds)
    {
        Validate();
        if (!double.IsFinite(simulationSeconds) || simulationSeconds < 0)
            throw new InvalidDataException("Mine simulation delta is invalid.");
        if (Phase != FalloutMinePhase.Countdown) return this;
        // Do not mark detonation before its damage/effect owner has committed.
        // A zero remainder is retained and retried or exposed as a fault.
        return this with { RemainingSeconds = Math.Max(0, RemainingSeconds - simulationSeconds) };
    }

    internal FalloutMineState Disable(FalloutMineSource source)
    {
        Validate(source);
        if (!source.Projectile.CanBeDisabled) throw new NotSupportedException("Source projectile cannot be disabled.");
        if (Phase is not (FalloutMinePhase.Armed or FalloutMinePhase.Countdown))
            throw new InvalidOperationException("Mine is no longer armed.");
        return this with
        {
            Phase = FalloutMinePhase.Disabled,
            RemainingSeconds = 0,
            TriggerActor = null,
            BlinkSeconds = 0,
            LightOn = false
        };
    }
}
