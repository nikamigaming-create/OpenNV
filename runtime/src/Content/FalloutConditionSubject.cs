using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Caller, target and linked reference are invocation state, never identities
// borrowed from whichever consumer happens to evaluate the declaration.
internal readonly record struct FalloutConditionScope(FalloutFormKey? Caller = null, FalloutFormKey? Target = null,
    Func<FalloutFormKey, FalloutFormKey?>? LinkedReference = null)
{
    internal FalloutFormKey Resolve(FalloutCondition condition) => condition.SubjectRunOn switch
    {
        0 => ActualCaller,
        1 => Target ?? PackageTarget(condition),
        2 => condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference) ??
            throw new InvalidDataException("Condition has no explicit run-on reference."),
        4 when condition.Reference == 0 => LinkedReference?.Invoke(ActualCaller) ??
            throw new NotSupportedException("Condition has no source-linked reference owner."),
        _ => throw new NotSupportedException($"Condition run-on {condition.RunOn} has no qualified subject owner."),
    };

    private FalloutFormKey ActualCaller => Caller ??
        throw new NotSupportedException("Condition has no calling-reference owner.");

    private static FalloutFormKey PackageTarget(FalloutCondition condition)
    {
        if (condition.Owner.Signature != "PACK")
            throw new NotSupportedException("Condition has no invocation target.");
        var fields = condition.Owner.ReadSubrecords().Where(field => field.Signature == "PTDT").ToArray();
        if (fields.Length != 1 || fields[0].Data.Length != 16)
            throw new InvalidDataException("Condition has an invalid package target extent.");
        var data = fields[0].Data.Span;
        if (BinaryPrimitives.ReadInt32LittleEndian(data) != 0)
            throw new NotSupportedException("Condition package target is not an explicit reference.");
        return condition.Owner.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(data[4..])) ??
            throw new InvalidDataException("Condition has no package target reference.");
    }
}
