using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutNpcFacePresets
{
    // Native selection prefers marked presets, falls back to the other NPCs,
    // excludes the player, and sorts the source display-name bytes.
    internal static IReadOnlyList<FalloutPluginRecord> Resolve(FalloutPluginStack records,
        FalloutFormKey player, FalloutFormKey race, bool female, Func<FalloutPluginRecord, FalloutFormKey>? currentRace = null)
    {
        var candidates = records.EffectiveRecordsInRegistrationOrder("NPC_").Where(row => row.FormKey != player &&
            (currentRace?.Invoke(row) ?? Race(row)) == race && ((Flags(row) & 1) != 0) == female).ToArray();
        var marked = candidates.Where(row => (Flags(row) & 4) != 0).ToArray();
        var result = marked.Length == 0 ? candidates : marked;
        FalloutFacePresetOrder.Sort(result, (left, right) => StringComparer.Ordinal.Compare(Name(left), Name(right)));
        return result;
    }

    internal static FalloutPluginRecord First(IReadOnlyList<FalloutPluginRecord> presets)
    {
        if (presets.Count == 0) throw new NotSupportedException("Face matching has no source race/sex preset.");
        return presets[0];
    }

    private static uint Flags(FalloutPluginRecord record) => BinaryPrimitives.ReadUInt32LittleEndian(Field(record, "ACBS", 24).Span);
    private static FalloutFormKey Race(FalloutPluginRecord record) => record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(Field(record, "RNAM", 4).Span));
    private static string Name(FalloutPluginRecord record)
    {
        var fields = record.ReadSubrecords().Where(row => row.Signature == "FULL").ToArray();
        if (fields.Length == 0) return "";
        if (fields.Length != 1) throw new InvalidDataException("Face preset has duplicate display names.");
        return FalloutDialogueTopic.Text(fields[0].Data.Span);
    }
    private static ReadOnlyMemory<byte> Field(FalloutPluginRecord record, string signature, int length)
    {
        var fields = record.ReadSubrecords().Where(row => row.Signature == signature).ToArray();
        return fields.Length == 1 && fields[0].Data.Length == length ? fields[0].Data :
            throw new InvalidDataException($"Face preset {signature} has an invalid extent.");
    }
}
