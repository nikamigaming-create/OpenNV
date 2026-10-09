namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutScriptArrayStore
{
    internal bool NativeContains(uint id) => id != 0 && _arrays.ContainsKey(id);
    internal FalloutScriptValue NativeConstruct(FalloutScriptArrayKind kind) => Construct(kind);
    internal IReadOnlyList<(FalloutScriptValue Key, FalloutScriptValue Value)> NativeEntries(uint id) =>
        Data(Reference(id)).Entries.ToArray();
}
