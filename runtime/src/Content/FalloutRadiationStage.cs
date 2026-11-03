using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutRadiationStage(FalloutFormKey Form, uint Threshold, FalloutFormKey? Spell)
{
    private static readonly ConditionalWeakTable<FalloutPluginStack, FalloutRadiationStage[]> Stages = new();
    internal static IReadOnlyList<FalloutRadiationStage> Read(FalloutPluginStack records) => Stages.GetValue(records, stack =>
        stack.EffectiveRecords("RADS").Select(record =>
        {
            var data = record.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
            if (data.Length != 8) throw new InvalidDataException("RADS DATA extent is not eight bytes.");
            var threshold = BinaryPrimitives.ReadUInt32LittleEndian(data.Span);
            var spell = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[4..]);
            return new FalloutRadiationStage(record.FormKey, threshold, spell == 0 ? null : record.Plugin.AdjustFormId(spell));
        }).OrderByDescending(stage => stage.Threshold).ToArray());

    internal static float MenuMaximum(FalloutPluginStack records)
    {
        var stages = Read(records);
        var maximum = stages.Count == 0 ? 1000u : stages[0].Threshold;
        if (maximum == 0 || maximum > int.MaxValue)
            throw new NotSupportedException("Radiation menu scale has no positive signed threshold.");
        return maximum;
    }

    internal static float DisplayRads(float rads)
    {
        if (!float.IsFinite(rads)) throw new InvalidDataException("Radiation is not finite.");
        rads = Math.Max(0, rads);
        return rads is > 0 and < 1 ? 1 : (float)Math.Floor((double)rads + .5);
    }
}
