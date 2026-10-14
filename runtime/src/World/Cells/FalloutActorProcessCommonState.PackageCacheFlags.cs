using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessPackageCacheClear(FalloutPackageCommandFieldInvocation Invocation,
    FalloutProcessSceneQuery Process, Guid Context, uint Mask, uint BeforeFlags, uint AfterFlags, long Changed);

internal sealed partial class FalloutActorProcessCommonState
{
    internal const uint PackageStartFirstCacheMask = 0x2000;
    internal const uint PackageStartSecondCacheMask = 0x1000;

    // Package start borrows the same MiddleHigh cached-value child as the
    // weapon/status query. Each call reads that child independently; its flag
    // stores neither query actor values nor modify the status-valid bit.
    internal FalloutProcessPackageCacheClear ClearPackageStartCacheFlag(FalloutPackageCommandFieldInvocation invocation,
        uint mask, Action<FalloutPackageCommandFieldInvocation> requireActualCommand)
    {
        return PackageCommandStore(invocation, requireActualCommand, query =>
        {
            if (query.Process != invocation.Process || query.Actor != invocation.Actor ||
                invocation.ThreadId != Environment.CurrentManagedThreadId || invocation.SourceCommand == Guid.Empty ||
                mask is not (PackageStartFirstCacheMask or PackageStartSecondCacheMask))
                throw new InvalidDataException("Package cleanup lost its actual command, current child or selected flag writer.");
            var entry = Require(invocation.Actor.Reference);
            var fields = RequireWeaponFields(entry);
            if (!fields.CacheAvailable) throw new NotSupportedException("source-package-start-current-cache-child-writer-unbound");
            var changed = Next();
            var cleared = fields.CacheFlags & ~mask;
            var receipt = new FalloutProcessPackageCacheClear(invocation, query, fields.StatusContext,
                mask, fields.CacheFlags, cleared, changed);
            _current[entry.Source.Reference] = entry with
            {
                SceneFields = entry.SceneFields! with
                {
                    WeaponContinuation = fields with
                    {
                        CacheFlags = cleared,
                        PackageFlagClears = AppendPackageCommandStore(fields.PackageFlagClears, receipt)
                    }
                },
                Changed = changed
            };
            return receipt;
        });
    }

    private static void ValidatePackageCacheClears(FalloutProcessCommonEntry entry,
        FalloutProcessWeaponContinuationFields fields)
    {
        if (fields.PackageFlagClears is null) throw new InvalidDataException("Package cleanup omitted its cache-child writer history.");
        long prior = 0;
        var masks = new Dictionary<Guid, uint>();
        foreach (var clear in fields.PackageFlagClears)
        {
            if (clear is null || clear.Invocation is null || clear.Process is null ||
                clear.Invocation.SourceCommand == Guid.Empty || clear.Invocation.ThreadId < 1 ||
                clear.Invocation.Actor != entry.Source || clear.Invocation.Epoch != entry.Epoch ||
                clear.Invocation.Process != clear.Process.Process || clear.Process.Actor != entry.Source ||
                clear.Process.Epoch != entry.Epoch || clear.Context != fields.StatusContext ||
                clear.Mask is not (PackageStartFirstCacheMask or PackageStartSecondCacheMask) ||
                clear.AfterFlags != (clear.BeforeFlags & ~clear.Mask) || clear.BeforeFlags is not (0 or FalloutPlayerWeaponContinuationSource.StatusCacheMask) ||
                clear.Changed <= prior || clear.Changed > entry.Changed)
                throw new InvalidDataException("Package cleanup changed its actual cached-value child, writer order or independent flags.");
            var previous = masks.GetValueOrDefault(clear.Invocation.SourceCommand);
            if (previous == 0 && clear.Mask != PackageStartFirstCacheMask ||
                previous != 0 && (previous != PackageStartFirstCacheMask || clear.Mask != PackageStartSecondCacheMask))
                throw new InvalidDataException("Package cleanup duplicated or reordered its two child flag stores.");
            masks[clear.Invocation.SourceCommand] = clear.Mask;
            prior = clear.Changed;
        }
    }
}
