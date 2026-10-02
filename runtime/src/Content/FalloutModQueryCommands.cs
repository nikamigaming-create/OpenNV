namespace OpenNV.Runtime.Content;

internal static class FalloutModQueryCommands
{
    internal static FalloutScriptFunction? Function(FalloutPluginStack records, string operation) => operation switch
    {
        "ismodloaded" => new([FalloutScriptArgumentKind.String],
            arguments => records.IsPluginLoaded(arguments[0].Text) ? 1 : 0)
        { ReadOnly = true },
        _ => null,
    };
}
