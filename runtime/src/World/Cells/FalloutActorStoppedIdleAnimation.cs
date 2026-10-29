using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// A previously selected source IDLE can outlive its dialogue voice or script
// invocation. Its clock and residual pose remain independent of a failed PACK
// begin; restoring that pose must not repeat selection or the original result.
internal sealed record FalloutActorStoppedIdleAnimation(string Owner, FalloutActorPackageIdleAnimation Animation,
    FalloutActorIdleSnapshotTransition? Transition = null)
{
    internal void Validate()
    {
        if (Owner is not ("script" or "dialogue-response" or "package-event") || Animation is null)
            throw new InvalidDataException("Stopped source IDLE has an invalid independent owner.");
        Animation.Validate();
        Transition?.Validate();
        if (Transition is not null && Owner != "script")
            throw new InvalidDataException("Snapshot transition belongs to a script idle owner.");
        if (Transition is { Phase.Complete: false } && (Animation.Clock.SourceSeconds != 0 || Animation.Clock.CompletedRepeats != 0))
            throw new InvalidDataException("Snapshot transition advanced its held incoming source phase.");
        if (Transition is { Phase.Started: false } && !Animation.Clock.IncludeStart)
            throw new InvalidDataException("Unstarted snapshot transition consumed its source start boundary.");
    }

    internal void Validate(FalloutPluginStack records)
    {
        Validate();
        Animation.ValidateSource(records);
    }

    internal FalloutActorStoppedIdleAnimation Copy() => this with { Animation = Animation.Copy(), Transition = Transition?.Copy() };
}

internal sealed record FalloutActorIdleSnapshotTransition(FalloutIdleTransition.Snapshot Phase,
    FalloutIdleTransition.Settings Settings, FalloutActorResidualPose Pose,
    IReadOnlyList<FalloutActorIdleSnapshotTarget>? Targets = null)
{
    internal void Validate()
    {
        if (Phase is null || Pose is null || Phase.Slot.Index > 7 || !float.IsFinite(Phase.DurationSeconds) ||
            Phase.DurationSeconds <= 0.009999999776482582 || !double.IsFinite(Phase.ElapsedSeconds) ||
            Phase.ElapsedSeconds < 0 || Phase.ElapsedSeconds > Phase.DurationSeconds ||
            !Phase.Started && (Phase.ElapsedSeconds != 0 || Phase.Complete) ||
            Phase.Complete != (Phase.ElapsedSeconds == Phase.DurationSeconds) ||
            !float.IsFinite(Settings.DefaultBlendSeconds) ||
            !float.IsFinite(Settings.AnimationMultiplier) || Settings.AnimationMultiplier <= 0)
            throw new InvalidDataException("Saved script idle snapshot transition is invalid.");
        Pose.Validate();
        if (Targets is not null)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in Targets)
            {
                if (target is null || !names.Add(target.Name)) throw new InvalidDataException("Idle snapshot targets are duplicated or absent.");
                target.Validate();
            }
        }
    }

    internal FalloutActorIdleSnapshotTransition Copy() => this with { Pose = Pose.Copy(), Targets = Targets?.Select(value => value.Copy()).ToArray() };
}

internal sealed record FalloutActorIdleSnapshotTarget(string Name, byte Priority, IReadOnlyList<float> Position,
    IReadOnlyList<float> Rotation, float Scale)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Position is null || Position.Count != 3 || Position.Any(value => !float.IsFinite(value)) ||
            Rotation is null || Rotation.Count != 4 || Rotation.Any(value => !float.IsFinite(value)) ||
            Math.Abs(Rotation.Sum(value => value * value) - 1) > .001f || !float.IsFinite(Scale) || Scale <= 0)
            throw new InvalidDataException("Idle snapshot external transform is invalid.");
    }
    internal FalloutActorIdleSnapshotTarget Copy() => this with { Position = Position.ToArray(), Rotation = Rotation.ToArray() };
}
