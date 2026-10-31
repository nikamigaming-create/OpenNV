using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Source-resolved single-candidate declarations and their process-owned percentage gate.
// Audio resource/variant selection and playback remain with the sound owner.
internal static class FalloutCreatureAnimationSounds
{
    internal enum SelectionKind { DeclaredNull, ChanceMiss, Selected }

    internal readonly record struct Selection(SelectionKind Kind, FalloutFormKey? Sound, uint? Roll);

    internal sealed record Declaration(FalloutFormKey Creature, uint Type, FalloutFormKey? Sound, byte Chance)
    {
        internal Selection Select(Func<uint, uint> sharedNextBounded)
        {
            if (Creature.ObjectId == 0 || !SupportedType(Type) || Chance > 100 ||
                Sound is { ObjectId: 0 })
                throw new InvalidDataException("Creature sound selection has an invalid source declaration.");
            // The source tests the candidate pointer before touching shared RNG.
            if (Sound is null) return new(SelectionKind.DeclaredNull, null, null);
            ArgumentNullException.ThrowIfNull(sharedNextBounded);
            // Even a 0% or 100% nonnull candidate consumes this one draw.
            var roll = sharedNextBounded(100);
            if (roll >= 100) throw new InvalidDataException("Creature sound gate returned an out-of-domain roll.");
            return roll < Chance ? new(SelectionKind.Selected, Sound, roll) : new(SelectionKind.ChanceMiss, null, roll);
        }
    }

    internal static Declaration ReadSingleSound(FalloutPluginStack records,
        FalloutFormKey creature, string enumName)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(enumName);
        if (!TryType(enumName, out var type))
            throw new NotSupportedException($"Creature animation enum {enumName} has no source sound owner.");
        var name = enumName.Trim();
        var record = records.GetEffective(creature);
        var visited = new HashSet<FalloutFormKey>();
        FalloutPluginSubrecord[] fields;
        while (true)
        {
            if (record.Signature != "CREA" || (record.Flags & FalloutPluginRecord.DeletedFlag) != 0 || !visited.Add(record.FormKey))
                throw new InvalidDataException($"Creature enum sound has a deleted, non-CREA or cyclic source at {record.FormKey}.");
            fields = record.ReadSubrecords().ToArray();
            var links = fields.Where(field => field.Signature == "CSCR").ToArray();
            if (links.Length > 1 || links.Length == 1 && links[0].Data.Length != sizeof(uint))
                throw new InvalidDataException($"CREA {record.FormKey} has an invalid CSCR sound-inheritance declaration.");
            var next = links.Length == 0 ? null : record.Plugin.AdjustOptionalFormId(
                BinaryPrimitives.ReadUInt32LittleEndian(links[0].Data.Span));
            if (next is null) break;
            if (fields.Any(field => field.Signature == "CSDT"))
                throw new NotSupportedException($"CREA {record.FormKey} combines local and inherited sound declarations.");
            record = records.GetEffective(next.Value);
        }
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
        if (start < 0) return new(creature, type, null, 0);
        var selected = fields[start..end];
        if (selected.Any(field => field.Signature.StartsWith("CSD", StringComparison.Ordinal) &&
            field.Signature is not ("CSDI" or "CSDC")))
            throw new NotSupportedException($"CREA {creature} sound type {type} contains an unsupported sound declaration.");
        var sounds = selected.Select((field, index) => (Field: field, Index: index))
            .Where(value => value.Field.Signature == "CSDI").ToArray();
        var chances = selected.Select((field, index) => (Field: field, Index: index))
            .Where(value => value.Field.Signature == "CSDC").ToArray();
        if (sounds.Length == 0 && chances.Length == 0) return new(creature, type, null, 0);
        if (sounds.Length != 1 || chances.Length != 1)
            throw new NotSupportedException($"CREA {creature} sound type {type} requires exactly one CSDI/CSDC pair; " +
                $"found {sounds.Length}/{chances.Length}. Candidate selection is unqualified.");
        if (sounds[0].Field.Data.Length != sizeof(uint) || chances[0].Field.Data.Length != sizeof(byte) ||
            chances[0].Index != sounds[0].Index + 1)
            throw new InvalidDataException($"CREA {creature} sound type {type} has an invalid CSDI/CSDC extent or order.");
        var chance = chances[0].Field.Data.Span[0];
        if (chance > 100) throw new InvalidDataException($"CREA {creature} sound type {type} chance exceeds 100%.");
        var sound = record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(sounds[0].Field.Data.Span));
        if (sound is { } form && (!records.TryGetEffective(form, out var source) || source.Signature != "SOUN" ||
            (source.Flags & FalloutPluginRecord.DeletedFlag) != 0))
            throw new InvalidDataException($"CREA {creature} sound type {type} does not bind a live winning SOUN {sound}.");
        return new(creature, type, sound, chance);

    }
    private static bool SupportedType(uint type) => type <= 6 || type is >= 12 and <= 19;

    internal static bool TryType(string text, out uint type)
    {
        var name = text.Trim();
        if (name.StartsWith("Enum:", StringComparison.OrdinalIgnoreCase)) name = name[5..].Trim();
        type = name.ToUpperInvariant() switch
        {
            "LEFT" => 0,
            "RIGHT" => 1,
            "BACKLEFT" => 2,
            "BACKRIGHT" => 3,
            "IDLE" => 4,
            "AWARE" => 5,
            "ATTACK" => 6,
            "AUXILIARY1" => 12,
            "AUXILIARY2" => 13,
            "AUXILIARY3" => 14,
            "AUXILIARY4" => 15,
            "AUXILIARY5" => 16,
            "AUXILIARY6" => 17,
            "AUXILIARY7" => 18,
            "AUXILIARY8" => 19,
            _ => uint.MaxValue
        };
        return type != uint.MaxValue;
    }

}
