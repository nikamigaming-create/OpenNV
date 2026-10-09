using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutPlayerProgress _playerProgress = null!;
    internal object PlayerProgressState => RequirePlayerProgress().State;
    internal string? PlayerProgressSaveBlocker => RequirePlayerProgress().SaveBlocker;

    private FalloutPlayerProgress RequirePlayerProgress() => _playerProgress ??
        throw new InvalidOperationException("Persistent player progress is not initialized.");

    // Call after the actual actor-values/skills/vitals/XP owners exist, before
    // installing result callbacks or ticking a source invocation.
    private void InitializePlayerProgress(FalloutPlayerProgressSnapshot? restore)
    {
        if (_playerProgress is not null) throw new InvalidOperationException("Player progression is already initialized.");
        _experience.BindPerkConditions(PlayerProgressCondition);
        _playerProgress = FalloutPlayerProgress.Prepare(_pluginStack, _vitals, _playerActorValues, _playerSkills, _experience, restore);
    }

    private float PlayerProgressCondition(FalloutCondition condition)
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

    private void RewardPlayerExperience(double operand) => RequirePlayerProgress().Reward(operand);
    private FalloutPlayerProgressSnapshot CapturePlayerProgress() => RequirePlayerProgress().Capture();
    private void ChangePlayerActorValue(string name, string operation, double value)
    {
        if (_playerSkills.IsSkill(name)) _playerSkills.ChangeSkill(name, operation, value);
        else _playerActorValues.Change(name, operation, value);
    }

    // Native/dependency code binds real source-rate, activity and menu owners.
    // Neither ordinary script completion nor a save request supplies admission.
    internal void BindPlayerAdvancement(FalloutPlayerAdvancementSource source, FalloutPlayerAdvancementBinding binding) =>
        RequirePlayerProgress().BindAdvancement(source, binding);
    internal bool UpdatePlayerAdvancement() => RequirePlayerProgress().TryOpen(_scripts.Session.InCharGen);
    internal bool CompletePlayerLevelUpMenu()
    {
        var progress = RequirePlayerProgress();
        if (progress.Menu is not { Completed: true } menu) return false;
        if (!progress.FinishMenu()) return false;
        ExperienceNotifications.CompletedLevelMenuSubmitted(menu);
        return true;
    }
    internal void ConsumePlayerLevel(int level) => _vitals.AdvancePlayerLevel(level);
}
