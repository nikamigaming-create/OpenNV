using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// A functional certain/silent-event lane, not the native chance/selection RNG policy.
// Neither this reader nor the caller invents a creature chance draw. Audio
// resource/variant selection and playback remain with the real sound owner.
internal static class FalloutCreatureAnimationSounds
{
    internal static FalloutFormKey? RequiredCertainSound(FalloutPluginStack records,
        FalloutFormKey creature, string enumName)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(enumName);
        var name = enumName.Trim();
        if (name.StartsWith("Enum:", StringComparison.OrdinalIgnoreCase)) name = name[5..].Trim();
        var type = name.ToUpperInvariant() switch
        {
            "AUXILIARY1" => 12u,
            "AUXILIARY2" => 13u,
            "AUXILIARY3" => 14u,
            _ => throw new NotSupportedException($"Creature animation enum {enumName} has no certain-event sound owner.")
        };
        var record = records.GetEffective(creature);
        if (record.Signature != "CREA" || (record.Flags & FalloutPluginRecord.DeletedFlag) != 0)
            throw new InvalidDataException($"Creature enum sound requires a live winning CREA, found {creature}/{record.Signature}.");
        var fields = record.ReadSubrecords().ToArray();
        RequireLocal("CSCR"); RequireLocal("TPLT");
        var start = -1; var end = fields.Length;
        for (var index = 0; index < fields.Length; ++index)
        {
            if (fields[index].Signature != "CSDT") continue;
            // A malformed marker cannot be classified as unrelated. Child
            // data of a valid different type is deliberately not interpreted.
            if (fields[index].Data.Length != sizeof(uint))
                throw new InvalidDataException($"CREA {creature} has an unclassifiable CSDT extent.");
            if (start >= 0 && end == fields.Length) end = index;
            if (BinaryPrimitives.ReadUInt32LittleEndian(fields[index].Data.Span) != type) continue;
            if (start >= 0) throw new InvalidDataException($"CREA {creature} repeats sound type {type} ({name}).");
            start = index + 1;
        }
        if (start < 0) throw new NotSupportedException($"CREA {creature} has no sound type {type} ({name}).");
        var selected = fields[start..end];
        if (selected.Any(field => field.Signature.StartsWith("CSD", StringComparison.Ordinal) &&
            field.Signature is not ("CSDI" or "CSDC")))
            throw new NotSupportedException($"CREA {creature} sound type {type} contains an unsupported sound declaration.");
        var sounds = selected.Select((field, index) => (Field: field, Index: index))
            .Where(value => value.Field.Signature == "CSDI").ToArray();
        var chances = selected.Select((field, index) => (Field: field, Index: index))
            .Where(value => value.Field.Signature == "CSDC").ToArray();
        if (sounds.Length != 1 || chances.Length != 1)
            throw new NotSupportedException($"CREA {creature} sound type {type} requires exactly one CSDI/CSDC pair; " +
                $"found {sounds.Length}/{chances.Length}. Candidate selection is unqualified.");
        if (sounds[0].Field.Data.Length != sizeof(uint) || chances[0].Field.Data.Length != sizeof(byte) ||
            chances[0].Index != sounds[0].Index + 1)
            throw new InvalidDataException($"CREA {creature} sound type {type} has an invalid CSDI/CSDC extent or order.");
        var chance = chances[0].Field.Data.Span[0];
        if (chance > 100) throw new InvalidDataException($"CREA {creature} sound type {type} chance exceeds 100%.");
        var sound = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(sounds[0].Field.Data.Span));
        // A single source-null candidate is silent for every valid outcome.
        // This does not establish whether the native gate consumes RNG.
        if (sound is null) return null;
        if (chance != 100)
            throw new NotSupportedException($"CREA {creature} sound type {type} is not a certain (100%) event; chance policy is unqualified.");
        if (!records.TryGetEffective(sound.Value, out var source) || source.Signature != "SOUN" ||
            (source.Flags & FalloutPluginRecord.DeletedFlag) != 0)
            throw new InvalidDataException($"CREA {creature} sound type {type} does not bind a live winning SOUN {sound}.");
        return sound.Value;

        void RequireLocal(string signature)
        {
            var links = fields.Where(field => field.Signature == signature).ToArray();
            if (links.Length == 0) return;
            if (links.Length != 1 || links[0].Data.Length != sizeof(uint))
                throw new InvalidDataException($"CREA {creature} has an invalid {signature} sound-inheritance declaration.");
            if (record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span)) is not null)
                throw new NotSupportedException($"CREA {creature} {signature} requires its unqualified inherited sound owner.");
        }
    }
}
