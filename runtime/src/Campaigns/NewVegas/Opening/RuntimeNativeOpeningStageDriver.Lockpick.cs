using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    internal bool IgnoreBrokenLock() => FalloutPlayerPerkNumericEffects.Apply(32, 0,
        _playerSkills.PerkEntries, PlayerExperienceCondition) != 0;
}
