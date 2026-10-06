using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutActorInventoryConditions
{
    internal static float ItemCount(FalloutCondition condition, FalloutFormKey caller,
        Func<FalloutFormKey, FalloutFormKey, double>? itemCount)
    {
        if (condition.Function != 47 || condition.Argument2 != 0)
            throw new InvalidDataException("Actor inventory predicate differs from the source GetItemCount declaration.");
        var subject = FalloutAiPackages.ConditionSubject(condition, caller);
        var count = (itemCount ?? throw new NotSupportedException("Actor item count has no shared inventory owner."))
            (subject, condition.FormArgument1);
        if (!double.IsFinite(count) || count < 0 || count != Math.Truncate(count) || count > int.MaxValue)
            throw new InvalidDataException("Actor item count has no finite nonnegative source count.");
        return (float)count;
    }
}
