using OpenNV.Runtime.Content;
using System.Collections.Immutable;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessPackageRequestFlagStore(FalloutPackageCommandFieldInvocation Invocation,
    FalloutProcessSceneQuery Process, bool Requested, byte Before, byte After, long Changed);
internal sealed record FalloutProcessPackageStartStore(FalloutPackageCommandFieldInvocation Invocation,
    FalloutProcessSceneQuery Process, byte Before, byte After, long Changed);
internal sealed record FalloutProcessPackageStartFields(byte Value, byte AdjacentByte, bool Available,
    IReadOnlyList<FalloutProcessPackageStartStore> Stores)
{
    internal static FalloutProcessPackageStartFields Constructed => new(0, 0, true, []);
}

internal sealed partial class FalloutActorProcessCommonState
{
    internal const byte PackageRequestMask = 0x20;

    internal FalloutProcessPackageRequestFlagStore StorePackageRequestFlag(FalloutPackageCommandFieldInvocation invocation,
        bool requested, Action<FalloutPackageCommandFieldInvocation> requireActualCommand)
    {
        return PackageCommandStore(invocation, requireActualCommand, process =>
        {
            var current = Require(invocation.Actor.Reference);
            var flags = current.Scalars.Flags;
            var after = requested ? (byte)(flags | PackageRequestMask) : (byte)(flags & ~PackageRequestMask);
            var store = new FalloutProcessPackageRequestFlagStore(invocation, process, requested, flags, after, Next());
            _current[current.Source.Reference] = current with
            {
                Scalars = current.Scalars with { Flags = after },
                Changed = store.Changed,
                RequestFlagStores = AppendPackageCommandStore(current.RequestFlagStores, store)
            };
            return store;
        });
    }

    internal FalloutProcessPackageStartStore StorePackageStartByte(FalloutPackageCommandFieldInvocation invocation,
        byte value, Action<FalloutPackageCommandFieldInvocation> requireActualCommand)
    {
        return PackageCommandStore(invocation, requireActualCommand, process =>
        {
            var current = Require(invocation.Actor.Reference);
            var fields = current.SceneFields?.PackageStart ??
                throw new NotSupportedException("source-package-start-selected-process-byte-unconstructed");
            if (!fields.Available) throw new NotSupportedException("source-package-start-byte-current-writer-unbound");
            var store = new FalloutProcessPackageStartStore(invocation, process, fields.Value, value, Next());
            _current[current.Source.Reference] = current with
            {
                SceneFields = current.SceneFields! with
                {
                    PackageStart = fields with
                    { Value = value, Stores = AppendPackageCommandStore(fields.Stores, store) }
                },
                Changed = store.Changed
            };
            return store;
        });
    }

    private static IReadOnlyList<T> AppendPackageCommandStore<T>(IReadOnlyList<T> history, T store) =>
        (history as ImmutableList<T> ?? ImmutableList.CreateRange(history)).Add(store);

    private T PackageCommandStore<T>(FalloutPackageCommandFieldInvocation invocation,
        Action<FalloutPackageCommandFieldInvocation> requireActualCommand, Func<FalloutProcessSceneQuery, T> store)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(requireActualCommand);
        var process = ReadSceneQueriesForPackageCommand(invocation);
        _busy = true; _packageMutationInvocation = invocation;
        try
        {
            Guard(); var result = store(process); Guard(); return result;
        }
        finally { _packageMutationInvocation = null; _busy = false; }
        void Guard() => Callback(() => { requireActualCommand(invocation); return true; });
    }

    private static void ValidatePackageCommandFlags(FalloutProcessCommonEntry entry, string executable, string stack,
        IReadOnlySet<Guid>? processes)
    {
        if (entry.RequestFlagStores is null) throw new InvalidDataException("Request byte omitted its actual writer history.");
        byte flags = 0; long prior = 0;
        foreach (var store in entry.RequestFlagStores)
        {
            Require(store.Invocation, store.Process, store.Changed);
            if (store.Before != flags || store.After != (store.Requested ? (byte)(flags | PackageRequestMask) : (byte)(flags & ~PackageRequestMask)))
                throw new InvalidDataException("Request byte changed independent bits or omitted its preceding writer.");
            flags = store.After;
        }
        if (entry.Scalars with { Flags = 0 } != FalloutProcessCommonScalars.Constructed || entry.Scalars.Flags != flags)
            throw new InvalidDataException("Common scalars lack their constructor or selected byte writer.");
        foreach (var clear in entry.SceneFields?.WeaponContinuation?.PackageFlagClears ?? [])
            if (processes?.Contains(clear.Invocation.Process) != true ||
                clear.Process.Stack != stack || clear.Process.ExecutableSha256 != executable || clear.Process.Level != entry.Level ||
                clear.Process.GameplayOwnership == Guid.Empty || clear.Process.Changed >= clear.Changed)
                throw new InvalidDataException("Package cache cleanup omitted its actual retained source process publication.");
        if (entry.SceneFields?.PackageStart is { } start)
        {
            if (entry.Level is not (FalloutDetectionProcessLevel.MiddleHigh or FalloutDetectionProcessLevel.High) ||
                start.AdjacentByte != 0 || !start.Available || start.Stores is null)
                throw new InvalidDataException("Package-start byte lost its distinct constructor word.");
            byte value = 0; prior = 0;
            foreach (var store in start.Stores)
            {
                Require(store.Invocation, store.Process, store.Changed);
                if (store.Before != value) throw new InvalidDataException("Package-start byte omitted an actual preceding writer.");
                value = store.After;
            }
            if (start.Value != value) throw new InvalidDataException("Package-start byte differs from its ordered stores.");
        }
        else if (FalloutSourceMainFamily.IsFallout3(executable) && entry.Level is (FalloutDetectionProcessLevel.MiddleHigh or FalloutDetectionProcessLevel.High))
            throw new InvalidDataException("MiddleHigh process omitted its distinct package-start constructor field.");
        void Require(FalloutPackageCommandFieldInvocation invocation, FalloutProcessSceneQuery process, long changed)
        {
            if (invocation is null || process is null || invocation.SourceCommand == Guid.Empty || invocation.ThreadId < 1 ||
                invocation.Actor != entry.Source || invocation.Epoch > entry.Epoch || invocation.Epoch < 1 || invocation.Process != process.Process ||
                processes?.Contains(invocation.Process) != true || process.Actor != entry.Source || process.Epoch != invocation.Epoch ||
                process.Stack != stack || process.ExecutableSha256 != executable ||
                process.GameplayOwnership == Guid.Empty || invocation.Epoch == entry.Epoch && entry.Gameplay is { } lease && process.GameplayOwnership != lease.Ownership ||
                (invocation.Epoch == entry.Epoch ? process.Level != entry.Level : process.Level != FalloutDetectionProcessLevel.Low ||
                    entry.Level != FalloutDetectionProcessLevel.High || entry.Epoch != invocation.Epoch + 1) ||
                changed <= prior || changed > entry.Changed || process.Changed >= changed)
                throw new InvalidDataException("Package byte writer lost actual source/process/thread/epoch/store order.");
            prior = changed;
        }
    }
}
