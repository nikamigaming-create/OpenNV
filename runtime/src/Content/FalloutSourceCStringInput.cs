using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal enum FalloutSourceCStringInputPhase { Source, Bound, ProtectEntered, ProtectReturned, Stored, Returned, Failed, Retired, Closed }
internal sealed record FalloutSourceCStringInputInvocation(ulong Generation, int Process, ulong Module,
    uint Thread, ulong Parent, ulong Callback);
internal sealed record FalloutSourceCStringInputEvent(ulong Ordinal, string Operation,
    FalloutSourceCStringInputInvocation Invocation, FalloutSourceCStringInputPhase Phase, uint Pointer, string Value,
    uint? Result = null, uint? LastError = null, uint? OldBefore = null, uint? OldAfter = null, string? Failure = null);
internal sealed class FalloutSourceCStringInputConsumer : IDisposable
{
    private FalloutSourceCStringInput? _owner;
    internal string Value { get; }
    internal uint Pointer { get; }
    internal FalloutSourceCStringInputSource Source { get; }
    internal FalloutSourceCStringInputConsumer(FalloutSourceCStringInput owner, string value, uint pointer)
    { _owner = owner; Value = value; Pointer = pointer; Source = owner.Source; }
    public void Dispose()
    {
        if (_owner is not { } owner) return;
        owner.ReleaseConsumer(this); _owner = null;
    }
}

// One source-qualified pointer input. Its native data and actual consumer
// have separate retained lifetimes; no code or receiver behavior is supplied.
internal sealed class FalloutSourceCStringInput
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Action _requireCurrent;
    private readonly Action _requireConsumerRetired;
    private readonly List<FalloutSourceCStringInputEvent> _events = [];
    private readonly HashSet<FalloutSourceCStringInputConsumer> _consumers = [];
    private object? _nativeOwner;
    private Action? _requireNativeCurrent;
    private Func<string>? _readNativeValue;
    private FalloutSourceCStringInputInvocation? _entered;
    private uint? _firstOldOutput;
    private uint? _initialOldOutput;
    private bool _firstReturned;
    private string _value;
    internal FalloutSourceCStringInputSource Source { get; }
    internal FalloutSourceCStringInputPhase Phase { get; private set; }
    internal uint Pointer { get; private set; }
    internal string? Failure { get; private set; }
    internal IReadOnlyList<FalloutSourceCStringInputEvent> Events => _events.AsReadOnly();
    internal int ActiveConsumers { get { RequireThread(); return _consumers.Count; } }

    internal FalloutSourceCStringInput(FalloutSourceCStringInputSource source, Action requireCurrent, Action requireConsumerRetired)
    {
        Source = source; _requireCurrent = requireCurrent; _requireConsumerRetired = requireConsumerRetired;
        _value = source.Value; Pointer = source.Pointer; RequireCurrent();
    }
    internal string ReadCurrentValue()
    {
        RequireCurrent();
        if (Failure is not null || Phase is not (FalloutSourceCStringInputPhase.Source or FalloutSourceCStringInputPhase.Bound or FalloutSourceCStringInputPhase.Returned))
            throw new InvalidOperationException("Source CString input retains an entered, failed or retired source prefix: " + Failure);
        _requireNativeCurrent?.Invoke();
        if (_readNativeValue is not null && _readNativeValue() != _value)
            throw new NotSupportedException("The actual CString buffer changed without its selected source-copy writer.");
        return _value;
    }
    internal FalloutSourceCStringInputConsumer BorrowConsumer()
    {
        var consumer = new FalloutSourceCStringInputConsumer(this, ReadCurrentValue(), Pointer);
        if (!_consumers.Add(consumer)) throw new InvalidDataException("Value input repeated its actual consumer lease.");
        return consumer;
    }
    internal void ReleaseConsumer(FalloutSourceCStringInputConsumer consumer)
    {
        RequireThread();
        if (!_consumers.Remove(consumer)) throw new InvalidDataException("Value input consumer changed or already released its retained lifetime.");
    }
    internal void Bind(object owner, Action requireNativeCurrent, Func<string> readNativeValue)
    {
        RequireCurrent();
        if (_nativeOwner is not null || Phase != FalloutSourceCStringInputPhase.Source)
            throw new InvalidOperationException("Source CString input cannot rebind its original native lifetime.");
        _nativeOwner = owner; _requireNativeCurrent = requireNativeCurrent; _readNativeValue = readNativeValue;
        Phase = FalloutSourceCStringInputPhase.Bound;
    }
    internal void EnterProtect(object owner, FalloutSourceCStringInputInvocation invocation, ReadOnlySpan<byte> preimage, uint oldBefore)
    {
        RequireOwner(owner); RequireInvocation(invocation);
        if (Phase != FalloutSourceCStringInputPhase.Bound || _entered is not null || !preimage.SequenceEqual(Source.Preimage.AsSpan()))
            throw new InvalidDataException("Source CString operand mutation lost its unique initial instruction and entered prefix.");
        _entered = invocation; _initialOldOutput = oldBefore; Phase = FalloutSourceCStringInputPhase.ProtectEntered;
        Record("protect-enter", invocation, before: oldBefore);
    }
    internal void Protected(object owner, FalloutSourceCStringInputInvocation invocation,
        uint result, uint last, uint oldBefore, uint oldAfter)
    {
        RequireEntered(owner, invocation);
        if (Phase != FalloutSourceCStringInputPhase.ProtectEntered || oldBefore != _initialOldOutput)
            throw new InvalidDataException("Source CString first SDK output lost its actual returned prefix.");
        _firstOldOutput = oldAfter; _firstReturned = result != 0;
        Phase = FalloutSourceCStringInputPhase.ProtectReturned; Record("protect-return", invocation, result, last, oldBefore, oldAfter);
    }
    internal void ObserveStore(object owner, FalloutSourceCStringInputInvocation invocation,
        ReadOnlySpan<byte> instruction, uint modulePointer, string value)
    {
        RequireEntered(owner, invocation);
        if (Phase != FalloutSourceCStringInputPhase.ProtectReturned || instruction.Length != 5 || instruction[0] != 0x68 ||
            modulePointer == 0 || BinaryPrimitives.ReadUInt32LittleEndian(instruction[1..]) != modulePointer)
            throw new InvalidDataException("The original CString store has no complete instruction/module-data readback.");
        Pointer = modulePointer; _value = value; Phase = FalloutSourceCStringInputPhase.Stored; Record("store-observed", invocation);
    }
    internal void Restored(object owner, FalloutSourceCStringInputInvocation invocation,
        uint requested, uint result, uint last, uint oldBefore, uint oldAfter)
    {
        RequireEntered(owner, invocation);
        if (Phase != FalloutSourceCStringInputPhase.Stored || requested != _firstOldOutput || oldBefore != _firstOldOutput ||
            requested is not (0x20 or 0x40))
            throw new InvalidDataException("Source CString restore lost the original output cell and prior stored word.");
        Phase = FalloutSourceCStringInputPhase.Returned; Record("restore-return", invocation, result, last, oldBefore, oldAfter);
        _entered = null;
        if (!_firstReturned || result == 0) Fail(owner, invocation, "The original CString mutation retains a failed Win32 protection result.");
    }
    internal void Fail(object owner, FalloutSourceCStringInputInvocation invocation, string reason)
    {
        RequireOwnerIdentity(owner); Failure ??= reason; Phase = FalloutSourceCStringInputPhase.Failed;
        Record("failure", invocation, failure: reason);
    }
    internal void RequireRetirement(object owner)
    {
        RequireOwnerIdentity(owner); _requireConsumerRetired();
        if (_consumers.Count != 0 || _entered is not null || Failure is not null || Phase is not (FalloutSourceCStringInputPhase.Bound or FalloutSourceCStringInputPhase.Returned))
            throw new InvalidOperationException("Source CString operand retirement retains an entered or failed native store.");
    }
    internal void Retired(object owner, FalloutSourceCStringInputInvocation invocation)
    {
        RequireRetirement(owner); Phase = FalloutSourceCStringInputPhase.Retired; Record("retired", invocation);
    }
    internal void ClosedAfterChildExit(object owner)
    {
        RequireOwnerIdentity(owner);
        _entered = null; Phase = FalloutSourceCStringInputPhase.Closed; _requireNativeCurrent = null; _readNativeValue = null;
    }
    internal void RequireColdOwned()
        => throw new NotSupportedException("The original CString operand, mutable module buffer and native constructor globals have no complete cold owner.");
    private void RequireCurrent()
    {
        RequireThread();
        _requireCurrent();
    }
    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Source CString input escaped its actual product thread.");
    }
    private void RequireOwnerIdentity(object owner)
    {
        RequireThread();
        if (!ReferenceEquals(owner, _nativeOwner)) throw new InvalidDataException("Source CString input changed its actual native owner.");
    }
    private void RequireOwner(object owner)
    {
        RequireCurrent();
        if (!ReferenceEquals(owner, _nativeOwner)) throw new InvalidDataException("Source CString input changed its actual native owner.");
    }
    private static void RequireInvocation(FalloutSourceCStringInputInvocation invocation)
    {
        if (invocation.Generation == 0 || invocation.Process <= 0 || invocation.Module == 0 || invocation.Thread == 0 ||
            invocation.Parent == 0 || invocation.Callback == 0)
            throw new InvalidDataException("Source CString operand callback lacks its entered process/module/thread/parent identity.");
    }
    private void RequireEntered(object owner, FalloutSourceCStringInputInvocation invocation)
    {
        RequireOwner(owner); RequireInvocation(invocation);
        if (_entered is not { } entered || entered.Generation != invocation.Generation || entered.Process != invocation.Process ||
            entered.Module != invocation.Module || entered.Thread != invocation.Thread || entered.Parent != invocation.Parent ||
            invocation.Callback <= entered.Callback)
            throw new InvalidDataException("Source CString mutation changed its actual source invocation.");
        _entered = invocation;
    }
    private void Record(string operation, FalloutSourceCStringInputInvocation invocation, uint? result = null,
        uint? last = null, uint? before = null, uint? after = null, string? failure = null)
        => _events.Add(new(checked((ulong)_events.Count + 1), operation, invocation, Phase, Pointer, _value, result, last, before, after, failure));
}
