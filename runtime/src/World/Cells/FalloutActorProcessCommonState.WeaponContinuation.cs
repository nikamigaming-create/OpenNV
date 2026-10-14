using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutProcessWeaponObservedLane { WeaponAction, Activity, StatusCache }
internal sealed record FalloutProcessWeaponActionInitialization(string Source, Guid PlayerConstructor,
    Guid GameplayOwnership, long Epoch, long Changed);
internal sealed record FalloutProcessWeaponWriterObservation(Guid Observation, string Producer,
    FalloutProcessWeaponObservedLane Lane, long Changed);
internal sealed record FalloutProcessWeaponContinuationFields(byte? WeaponAction, long ActionStores,
    FalloutProcessWeaponActionInitialization? Initialization, sbyte Activity,
    Guid StatusContext, IReadOnlyList<uint> CachedScalarBits, uint CacheFlags, long CacheStores,
    bool ActionAvailable, bool ActivityAvailable, bool CacheAvailable,
    IReadOnlyList<FalloutProcessWeaponWriterObservation> Observations,
    int FirstContextWord = -1, int SecondContextWord = -1, Guid? FirstContextPointer = null,
    Guid? SecondContextPointer = null, byte ContextByte = 0)
{
    public IReadOnlyList<FalloutProcessPackageCacheClear> PackageFlagClears { get; init; } = [];
    internal static FalloutProcessWeaponContinuationFields Constructed => new(null, 0, null, 0,
        Guid.NewGuid(), Enumerable.Repeat(FalloutPlayerWeaponContinuationSource.UnqueriedScalarBits,
            FalloutPlayerWeaponContinuationSource.CachedScalarCount).ToArray(),
        0, 0, false, true, true, []);
}
internal sealed record FalloutProcessWeaponActionRead(FalloutProcessSceneQuery Process, byte Value);
internal sealed record FalloutProcessWeaponActivityRead(FalloutProcessSceneQuery Process, sbyte Value);
internal sealed record FalloutProcessActorStatusRead(FalloutProcessSceneQuery Process, Guid Context,
    uint BeforeFlags, uint ScalarBits, uint AfterFlags, bool PoolQueried, long Changed, bool Positive);

internal sealed partial class FalloutActorProcessCommonState
{
    private sealed class FollowingObservationHistory(long epoch, IReadOnlyList<FalloutProcessWeaponWriterObservation> observations)
    {
        internal long Epoch { get; } = epoch;
        internal List<FalloutProcessWeaponWriterObservation> Ordered { get; } = observations.ToList();
        internal HashSet<Guid> Identities { get; } = observations.Select(value => value.Observation).ToHashSet();
    }
    private readonly Dictionary<FalloutFormKey, FollowingObservationHistory> _followingObservations = new(FalloutFormKeyComparer.Instance);
    private bool _followingStatusReading;
    private long _followingStatusFault;
    internal void InitializePlayerWeaponActionConstructor(FalloutFormKey actor, long epoch,
        FalloutPlayerWeaponContinuationSource source, Guid playerConstructor, Action requireActualConstruction)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(requireActualConstruction); requireActualConstruction();
        var process = ReadSceneQueries(actor, epoch); var current = Require(actor);
        var fields = RequireWeaponFields(current);
        if (_cold is not null || !process.Actor.EnginePlayer || process.Level != FalloutDetectionProcessLevel.High || epoch != 1 ||
            source.Controls.Main.EngineSha256 != _source.ExecutableSha256 || playerConstructor == Guid.Empty ||
            current.Phase != FalloutProcessCommonPhase.Constructed || fields.Initialization is not null ||
            fields.ActionStores != 0 || fields.WeaponAction is not null || fields.Observations.Count != 0)
            throw new InvalidOperationException("Player weapon-action initialization changed or replayed its actual constructor.");
        var changed = Next();
        var initialization = new FalloutProcessWeaponActionInitialization(source.Identity, playerConstructor,
            process.GameplayOwnership, epoch, changed);
        _current[actor] = current with
        {
            SceneFields = current.SceneFields! with
            { WeaponContinuation = fields with { WeaponAction = 0, ActionStores = 1, ActionAvailable = true, Initialization = initialization } },
            Changed = changed
        };
    }

    internal FalloutProcessWeaponActionRead ReadPlayerWeaponAction(FalloutFormKey actor, long epoch)
    {
        var process = ReadSceneQueries(actor, epoch); var fields = RequireWeaponFields(Require(actor));
        if (!fields.ActionAvailable || fields.WeaponAction is not { } value)
            throw new NotSupportedException("source-Player-weapon-action-current-writer-unbound");
        return new(process, value);
    }
    internal void RequirePlayerWeaponContinuationSource(FalloutFormKey actor, long epoch,
        FalloutPlayerWeaponContinuationSource source)
    {
        source.Validate(); var process = ReadSceneQueries(actor, epoch); var fields = RequireWeaponFields(Require(actor));
        if (!process.Actor.EnginePlayer || process.ExecutableSha256 != source.Controls.Main.EngineSha256 ||
            fields.Initialization is not { } initialization || initialization.Source != source.Identity ||
            initialization.Epoch != epoch || initialization.GameplayOwnership != process.GameplayOwnership)
            throw new InvalidDataException("Player weapon-action fields changed their original selected constructor source or retained lease.");
    }
    internal FalloutProcessWeaponActivityRead ReadPlayerWeaponActivity(FalloutFormKey actor, long epoch)
    {
        var process = ReadSceneQueries(actor, epoch); var fields = RequireWeaponFields(Require(actor));
        if (!fields.ActivityAvailable) throw new NotSupportedException("source-Player-following-activity-current-writer-unbound");
        return new(process, fields.Activity);
    }
    internal FalloutProcessActorStatusRead ReadPlayerFollowingStatus(FalloutFormKey actor, long epoch, Func<float> readCurrentParalysis)
    {
        ArgumentNullException.ThrowIfNull(readCurrentParalysis);
        if (_followingStatusReading) { ++_followingStatusFault; throw new InvalidOperationException("Actor status pool getter reentered its actual source call."); }
        var process = ReadSceneQueries(actor, epoch); var current = Require(actor); var fields = RequireWeaponFields(current);
        if (!fields.CacheAvailable) throw new NotSupportedException("source-Player-following-status-cache-current-writer-unbound");
        var beforeFlags = fields.CacheFlags; var flags = beforeFlags; var queried = (flags & FalloutPlayerWeaponContinuationSource.StatusCacheMask) == 0;
        var bits = fields.CachedScalarBits[FalloutPlayerWeaponContinuationSource.ParalysisCacheSlot]; var changed = current.Changed;
        if (queried)
        {
            var fault = _followingStatusFault; _followingStatusReading = true;
            float value;
            try { value = readCurrentParalysis(); }
            finally { _followingStatusReading = false; }
            var after = ReadSceneQueries(actor, epoch);
            if (after != process || !ReferenceEquals(Require(actor), current) || fault != _followingStatusFault)
                throw new InvalidOperationException("Status pool query changed its actual process or reentered its cached fields.");
            bits = BitConverter.SingleToUInt32Bits(value);
            var scalars = fields.CachedScalarBits.ToArray(); scalars[FalloutPlayerWeaponContinuationSource.ParalysisCacheSlot] = bits;
            _current[actor] = current = current with
            {
                SceneFields = current.SceneFields! with
                { WeaponContinuation = fields = fields with { CachedScalarBits = scalars, CacheStores = fields.CacheStores + 1 } },
                Changed = Next()
            };
            flags |= FalloutPlayerWeaponContinuationSource.StatusCacheMask;
            _current[actor] = current with
            {
                SceneFields = current.SceneFields! with
                { WeaponContinuation = fields with { CacheFlags = flags, CacheStores = fields.CacheStores + 1 } },
                Changed = changed = Next()
            };
        }
        return new(process, fields.StatusContext, beforeFlags,
            bits, flags, queried, changed, BitConverter.UInt32BitsToSingle(bits) > 0);
    }

    internal void ObservePlayerWeaponContinuationWriter(FalloutFormKey actor, long epoch,
        Guid observation, string producer, FalloutProcessWeaponObservedLane lane)
    {
        _ = ReadSceneQueries(actor, epoch); var current = Require(actor); var fields = RequireWeaponFields(current);
        if (observation == Guid.Empty || string.IsNullOrWhiteSpace(producer) || !Enum.IsDefined(lane))
            throw new InvalidOperationException("Weapon continuation lost or replayed its actual source writer observation.");
        if (!_followingObservations.TryGetValue(actor, out var history) || history.Epoch != epoch)
            _followingObservations[actor] = history = new(epoch, fields.Observations);
        if (!history.Identities.Add(observation))
            throw new InvalidOperationException("Weapon continuation source writer observation was replayed.");
        var changed = Next();
        history.Ordered.Add(new(observation, producer, lane, changed));
        fields = fields with
        {
            ActionAvailable = lane == FalloutProcessWeaponObservedLane.WeaponAction ? false : fields.ActionAvailable,
            ActivityAvailable = lane == FalloutProcessWeaponObservedLane.Activity ? false : fields.ActivityAvailable,
            CacheAvailable = lane == FalloutProcessWeaponObservedLane.StatusCache ? false : fields.CacheAvailable,
            Observations = history.Ordered.AsReadOnly()
        };
        _current[actor] = current with { SceneFields = current.SceneFields! with { WeaponContinuation = fields }, Changed = changed };
    }
    private static FalloutProcessWeaponContinuationFields RequireWeaponFields(FalloutProcessCommonEntry current) =>
        current.SceneFields?.WeaponContinuation ?? throw new NotSupportedException("source-Player-following-weapon-current-fields-unconstructed");
    private static FalloutProcessCommonEntry CaptureFollowingObservationHistory(FalloutProcessCommonEntry entry) =>
        entry.SceneFields?.WeaponContinuation is { } fields ? entry with
        {
            SceneFields = entry.SceneFields with
            {
                WeaponContinuation = fields with
                {
                    CachedScalarBits = fields.CachedScalarBits.ToArray(),
                    Observations = fields.Observations.ToArray(),
                    PackageFlagClears = Array.AsReadOnly(fields.PackageFlagClears.ToArray())
                }
            }
        } : entry;

    private static void ValidateWeaponContinuationFields(FalloutProcessCommonEntry entry)
    {
        var fields = entry.SceneFields?.WeaponContinuation;
        if (entry.SceneFields is null) { if (fields is not null) throw new InvalidDataException("Lower process acquired foreign weapon continuation fields."); return; }
        if (fields is null || fields.StatusContext == Guid.Empty || fields.ActionStores is < 0 or > 1 || fields.Activity != 0 ||
            fields.CacheStores is < 0 or > 2 || fields.CachedScalarBits is not { Count: FalloutPlayerWeaponContinuationSource.CachedScalarCount } ||
            fields.FirstContextWord != -1 || fields.SecondContextWord != -1 || fields.FirstContextPointer is not null ||
            fields.SecondContextPointer is not null || fields.ContextByte != 0 ||
            fields.CachedScalarBits.Where((bits, index) => index != FalloutPlayerWeaponContinuationSource.ParalysisCacheSlot &&
                bits != FalloutPlayerWeaponContinuationSource.UnqueriedScalarBits).Any() ||
            fields.CacheFlags is not (0 or FalloutPlayerWeaponContinuationSource.StatusCacheMask) ||
            fields.CacheFlags != 0 && fields.CacheStores != 2 || fields.CacheFlags == 0 && fields.CacheStores > 1 ||
            fields.CacheStores == 0 && fields.CachedScalarBits[FalloutPlayerWeaponContinuationSource.ParalysisCacheSlot] != FalloutPlayerWeaponContinuationSource.UnqueriedScalarBits ||
            fields.Observations is null || fields.Observations.Select(value => value.Observation).Distinct().Count() != fields.Observations.Count ||
            fields.Observations.Any(value => value.Observation == Guid.Empty || string.IsNullOrWhiteSpace(value.Producer) || !Enum.IsDefined(value.Lane) || value.Changed < 1 || value.Changed > entry.Changed) ||
            fields.ActionAvailable != (fields.Initialization is not null && fields.Observations.All(value => value.Lane != FalloutProcessWeaponObservedLane.WeaponAction)) ||
            fields.ActivityAvailable != fields.Observations.All(value => value.Lane != FalloutProcessWeaponObservedLane.Activity) ||
            fields.CacheAvailable != fields.Observations.All(value => value.Lane != FalloutProcessWeaponObservedLane.StatusCache))
            throw new InvalidDataException("Process weapon continuation lost its independent constructor, status cache or writer prefix.");
        ValidatePackageCacheClears(entry, fields);
        long priorChanged = fields.Initialization?.Changed ?? 0;
        foreach (var observation in fields.Observations)
        {
            if (observation.Changed <= priorChanged) throw new InvalidDataException("Weapon continuation lost its ordered actual writer history.");
            priorChanged = observation.Changed;
        }
        if (fields.Initialization is { } initializer)
        {
            if (!entry.Source.EnginePlayer || entry.Level != FalloutDetectionProcessLevel.High || entry.Epoch != initializer.Epoch ||
                fields.WeaponAction != 0 || fields.ActionStores != 1 || initializer.PlayerConstructor == Guid.Empty || initializer.GameplayOwnership == Guid.Empty ||
                initializer.Source is not { Length: 64 } || !initializer.Source.All(Uri.IsHexDigit) || initializer.Changed < 1 || initializer.Changed > entry.Changed ||
                entry.Gameplay is { } gameplay && initializer.GameplayOwnership != gameplay.Ownership)
                throw new InvalidDataException("Weapon-action byte lacks its genuine original Player constructor writer.");
        }
        else if (fields.WeaponAction is not null || fields.ActionStores != 0)
            throw new InvalidDataException("Uninitialized process weapon-action byte was promoted to constructor zero.");
    }
}
