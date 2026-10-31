using System.Buffers.Binary;
using System.Text;

namespace OpenNV.Runtime.Content;

// These records are inputs to real population/render owners,
// not proof that a source GRAS has been placed or drawn.
internal sealed record FalloutGrassDefinition(FalloutFormKey Form, string Model,
    byte Density, byte MinimumSlope, byte MaximumSlope, ushort WaterDistance,
    uint WaterMode, float PositionRange, float HeightRange, float ColorRange,
    float WavePeriod, byte Flags);

internal static class FalloutGrassSource
{
    internal static IReadOnlyList<FalloutGrassDefinition> ReadAssociations(
        FalloutPluginStack stack, FalloutPluginRecord texture)
    {
        if (texture.Signature != "LTEX") throw new InvalidDataException("Grass associations require LTEX.");
        var result = new List<FalloutGrassDefinition>();
        foreach (var row in texture.ReadSubrecords().Where(row => row.Signature == "GNAM"))
        {
            if (row.Data.Length != 4) throw new InvalidDataException($"LTEX {texture.FormKey} GNAM is not a FormID.");
            var key = texture.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(row.Data.Span));
            if (key is null) continue; // Native loading omits null GNAM rows.
            // Preserve order and duplicates. Native list/area admission consumes them.
            result.Add(ReadDefinition(stack.GetEffective(key.Value)));
        }
        return result;
    }

    internal static FalloutGrassDefinition ReadDefinition(FalloutPluginRecord record)
    {
        if (record.Signature != "GRAS") throw new InvalidDataException($"Grass association {record.FormKey} is not GRAS.");
        var rows = record.ReadSubrecords().ToArray();
        var dataRows = rows.Where(row => row.Signature == "DATA").ToArray();
        var models = rows.Where(row => row.Signature == "MODL").ToArray();
        if (dataRows.Length != 1 || dataRows[0].Data.Length != 32 || models.Length != 1)
            throw new InvalidDataException($"GRAS {record.FormKey} needs one DATA32 and source MODL.");
        var data = dataRows[0].Data.Span;
        var model = FalloutCellSceneReader.NormalizeModelPath(
            Encoding.UTF8.GetString(models[0].Data.Span).TrimEnd('\0'), "GRAS");
        var position = BinaryPrimitives.ReadSingleLittleEndian(data[12..]);
        var height = BinaryPrimitives.ReadSingleLittleEndian(data[16..]);
        var color = BinaryPrimitives.ReadSingleLittleEndian(data[20..]);
        var wave = BinaryPrimitives.ReadSingleLittleEndian(data[24..]);
        if (!float.IsFinite(position) || position <= 0 || !float.IsFinite(height) ||
            !float.IsFinite(color) || !float.IsFinite(wave))
            throw new InvalidDataException($"GRAS {record.FormKey} has unusable placement/variation fields.");
        // Alignment/padding bytes are deliberately not required to be zero.
        return new(record.FormKey, model, data[0], data[1], data[2],
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data[8..]), position, height, color, wave, data[28]);
    }

    internal static bool WaterAdmits(uint mode, float terrain, float water, ushort distance) => mode switch
    {
        0 => terrain >= water - distance,
        1 => terrain >= water && terrain <= water + distance,
        2 => terrain <= water - distance,
        3 => terrain >= water - distance && terrain <= water,
        4 => terrain >= water + distance || terrain <= water - distance,
        5 => terrain >= water - distance && terrain <= water + distance,
        _ => true, // Native switch bypasses exclusions for the remaining u32 modes.
    };
}
