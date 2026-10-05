using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutActorStoppedPose
{
    internal static void Validate(FalloutReferenceSnapshot snapshot)
    {
        if (snapshot.PackageBindingFailure is null && snapshot.SelectionFailure is null && snapshot.PendingPackageSelection is null) return;
        if (snapshot.KnockedDown || snapshot.HitReaction is not null)
            throw new InvalidDataException("Stopped AI has an unowned knockdown or hit-reaction composition.");
        if (snapshot.Injury?.Dead == true && snapshot.Ragdoll is null)
            throw new InvalidDataException("Stopped dead AI requires its complete physical corpse capture.");
        if (snapshot.Injury?.Dead == true && snapshot.Engagement?.WeaponHandling is not null)
            throw new NotSupportedException("Stopped corpse weapon handling requires its independent attachment continuation.");
        if (snapshot.Injury?.Dead != true && snapshot.Engagement is { } engagement &&
            (engagement.Action != "idle" || engagement.StartPending || engagement.Animation is null ||
                engagement.Position is null || engagement.Rotation is null))
            throw new InvalidDataException("Stopped living AI requires a captured stationary combat idle.");
    }

    internal static void ValidateTarget(FalloutPluginStack records, FalloutReferenceSnapshot snapshot)
    {
        if (snapshot.PackageBindingFailure is null && snapshot.SelectionFailure is null && snapshot.PendingPackageSelection is null ||
            snapshot.Engagement is not { } engagement) return;
        if (records.RuntimeFormId(engagement.Target) != 0x14 && records.GetEffective(engagement.Target).Signature is not ("ACHR" or "ACRE"))
            throw new InvalidDataException("Stopped AI combat history has no source actor target.");
    }
}
