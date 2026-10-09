namespace OpenNV.Runtime.Content;

// Startup and the resident player share these original command contracts.
// Source records select identities; this owner never creates a replacement
// actor, predicts a quest result or interprets diagnostic script text.
internal static class FalloutScriptStartupCommands
{
    internal static FalloutNativeRaceSexSelection ChangeSex(FalloutPluginStack records,
        FalloutNativeRaceSexContract contract, FalloutNativeRaceSexSelection current,
        Func<FalloutInstallationSettings> settings, IReadOnlyList<string> arguments)
    {
        if (arguments.Count > 2) throw new InvalidDataException("SexChange has too many arguments.");
        var female = arguments.Count == 0 ? !current.Female : arguments[0].ToLowerInvariant() switch
        {
            "male" or "0" => false,
            "female" or "1" => true,
            _ => throw new InvalidDataException("SexChange target sex is invalid.")
        };
        var reset = arguments.Count == 2 ? arguments[1] switch
        {
            "0" => false,
            "1" => true,
            _ => throw new InvalidDataException("SexChange reset flag is invalid.")
        } : false;
        FalloutNativeRaceSexResolver.Validate(contract, current);
        if (female == current.Female) return current;
        FalloutNativeRaceSexSelection next;
        if (reset)
        {
            var creation = new FalloutNativeCharacterCreation(records, contract, current, settings());
            creation.ChangeIdentity(current.RaceRuntimeFormId, female);
            next = creation.Selection;
        }
        else next = contract.Select(current.RaceRuntimeFormId, female, current) with { Face = current.Face };
        FalloutNativeRaceSexResolver.Validate(contract, next);
        return next;
    }

    internal static void RequireMusic(FalloutPluginRecord music)
    {
        if (music.Signature != "MUSC") throw new InvalidDataException("Script music is not MUSC.");
        var fields = music.ReadSubrecords().ToArray();
        if (fields.Any(field => field.Signature != "EDID"))
            throw new NotSupportedException("Nonempty script music requires its streaming playback owner.");
        if (fields.Length > 1)
            throw new InvalidDataException("Script music repeats its source identity field.");
        if (fields.Length == 1) _ = FalloutDialogueTopic.Text(fields[0].Data.Span);
    }
}
