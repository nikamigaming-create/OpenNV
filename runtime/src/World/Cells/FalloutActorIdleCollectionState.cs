using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Collection source ownership is independent of the procedure using it. The
// procedure must supply its expected PACK or IDLM identity and signature.
// This validates the existing declared collection-playback policy, not a
// Sandbox marker cursor: its ordering and wait policy require their own owner.
internal sealed record FalloutActorIdleCollectionState(FalloutFormKey Source, string Sha256,
    FalloutIdleCollectionPlaybackSnapshot Collection, IReadOnlyList<FalloutIdleReplayCooldown> Cooldowns, string? Error,
    FalloutActorPackageIdleAnimation? ActiveAnimation = null)
{
    internal void Validate(FalloutPluginStack records, FalloutFormKey expectedSource, string expectedSignature)
    {
        if (expectedSignature is not ("PACK" or "IDLM") || Source != expectedSource ||
            !FalloutActorFurnitureContinuation.ValidHash(Sha256) || Collection is null ||
            Cooldowns is null || Error is not null && string.IsNullOrWhiteSpace(Error))
            throw new InvalidDataException("Saved idle collection has an invalid source owner.");
        var source = records.GetEffective(Source);
        if (source.Signature != expectedSignature || FalloutActorFurnitureContinuation.RecordHash(source) != Sha256)
            throw new InvalidDataException("Saved idle collection differs from its winning source.");
        var replay = new FalloutIdleReplayState();
        var playback = new FalloutIdleCollectionPlayback(FalloutIdleCollection.ReadSource(source), replay, _ =>
            throw new InvalidOperationException("Restore must not evaluate idle predicates."), _ =>
            throw new InvalidOperationException("Restore must not select an idle random value."));
        playback.Restore(Collection);
        if (ActiveAnimation is { } animation)
        {
            if (Error is not null || Collection.Cursor == 0 || Collection.SelectionCount == 0 ||
                Collection.WaitSeconds != 0 || Collection.Complete)
                throw new InvalidDataException("Active collection animation has no consumed selection or still has a wait/fault.");
            animation.ValidateCollection(records, Source, expectedSignature);
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

    internal FalloutActorIdleCollectionState Copy() => this with
    { Cooldowns = Cooldowns.ToArray(), ActiveAnimation = ActiveAnimation?.Copy() };
}
