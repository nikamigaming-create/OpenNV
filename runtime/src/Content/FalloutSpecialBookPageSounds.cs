namespace OpenNV.Runtime.Content;

internal sealed record FalloutSpecialBookPageSoundRead(FalloutSpecialBookPageDirection Direction, string EditorId, uint Pointer, uint Flags);
internal sealed class FalloutSpecialBookPageSoundsConsumer : IDisposable
{
    private FalloutSpecialBookPageSounds? _owner;
    private readonly FalloutSourceCStringInputConsumer _forward, _backward;
    internal FalloutSpecialBookPageSoundsConsumer(FalloutSpecialBookPageSounds owner,
        FalloutSourceCStringInputConsumer forward, FalloutSourceCStringInputConsumer backward)
    { _owner = owner; _forward = forward; _backward = backward; }
    internal FalloutSpecialBookPageSoundRead ReadCurrent(int direction)
        => (_owner ?? throw new ObjectDisposedException(nameof(FalloutSpecialBookPageSoundsConsumer))).ReadCurrent(direction);
    internal FalloutSoundRecord ResolveCurrent(int direction, FalloutPluginStack records)
        => (_owner ?? throw new ObjectDisposedException(nameof(FalloutSpecialBookPageSoundsConsumer))).ResolveCurrent(direction, records);
    public void Dispose()
    {
        if (_owner is not { } owner) return;
        owner.ReleaseConsumer(this); _backward.Dispose(); _forward.Dispose(); _owner = null;
    }
}

// The two directions remain independent, including their native partial stores.
// These fields provide sound identifiers to the existing Book consumer, never
// return the original Book/audio children or construct a receiver.
internal sealed class FalloutSpecialBookPageSounds
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly HashSet<FalloutSpecialBookPageSoundsConsumer> _consumers = [];
    private readonly FalloutSourceCStringInput _forward, _backward;
    private readonly FalloutPluginStack? _records;
    internal FalloutSpecialBookPageSoundsSource Source { get; }
    internal bool HasNativeLifetime => _forward.Phase != FalloutSourceCStringInputPhase.Source || _backward.Phase != FalloutSourceCStringInputPhase.Source;
    internal int ActiveConsumers { get { RequireThread(); return _consumers.Count; } }
    internal FalloutSpecialBookPageSounds(FalloutSpecialBookPageSoundsSource source, Action requireCurrent, Action requireConsumerRetired,
        FalloutPluginStack? records = null)
    {
        Source = source; _records = records;
        if (records?.OwnedSource is { } owned && !StringComparer.OrdinalIgnoreCase.Equals(owned.FalloutExecutablePath, source.RuntimePath))
            throw new InvalidDataException("Book sound input and actual winning records differ in selected executable.");
        _forward = new(source.Input(FalloutSpecialBookPageDirection.Forward), requireCurrent, requireConsumerRetired);
        _backward = new(source.Input(FalloutSpecialBookPageDirection.Backward), requireCurrent, requireConsumerRetired);
    }
    internal FalloutSourceCStringInput Field(FalloutSpecialBookPageDirection direction) => direction switch
    {
        FalloutSpecialBookPageDirection.Forward => _forward,
        FalloutSpecialBookPageDirection.Backward => _backward,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
    internal FalloutSpecialBookPageSoundRead ReadCurrent(int direction)
    {
        RequireThread(); var selected = direction switch
        {
            1 => FalloutSpecialBookPageDirection.Forward,
            -1 => FalloutSpecialBookPageDirection.Backward,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        var field = Field(selected); var value = field.ReadCurrentValue(); FalloutExecutableStringTable.RequireSoundEditorId(value);
        return new(selected, value, field.Pointer, Source.For(selected).Flags);
    }
    internal FalloutSoundRecord ResolveCurrent(int direction, FalloutPluginStack records)
    {
        if (!ReferenceEquals(records, _records)) throw new InvalidDataException("Book sound lookup changed its actual winning record owner.");
        var current = ReadCurrent(direction); var record = FalloutSoundRecordReader.Find(records, current.EditorId);
        return FalloutSoundRecordReader.Read(records, record.FormKey);
    }
    internal FalloutSpecialBookPageSoundsConsumer BorrowConsumer()
    {
        RequireThread(); var forward = _forward.BorrowConsumer();
        try
        {
            var consumer = new FalloutSpecialBookPageSoundsConsumer(this, forward, _backward.BorrowConsumer());
            if (!_consumers.Add(consumer)) throw new InvalidDataException("Book page input repeated its actual consumer lease.");
            return consumer;
        }
        catch { forward.Dispose(); throw; }
    }
    internal void ReleaseConsumer(FalloutSpecialBookPageSoundsConsumer consumer)
    {
        RequireThread();
        if (!_consumers.Remove(consumer)) throw new InvalidDataException("Book page input lost its retained actual consumer lifetime.");
    }
    internal void Bind(object owner, Action requireNativeCurrent, Func<FalloutSpecialBookPageDirection, string> readNative)
    {
        _forward.Bind(owner, requireNativeCurrent, () => readNative(FalloutSpecialBookPageDirection.Forward));
        _backward.Bind(owner, requireNativeCurrent, () => readNative(FalloutSpecialBookPageDirection.Backward));
    }
    internal void RequireRetirement(object owner)
    {
        RequireThread();
        if (_consumers.Count != 0) throw new InvalidOperationException("Book page inputs still retain an actual menu/audio consumer.");
        _backward.RequireRetirement(owner); _forward.RequireRetirement(owner);
    }
    internal void ClosedAfterChildExit(object owner)
    { _backward.ClosedAfterChildExit(owner); _forward.ClosedAfterChildExit(owner); }
    internal void RequireColdOwned()
        => throw new NotSupportedException("Book pointer operands and original native module buffers have no complete cold owner.");
    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Book page inputs escaped their actual product thread.");
    }
}
