using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutPlayerAdvancement? _advancement;
    private CanvasLayer? _levelUpLayer;
    private NativeOwnedLevelUpMenu? _levelUpMenu;
    private Action? _releaseLevelUpInput;
    private IReadOnlyList<FalloutLevelUpSkillPresentation> _levelUpSkills = [];
    private IReadOnlyList<FalloutLevelUpPerkSource> _levelUpPerks = [];
    internal string? PlayerAdvancementSaveBlocker => _advancement?.SaveBlocker;

    // The allocation rules and healing policy are supplied explicitly. The
    // recovered menu is not authority for either game rule.
    internal void ConfigurePlayerAdvancement(FalloutPlayerAdvancementSource source,
        Func<int, int, int> skillPointBudget,
        Func<GameplayVitals, GameplayVitals, GameplayVitals> applyLevelEffects,
        FalloutPlayerAdvancementSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(skillPointBudget);
        ArgumentNullException.ThrowIfNull(applyLevelEffects);
        if (_advancement is not null) throw new InvalidOperationException("Player advancement is already configured.");
        source.Validate();
        var actor = _playerActorValues.Source;
        if (source.Player != actor.Player || source.PlayerSha256 != actor.PlayerSha256 ||
            source.StatsOwner != actor.StatsOwner || source.StatsSha256 != actor.StatsSha256)
            throw new InvalidDataException("Player advancement differs from the active player source.");
        var rules = FalloutLevelUpRules.Read(_pluginStack, source.SkillRate);
        _advancement = new(source, new(() => _vitals.State.Level, () => _vitals.State.ExperiencePoints,
            _vitals.ExperienceThreshold, level => _vitals.AdvanceLevel(level, applyLevelEffects),
            PermanentLevelUpIntelligence,
            () => FalloutLevelUpRules.Read(_pluginStack, source.SkillRate), ReadLevelUpAdmission,
            level => BindLevelUpMenu(level, rules), PresentLevelUpMenu, CloseLevelUpMenu, skillPointBudget), restore);
        _experience.ExperienceChanged += _advancement.EarnedExperience;
        _vitals.BindLevelUpOwner(_advancement.RequestEligibility);
    }

    private int PermanentLevelUpIntelligence()
    {
        var value = _playerActorValues.ReadPermanent(9);
        if (!float.IsFinite(value) || value != MathF.Truncate(value) || value < int.MinValue || (double)value > int.MaxValue)
            throw new NotSupportedException("Fractional or exceptional level-up Intelligence requires its conversion policy.");
        return checked((int)value);
    }

    private FalloutLevelUpAdmission ReadLevelUpAdmission() => new(
        !GetTree().Paused && _vitals.State.ExactHitPoints > 0 && !_moviePlaying &&
        !_player.FurnitureActive && !_player.ModalInput && _conversation?.Active != true && _speech?.Active != true &&
        _nameEntry is null && _raceSexEntry is null && _vigorEntry is null && _specialBookEntry is null &&
        _tagSkillEntry is null && _traitEntry is null && _recipeMenu is null && _barterMenu is null &&
        TerminalSaveBlocker is null && !_scripts.References!.PlayerMoves.Pending);

    private FalloutLevelUpMenuBinding BindLevelUpMenu(int level, FalloutLevelUpRules rules)
    {
        _levelUpSkills = _skillCatalog.Select(skill =>
        {
            var record = _pluginStack.GetEffective(_pluginStack.RuntimeFormKey(skill.RuntimeFormId));
            string? Text(string name)
            {
                var fields = record.ReadSubrecords().Where(field => field.Signature == name).ToArray();
                if (fields.Length > 1) throw new InvalidDataException("Level-up skill text is ambiguous.");
                return fields.Length == 0 ? null : FalloutDialogueTopic.Text(fields[0].Data.Span);
            }
            return new FalloutLevelUpSkillPresentation(FalloutPlayerSkills.SkillActorValue(
                FalloutPlayerSkills.SkillName(_pluginStack, skill)), skill, Text("DESC") ?? "", Text("ICON"));
        }).ToArray();
        if (!_levelUpSkills.Select(skill => skill.ActorValue).Order().SequenceEqual(_playerSkills.SkillOrder.Order()))
            throw new NotSupportedException("Level-up rows differ from the current skill catalog.");
        _levelUpPerks = _pluginStack.EffectiveRecords("PERK")
            .Select(record => FalloutLevelUpPerkSource.Read(_pluginStack, record.FormKey))
            .Where(perk => !perk.Trait && perk.Playable && !perk.Hidden)
            .OrderBy(perk => perk.Name ?? perk.EditorId, StringComparer.Ordinal).ToArray();
        var world = _scripts.References!;
        var player = _pluginStack.RuntimeFormKey(0x14);
        var abilities = new FalloutAbilityModifiers(_pluginStack);
        int Rank(FalloutFormKey perk) => world.AcquiredPerkRank(player, perk);
        FalloutLevelUpPerkSource Perk(FalloutFormKey form) => _levelUpPerks.Single(perk => perk.Form == form);
        void RequireEffects(FalloutFormKey form)
        {
            var effects = abilities.Perk(form);
            var spells = effects.Spells.Distinct().ToArray();
            FalloutPlayerPerkCapabilities.RequireFirstRank(Perk(form), Rank(form), effects.Entries,
                _playerSkills.PerkEntries, spells.SelectMany(abilities.ConstantModifiers).ToArray(),
                spells.SelectMany(abilities.Scripts).ToArray(), _playerSkills.SkillOrder,
                RequireNumericPerkCondition, RequireAbilityPerkCondition);
        }
        string? Unsupported(FalloutFormKey form)
        {
            if (Rank(form) > 0) return "Additional perk ranks require their rank-effect owner.";
            try
            {
                _ = Perk(form).Enabled(level, Rank(form), PlayerExperienceCondition);
                RequireEffects(form);
                return null;
            }
            catch (Exception error) when (error is NotSupportedException or InvalidDataException)
            { return error.Message; }
        }
        return new(_levelUpSkills.Select(skill => skill.ActorValue).ToArray(), _playerSkills.ReadSkillBase,
            _playerSkills.WriteSkillBase, _playerSkills.DisplayedSkill, _playerSkills.IsTagged,
            requested => _playerSkills.LevelUpSkillBudget(requested, rules.TaggedSkillMultiplier),
            _levelUpPerks.Select(perk => new FalloutLevelUpPerkChoice(perk.Form, perk.SourceSha256, perk.MaximumRank)).ToArray(),
            form => Unsupported(form) is null && Perk(form).Enabled(level, Rank(form), PlayerExperienceCondition), Rank,
            (form, rank) => world.AcquireFirstPerkRank(player, form, rank, () => RequireEffects(form)), Unsupported);
    }

    private void RequireNumericPerkCondition(FalloutCondition condition)
    {
        if (!FalloutInventoryConditions.TargetsPlayer(_pluginStack, condition))
            throw new NotSupportedException("Perk numeric condition requires a non-player subject owner.");
        RequirePlayerPerkConditionFunction(condition, allowLevel: true);
    }

    private void RequireAbilityPerkCondition(FalloutCondition condition)
    {
        if (condition.RunOn != 0)
            throw new NotSupportedException("Perk ability condition requires a non-subject run-on owner.");
        RequirePlayerPerkConditionFunction(condition, allowLevel: false);
    }

    private void RequirePlayerPerkConditionFunction(FalloutCondition condition, bool allowLevel)
    {
        // Capability checks only: do not read live values or execute conditions.
        if (allowLevel && condition.Function == 80) return;
        if (condition.Function == 74 && _globals is not null &&
            _pluginStack.GetEffective(condition.FormArgument1).Signature == "GLOB") return;
        if (condition.Function == 14 && condition.Argument1 <= int.MaxValue)
        {
            var value = (int)condition.Argument1;
            if (FalloutPlayerActorValues.HasPool(value) || _playerSkills.SkillOrder.Contains(value) || value is 13 or 20 or 46) return;
        }
        throw new NotSupportedException($"Perk condition {condition.Function} has no admitted player effect capability.");
    }

    private void AdvancePlayerAdvancement()
    {
        if (_advancement is null) return;
        if (_advancement.FinishMenu()) return;
        if (_advancement.Active)
        {
            if (_levelUpMenu is null) _advancement.ReopenMenu();
            return;
        }
        _advancement.TryOpen(_scripts.Session.InCharGen);
    }

    private void PresentLevelUpMenu(FalloutLevelUpMenuSession session)
    {
        if (_levelUpMenu is not null) throw new InvalidOperationException("A level-up menu is already active.");
        var layer = new CanvasLayer { Layer = 98, ProcessMode = ProcessModeEnum.Always };
        NativeOwnedLevelUpMenu? menu = null;
        try
        {
            menu = new NativeOwnedLevelUpMenu(_pluginStack, session, _levelUpSkills, _levelUpPerks,
                () => _advancement!.FinishMenu(), FailLevelUpMenu);
            var tree = GetTree();
            var paused = tree.Paused;
            _levelUpLayer = layer;
            _levelUpMenu = menu;
            var release = AcquireInteractionInput();
            _releaseLevelUpInput = () =>
            {
                try { tree.Paused = paused; }
                finally { release(); }
            };
            layer.TreeExiting += CloseLevelUpMenu;
            menu.TreeExiting += CloseLevelUpMenu;
            AddChild(layer);
            layer.AddChild(menu);
            if (menu.Error is { } error) throw new InvalidOperationException("Level-up presentation failed: " + error);
            tree.Paused = true;
        }
        catch
        {
            CloseLevelUpMenu();
            if (menu is not null && GodotObject.IsInstanceValid(menu) && menu.GetParent() is null) menu.Free();
            if (!layer.IsInsideTree()) layer.Free();
            throw;
        }
    }

    private void FailLevelUpMenu(Exception error)
    {
        try { RetainDriverFailure(error); }
        finally { CloseLevelUpMenu(); }
    }

    private void CloseLevelUpMenu()
    {
        if (_levelUpLayer is not { } layer) return;
        _levelUpLayer = null;
        _levelUpMenu = null;
        var release = _releaseLevelUpInput;
        _releaseLevelUpInput = null;
        try { release?.Invoke(); }
        finally { if (!layer.IsQueuedForDeletion()) layer.QueueFree(); }
    }
}
