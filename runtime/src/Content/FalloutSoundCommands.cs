namespace OpenNV.Runtime.Content;

internal static class FalloutSoundCommands
{
    internal static FalloutScriptFunction? Function(FalloutPluginStack records, string operation) =>
        operation == "stopsound" ? new([FalloutScriptArgumentKind.Value, FalloutScriptArgumentKind.OptionalValue], arguments =>
        {
            FalloutFormKey Form(FalloutScriptValue value)
            {
                if (value.Kind == FalloutScriptValueKind.Number) value = FalloutScriptValue.Form(value.Number);
                return value.FormKey(records);
            }
            var sound = Form(arguments[0].Value);
            FalloutFormKey? reference = null;
            if (arguments.Count == 2)
            {
                var filter = arguments[1].Value;
                if (filter.Kind is not (FalloutScriptValueKind.Number or FalloutScriptValueKind.Form) || filter.Number != 0)
                    reference = Form(filter);
            }
            records.SoundVoices.Stop(sound, reference);
            // The command does not return a count of stopped instances.
            return 0;
        }) : null;
}
