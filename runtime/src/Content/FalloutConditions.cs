using System.Buffers.Binary;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Content;

// Source platform predicates select the PC edition's authored UI and scripts.
// They describe the game platform, not the host process's pointer width.
internal static class FalloutPlatformConditions
{
    internal static float? Evaluate(FalloutCondition condition) => condition.Kind switch
    {
        FalloutConditionFunction.IsXBox or FalloutConditionFunction.IsPS3 => 0,
        FalloutConditionFunction.IsWin32 => 1, // PC edition, including the 64-bit reimplementation.
        _ => null,
    };
}

internal sealed record FalloutCondition(FalloutPluginRecord Owner, byte Flags, float Comparison,
    ushort Function, uint Argument1, uint Argument2, uint RunOn, uint Reference)
{
    internal FalloutFormKey FormArgument1 => Owner.Plugin.AdjustFormId(Argument1);
    internal FalloutConditionFunction Kind => (FalloutConditionFunction)Function;
    internal uint SubjectRunOn => (Flags & 2) != 0 ? 1 : RunOn;

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

    internal static bool AllPass(IReadOnlyList<FalloutCondition> conditions, Func<FalloutCondition, float> evaluate,
        bool evaluateRunOn = false)
    {
        var group = false;
        foreach (var condition in conditions)
        {
            if (!group)
            {
                if ((condition.Flags & 0x18) != 0 || condition.SubjectRunOn != 0 && !evaluateRunOn)
                    throw new NotSupportedException($"{condition.Owner.FormKey} CTDA needs its flags/run-on owner.");
                var actual = evaluate(condition);
                if (!float.IsFinite(actual)) throw new InvalidDataException("Non-finite condition result.");
                // The comparison union contains a declaring-plugin GLOB FormID
                // when Use Global is set. Its value uses the same bound global
                // query owner as an authored GetGlobalValue predicate.
                var comparison = (condition.Flags & 4) == 0 ? condition.Comparison : evaluate(condition with
                {
                    Flags = 0,
                    Comparison = 0,
                    Function = (ushort)FalloutConditionFunction.GetGlobalValue,
                    Argument1 = unchecked((uint)BitConverter.SingleToInt32Bits(condition.Comparison)),
                    Argument2 = 0,
                    RunOn = 0,
                    Reference = 0,
                });
                if (!float.IsFinite(comparison)) throw new InvalidDataException("Non-finite condition comparison.");
                group = (condition.Flags >> 5) switch
                {
                    0 => actual == comparison,
                    1 => actual != comparison,
                    2 => actual > comparison,
                    3 => actual >= comparison,
                    4 => actual < comparison,
                    5 => actual <= comparison,
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
