using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private bool _nativeAdvancementUpdateActive;

    internal void ConfigureCurrentPlayerAdvancement() => ConfigureSourcePlayerAdvancement(
        ObserveNativeAdvancementActivity, AdmitPlayerLevelUpSkillBudget, AcquirePlayerPerkRank);

    private void AdvanceNativePlayerInCurrentFrame()
    {
        if (_nativeAdvancementUpdateActive)
            throw new InvalidOperationException("Player advancement cannot reenter its current frame owner.");
        _nativeAdvancementUpdateActive = true;
        try { UpdateNativePlayerAdvancement(); }
        finally { _nativeAdvancementUpdateActive = false; }
    }

    private FalloutAdvancementActivityObservation ObserveNativeAdvancementActivity(FalloutAdvancementActivityFact fact)
    {
        static FalloutAdvancementActivityObservation Observed(bool satisfied, string owner) => new(
            satisfied ? FalloutAdvancementActivityState.Satisfied : FalloutAdvancementActivityState.Held, owner);
        return fact switch
        {
            FalloutAdvancementActivityFact.AdmittedPlayerUpdate => Observed(
                _nativeAdvancementUpdateActive && IsInsideTree() && CanProcess(), "current-player-process-callback"),
            FalloutAdvancementActivityFact.CharacterGenerationEnded => Observed(
                !_scripts.Session.InCharGen, "shared-character-generation-state"),
            FalloutAdvancementActivityFact.NoActiveMenu => Observed(
                _scripts.Menus.GameMode && !ActiveMenus().Any(), "shared-source-menu-frame"),
            FalloutAdvancementActivityFact.PlayerAlive => Observed(
                _vitals.State.ExactHitPoints > 0, "authoritative-player-vitals"),
            FalloutAdvancementActivityFact.CombatEnded => Observed(
                !IsInCombat(_pluginStack.RuntimeFormKey(0x14)), "shared-player-combat-query"),
            FalloutAdvancementActivityFact.NotificationSequenceSettled => ObserveExperienceNotificationActivity(),
            FalloutAdvancementActivityFact.PlayerAwake or FalloutAdvancementActivityFact.PlayerUpright =>
                _player.ObservePhysicalActivity(fact),
            FalloutAdvancementActivityFact.OriginalUiFrameGate => ObserveOriginalExperienceFrameActivity(),
            FalloutAdvancementActivityFact.OriginalPlayerFrameGate => ObserveInterfaceActivationFrameActivity(),
            FalloutAdvancementActivityFact.SelectedDependencyAdvancementEffects => new(FalloutAdvancementActivityState.Unowned,
                "selected-native-dependency-advancement-effects"),
            _ => throw new NotSupportedException("Player advancement requested an undeclared activity fact."),
        };
    }
}
