namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutScriptValueStore
{
    internal string? NativeString(uint id) => id != 0 && _strings.TryGetValue(id, out var value) ? value.Text : null;
    internal void NativeSetString(uint id, string text)
    {
        _ = FalloutScriptValue.String(text);
        if (id != 0 && _strings.ContainsKey(id)) _ = WriteString(id, text, string.Empty);
    }
    internal uint NativeCreateString(string text, string ownerPlugin) => checked((uint)WriteString(0, text, ownerPlugin));
}
