namespace OpenNV.Runtime.Content;

internal sealed record FalloutSpecialBookPageSoundRead(FalloutSpecialBookPageDirection Direction, string EditorId, uint Pointer, uint Flags);
internal sealed class FalloutSpecialBookPageSoundsConsumer : IDisposable
{
    private FalloutSpecialBookPageSounds? _owner;
    private readonly IReadOnlyList<FalloutSourceCStringInputConsumer> _fields;
    internal FalloutSpecialBookPageSoundsConsumer(FalloutSpecialBookPageSounds owner,
        IReadOnlyList<FalloutSourceCStringInputConsumer> fields)
    { _owner = owner; _fields = fields; }
    internal FalloutSpecialBookPageSoundRead ReadCurrent(int direction)
        => (_owner ?? throw new ObjectDisposedException(nameof(FalloutSpecialBookPageSoundsConsumer))).ReadCurrent(direction);
    internal FalloutSoundRecord ResolveCurrent(int direction, FalloutPluginStack records)
        => (_owner ?? throw new ObjectDisposedException(nameof(FalloutSpecialBookPageSoundsConsumer))).ResolveCurrent(direction, records);
    internal FalloutSoundRecord ResolveFocusCurrent(int direction, FalloutPluginStack records)
        => (_owner ?? throw new ObjectDisposedException(nameof(FalloutSpecialBookPageSoundsConsumer))).ResolveFocusCurrent(direction, records);
    public void Dispose()
    {
        if (_owner is not { } owner) return;
        owner.ReleaseConsumer(this);
        foreach (var field in _fields.Reverse()) field.Dispose();
        _owner = null;
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
    private readonly FalloutSourceCStringInput? _focusPrevious, _focusNext;
    private readonly FalloutPluginStack? _records;
    internal FalloutSpecialBookPageSoundsSource Source { get; }
    internal FalloutSpecialBookFocusSoundsSource? FocusSource { get; }
    internal bool HasNativeLifetime => Fields().Any(input => input.Phase != FalloutSourceCStringInputPhase.Source);
    internal int ActiveConsumers { get { RequireThread(); return _consumers.Count; } }
    internal FalloutSpecialBookPageSounds(FalloutSpecialBookPageSoundsSource source, Action requireCurrent, Action requireConsumerRetired,
        FalloutPluginStack? records = null, FalloutSpecialBookFocusSoundsSource? focusSource = null)
    {
        Source = source; _records = records; FocusSource = focusSource;
        focusSource?.CheckPageOwner(source);
        if (records?.OwnedSource is { } owned && !StringComparer.OrdinalIgnoreCase.Equals(owned.FalloutExecutablePath, source.RuntimePath))
            throw new InvalidDataException("Book sound input and actual winning records differ in selected executable.");
        _forward = new(source.Input(FalloutSpecialBookPageDirection.Forward), requireCurrent, requireConsumerRetired);
        _backward = new(source.Input(FalloutSpecialBookPageDirection.Backward), requireCurrent, requireConsumerRetired);
        if (focusSource is not null)
        {
            _focusPrevious = new(focusSource.Input(FalloutSpecialBookFocusDirection.Previous), requireCurrent, requireConsumerRetired);
            _focusNext = new(focusSource.Input(FalloutSpecialBookFocusDirection.Next), requireCurrent, requireConsumerRetired);
        }
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
    internal FalloutSourceCStringInput FocusField(FalloutSpecialBookFocusDirection direction) => direction switch
    {
        FalloutSpecialBookFocusDirection.Previous => _focusPrevious ?? throw new NotSupportedException("Previous focus input has no source owner."),
        FalloutSpecialBookFocusDirection.Next => _focusNext ?? throw new NotSupportedException("Next focus input has no source owner."),
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };
    internal FalloutSoundRecord ResolveFocusCurrent(int direction, FalloutPluginStack records)
    {
        if (!ReferenceEquals(records, _records)) throw new InvalidDataException("Book focus lookup changed its actual winning record owner.");
        var value = ReadFocusCurrent(direction).EditorId;
        var record = FalloutSoundRecordReader.Find(records, value); return FalloutSoundRecordReader.Read(records, record.FormKey);
    }
    internal FalloutSpecialBookFocusSoundRead ReadFocusCurrent(int direction)
    {
        RequireThread();
        var selected = direction switch
        {
            -1 => FalloutSpecialBookFocusDirection.Previous,
            1 => FalloutSpecialBookFocusDirection.Next,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        var value = FocusField(selected).ReadCurrentValue(); FalloutExecutableStringTable.RequireSoundEditorId(value);
        return new(selected, value, FocusField(selected).Pointer, FocusSource!.For(selected).Flags);
    }
    private IEnumerable<FalloutSourceCStringInput> Fields()
    {
        yield return _forward; yield return _backward;
        if (_focusPrevious is not null) yield return _focusPrevious;
        if (_focusNext is not null) yield return _focusNext;
    }
    internal FalloutSpecialBookPageSoundsConsumer BorrowConsumer()
    {
        RequireThread(); var fields = new List<FalloutSourceCStringInputConsumer>();
        try
        {
            foreach (var field in Fields()) fields.Add(field.BorrowConsumer());
            var consumer = new FalloutSpecialBookPageSoundsConsumer(this, fields);
            if (!_consumers.Add(consumer)) throw new InvalidDataException("Book input repeated its actual consumer lease.");
            return consumer;
        }
        catch { foreach (var field in fields.AsEnumerable().Reverse()) field.Dispose(); throw; }
    }

    internal void ReleaseConsumer(FalloutSpecialBookPageSoundsConsumer consumer)
    {
        RequireThread();
        if (!_consumers.Remove(consumer)) throw new InvalidDataException("Book page input lost its retained actual consumer lifetime.");
    }
    internal void Bind(object owner, Action requireNativeCurrent, Func<FalloutSpecialBookPageDirection, string> readNative,
        Func<FalloutSpecialBookFocusDirection, string>? readFocus = null)
    {
        if (FocusSource is not null && readFocus is null) throw new InvalidDataException("Book focus needs its actual current native getter.");
        _forward.Bind(owner, requireNativeCurrent, () => readNative(FalloutSpecialBookPageDirection.Forward));
        _backward.Bind(owner, requireNativeCurrent, () => readNative(FalloutSpecialBookPageDirection.Backward));
        _focusPrevious?.Bind(owner, requireNativeCurrent, () => readFocus!(FalloutSpecialBookFocusDirection.Previous));
        _focusNext?.Bind(owner, requireNativeCurrent, () => readFocus!(FalloutSpecialBookFocusDirection.Next));
    }
    internal void RequireRetirement(object owner)
    {
        RequireThread();
        if (_consumers.Count != 0) throw new InvalidOperationException("Book page inputs still retain an actual menu/audio consumer.");
        foreach (var field in Fields().Reverse()) field.RequireRetirement(owner);
    }
    internal void ClosedAfterChildExit(object owner)
    { foreach (var field in Fields().Reverse()) field.ClosedAfterChildExit(owner); }
    internal void RequireColdOwned()
        => throw new NotSupportedException("Book pointer operands and original native module buffers have no complete cold owner.");
    private void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Book page inputs escaped their actual product thread.");
    }
}
