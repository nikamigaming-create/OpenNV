using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutPlayerAbilityScripts _playerAbilities = null!;

    private void BindPlayerAbilityState(FalloutNativeCampaignRestore? restore, FalloutGameTime? gameTime)
    {
        if (restore is not null && (restore.State.PlayerSkillValues is null || restore.State.PlayerAbilityScripts is null))
            throw new InvalidDataException("Current gameplay restore requires complete player skill/effect authority.");
        _playerAbilities = new(_pluginStack, _playerSkills.SelectedConstantEffects, _playerSkills.AbilityCondition,
            restore?.State.PlayerAbilityScripts);
        var executor = new FalloutReferenceScripts(_pluginStack, _scripts.References!, _quests,
            new(AbilityCurrentFurniture, ApplyReferenceEffect, _scripts.MessageResults.Take,
                actor => (_speech ?? throw new NotSupportedException("Ability speech query requires initialized native speech.")).IsTalking(actor),
                ActorValue, IsPlayerTagSkill, _globals, ApplyNativeSourceCommand, IsInCombat, IsInSameCell, _scripts.Events,
                ReferenceDistance, IsInInterior,
                (reference, group, initialization) => ReferencePresentation().PlayGroup(reference, group, initialization),
                (reference, group) => ReferencePresentation().IsAnimPlaying(reference, group),
                () => (_vitals ?? throw new NotSupportedException("Ability level query requires initialized player vitals.")).State.Level,
                () => _scripts.Session.LocationSpecificLoadScreensOnly, () => _scripts.Session.InCharGen,
                reference => ReferencePresentation().GetOpenState(reference),
                ReadActorValue: ReadActorValue, ChangeActorValue: ChangeActorValue, Inventory: InventoryCommands,
                Challenges: _scripts.Challenges, HeadingAngle: ReferenceHeadingAngle,
                ResetPlayerHealth: () => (_vitals ?? throw new NotSupportedException("Ability ResetHealth requires initialized player vitals.")).ResetHealth(),
                CurrentPackage: CurrentActorPackage, Sitting: ActorSitting, TagSkills: _tagSkills, IsInCell: IsInCell,
                IsHardcore: () => _scripts.Session.Hardcore,
                RewardXp: RewardPlayerExperience,
                GameTime: gameTime, Statistics: PlayerStatistics));
        _playerAbilities.BindExecutor(executor.ExecuteActiveEffect);
        _playerSkills.BindAbilityScripts(_playerAbilities);
        _playerActorValues.BindAbilityLifecycle(_playerAbilities.Synchronize);
        if (restore?.State.PlayerSkillValues is { } skills) _playerSkills.RestoreValues(skills);
    }

    private bool AbilityCurrentFurniture(FalloutFormKey actor, FalloutFormKey furniture)
    {
        if (_pluginStack.RuntimeFormId(actor) == 0x14) return _player.CurrentFurniture == furniture;
        return GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
            .Single(npc => npc.Appearance.Reference == actor).CurrentFurniture == furniture;
    }
}
