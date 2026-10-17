namespace OpenNV.Runtime.Content;

// Menu identity is published by the input/presentation adapter. Script queries
// and event filters read one frame context; absence of an identity is explicit.
internal sealed class FalloutScriptMenus
{
    private bool _gameMode = true;
    private HashSet<uint>? _codes;
    internal object State => new { gameMode = _gameMode, codes = _codes?.Order().ToArray() };

    internal void Publish(bool gameMode, IEnumerable<uint>? codes = null)
    {
        var selected = codes?.ToHashSet();
        if (selected?.Contains(0) == true || gameMode && selected is { Count: > 0 })
            throw new InvalidDataException("Menu frame identity conflicts with its mode.");
        _gameMode = gameMode;
        _codes = selected;
    }

    internal double Query(double? code = null)
    {
        if (code is { } value && (!double.IsFinite(value) || value < 0 || value > uint.MaxValue || value != Math.Truncate(value)))
            throw new InvalidDataException("MenuMode argument is not an unsigned menu code.");
        if (_gameMode) return 0;
        if (code is null or 0) return 1;
        if (_codes is null) throw new NotSupportedException("Filtered MenuMode has no active menu identity.");
        return _codes.Contains((uint)code.Value) ? 1 : 0;
    }

    internal IDisposable Enter(uint menu)
    {
        var previousMode = _gameMode; var previousCodes = _codes;
        Publish(false, [menu, Category(menu)]);
        return new Scope(() => { _gameMode = previousMode; _codes = previousCodes; });
    }

    internal static uint Category(uint menu) => FalloutScriptEventProgram.MenuCategory(menu);
    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() { var restore = _restore; _restore = null; restore?.Invoke(); }
    }
}
