using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private float PlayerExperienceCondition(FalloutCondition condition)
    {
        if (condition.Function == 80)
        {
            if (!FalloutInventoryConditions.TargetsPlayer(_pluginStack, condition))
                throw new NotSupportedException("XP perk GetLevel condition has no original actor subject.");
            return SourcePlayerLevel;
        }
        if (condition.Function == 14 && FalloutInventoryConditions.TargetsPlayer(_pluginStack, condition))
        {
            var value = checked((int)condition.Argument1);
            return value is >= 5 and <= 11 ? _playerActorValues.ReadCurrent(value) :
                _playerSkills.SkillOrder.Contains(value) ? _playerSkills.ReadSkill(value, FalloutActorValueRead.Current) :
                PlayerCombatValue(value);
        }
        return EvaluateMessageCondition(condition);
    }
}
