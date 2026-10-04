using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal float? EvaluateActorReferenceCondition(FalloutFormKey caller, FalloutCondition condition,
        FalloutReferencePlacement? player = null, float unitsToMetres = 1)
    {
        if (condition.Function is not (1 or 14)) return null;
        if (condition.Argument2 != 0) throw new InvalidDataException("Actor reference condition has an unexpected second argument.");
        var subject = FalloutAiPackages.ConditionSubject(condition, caller);
        if (condition.Function == 1) return Distance(subject, condition.FormArgument1, player, unitsToMetres);
        var value = checked((int)condition.Argument1);
        return ActorValue(subject, value switch
        {
            4 => "aggression",
            16 => "health",
            >= 62 and <= 71 => FalloutActorValue.UserSlot(value),
            _ => throw new NotSupportedException($"AI actor value slot {value} has no shared source owner."),
        });
    }
}
