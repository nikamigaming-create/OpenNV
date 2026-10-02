namespace OpenNV.Runtime.Content;

internal static class FalloutNumericGameSettingCommands
{
    internal static FalloutScriptFunction? Function(FalloutPluginStack records, string operation) => operation switch
    {
        "getnumericgamesetting" => new([FalloutScriptArgumentKind.SourceString], arguments => records.NumericSettings.Get(arguments[0].Text)) { ReadOnly = true },
        "getgamesetting" or "getgs" => new([FalloutScriptArgumentKind.Identifier], arguments => records.NumericSettings.Read(arguments[0].Identifier!)) { ReadOnly = true },
        "setnumericgamesetting" => new([FalloutScriptArgumentKind.SourceString, FalloutScriptArgumentKind.Number],
            arguments => records.NumericSettings.Set(arguments[0].Text, arguments[1].Number) ? 1 : 0),
        _ => null,
    };

    internal static void Set(FalloutPluginStack records, IReadOnlyList<string> arguments, Func<string, double> number,
        Func<string, string> settingName)
    {
        if (arguments.Count != 2) throw new InvalidDataException("SetNumericGameSetting requires a setting name and value.");
        // The host distinguishes source bare-name constants from declared
        // string locals and already resolved string expressions.
        _ = records.NumericSettings.Set(settingName(arguments[0]), number(arguments[1]));
    }
}
