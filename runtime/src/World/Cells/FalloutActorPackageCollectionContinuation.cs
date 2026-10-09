using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// The native collection owner is separate from POBA/POEA/POCA event idles.
internal sealed record FalloutActorPackageCollectionContinuation(FalloutActorPackageIdleState IdleState,
    long AnimationRevision, FalloutFollowElection? Election = null, ulong? RandomState = null,
    bool UsesPackageBase = false)
{
    internal void Validate()
    {
        if (IdleState is null || IdleState.Collection is null || IdleState.Cooldowns is null ||
            !FalloutActorFurnitureContinuation.ValidKey(IdleState.Package) ||
            !FalloutActorFurnitureContinuation.ValidHash(IdleState.Sha256) ||
            AnimationRevision < 0 || AnimationRevision == long.MaxValue ||
            UsesPackageBase && IdleState.ActiveAnimation is null)
            throw new InvalidDataException("Saved native idle collection has no valid source/revision owner.");
        IdleState.ActiveAnimation?.Validate(); Election?.Validate();
    }

    internal void Validate(FalloutPluginStack records, FalloutFormKey package)
    {
        Validate();
        if (IdleState is null || AnimationRevision < 0 || AnimationRevision == long.MaxValue)
            throw new InvalidDataException("Saved native idle collection has no valid revision owner.");
        IdleState.Validate(records, package);
        Election?.Validate();
        if (IdleState.ActiveAnimation is { } active && active.Revision != AnimationRevision)
            throw new InvalidDataException("Saved native idle selection differs from its published animation revision.");
    }

    internal FalloutActorPackageCollectionContinuation Copy() => this with { IdleState = IdleState.Copy() };
}
