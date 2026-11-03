namespace OpenNV.Runtime.Content;

internal static class FalloutRecordDisplayName
{
    // TESFullName returns an empty string for an unassigned component. EDID is
    // an editor identity and is not the native display-name fallback.
    internal static string Read(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(field => field.Signature == "FULL").ToArray();
        if (fields.Length == 0) return string.Empty;
        if (fields.Length != 1) throw new InvalidDataException("Source FULL identity is ambiguous.");
        return fields[0].Data.IsEmpty ? string.Empty : FalloutDialogueTopic.Text(fields[0].Data.Span);
    }

    // The retained encounter selection is shared with every other template
    // group; resolving a name must never create a second leveled choice.
    internal static string ReadActor(FalloutPluginStack records, FalloutPluginRecord actor,
        FalloutActorTemplateSelection? selection = null) => Read(FalloutActorTemplateOwner.Resolve(records, actor,
            (ushort)Definitions.FalloutActorTemplateGroup.BaseData, selection));

    internal static string ReadOrMissing(FalloutPluginStack records, FalloutPluginRecord record)
    {
        var name = Read(record);
        return name.Length == 0 ? FalloutGameSettingStrings.Read(records, "sMissingName") : name;
    }
}
