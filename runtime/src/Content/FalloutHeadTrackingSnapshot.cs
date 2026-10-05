namespace OpenNV.Runtime.Content;

internal sealed record FalloutHeadTrackingSlot(FalloutFormKey? Target, bool Enabled);

internal sealed record FalloutHeadTrackingTargets(float SourceHoldSeconds, float DefaultHoldSeconds,
    IReadOnlyList<FalloutHeadTrackingSlot> Slots, FalloutFormKey? CachedTarget, long Revision, long TargetRevision)
{
    internal void Validate()
    {
        if (!float.IsFinite(SourceHoldSeconds) || SourceHoldSeconds < 0 ||
            !float.IsFinite(DefaultHoldSeconds) || DefaultHoldSeconds > SourceHoldSeconds ||
            Slots is not { Count: 6 } || Revision < 0 || TargetRevision < 0 || TargetRevision > Revision)
            throw new InvalidDataException("Saved head-target lifetime or slot extent is invalid.");
        foreach (var slot in Slots)
        {
            if (slot is null)
                throw new InvalidDataException("Saved head-target slot is absent.");
            if (slot.Target is { } target) FalloutHeadTrackingBinding.ValidateKey(target);
        }
        if (CachedTarget is { } cached) FalloutHeadTrackingBinding.ValidateKey(cached);
        // StopLook deliberately leaves the cache independent of selection and
        // the default timer retains its exact Float32 negative overshoot.
    }

    internal FalloutHeadTrackingTargets Copy() => this with { Slots = Slots.Select(slot => slot with { }).ToArray() };
}

internal sealed record FalloutHeadTrackingBinding(FalloutFormKey Actor, string ActorSha256,
    FalloutFormKey BodyPartSource, string BodyPartSha256, FalloutBodyPartLook? Part,
    FalloutLookSettings Settings, string SkeletonResource, string SkeletonSha256, float UnitsToMetres,
    int? Bone, string? BoneName, int? Parent, string? ParentName, int? NifBlock,
    int? OverrideController, string? OverrideName)
{
    internal void Validate()
    {
        ValidateKey(Actor); ValidateKey(BodyPartSource);
        foreach (var hash in new[] { ActorSha256, BodyPartSha256, SkeletonSha256 })
            if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Saved head source hash is invalid.");
        if (string.IsNullOrWhiteSpace(SkeletonResource) || SkeletonResource.Contains('\\') ||
            !SkeletonResource.StartsWith("meshes/", StringComparison.OrdinalIgnoreCase) ||
            SkeletonResource.Split('/').Any(piece => piece is "" or "." or "..") ||
            !float.IsFinite(UnitsToMetres) || UnitsToMetres <= 0 || Settings is null)
            throw new InvalidDataException("Saved head rig or unit binding is invalid.");
        _ = FalloutLookSettings.Read(key => key switch
        {
            "fMinTrackingDist:LookIK" => Settings.MinimumDistance,
            "fMaxTrackingDist:LookIK" => Settings.MaximumDistance,
            "fAngleMax:LookIK" => Settings.MaximumStepDegrees,
            "fAngleMaxEase:LookIK" => Settings.EasingStepDegrees,
            "fEaseAngleShutOff:LookIK" => Settings.EasingStopDegrees,
            _ => throw new InvalidDataException("Unknown saved LookIK setting."),
        });
        if (Part is null)
        {
            if (Bone is not null || BoneName is not null || Parent is not null || ParentName is not null ||
                NifBlock is not null || OverrideController is not null || OverrideName is not null)
                throw new InvalidDataException("Saved head rig has a pose without an eligible body part.");
        }
        else if (Part.Source != BodyPartSource || Part.BodyPart >= 15 || string.IsNullOrWhiteSpace(Part.TargetNode) ||
            !float.IsFinite(Part.ConeDegrees) || Part.ConeDegrees is < 0 or > 180 ||
            Bone is not >= 0 || Parent is not >= 0 || Parent >= Bone || NifBlock is not >= 0 ||
            BoneName != Part.TargetNode || string.IsNullOrWhiteSpace(ParentName))
            throw new InvalidDataException("Saved head bone or body-part binding is invalid.");
        if ((OverrideController is null) != (OverrideName is null) || OverrideController is < 0 ||
            OverrideName is not null && string.IsNullOrWhiteSpace(OverrideName))
            throw new InvalidDataException("Saved head override controller is invalid.");
    }

    internal static void ValidateKey(FalloutFormKey key)
    {
        if (string.IsNullOrWhiteSpace(key.OwnerPlugin) || key.ObjectId is 0 or > FalloutFormKey.ObjectIdMask)
            throw new InvalidDataException("Saved head target identity is invalid.");
    }
}

internal sealed record FalloutHeadTrackingPose(float[]? Previous, float[]? Authored, float[] Current,
    bool Active, bool Clamped, bool InRange, bool Overridden, float StepRadians, float[]? Target)
{
    internal void Validate()
    {
        Rotation(Current);
        if (Previous is not null) Rotation(Previous);
        if (Authored is not null) Rotation(Authored);
        if (Active && Previous is null || !float.IsFinite(StepRadians) || StepRadians is < 0 or > 3.141603f ||
            Target is not null && (Target.Length != 3 || Target.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException("Saved head pose history is invalid.");
    }

    private static void Rotation(float[] values)
    {
        if (values is not { Length: 4 } || values.Any(value => !float.IsFinite(value)) ||
            values.Sum(value => (double)value * value) <= 1e-20)
            throw new InvalidDataException("Saved head rotation is invalid.");
    }

    internal FalloutHeadTrackingPose Copy() => this with
    {
        Previous = Previous?.ToArray(),
        Authored = Authored?.ToArray(),
        Current = Current.ToArray(),
        Target = Target?.ToArray(),
    };
}

internal sealed record FalloutActorHeadTrackingSnapshot(FalloutHeadTrackingBinding Binding,
    FalloutHeadTrackingTargets Targets, FalloutHeadTrackingPose? Pose, float? OverrideValue, string? Error)
{
    internal void Validate()
    {
        (Binding ?? throw new InvalidDataException("Saved head source binding is absent.")).Validate();
        (Targets ?? throw new InvalidDataException("Saved head targets are absent.")).Validate();
        Pose?.Validate();
        if ((Binding.Part is null) != (Pose is null) ||
            (Binding.OverrideName is null) != (OverrideValue is null) ||
            OverrideValue is { } value && !float.IsFinite(value) || Error is not null && string.IsNullOrWhiteSpace(Error))
            throw new InvalidDataException("Saved head pose/override ownership is incomplete.");
    }

    internal FalloutActorHeadTrackingSnapshot Copy() => this with { Targets = Targets.Copy(), Pose = Pose?.Copy() };
}
