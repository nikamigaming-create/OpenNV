using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutIdleReplayCooldown(FalloutFormKey Idle, string Sha256, float Remaining);

// Collection and replay delays continue independently of its currently selected
// pose. An optional selected animation retains its independent resource/phase.
internal sealed record FalloutActorPackageIdleState(FalloutFormKey Package, string Sha256,
    FalloutIdleCollectionPlaybackSnapshot Collection, IReadOnlyList<FalloutIdleReplayCooldown> Cooldowns, string? Error,
    FalloutActorPackageIdleAnimation? ActiveAnimation = null)
{
    internal void Validate(FalloutPluginStack records, FalloutFormKey package)
    {
        if (Package != package || !FalloutActorFurnitureContinuation.ValidHash(Sha256) || Collection is null ||
            Cooldowns is null || Error is not null && string.IsNullOrWhiteSpace(Error))
            throw new InvalidDataException("Saved idle collection has an invalid owner.");
        var source = records.GetEffective(Package);
        if (source.Signature != "PACK" || FalloutActorFurnitureContinuation.RecordHash(source) != Sha256)
            throw new InvalidDataException("Saved idle collection differs from its winning package.");
        var replay = new FalloutIdleReplayState();
        var playback = new FalloutIdleCollectionPlayback(FalloutScriptPackage.Read(source), replay, _ =>
            throw new InvalidOperationException("Restore must not evaluate idle predicates."));
        playback.Restore(Collection);
        if (ActiveAnimation is { } animation)
        {
            if (Error is not null || Collection.Cursor == 0 || Collection.SelectionCount == 0 ||
                Collection.WaitSeconds != 0 || Collection.Complete)
                throw new InvalidDataException("Active collection animation has no consumed selection or still has a wait/fault.");
            animation.Validate(records, package);
        }
        var seen = new HashSet<FalloutFormKey>();
        foreach (var cooldown in Cooldowns)
        {
            if (cooldown is null || !seen.Add(cooldown.Idle) || !FalloutActorFurnitureContinuation.ValidHash(cooldown.Sha256) ||
                !float.IsFinite(cooldown.Remaining) || cooldown.Remaining <= 0)
                throw new InvalidDataException("Saved idle replay delay is invalid or duplicated.");
            var idle = records.GetEffective(cooldown.Idle);
            if (idle.Signature != "IDLE" || FalloutActorFurnitureContinuation.RecordHash(idle) != cooldown.Sha256 ||
                cooldown.Remaining > FalloutIdleAnimationData.Read(idle).ReplayDelaySeconds)
                throw new InvalidDataException("Saved idle replay delay differs from its winning IDLE.");
        }
    }

    internal FalloutActorPackageIdleState Copy() => this with
    { Cooldowns = Cooldowns.ToArray(), ActiveAnimation = ActiveAnimation?.Copy() };
}
