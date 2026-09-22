namespace OpenNV.Runtime.Content;

internal delegate double FalloutUserFunctionInvoker(FalloutFormKey script, FalloutFormKey? caller,
    IReadOnlyList<double> arguments, double seconds);

// Process-owned extension state. Saving or replacing a scene must not reset the
// per-script restart query or capture delegates into retired world owners.
internal sealed class FalloutScriptEvents
{
    private sealed class Callback(FalloutFormKey script, FalloutFormKey caller, int delay, int modes)
    {
        internal readonly FalloutFormKey Script = script, Caller = caller;
        internal readonly int Delay = delay, Modes = modes;
        internal int Remaining = delay;
        internal long Executions;
        internal string? Error;
    }

    private readonly HashSet<FalloutFormKey> _restartConsumed = [], _loadConsumed = [];
    private readonly Dictionary<(FalloutFormKey Script, FalloutFormKey Caller), Callback> _mainLoop = [];
    private readonly Dictionary<(FalloutFormKey Script, int Key, bool Down), Callback> _keys = [];
    private readonly HashSet<int> _pressed = [];
    private bool _loaded;
    internal object State => new
    {
        restartConsumers = _restartConsumed.Count,
        loadConsumers = _loadConsumed.Count,
        mainLoop = _mainLoop.Values.Select(Describe).ToArray(),
        keys = _keys.Select(pair => new { pair.Key.Key, pair.Key.Down, callback = Describe(pair.Value) }).ToArray(),
    };
    private static object Describe(Callback value) => new
    {
        value.Script,
        value.Caller,
        value.Delay,
        value.Modes,
        value.Remaining,
        value.Executions,
        value.Error,
    };

    internal bool GetGameRestarted(FalloutFormKey script) => _restartConsumed.Add(script);
    internal bool GetGameLoaded(FalloutFormKey script) => _loaded && _loadConsumed.Add(script);
    internal void LoadGame()
    {
        _loaded = true;
        _loadConsumed.Clear();
        EnterMainMenu();
    }
    internal void EnterMainMenu()
    {
        foreach (var key in _mainLoop.Where(pair => (pair.Value.Modes & 8) != 0).Select(pair => pair.Key).ToArray())
            _mainLoop.Remove(key);
        _pressed.Clear();
    }

    internal void SetMainLoop(FalloutFormKey script, FalloutFormKey caller, bool register, int delay = 1, int modes = 3)
    {
        var key = (script, caller);
        if (!register) { _mainLoop.Remove(key); return; }
        if (delay < 1 || (modes & ~11) != 0 || (modes & 3) == 0)
            throw new NotSupportedException("Main-loop callback delay or mode flags are invalid or unbound.");
        _mainLoop[key] = new(script, caller, delay, modes);
    }

    internal void SetKey(FalloutFormKey script, FalloutFormKey player, bool register, bool down, int? key)
    {
        if (key is not null) ValidateKey(key.Value);
        if (!register)
        {
            foreach (var item in _keys.Keys.Where(item => item.Script == script && item.Down == down &&
                (key is null || item.Key == key)).ToArray()) _keys.Remove(item);
            return;
        }
        if (key is null) throw new InvalidDataException("Key callback registration needs a key ID.");
        _keys[(script, key.Value, down)] = new(script, player, 1, 3);
    }

    internal bool IsKeyPressed(int key) { ValidateKey(key); return _pressed.Contains(key); }

    internal void Key(int key, bool down, FalloutUserFunctionInvoker invoke)
    {
        ValidateKey(key);
        if (!(down ? _pressed.Add(key) : _pressed.Remove(key))) return;
        // Snapshot admission prevents handlers added by this event from being
        // invoked recursively. Removal still takes effect before a later call.
        foreach (var pair in _keys.Where(pair => pair.Key.Key == key && pair.Key.Down == down).ToArray())
            if (_keys.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, pair.Value))
                Invoke(current, [key], 0, invoke);
    }

    internal void Advance(double seconds, bool gameMode, FalloutUserFunctionInvoker invoke)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        foreach (var pair in _mainLoop.ToArray())
        {
            var callback = pair.Value;
            if (!_mainLoop.TryGetValue(pair.Key, out var current) || !ReferenceEquals(callback, current) ||
                callback.Error is not null || (callback.Modes & (gameMode ? 1 : 2)) == 0) continue;
            if (--callback.Remaining != 0) continue;
            callback.Remaining = callback.Delay;
            Invoke(callback, [], seconds, invoke);
        }
    }

    private static void Invoke(Callback callback, IReadOnlyList<double> arguments, double seconds, FalloutUserFunctionInvoker invoke)
    {
        if (callback.Error is not null) return;
        try { _ = invoke(callback.Script, callback.Caller, arguments, seconds); ++callback.Executions; }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        { callback.Error = error.Message; }
    }

    private static void ValidateKey(int key)
    {
        if (key is < 1 or > 265) throw new NotSupportedException("DirectInput key ID is invalid or unbound.");
    }
}
