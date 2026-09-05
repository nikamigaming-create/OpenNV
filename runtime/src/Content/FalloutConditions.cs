using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCondition(FalloutPluginRecord Owner, byte Flags, float Comparison,
    ushort Function, uint Argument1, uint Argument2, uint RunOn, uint Reference)
{
    internal FalloutFormKey FormArgument1 => Owner.Plugin.AdjustFormId(Argument1);

    internal static IReadOnlyList<FalloutCondition> Read(FalloutPluginRecord record) => record.ReadSubrecords()
        .Where(field => field.Signature == "CTDA").Select(field => Read(record, field.Data.Span)).ToArray();

    internal static FalloutCondition Read(FalloutPluginRecord record, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is not (20 or 24 or 28)) throw new InvalidDataException($"{record.FormKey} has an invalid CTDA extent.");
        var flags = bytes[0];
        var comparison = BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]);
        if ((flags & 4) == 0 && !float.IsFinite(comparison)) throw new InvalidDataException("Non-finite CTDA comparison.");
        if (flags >> 5 > 5) throw new InvalidDataException("Unknown CTDA comparison operation.");
        return new(record, flags, comparison, BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            bytes.Length >= 24 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]) : 0,
            bytes.Length == 28 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) : 0);
    }

    internal static bool AllPass(IReadOnlyList<FalloutCondition> conditions, Func<FalloutCondition, float> evaluate)
    {
        var group = false;
        foreach (var condition in conditions)
        {
            if (!group)
            {
                if ((condition.Flags & 0x1e) != 0 || condition.RunOn != 0)
                    throw new NotSupportedException($"{condition.Owner.FormKey} CTDA needs its flags/run-on owner.");
                var actual = evaluate(condition);
                if (!float.IsFinite(actual)) throw new InvalidDataException("Non-finite condition result.");
                group = (condition.Flags >> 5) switch
                {
                    0 => actual == condition.Comparison,
                    1 => actual != condition.Comparison,
                    2 => actual > condition.Comparison,
                    3 => actual >= condition.Comparison,
                    4 => actual < condition.Comparison,
                    5 => actual <= condition.Comparison,
                    _ => throw new InvalidDataException("Unknown CTDA comparison operation."),
                };
            }
            if ((condition.Flags & 1) == 0)
            {
                if (!group) return false;
                group = false;
            }
        }
        return conditions.Count == 0 || (conditions[^1].Flags & 1) == 0 || group;
    }
}
