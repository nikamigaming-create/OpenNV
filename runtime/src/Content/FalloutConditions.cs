using System.Buffers.Binary;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

// Source platform predicates select the PC edition's authored UI and scripts.
// They describe the game platform, not the host process's pointer width.
internal static class FalloutPlatformConditions
{
    internal static float? Evaluate(FalloutCondition condition) => condition.Function switch
    {
        309 or 523 => 0, // IsXBox / IsPS3
        524 => 1, // IsWin32 (PC edition, including the 64-bit reimplementation)
        _ => null,
    };
}

// The comparison-global flag changes the four-byte RHS field's type. The raw
// FormID belongs to the condition's plugin, independently of function arguments.
internal sealed record FalloutConditionGlobalComparison(uint RawFormId);

internal sealed record FalloutCondition(FalloutPluginRecord Owner, byte Flags, float Comparison,
    ushort Function, uint Argument1, uint Argument2, uint RunOn, uint Reference,
    FalloutConditionGlobalComparison? GlobalComparison = null)
{
    internal const byte ComparisonGlobalFlag = 0x04;
    internal const byte UnownedFlagMask = 0x1a;
    internal FalloutFormKey FormArgument1 => Owner.Plugin.AdjustFormId(Argument1);
    internal bool UsesComparisonGlobal => (Flags & ComparisonGlobalFlag) != 0;

    private float ComparisonValue(FalloutGlobalState? globals)
    {
        if (UsesComparisonGlobal)
        {
            if (GlobalComparison is null || Comparison != 0)
                throw new InvalidDataException("Comparison-global CTDA lost or mixed its raw FormID operand.");
            return (globals ?? throw new NotSupportedException(
                $"{Owner.FormKey} CTDA comparison-global has no live shared global owner."))
                .ReadConditionComparison(this);
        }
        if (GlobalComparison is not null || !float.IsFinite(Comparison))
            throw new InvalidDataException("Literal CTDA has a mismatched or non-finite comparison operand.");
        return Comparison;
    }

    internal static IReadOnlyList<FalloutCondition> Read(FalloutPluginRecord record) => record.ReadSubrecords()
        .Where(field => field.Signature == "CTDA").Select(field => Read(record, field.Data.Span)).ToArray();

    internal static FalloutCondition Read(FalloutPluginRecord record, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is not (20 or 24 or 28)) throw new InvalidDataException($"{record.FormKey} has an invalid CTDA extent.");
        var flags = bytes[0];
        var global = (flags & ComparisonGlobalFlag) != 0;
        var comparison = global ? 0 : BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]);
        if (!float.IsFinite(comparison)) throw new InvalidDataException("Non-finite CTDA comparison.");
        if (flags >> 5 > 5) throw new InvalidDataException("Unknown CTDA comparison operation.");
        return new(record, flags, comparison, BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]), BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]),
            bytes.Length >= 24 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]) : 0,
            bytes.Length == 28 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]) : 0,
            global ? new(BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..])) : null);
    }

    internal static bool AllPass(IReadOnlyList<FalloutCondition> conditions, Func<FalloutCondition, float> evaluate,
        bool evaluateRunOn = false, FalloutGlobalState? globals = null)
    {
        var group = false;
        foreach (var condition in conditions)
        {
            if (!group)
            {
                if ((condition.Flags & UnownedFlagMask) != 0 || condition.RunOn != 0 && !evaluateRunOn)
                    throw new NotSupportedException($"{condition.Owner.FormKey} CTDA needs its flags/run-on owner.");
                var actual = evaluate(condition);
                if (!float.IsFinite(actual)) throw new InvalidDataException("Non-finite condition result.");
                // The function result and comparison global are independent reads.
                // Resolve the RHS only for a reached condition in this OR group.
                var comparison = condition.ComparisonValue(globals);
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
