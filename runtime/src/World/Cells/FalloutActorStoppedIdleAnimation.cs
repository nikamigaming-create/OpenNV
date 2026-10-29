using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

// A previously selected source IDLE can outlive its dialogue voice or script
// invocation. Its clock and residual pose remain independent of a failed PACK
// begin; restoring that pose must not repeat selection or the original result.
internal sealed record FalloutActorStoppedIdleAnimation(string Owner, FalloutActorPackageIdleAnimation Animation,
    FalloutActorIdleSnapshotTransition? Transition = null, FalloutCreatureScriptIdlePhase? ScriptPhase = null)
{
    private bool OwnsCompletedClock => ScriptPhase is { RequestState: 2, SequenceState: 3, Deactivation: not null };

    internal void Validate()
    {
        if (Owner is not ("script" or "dialogue-response" or "package-event") || Animation is null)
            throw new InvalidDataException("Stopped source IDLE has an invalid independent owner.");
        Animation.Validate(allowCompleted: OwnsCompletedClock);
        Transition?.Validate();
        ScriptPhase?.Validate(Transition);
        if (ScriptPhase is not null && Owner != "script")
            throw new InvalidDataException("Creature return phase belongs to a script idle owner.");
        if (Transition is not null && Owner != "script")
            throw new InvalidDataException("Snapshot transition belongs to a script idle owner.");
        if (Transition is { Phase.Complete: false } && (Animation.Clock.SourceSeconds != 0 || Animation.Clock.CompletedRepeats != 0))
            throw new InvalidDataException("Snapshot transition advanced its held incoming source phase.");
        if (ScriptPhase is { SequenceState: 5 } && (Animation.Clock.SourceSeconds != 0 || Animation.Clock.CompletedRepeats != 0))
            throw new InvalidDataException("Held creature sequence advanced its source phase.");
        if (Transition is { Phase.Started: false } && !Animation.Clock.IncludeStart)
            throw new InvalidDataException("Unstarted snapshot transition consumed its source start boundary.");
    }

    internal void Validate(FalloutPluginStack records)
    {
        Validate();
        Animation.ValidateSource(records, allowCompleted: OwnsCompletedClock);
    }

    internal FalloutActorStoppedIdleAnimation Copy() => this with { Animation = Animation.Copy(), Transition = Transition?.Copy() };
}

// The request stays active through ordinary sequence ease-out. Its finite KF
// clock may already be clamped while this independent deactivation owns pose.
internal sealed record FalloutCreatureScriptIdlePhase(int RequestState, int SequenceState,
    FalloutIdleDeactivationSnapshot? Deactivation = null)
{
    internal void Validate(FalloutActorIdleSnapshotTransition? transition)
    {
        if (RequestState != 2 || SequenceState is not (1 or 3 or 5) || transition is null ||
            SequenceState != 5 && !transition.Phase.Complete ||
            (SequenceState == 3) != (Deactivation is not null))
            throw new InvalidDataException("Creature script request, sequence and transition phases disagree.");
        Deactivation?.Validate();
        if (Deactivation is { } fade && (!fade.Started || fade.DurationSeconds <= 0 || fade.ElapsedSeconds >= fade.DurationSeconds))
            throw new InvalidDataException("An inactive creature sequence cannot retain its execution slot.");
    }
}

internal sealed record FalloutActorIdleSnapshotTransition(FalloutIdleTransition.Snapshot Phase,
    FalloutIdleTransition.Settings Settings, FalloutActorResidualPose Pose,
    IReadOnlyList<FalloutActorIdleSnapshotTarget>? Targets = null, IReadOnlyList<FalloutNifSnapshotScalar>? Scalars = null)
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
        if (Scalars is not null)
        {
            var targets = new HashSet<FalloutNifScalarTarget>();
            foreach (var scalar in Scalars)
            {
                if (scalar is null || !targets.Add(scalar.Target))
                    throw new InvalidDataException("Idle snapshot scalar targets are duplicated or absent.");
                scalar.Validate();
            }
        }
    }

    internal FalloutActorIdleSnapshotTransition Copy() => this with
    { Pose = Pose.Copy(), Targets = Targets?.Select(value => value.Copy()).ToArray(), Scalars = Scalars?.ToArray() };
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
