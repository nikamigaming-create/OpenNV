using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorIdleRequestSnapshot(FalloutFormKey Idle, string IdleSha256, string Owner,
    int State, int SequenceState, string? Resource = null, string? Sha256 = null,
    FalloutActorPackageIdleAnimation? Animation = null, FalloutIdleDeactivationSnapshot? Ease = null,
    FalloutActorIdleSnapshotTransition? SnapshotBlend = null, FalloutFurnitureClipSnapshot? TransitionOther = null)
{
    internal void Validate(FalloutPluginStack? records = null, bool outgoing = false)
    {
        if (!FalloutActorFurnitureContinuation.ValidKey(Idle) || !FalloutActorFurnitureContinuation.ValidHash(IdleSha256) ||
            string.IsNullOrWhiteSpace(Owner) || State is < 1 or > 3 || SequenceState is not (0 or 1 or 2 or 3 or 5))
            throw new InvalidDataException("Saved idle request identity or lifecycle is invalid.");
        Ease?.Validate(); SnapshotBlend?.Validate();
        TransitionOther?.Validate();
        if (State == 1)
        {
            if (outgoing || SequenceState != 0 || Resource is not null || Sha256 is not null || Animation is not null || Ease is not null || SnapshotBlend is not null || TransitionOther is not null)
                throw new InvalidDataException("Pending idle request already owns a sequence.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Resource) || !FalloutActorFurnitureContinuation.ValidHash(Sha256))
                throw new InvalidDataException("Idle request lost its bound sequence identity.");
            if (State == 3 && (SequenceState != 0 || Animation is not null || Ease is not null || SnapshotBlend is not null || TransitionOther is not null) ||
                State == 2 && (SequenceState == 0 || Animation is null || Animation.Idle != Idle ||
                    Animation.Resource != Resource || !Animation.Sha256.Equals(Sha256, StringComparison.OrdinalIgnoreCase)) ||
                outgoing && (State != 2 || SequenceState != 3 || Ease is null || SnapshotBlend is not null) ||
                State == 2 && ((SequenceState is 2 or 3) != (Ease is not null) || (SequenceState == 5) != (SnapshotBlend is not null)) && !outgoing)
                throw new InvalidDataException("Idle request sequence and transition owners disagree.");
        }
        Animation?.Validate(outgoing || SequenceState == 3);
        if (records is null) return;
        var source = records.GetEffective(Idle);
        if (source.Signature != "IDLE" || !FalloutActorFurnitureContinuation.RecordHash(source).Equals(IdleSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved idle request differs from its winning source.");
        Animation?.ValidateSource(records, outgoing || SequenceState == 3);
        if (Resource is not null && !FalloutActorIdleSource.Resolve(records, source).AnimationPath.Equals(Resource, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved idle request resource differs from its source IDLE.");
    }

    internal FalloutActorIdleRequestSnapshot Copy() => this with
    { Animation = Animation?.Copy(), SnapshotBlend = SnapshotBlend?.Copy() };
}

internal sealed record FalloutActorIdleRequestsSnapshot(FalloutActorIdleRequestSnapshot? Current,
    FalloutActorIdleRequestSnapshot? Queued, IReadOnlyList<FalloutActorIdleRequestSnapshot> Outgoing,
    bool RetirementPending, FalloutActorResidualPose Pose, FalloutIdleTransition.Settings Settings,
    IReadOnlyList<FalloutIdleReplayCooldown> Cooldowns)
{
    internal void Validate(FalloutPluginStack? records = null)
    {
        if (Outgoing is null || Outgoing.Any(request => request is null) || Cooldowns is null || Queued is { State: not 1 } ||
            Current is { State: 1 } || Pose is null)
            throw new InvalidDataException("Saved idle request slots or pose are invalid.");
        if (!float.IsFinite(Settings.DefaultBlendSeconds) || !float.IsFinite(Settings.AnimationMultiplier) || Settings.AnimationMultiplier <= 0)
            throw new InvalidDataException("Saved idle request animation settings are invalid.");
        Current?.Validate(records); Queued?.Validate(records);
        foreach (var request in Outgoing) request.Validate(records, outgoing: true);
        Pose.Validate();
        var seen = new HashSet<FalloutFormKey>();
        foreach (var cooldown in Cooldowns)
        {
            if (!seen.Add(cooldown.Idle) || !FalloutActorFurnitureContinuation.ValidKey(cooldown.Idle) ||
                !FalloutActorFurnitureContinuation.ValidHash(cooldown.Sha256) || !float.IsFinite(cooldown.Remaining) || cooldown.Remaining <= 0)
                throw new InvalidDataException("Saved idle request cooldown is invalid or duplicated.");
            if (records is not null)
            {
                var source = records.GetEffective(cooldown.Idle);
                if (source.Signature != "IDLE" || !FalloutActorFurnitureContinuation.RecordHash(source).Equals(cooldown.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    cooldown.Remaining > FalloutIdleAnimationData.Read(source).ReplayDelaySeconds)
                    throw new InvalidDataException("Saved idle request cooldown differs from its source.");
            }
        }
    }

    internal FalloutActorIdleRequestsSnapshot Copy() => this with
    { Current = Current?.Copy(), Queued = Queued?.Copy(), Outgoing = Outgoing.Select(request => request.Copy()).ToArray(), Pose = Pose.Copy(), Cooldowns = Cooldowns.ToArray() };
}
