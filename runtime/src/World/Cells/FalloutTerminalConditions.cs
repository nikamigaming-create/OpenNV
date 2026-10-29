using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed class FalloutTerminalConditions(FalloutPluginStack records, FalloutQuestState quests,
    FalloutReferenceWorld world, Func<FalloutFormKey, int> getResidentOpenState,
    Func<FalloutFormKey, FalloutCondition, float>? evaluateOther = null, FalloutConditionRuntime? runtime = null)
{
    private readonly FalloutConditionRuntime _runtime = runtime ?? new(records, quests, world)
    {
        OpenState = getResidentOpenState,
        Native = (subject, condition) => evaluateOther is { } query ? query(subject, condition.Declaration) :
            world.QueryNativeCondition(subject, condition),
    };

    internal float Evaluate(FalloutFormKey terminalReference, FalloutCondition condition)
    {
        if (condition.Owner.Signature != "TERM") throw new InvalidDataException("Terminal condition is not declared by TERM.");
        if (records.GetEffective(terminalReference).Signature != "REFR" ||
            records.GetEffective(world.Get(terminalReference).Base).Signature != "TERM")
            throw new InvalidDataException("Terminal condition subject has no actual placed TERM owner.");
        if (condition.SubjectRunOn is not (0 or 2 or 4))
            throw new NotSupportedException($"Terminal condition run-on {condition.RunOn} has no context owner.");
        return _runtime.Evaluate(condition, new(terminalReference, LinkedReference: world.GetLinkedRef));
    }
}
