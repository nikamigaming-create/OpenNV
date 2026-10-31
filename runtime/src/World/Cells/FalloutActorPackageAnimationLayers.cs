using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutPackageAnimationSource(string Resource, string Sha256)
{
    internal void Validate() => FalloutActorAnimationState.Validate(new(Resource, Sha256, 0, true));
}

internal sealed record FalloutPackageAnimationLayer(string Resource, string Sha256, double Seconds, bool StartPending,
    FalloutIdleDeactivationSnapshot? EaseIn = null, FalloutActorIdleSnapshotTransition? Snapshot = null,
    FalloutPackageAnimationSource? EaseFrom = null, float? AccumulationTime = null)
{
    internal void Validate()
    {
        FalloutActorAnimationState.Validate(new(Resource, Sha256, Seconds, StartPending));
        EaseIn?.Validate(); Snapshot?.Validate(); EaseFrom?.Validate();
        if (AccumulationTime is { } accumulation && !float.IsFinite(accumulation))
            throw new InvalidDataException("Package accumulation phase is not finite.");
        if ((EaseIn is null) != (EaseFrom is null) || EaseIn is not null && Snapshot is not null || Snapshot is not null &&
            (Snapshot.Phase.Slot.Index > 1 || Seconds != 0 || !StartPending))
            throw new InvalidDataException("Package movement has conflicting retained transition owners.");
    }
    internal FalloutPackageAnimationLayer Copy() => this with { Snapshot = Snapshot?.Copy() };
}

internal sealed record FalloutPackageAnimationFade(FalloutPackageAnimationLayer Layer,
    FalloutIdleDeactivationSnapshot EaseOut, bool HoldPhase = false, FalloutPackageAnimationSource? Incoming = null, byte Slot = 1)
{
    internal void Validate()
    {
        Layer.Validate(); EaseOut.Validate(); Incoming?.Validate();
        if (Slot > 1 || Layer.EaseIn is not null || Layer.Snapshot is not null || HoldPhase && (Layer.Seconds != 0 || !Layer.StartPending))
            throw new InvalidDataException("Outgoing package layer retains a selected-slot transition.");
    }
}

// Logical movement selection is independent of fading manager sequences and Idle0.
internal sealed record FalloutActorPackageAnimationLayers(FalloutPackageAnimationLayer Idle,
    FalloutPackageAnimationLayer? Movement, IReadOnlyList<FalloutPackageAnimationFade> Outgoing, bool OwnsIdleClock = true)
{
    internal void Validate()
    {
        Idle.Validate(); Movement?.Validate();
        if (Idle.Snapshot is { Phase.Slot.Index: not 0 } || Movement?.Snapshot is { Phase.Slot.Index: not 1 } || Outgoing is null)
            throw new InvalidDataException("Package base idle has an invalid transition owner.");
        if (!OwnsIdleClock && (Movement is not null || Outgoing.Count != 0 || Idle.EaseIn is not null || Idle.Snapshot is not null))
            throw new InvalidDataException("A retired package clock retains active sequence work.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        paths.Add(Idle.Resource);
        if (Movement is not null && !paths.Add(Movement.Resource))
            throw new InvalidDataException("Package slots contain the same source sequence.");
        foreach (var fade in Outgoing)
        {
            if (fade is null || !paths.Add(fade.Layer.Resource))
                throw new InvalidDataException("Package manager has duplicate retained movement sequences.");
            fade.Validate();
        }
    }
    internal FalloutActorPackageAnimationLayers Copy() => this with
    {
        Idle = Idle.Copy(),
        Movement = Movement?.Copy(),
        Outgoing = Outgoing.Select(value => value with { Layer = value.Layer.Copy() }).ToArray()
    };
}
