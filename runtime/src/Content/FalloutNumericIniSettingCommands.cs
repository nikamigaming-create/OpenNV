namespace OpenNV.Runtime.Content;

internal static class FalloutNumericIniSettingCommands
{
    internal static FalloutScriptFunction? Function(FalloutPluginStack records, string operation) => operation switch
    {
        "getnumericinisetting" => new([FalloutScriptArgumentKind.SourceString],
            arguments => records.IniSettings.Get(arguments[0].Text))
        { ReadOnly = true },
        _ => null,
    };
}
