using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutNewVegasDeferredBuffer(uint SkipBit, bool Secondary, uint Growth, uint Capacity,
    IReadOnlyList<Guid> Objects);
internal sealed record FalloutNewVegasDeferredCaller(int ManagedThread, byte Enabled, Guid Constructor);
internal sealed record FalloutNewVegasDeferredAttempt(Guid? Main, int ManagedThread, bool TryLock,
    bool BusyReturn, bool LockReturn, byte? CallerBefore, int PrimaryQueues, int SecondaryQueues,
    bool Complete, string? Error);
internal sealed record FalloutNewVegasDeferredReleaseSnapshot(string Schema,
    FalloutNewVegasDeferredReleaseSource Source, FalloutMainPlayerCellSource Player, string Stack,
    Guid Process, Guid Constructor, long Sequence, uint SkipMask,
    IReadOnlyList<FalloutNewVegasDeferredBuffer> Buffers,
    IReadOnlyList<FalloutNewVegasDeferredCaller> Callers,
    FalloutNewVegasDeferredAttempt? Last, string? Error);

// This owns the original queue, recursive-lock and compiler-thread constructors.
// It does not identify a rendered node with an original allocation/refcount.
// Any actual object factory requiring an unbound enqueue remains a failure.
internal sealed class FalloutNewVegasDeferredRelease
{
    internal const string Schema = "opennv-newvegas-deferred-release/v1";
    private sealed class Buffer(uint skip, bool secondary, uint growth, uint capacity)
    {
        internal readonly uint Skip = skip, Growth = growth;
        internal readonly bool Secondary = secondary;
        internal readonly Guid?[] Storage = new Guid?[capacity];
        internal readonly List<Guid> Objects = [];
        internal FalloutNewVegasDeferredBuffer Capture() => new(Skip, Secondary, Growth, (uint)Storage.Length, Objects.ToArray());
    }
    private static readonly uint[] PrimaryOrder = [16, 8, 4, 2, 1, 32];
    private static readonly uint[] SecondaryOrder = [1, 2, 4, 16, 32];
    private readonly FalloutNewVegasDeferredReleaseSource _source;
    private readonly FalloutMainPlayerCellSource _player;
    private readonly string _stack;
    private readonly Guid _process, _constructor = Guid.NewGuid();
    private readonly object _drainLock = new(), _rawLock = new(), _allocationLock = new();
    private readonly Buffer[] _primary, _secondary;
    private readonly Dictionary<int, FalloutNewVegasDeferredCaller> _callers = [];
    private readonly Dictionary<Guid, IFalloutSourceDeferredObject> _unboundFactories = [];
    private uint _skipMask;
    private long _sequence;
    private bool _busy, _retired;
    private string? _error;
    private FalloutNewVegasDeferredAttempt? _last;

    internal FalloutNewVegasDeferredRelease(FalloutMainPlayerCellSource player, string stack, Guid process,
        FalloutNewVegasDeferredReleaseSnapshot? saved = null)
    {
        player.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (process == Guid.Empty) throw new InvalidDataException("Deferred source constructor omitted its real Main process.");
        _player = player; _source = FalloutNewVegasDeferredReleaseSource.Read(player); _stack = stack; _process = process;
        _skipMask = _source.InitialSkipMask;
        uint Growth(uint skip) => skip == 1 ? _source.FormGrowth : skip == 32 ? 0u : _source.ArrayGrowth;
        uint Capacity(uint skip) => skip == 32 ? _source.EmbeddedCapacity : 0;
        _primary = PrimaryOrder.Select(skip => new Buffer(skip, false, Growth(skip), Capacity(skip))).ToArray();
        _secondary = SecondaryOrder.Select(skip => new Buffer(skip, true, Growth(skip), Capacity(skip))).ToArray();
        if (saved is null) return;
        Validate(saved);
        if (saved.Player != player || saved.Stack != stack || saved.Process == process)
            throw new InvalidDataException("Cold deferred release changed its selected source or reused its previous Main process.");
        _sequence = saved.Sequence;
        // Current empty source buffers are reconstructed. Historical locks,
        // thread IDs and completed children cannot enter this new process.
    }

    internal void RequireBinding(FalloutMainPlayerCellSource player, string stack, Guid process)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_player != player || _stack != stack || _process != process)
            throw new InvalidDataException("New Vegas deferred arrays changed their actual constructor/Main process.");
    }

    internal void SubmitSourceObject(IFalloutSourceDeferredObject actual)
    {
        ObjectDisposedException.ThrowIf(_retired, this); ArgumentNullException.ThrowIfNull(actual);
        actual.RequireCurrentFactory();
        var source = actual.Source;
        if (source.Identity == Guid.Empty || source.Process != _process || source.Contract != _source.Contract ||
            string.IsNullOrWhiteSpace(source.Owner))
            throw new InvalidDataException("New Vegas deferred submission changed its genuine object factory/process.");
        // Retain the real factory, not a stand-in object or a queue receipt.
        // The unreviewed writer must not leave the pool appearing unmodified
        // and saveable after it has attempted a source ownership transfer.
        _unboundFactories.TryAdd(source.Identity, actual);
        _error ??= "source-NV-deferred-release-actual-object-enqueue-and-native-children-unbound";
        throw new NotSupportedException(_error);
    }

    internal void FlushPending(FalloutMainPlayerCellInvocation invocation)
    {
        invocation.Require(FalloutMainPlayerCellStep.PendingDeferredDestruction);
        if (invocation.Main.Process != _process || invocation.Owner.MainPlayerCellSource != _player)
            throw new InvalidDataException("New Vegas deferred drain changed its actual entered Player/Main source.");
        Drain(invocation.Main.Identity, () => invocation.Require(FalloutMainPlayerCellStep.PendingDeferredDestruction));
    }

    private void Drain(Guid? main, Action requireCaller)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_error is not null) throw new InvalidOperationException("Deferred release retains its failed source prefix: " + _error);
        var thread = Environment.CurrentManagedThreadId;
        _last = new(main, thread, true, _busy, false, null, 0, 0, false, null);
        if (_busy) { _last = _last with { Complete = true }; return; }
        if (!Monitor.TryEnter(_drainLock)) { _last = _last with { LockReturn = true, Complete = true }; return; }
        FalloutNewVegasDeferredCaller? caller = null;
        try
        {
            requireCaller(); _busy = true;
            if (!_callers.TryGetValue(thread, out caller))
            {
                caller = new(thread, _source.InitialCallerEnabled, Guid.NewGuid());
                _callers.Add(thread, caller);
            }
            _last = _last with { CallerBefore = caller.Enabled };
            _callers[thread] = caller with { Enabled = 0 };
            foreach (var buffer in _primary)
            {
                requireCaller();
                if ((_skipMask & buffer.Skip) == 0)
                {
                    var gate = buffer.Skip == 16 ? _rawLock : buffer.Skip is 8 or 4 or 1 ? _allocationLock : null;
                    if (gate is null) RequireEmptyObjects(buffer);
                    else if (Monitor.TryEnter(gate))
                        try { RequireEmptyObjects(buffer); }
                        finally { Monitor.Exit(gate); }
                }
                _last = _last with { PrimaryQueues = _last.PrimaryQueues + 1 };
            }
            _callers[thread] = caller;
            if (caller.Enabled != 0)
                foreach (var secondary in _secondary)
                {
                    requireCaller(); RequireEmptyObjects(secondary);
                    var primary = _primary.Single(buffer => buffer.Skip == secondary.Skip);
                    primary.Objects.AddRange(secondary.Objects);
                    secondary.Objects.Clear();
                    _last = _last with { SecondaryQueues = _last.SecondaryQueues + 1 };
                }
            requireCaller(); _last = _last with { Complete = true }; _sequence = checked(_sequence + 1);
        }
        catch (Exception failure)
        {
            _error ??= failure.Message; _last = _last with { Error = failure.ToString() }; throw;
        }
        finally
        {
            // Independent cleanup returns the actual C# caller/lock leases;
            // it cannot remove the failed prefix or complete its source child.
            if (caller is not null) _callers[thread] = caller;
            _busy = false; Monitor.Exit(_drainLock);
        }
    }

    private static void RequireEmptyObjects(Buffer buffer)
    {
        if (buffer.Objects.Count != 0)
            throw new NotSupportedException("Actual New Vegas allocation/reference/virtual-deletion producers remain unbound.");
    }

    internal string? SaveBlocker => _retired ? "source-newvegas-deferred-release-retired" :
        _busy ? "source-newvegas-deferred-release-entered" : _error;
    internal object State => new
    {
        source = _source,
        process = _process,
        constructor = _constructor,
        sequence = _sequence,
        skipMask = _skipMask,
        last = _last,
        callers = _callers.Values.ToArray(),
        buffers = _primary.Concat(_secondary).Select(buffer => buffer.Capture()).ToArray(),
        retainedUnboundFactories = _unboundFactories.Keys.ToArray(),
        retired = _retired,
        saveBlocker = SaveBlocker,
        objectFactories = "independent-actual-enqueue-allocation-refcount-and-virtual-children-unbound"
    };

    internal FalloutNewVegasDeferredReleaseSnapshot Capture()
    {
        if (SaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        var snapshot = new FalloutNewVegasDeferredReleaseSnapshot(Schema, _source, _player, _stack, _process,
            _constructor, _sequence, _skipMask, _primary.Concat(_secondary).Select(buffer => buffer.Capture()).ToArray(),
            _callers.Values.ToArray(), _last, _error);
        Validate(snapshot); return snapshot;
    }

    internal static void Validate(FalloutNewVegasDeferredReleaseSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Player is null || snapshot.Source is null)
            throw new InvalidDataException("New Vegas deferred release omitted its selected source constructor.");
        snapshot.Player.Validate(); snapshot.Source.Validate(snapshot.Player);
        if (snapshot.Schema != Schema || string.IsNullOrWhiteSpace(snapshot.Stack) || snapshot.Process == Guid.Empty ||
            snapshot.Constructor == Guid.Empty || snapshot.Sequence < 0 || snapshot.SkipMask != snapshot.Source.InitialSkipMask ||
            snapshot.Error is not null || snapshot.Buffers is null || snapshot.Callers is null ||
            snapshot.Buffers.Count != 11 || snapshot.Buffers.Any(buffer => buffer is null || buffer.Objects is null || buffer.Objects.Count != 0) ||
            snapshot.Callers.Any(caller => caller is null || caller.ManagedThread < 1 || caller.Constructor == Guid.Empty || caller.Enabled != snapshot.Source.InitialCallerEnabled) ||
            snapshot.Callers.Select(caller => caller.ManagedThread).Distinct().Count() != snapshot.Callers.Count ||
            snapshot.Callers.Select(caller => caller.Constructor).Distinct().Count() != snapshot.Callers.Count ||
            snapshot.Callers.Count > snapshot.Sequence || snapshot.Last is null && snapshot.Callers.Count != 0)
            throw new InvalidDataException("New Vegas deferred release has no complete constructor/current empty-buffer continuation.");
        if (snapshot.Last is { } last)
        {
            if (last.Main is null || last.Main == Guid.Empty || last.ManagedThread < 1 || !last.TryLock ||
                !last.Complete || last.Error is not null || last.BusyReturn && last.LockReturn)
                throw new InvalidDataException("Deferred release lost its actual pending Main caller or returned attempt.");
            if (last.BusyReturn || last.LockReturn)
            {
                // Both source exits precede the compiler-thread byte read and
                // every queue consumer. They cannot certify a drained cohort.
                if (last.CallerBefore is not null || last.PrimaryQueues != 0 || last.SecondaryQueues != 0)
                    throw new InvalidDataException("Deferred release early return invented a caller-byte or queue-consumer prefix.");
            }
            else if (last.CallerBefore != snapshot.Source.InitialCallerEnabled || last.PrimaryQueues != PrimaryOrder.Length ||
                last.SecondaryQueues != SecondaryOrder.Length || snapshot.Sequence < 1 ||
                !snapshot.Callers.Any(caller => caller.ManagedThread == last.ManagedThread && caller.Enabled == last.CallerBefore))
                throw new InvalidDataException("Deferred release returned without its same restored caller and complete ordered queue cohorts.");
        }
        var expected = PrimaryOrder.Select(skip => (skip, false)).Concat(SecondaryOrder.Select(skip => (skip, true))).ToArray();
        for (var index = 0; index < expected.Length; index++)
        {
            var buffer = snapshot.Buffers[index]; var (skip, secondary) = expected[index];
            var growth = skip == 1 ? snapshot.Source.FormGrowth : skip == 32 ? 0 : snapshot.Source.ArrayGrowth;
            var capacity = skip == 32 ? snapshot.Source.EmbeddedCapacity : 0;
            if (buffer.SkipBit != skip || buffer.Secondary != secondary || buffer.Growth != growth || buffer.Capacity != capacity)
                throw new InvalidDataException("Deferred release changed its original ordered buffer constructors.");
        }
    }

    internal void Retire(Action requireMainClosed)
    {
        if (_retired) return;
        requireMainClosed();
        if (_busy) throw new InvalidOperationException("Deferred release still owns an entered source child.");
        if (_unboundFactories.Count != 0)
            throw new NotSupportedException("Actual unbound deferred object factories retain their native ownership during retirement.");
        foreach (var buffer in _primary.Concat(_secondary)) RequireEmptyObjects(buffer);
        _retired = true;
    }
}
