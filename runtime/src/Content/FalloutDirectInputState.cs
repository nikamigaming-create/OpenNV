namespace OpenNV.Runtime.Content;

internal enum FalloutInputProvider { WindowsDirectInput = 1, HeadlessCommands = 2 }

// Published keyboard/mouse layout shared by device, command and script adapters.
// These are fixed protocol values, not player preferences or tunable settings.
internal static class FalloutInputLayout
{
    internal const int KeyboardKeyCount = 256;
    internal const int MouseButtonCount = 8;
    internal const int FirstMouseButton = KeyboardKeyCount;
    internal const int MouseLeft = FirstMouseButton;
    internal const int MouseRight = FirstMouseButton + 1;
    internal const int MouseMiddle = FirstMouseButton + 2;
    internal const int MouseExtra1 = FirstMouseButton + 3;
    internal const int MouseExtra2 = FirstMouseButton + 4;
    internal const int WheelUp = FirstMouseButton + MouseButtonCount;
    internal const int WheelDown = WheelUp + 1;
    internal const int KeyCount = WheelDown + 1;
    internal const byte ButtonDown = 0x80;
    internal const int WheelStep = 120;
}

internal readonly record struct FalloutDirectInputKey(bool Raw, bool Game, bool Inserted,
    bool Hold, bool Tap, bool UserDisabled, bool ScriptDisabled);
internal readonly record struct FalloutDirectInputControlChange(int Key,
    FalloutDirectInputKey Before, FalloutDirectInputKey After);
internal sealed record FalloutDirectInputSnapshot(string Source, ulong Window, ulong Sample,
    long Revision, IReadOnlyList<FalloutDirectInputKey> Keys, int MouseX, int MouseY, int MouseWheel,
    bool Acquired, string? AcquisitionFailure, FalloutInputProvider Provider = FalloutInputProvider.WindowsDirectInput,
    FalloutOriginalInputSnapshot? Original = null);

// Each declared provider supplies the complete keyboard and mouse extent to
// one control/script owner. Headless samples do not certify Windows acquisition.
internal sealed partial class FalloutDirectInputState
{
    internal const int KeyCount = FalloutInputLayout.KeyCount;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly FalloutDirectInputKey[] _keys = new FalloutDirectInputKey[KeyCount];
    internal string Source { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutInputControls Controls { get; }
    internal ulong Window { get; }
    internal FalloutInputProvider Provider { get; }
    internal ulong Sample { get; private set; }
    internal long Revision { get; private set; }
    private bool _acquired, _retired;
    internal int NativeReaders { get; private set; }
    internal event Action? NativeReadersRetired;
    private int _x, _y, _wheel;
    internal string? Failure { get; private set; }
    internal event Action<IReadOnlyList<(int Key, bool Down)>>? GameEdges;

    internal FalloutDirectInputState(string source, ulong window, FalloutInputControls controls)
        : this(source, window, controls, FalloutInputProvider.WindowsDirectInput) { }

    private FalloutDirectInputState(string source, ulong window, FalloutInputControls controls, FalloutInputProvider provider)
    {
        if (source.Length != 64 || !source.All(Uri.IsHexDigit) || !ValidProvider(provider, window))
            throw new InvalidDataException("Input state requires its exact selected source and explicit provider/window identity.");
        ArgumentNullException.ThrowIfNull(controls);
        Source = source; Window = window; Controls = controls; Provider = provider;
    }

    internal static FalloutDirectInputState CreateHeadless(string source, FalloutInputControls controls) =>
        new(source, 0, controls, FalloutInputProvider.HeadlessCommands);

    internal static bool ValidProvider(FalloutInputProvider provider, ulong window) => provider switch
    {
        FalloutInputProvider.WindowsDirectInput => window != 0,
        FalloutInputProvider.HeadlessCommands => window == 0,
        _ => false,
    };

    internal void Publish(ReadOnlySpan<byte> keyboard, ReadOnlySpan<byte> mouse, int x, int y, int wheel)
    {
        RequireOwner(); RequireIdleOriginal();
        if (HasSourceControlQueries) throw new InvalidOperationException("Bound original input requires the actual per-device poll producer.");
        if (keyboard.Length != FalloutInputLayout.KeyboardKeyCount || mouse.Length != FalloutInputLayout.MouseButtonCount)
            throw new InvalidDataException("DirectInput publication lacks the complete original keyboard/mouse extent.");
        var edges = new List<(int Key, bool Down)>();
        for (var key = 0; key < KeyCount; ++key)
        {
            var raw = key < FalloutInputLayout.FirstMouseButton ? keyboard[key] != 0 :
                key < FalloutInputLayout.WheelUp ? mouse[key - FalloutInputLayout.FirstMouseButton] != 0 :
                key == FalloutInputLayout.WheelUp ? wheel > 0 : wheel < 0;
            _ = PublishHookKey(key, raw, edges);
        }
        _x = x; _y = y; _wheel = wheel; Sample = checked(Sample + 1); ++Revision;
        Failure = null; _acquired = true;
        // State publication precedes real script callbacks. Callback failure
        // does not roll the device back into an invented earlier sample.
        GameEdges?.Invoke(edges);
    }

    internal void DeviceUnavailable(string failure)
    {
        RequireOwner(); RequireIdleOriginal(); _acquired = false; Failure = failure;
        if (HasSourceControlQueries)
        {
            _keyboardKnown = _mouseKnown = false;
            _originalSelection = null; ++_bufferRevision;
        }
        // Preserve the last observed bytes; unavailable is not all-released.
    }
    internal FalloutDirectInputSnapshot Capture()
    {
        RequireCurrent();
        RequireIdleOriginal();
        return new(Source, Window, Sample, Revision, _keys.ToArray(), _x, _y, _wheel, _acquired, Failure, Provider,
            HasSourceControlQueries ? CaptureSourceInput() : null);
    }
    internal bool Pressed(uint key, uint flags = 0)
    {
        RequireCurrent(); if (key >= KeyCount) return false;
        if (flags == 0) flags = 1;
        var value = _keys[key];
        return !((flags & 8) != 0 && value.UserDisabled || (flags & 16) != 0 && value.ScriptDisabled) &&
            ((flags & 1) != 0 && value.Game || (flags & 2) != 0 && value.Raw || (flags & 4) != 0 && value.Inserted);
    }
    internal void SetDisable(uint key, bool disabled, uint mask = 0)
    {
        RequireOwner(); RequireIdleOriginal(); if (key >= KeyCount) return;
        if (mask == 0) mask = 3;
        var value = _keys[key];
        _keys[key] = value with
        {
            UserDisabled = (mask & 1) != 0 ? disabled : value.UserDisabled,
            ScriptDisabled = (mask & 2) != 0 ? disabled : value.ScriptDisabled
        }; ++Revision;
    }
    internal void SetHold(uint key, bool held)
    { RequireOwner(); RequireIdleOriginal(); if (key < KeyCount) { _keys[key] = _keys[key] with { Hold = held }; ++Revision; } }
    internal void Tap(uint key)
    { RequireOwner(); RequireIdleOriginal(); if (key < KeyCount) { _keys[key] = _keys[key] with { Tap = true }; ++Revision; } }

    internal void PublishNativeControls(IReadOnlyList<FalloutDirectInputControlChange> changes)
    {
        RequireCurrent(); RequireIdleOriginal();
        if (changes.Select(value => value.Key).Distinct().Count() != changes.Count)
            throw new InvalidDataException("Native DirectInput mutation repeats a key.");
        foreach (var change in changes)
        {
            if ((uint)change.Key >= KeyCount || change.Before.Raw != change.After.Raw ||
                change.Before.Game != change.After.Game || change.Before.Inserted != change.After.Inserted)
                throw new InvalidDataException("Native DirectInput mutation changes device-owned observations.");
            var current = _keys[change.Key];
            // Reentry may advance observations, but cannot overwrite a different
            // writer's hold/tap/disable publication.
            if (!ControlsEqual(current, change.Before) && !ControlsEqual(current, change.After))
                throw new InvalidOperationException("Native and campaign DirectInput controls have competing writers.");
        }
        foreach (var change in changes)
            _keys[change.Key] = _keys[change.Key] with
            {
                Hold = change.After.Hold,
                Tap = change.After.Tap,
                UserDisabled = change.After.UserDisabled,
                ScriptDisabled = change.After.ScriptDisabled
            };
        if (changes.Count != 0) ++Revision;
    }
    internal static bool ControlsEqual(FalloutDirectInputKey first, FalloutDirectInputKey second)
        => first.Hold == second.Hold && first.Tap == second.Tap && first.UserDisabled == second.UserDisabled && first.ScriptDisabled == second.ScriptDisabled;
    internal void RequireCurrent()
    {
        RequireOwner();
        // Public DIHookControl caches the last successful device update. Failed
        // acquisition does not rewrite those observations or imply new input.
        if (Sample == 0) throw new InvalidOperationException("Complete DirectInput device state has never been published: " + Failure);
    }
    internal FalloutDirectInputState ReadManagerOwner()
    {
        // The source singleton getter reads its current object. It neither
        // polls a device nor rewrites cached bytes after lost acquisition.
        RequireOwner(); return this;
    }
    internal int ReadRelativeMouseAxis(uint axis)
    {
        RequireCurrent();
        if (HasSourceControlQueries)
        {
            if (axis is not (1 or 2 or 3)) return 0;
            if (!_mouseKnown) throw new NotSupportedException("source-original-relative-mouse-current-writer-unowned");
            return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(_engineMouse.AsSpan(checked(((int)axis - 1) * sizeof(int))));
        }
        return axis switch { 1 => _x, 2 => _y, 3 => _wheel, _ => 0 };
    }
    internal IDisposable RetainNativeReader()
    {
        RequireCurrent(); NativeReaders = checked(NativeReaders + 1); return new NativeReader(this);
    }
    internal void Retire()
    {
        RequireOwner(); RequireIdleOriginal();
        if (NativeReaders != 0) throw new InvalidOperationException("Actual native generations still retain this device/input owner.");
        _acquired = false; _retired = true;
    }
    private sealed class NativeReader(FalloutDirectInputState owner) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (!_released)
            {
                owner.RequireOwner();
                if (owner.NativeReaders <= 0) throw new InvalidOperationException("Native input retention is unbalanced.");
                --owner.NativeReaders; _released = true;
            }
            // A failed device-retirement callback stays retryable. This lease
            // never decrements the reader count a second time.
            if (owner.NativeReaders == 0) owner.NativeReadersRetired?.Invoke();
        }
    }
    private void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("DirectInput state belongs to the product input thread.");
    }
    private bool PublishHookKey(int key, bool raw, List<(int Key, bool Down)> edges)
    {
        var before = _keys[key];
        var (game, inserted) = ReadHookFilter(before, raw);
        var after = before with { Raw = raw, Game = game, Inserted = inserted, Tap = before.ScriptDisabled && before.Tap };
        _keys[key] = after;
        if (before.Game != after.Game) edges.Add((key, after.Game));
        return game;
    }
    private static (bool Game, bool Inserted) ReadHookFilter(FalloutDirectInputKey fields, bool raw)
    {
        var inserted = !fields.ScriptDisabled && (fields.Hold || fields.Tap);
        return (raw && !fields.UserDisabled || inserted, inserted);
    }
}
