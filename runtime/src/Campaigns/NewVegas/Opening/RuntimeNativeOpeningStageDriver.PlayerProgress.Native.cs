using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private RuntimeNativeLevelUpEntry? _levelUpEntry;
    private FalloutLevelUpMenuCatalogue? _levelUpCatalogue;
    private bool _nativeAdvancementBound;
    internal object? PlayerLevelUpMenuState => _levelUpEntry?.State;
    internal uint? PlayerLevelUpMenuId => _levelUpEntry?.MenuId;

    private void RequireLevelUpMenuFree(string operation)
    {
        if (_levelUpEntry is not null)
            throw new InvalidOperationException($"{operation} cannot enter while the level-up menu owns player input.");
    }

    // The caller is the selected engine/dependency activity and perk-effect
    // owner. It supplies admitted source-rate/point/effect semantics, never a
    // default-ready flag or a rank dictionary masquerading as execution.
    internal void ConfigureNativePlayerAdvancement(FalloutPlayerAdvancementSource source,
        Func<FalloutLevelUpAdmission> activity, Func<int, int> admitSkillBudget,
        Action<FalloutFormKey, int> acquirePerkRank)
    {
        if (_nativeAdvancementBound) throw new InvalidOperationException("Native advancement is already bound.");
        source.Validate();
        var catalogue = new FalloutLevelUpMenuCatalogue(_pluginStack, _playerSkills.NativeSkillSlot);
        int Rank(FalloutFormKey perk) => PlayerPerkRank(perk);
        FalloutLevelUpAdmission Admission()
        {
            if (!IsInsideTree() || GetTree().Paused || BlockingExecutionFault is not null || _moviePlaying ||
                _player.FurnitureActive || _conversation?.Active == true || _speech?.Active == true ||
                _nameEntry is not null || _raceSexEntry is not null || _vigorEntry is not null || _specialBookEntry is not null ||
                _tagSkillEntry is not null || _traitEntry is not null || _recipeMenu is not null || _barterMenu is not null ||
                _terminalMenus.Values.Any(menu => menu.Active) || _levelUpEntry is not null) return new(false);
            return activity();
        }
        BindPlayerAdvancement(source, new(
            () => SourcePlayerLevel, () => _vitals.State.ExperiencePoints, _vitals.ExperienceThreshold, ConsumePlayerLevel,
            () => throw new InvalidOperationException("Permanent Intelligence is supplied by the persistent player owner."),
            () => FalloutLevelUpRules.Read(_pluginStack, source.Runtime), Admission,
            level => new(catalogue.Skills.Select(skill => skill.ActorValue).ToArray(),
                _playerSkills.ReadUnmodifiedSkill, _playerSkills.WriteUnmodifiedSkill,
                skill => checked((int)_playerSkills.Value(skill)), _playerSkills.IsTaggedSkill, admitSkillBudget,
                catalogue.Choices, perk => catalogue.Eligible(perk, level, Rank, PlayerProgressCondition), Rank, acquirePerkRank),
            PresentNativeLevelUp));
        _levelUpCatalogue = catalogue; _nativeAdvancementBound = true;
    }
    private void PresentNativeLevelUp(FalloutLevelUpMenuSession session)
    {
        if (_levelUpEntry is not null || !RequirePlayerProgress().OwnsMenu(session))
            throw new InvalidOperationException("Native level-up publication does not own the current player request.");
        var catalogue = _levelUpCatalogue ?? throw new InvalidOperationException("Native level-up has no selected source catalog.");
        var source = new NativeLevelUpMenuSource(_pluginStack, catalogue, session, _scripts.Ui);
        _levelUpEntry = RuntimeNativeLevelUpEntry.Attach(this, source, session,
            () => RequirePlayerProgress().OwnsMenu(session), _player.AcquireModalInput,
            () =>
            {
                if (!RequirePlayerProgress().OwnsMenu(session) || !session.Completed || !CompletePlayerLevelUpMenu())
                    throw new InvalidOperationException("Native level-up submission differs from its completed source request.");
            },
            error => RetainNativeLevelUpFailure(session, error), completed =>
            {
                _levelUpEntry = null;
                if (!completed && IsInsideTree() && RequirePlayerProgress().OwnsMenu(session))
                    RetainNativeLevelUpFailure(session, new InvalidOperationException("Native level-up left its living world before source completion."));
            });
    }
    private void RetainNativeLevelUpFailure(FalloutLevelUpMenuSession session, Exception error)
    {
        if (RequirePlayerProgress().OwnsNativeMenuReceipt(session)) RequirePlayerProgress().RetainMenuFailure(session, error);
        ExecutionError = error.Message;
    }

    // Invoke at the shared admitted update after source execution. A cold menu
    // is republished from its current snapshot without consuming another level.
    internal bool UpdateNativePlayerAdvancement()
    {
        var progress = RequirePlayerProgress();
        if (_levelUpEntry is not null) return false;
        if (progress.Menu is { Completed: true }) return CompletePlayerLevelUpMenu();
        if (progress.Menu is not null) return progress.PublishPendingMenu();
        return UpdatePlayerAdvancement();
    }
}
