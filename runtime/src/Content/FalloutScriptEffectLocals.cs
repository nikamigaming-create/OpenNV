namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptEffectLocalCell(uint Index, ulong Payload, int Ordinal = 0);

internal sealed partial class FalloutScriptEffectLocals
{
    private readonly FalloutScriptEffectLocalStorage _storage;
    internal FalloutFormKey Script => _storage.Script;
    internal FalloutScriptEffectLocals(FalloutPluginRecord script, IReadOnlyList<FalloutScriptEffectLocalCell>? restore = null)
        => _storage = new(script, restore);
    internal bool Contains(string name) => _storage.Contains(name);
    internal FalloutScriptLocalKind Kind(string name) => _storage.Kind(name);
    internal FalloutScriptValue Read(string name) => _storage.Read(name);
    internal void Write(string name, FalloutScriptValue value) => _storage.Write(name, value);
    internal IReadOnlyList<FalloutScriptEffectLocalCell> Capture() => _storage.Capture();
}
