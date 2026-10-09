using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private int AdmitPlayerLevelUpSkillBudget(int requested)
    {
        if (requested < 0) throw new InvalidDataException("Source level-up point request is negative.");
        var adjusted = FalloutPlayerPerkNumericEffects.Apply(22, requested,
            _playerSkills.PerkEntries, PlayerProgressCondition);
        var points = MathF.Truncate(adjusted);
        if (points < 0 || (double)points > int.MaxValue)
            throw new NotSupportedException("Source level-up skill points exceed admitted signed menu storage.");
        var taggedMultiplier = unchecked((int)FalloutGameSettingIntegers.Read(_pluginStack, "iSkillPointsTagSkillMult"));
        if (taggedMultiplier <= 0) throw new InvalidDataException("Source tagged skill point multiplier is not positive.");
        long capacity = 0;
        foreach (var skill in _playerSkills.SkillOrder)
        {
            var displayed = checked((int)_playerSkills.Value(skill));
            if (displayed is < 0 or > 100)
                throw new InvalidDataException("Level-up skill capacity differs from the current displayed skill owner.");
            var step = _playerSkills.IsTaggedSkill(skill) ? taggedMultiplier : 1;
            // Count the actual remaining positive arrow operations. The source
            // arrow can publish a tagged step beyond the displayed cap.
            capacity = checked(capacity + (100L - displayed + step - 1) / step);
        }
        return checked((int)Math.Min((long)points, capacity));
    }
}
