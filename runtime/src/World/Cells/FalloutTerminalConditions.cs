using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed class FalloutTerminalConditions(FalloutPluginStack records, FalloutQuestState quests,
    FalloutReferenceWorld world, Func<FalloutFormKey, int> getResidentOpenState,
    Func<FalloutFormKey, FalloutCondition, float>? evaluateOther = null)
{
    internal float Evaluate(FalloutFormKey terminalReference, FalloutCondition condition)
    {
        if (condition.Owner.Signature != "TERM") throw new InvalidDataException("Terminal condition is not declared by TERM.");
        if (records.GetEffective(terminalReference).Signature != "REFR" ||
            records.GetEffective(world.Get(terminalReference).Base).Signature != "TERM")
            throw new InvalidDataException("Terminal condition subject has no actual placed TERM owner.");
        if (condition.RunOn is not (0 or 2))
            throw new NotSupportedException($"Terminal condition run-on {condition.RunOn} has no context owner.");
        // These reads query the explicit QUST argument. They do not change
        // owner when the calling reference or run-on reference changes.
        if (condition.Function is 56 or 58 or 59 or 79 or 420 or 421 or 546) return quests.Evaluate(condition);
        if (condition.Function == 53)
            return (float)world.ReadVariable(quests, condition.FormArgument1, condition.Argument2);
        var subject = condition.RunOn == 0 ? terminalReference :
            condition.Owner.Plugin.AdjustOptionalFormId(condition.Reference)
                ?? throw new InvalidDataException("Terminal condition has no explicit run-on reference.");
        if (records.RuntimeFormId(subject) != 0x14 && records.GetEffective(subject).Signature is not ("REFR" or "ACHR" or "ACRE"))
            throw new InvalidDataException("Terminal condition run-on identity is not a placed reference.");
        if (FalloutPlatformConditions.Evaluate(condition) is { } platform) return platform;
        if (condition.Function == 157)
        {
            var state = world.IsResident(subject) ? getResidentOpenState(subject) :
                world.Retained(subject).DoorMotion?.OpenState
                    ?? throw new NotSupportedException("Terminal GetOpenState has no retained source animation owner.");
            return state is >= 0 and <= 4 ? state :
                throw new InvalidDataException("Terminal open-state owner returned an invalid phase.");
        }
        // A native adapter receives the resolved actual subject. It may
        // delegate player predicates only when that subject really is player.
        return (evaluateOther ?? throw new NotSupportedException(
            $"Terminal condition {condition.Owner.FormKey}/{condition.Function} has no typed runtime owner."))(subject, condition);
    }
}
