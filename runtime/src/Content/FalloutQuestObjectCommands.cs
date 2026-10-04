namespace OpenNV.Runtime.Content;

internal static class FalloutQuestObjectCommands
{
    internal static FalloutScriptFunction? Function(FalloutPluginStack records, string operation) => operation == "setquestobject"
        ? new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.Number], arguments =>
        {
            var value = arguments[0].Value;
            if (value.Kind == FalloutScriptValueKind.Number) value = FalloutScriptValue.Form(value.Number);
            records.QuestObjects.Set(value.FormKey(records), Flag(arguments[1].Number));
            return 0;
        }) : null;

    private static bool Flag(double value) => value switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("SetQuestObject requires a zero or one flag."),
    };
}
