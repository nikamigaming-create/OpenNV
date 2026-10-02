namespace OpenNV.Runtime.Content;

internal static class FalloutSoundCommands
{
    internal static FalloutScriptFunction? Function(FalloutPluginStack records, string operation) =>
        operation switch
        {
            "getsoundsourcefile" => FalloutScriptFunction.Typed([FalloutScriptArgumentKind.Value],
                arguments => FalloutScriptValue.String(records.SoundPaths.Read(Form(records, arguments[0].Value)).File), readOnly: true),
            "setsoundsourcefile" => new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.String], arguments =>
            {
                records.SoundPaths.Set(Form(records, arguments[0].Value), arguments[1].Text);
                return 0;
            }),
            "stopsound" => new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.OptionalValue], arguments =>
            {
                var sound = Form(records, arguments[0].Value);
                FalloutFormKey? reference = null;
                if (arguments.Count == 2)
                {
                    var filter = arguments[1].Value;
                    if (filter.Kind is not (FalloutScriptValueKind.Number or FalloutScriptValueKind.Form) || filter.Number != 0)
                        reference = Form(records, filter);
                }
                records.SoundVoices.Stop(sound, reference);
                // The command does not return a count of stopped instances.
                return 0;
            }),
            _ => null,
        };

    private static FalloutFormKey Form(FalloutPluginStack records, FalloutScriptValue value)
    {
        if (value.Kind == FalloutScriptValueKind.Number) value = FalloutScriptValue.Form(value.Number);
        return value.FormKey(records);
    }
}
