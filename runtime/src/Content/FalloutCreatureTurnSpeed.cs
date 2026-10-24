using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static class FalloutCreatureTurnSpeed
{
    internal static float ReadAuthoredDegrees(FalloutPluginStack records, FalloutPluginRecord creature,
        FalloutActorTemplateSelection? selection = null)
    {
        if (creature.Signature != "CREA") throw new InvalidDataException("Creature turn speed requires CREA.");
        var stats = FalloutActorTemplateOwner.Resolve(records, creature, 2, selection);
        var fields = stats.ReadSubrecords().Where(field => field.Signature == "TNAM").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException("Creature turn speed has invalid TNAM data.");
        var value = BinaryPrimitives.ReadSingleLittleEndian(fields[0].Data.Span);
        if (!float.IsFinite(value) || value < 0)
            throw new NotSupportedException("Creature turn speed requires a finite nonnegative authored value.");
        return value;
    }

    internal static float ResolveDegrees(FalloutPluginStack records, float authoredDegrees)
    {
        if (!float.IsFinite(authoredDegrees) || authoredDegrees < 0)
            throw new NotSupportedException("Creature turn speed requires a finite nonnegative authored value.");
        // Zero requests the current setting. It is not an instant turn and is
        // not replaced with a baked-in numeric default.
        var selected = authoredDegrees == 0
            ? FalloutGameSettingFloats.Read(records, "fCreatureDefaultTurningSpeed") : authoredDegrees;
        if (!float.IsFinite(selected) || selected <= 0)
            throw new NotSupportedException("Creature turn speed has no supported positive selected rate.");
        return selected;
    }
}
