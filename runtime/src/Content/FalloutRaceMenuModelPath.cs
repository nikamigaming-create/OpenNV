using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal enum FalloutRaceMenuModelPathPhase { Source, Bound, ProtectEntered, ProtectReturned, Stored, Returned, Failed, Retired, Closed }
internal sealed record FalloutRaceMenuModelPathInvocation(ulong Generation, int Process, ulong Module,
    uint Thread, ulong Parent, ulong Callback);
internal sealed record FalloutRaceMenuModelPathEvent(ulong Ordinal, string Operation,
    FalloutRaceMenuModelPathInvocation Invocation, FalloutRaceMenuModelPathPhase Phase, uint Pointer, string Model,
    uint? Result = null, uint? LastError = null, uint? OldBefore = null, uint? OldAfter = null, string? Failure = null);
internal sealed class FalloutRaceMenuModelPathConsumer : IDisposable
{
    private FalloutRaceMenuModelPath? _owner;
    internal string Model { get; }
    internal uint Pointer { get; }
    internal FalloutRaceMenuModelPathSource Source { get; }
    internal FalloutRaceMenuModelPathConsumer(FalloutRaceMenuModelPath owner, string model, uint pointer)
    { _owner = owner; Model = model; Pointer = pointer; Source = owner.Source; }
    public void Dispose()
    {
        if (_owner is not { } owner) return;
        owner.ReleaseConsumer(this); _owner = null;
    }
}

// One field selects the input to the existing rendered-device constructor.
// The later receiver-owned string and all menu children have separate owners.
internal sealed class FalloutRaceMenuModelPath
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Action _requireCurrent;
    private readonly Action _requireConsumerRetired;
    private readonly List<FalloutRaceMenuModelPathEvent> _events = [];
    private readonly HashSet<FalloutRaceMenuModelPathConsumer> _consumers = [];
    private object? _nativeOwner;
    private Action? _requireNativeCurrent;
    private Func<string>? _readNativeModel;
    private FalloutRaceMenuModelPathInvocation? _entered;
    private uint? _firstOldOutput;
    private uint? _initialOldOutput;
    private bool _firstReturned;
    private string _model;
    internal FalloutRaceMenuModelPathSource Source { get; }
    internal FalloutRaceMenuModelPathPhase Phase { get; private set; }
    internal uint Pointer { get; private set; }
    internal string? Failure { get; private set; }
    internal IReadOnlyList<FalloutRaceMenuModelPathEvent> Events => _events.AsReadOnly();
    internal int ActiveConsumers { get { RequireThread(); return _consumers.Count; } }

    internal FalloutRaceMenuModelPath(FalloutRaceMenuModelPathSource source, Action requireCurrent, Action requireConsumerRetired)
    {
        Source = source; _requireCurrent = requireCurrent; _requireConsumerRetired = requireConsumerRetired;
        _model = source.Model; Pointer = source.Pointer; RequireCurrent();
    }
    internal string ReadCurrentModel()
    {
        RequireCurrent();
        if (Failure is not null || Phase is not (FalloutRaceMenuModelPathPhase.Source or FalloutRaceMenuModelPathPhase.Bound or FalloutRaceMenuModelPathPhase.Returned))
            throw new InvalidOperationException("Race-menu model input retains an entered, failed or retired source prefix: " + Failure);
        _requireNativeCurrent?.Invoke();
        if (_readNativeModel is not null && _readNativeModel() != _model)
            throw new NotSupportedException("The actual model buffer changed without its selected source-copy writer.");
        return _model;
    }
    internal FalloutRaceMenuModelPathConsumer BorrowConsumer()
    {
        var consumer = new FalloutRaceMenuModelPathConsumer(this, ReadCurrentModel(), Pointer);
        if (!_consumers.Add(consumer)) throw new InvalidDataException("Model input repeated its actual consumer lease.");
        return consumer;
    }
    internal void ReleaseConsumer(FalloutRaceMenuModelPathConsumer consumer)
    {
        RequireThread();
        if (!_consumers.Remove(consumer)) throw new InvalidDataException("Model input consumer changed or already released its retained lifetime.");
    }
    internal void Bind(object owner, Action requireNativeCurrent, Func<string> readNativeModel)
    {
        RequireCurrent();
        if (_nativeOwner is not null || Phase != FalloutRaceMenuModelPathPhase.Source)
            throw new InvalidOperationException("Race-menu model input cannot rebind its original native lifetime.");
        _nativeOwner = owner; _requireNativeCurrent = requireNativeCurrent; _readNativeModel = readNativeModel;
        Phase = FalloutRaceMenuModelPathPhase.Bound;
    }
    internal void EnterProtect(object owner, FalloutRaceMenuModelPathInvocation invocation, ReadOnlySpan<byte> preimage, uint oldBefore)
    {
        RequireOwner(owner); RequireInvocation(invocation);
        if (Phase != FalloutRaceMenuModelPathPhase.Bound || _entered is not null || !preimage.SequenceEqual(Source.Preimage.AsSpan()))
            throw new InvalidDataException("Race-menu operand mutation lost its unique initial instruction and entered prefix.");
        _entered = invocation; _initialOldOutput = oldBefore; Phase = FalloutRaceMenuModelPathPhase.ProtectEntered;
        Record("protect-enter", invocation, before: oldBefore);
    }
    internal void Protected(object owner, FalloutRaceMenuModelPathInvocation invocation,
        uint result, uint last, uint oldBefore, uint oldAfter)
    {
        RequireEntered(owner, invocation);
        if (Phase != FalloutRaceMenuModelPathPhase.ProtectEntered || oldBefore != _initialOldOutput)
            throw new InvalidDataException("Race-menu first SDK output lost its actual returned prefix.");
        _firstOldOutput = oldAfter; _firstReturned = result != 0;
        Phase = FalloutRaceMenuModelPathPhase.ProtectReturned; Record("protect-return", invocation, result, last, oldBefore, oldAfter);
    }
    internal void ObserveStore(object owner, FalloutRaceMenuModelPathInvocation invocation,
        ReadOnlySpan<byte> instruction, uint modulePointer, string model)
    {
        RequireEntered(owner, invocation);
        if (Phase != FalloutRaceMenuModelPathPhase.ProtectReturned || instruction.Length != 5 || instruction[0] != 0x68 ||
            modulePointer == 0 || BinaryPrimitives.ReadUInt32LittleEndian(instruction[1..]) != modulePointer)
            throw new InvalidDataException("The original model store has no complete instruction/module-data readback.");
        Pointer = modulePointer; _model = model; Phase = FalloutRaceMenuModelPathPhase.Stored; Record("store-observed", invocation);
    }
    internal void Restored(object owner, FalloutRaceMenuModelPathInvocation invocation,
        uint requested, uint result, uint last, uint oldBefore, uint oldAfter)
    {
        RequireEntered(owner, invocation);
        if (Phase != FalloutRaceMenuModelPathPhase.Stored || requested != _firstOldOutput || oldBefore != _firstOldOutput ||
            requested is not (0x20 or 0x40))
            throw new InvalidDataException("Race-menu restore lost the original output cell and prior stored word.");
        Phase = FalloutRaceMenuModelPathPhase.Returned; Record("restore-return", invocation, result, last, oldBefore, oldAfter);
        _entered = null;
        if (!_firstReturned || result == 0) Fail(owner, invocation, "The original model mutation retains a failed Win32 protection result.");
    }
    internal void Fail(object owner, FalloutRaceMenuModelPathInvocation invocation, string reason)
    {
        RequireOwnerIdentity(owner); Failure ??= reason; Phase = FalloutRaceMenuModelPathPhase.Failed;
        Record("failure", invocation, failure: reason);
    }
    internal void RequireRetirement(object owner)
    {
        RequireOwnerIdentity(owner); _requireConsumerRetired();
        if (_consumers.Count != 0 || _entered is not null || Failure is not null || Phase is not (FalloutRaceMenuModelPathPhase.Bound or FalloutRaceMenuModelPathPhase.Returned))
            throw new InvalidOperationException("Race-menu operand retirement retains an entered or failed native store.");
    }
    internal void Retired(object owner, FalloutRaceMenuModelPathInvocation invocation)
    {
        RequireRetirement(owner); Phase = FalloutRaceMenuModelPathPhase.Retired; Record("retired", invocation);
    }
    internal void ClosedAfterChildExit(object owner)
    {
        if (!ReferenceEquals(owner, _nativeOwner)) throw new InvalidDataException("Race-menu model closure changed its native owner.");
        _entered = null; Phase = FalloutRaceMenuModelPathPhase.Closed; _requireNativeCurrent = null; _readNativeModel = null;
    }
    internal void RequireColdOwned()
        => throw new NotSupportedException("The original model operand, mutable module buffer and native constructor globals have no complete cold owner.");
    private void RequireCurrent()
    {
        RequireThread();
        _requireCurrent();
    }
    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Race-menu model input escaped its actual product thread.");
    }
    private void RequireOwnerIdentity(object owner)
    {
        RequireThread();
        if (!ReferenceEquals(owner, _nativeOwner)) throw new InvalidDataException("Race-menu model input changed its actual native owner.");
    }
    private void RequireOwner(object owner)
    {
        RequireCurrent();
        if (!ReferenceEquals(owner, _nativeOwner)) throw new InvalidDataException("Race-menu model input changed its actual native owner.");
    }
    private static void RequireInvocation(FalloutRaceMenuModelPathInvocation invocation)
    {
        if (invocation.Generation == 0 || invocation.Process <= 0 || invocation.Module == 0 || invocation.Thread == 0 ||
            invocation.Parent == 0 || invocation.Callback == 0)
            throw new InvalidDataException("Race-menu operand callback lacks its entered process/module/thread/parent identity.");
    }
    private void RequireEntered(object owner, FalloutRaceMenuModelPathInvocation invocation)
    {
        RequireOwner(owner); RequireInvocation(invocation);
        if (_entered is not { } entered || entered.Generation != invocation.Generation || entered.Process != invocation.Process ||
            entered.Module != invocation.Module || entered.Thread != invocation.Thread || entered.Parent != invocation.Parent ||
            invocation.Callback <= entered.Callback)
            throw new InvalidDataException("Race-menu model mutation changed its actual source invocation.");
        _entered = invocation;
    }
    private void Record(string operation, FalloutRaceMenuModelPathInvocation invocation, uint? result = null,
        uint? last = null, uint? before = null, uint? after = null, string? failure = null)
        => _events.Add(new(checked((ulong)_events.Count + 1), operation, invocation, Phase, Pointer, _model, result, last, before, after, failure));
}
