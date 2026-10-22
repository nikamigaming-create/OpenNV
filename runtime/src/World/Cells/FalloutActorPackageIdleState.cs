using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutIdleReplayCooldown(FalloutFormKey Idle, string Sha256, float Remaining);

// Collection and replay delays continue independently of its currently selected
// pose. An optional selected animation retains its independent resource/phase.
internal sealed record FalloutActorPackageIdleState(FalloutFormKey Package, string Sha256,
    FalloutIdleCollectionPlaybackSnapshot Collection, IReadOnlyList<FalloutIdleReplayCooldown> Cooldowns, string? Error,
    FalloutActorPackageIdleAnimation? ActiveAnimation = null)
{
    internal void Validate(FalloutPluginStack records, FalloutFormKey package) =>
        new FalloutActorIdleCollectionState(Package, Sha256, Collection, Cooldowns, Error, ActiveAnimation)
            .Validate(records, package, "PACK");

    internal FalloutActorPackageIdleState Copy() => this with
    { Cooldowns = Cooldowns.ToArray(), ActiveAnimation = ActiveAnimation?.Copy() };
}
