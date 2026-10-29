using Function = OpenNV.Runtime.Content.Definitions.FalloutConditionFunction;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutConditionRuntime? _sharedConditions;
    private FalloutConditionRuntime SharedConditions => _sharedConditions ??= new(_pluginStack, _quests, _scripts.References)
    {
        Globals = _globals,
        Hour = () => (_gameTime ?? throw new NotSupportedException("Condition has no simulation clock.")).Hour,
        RandomPercent = () => _scripts.ScriptValues.RandomPercent(),
        PlayerFemale = () => _character.Female,
        Race = subject => _scripts.References!.ActorRace(subject),
        ActorValue = DialogueActorValue,
        ItemCount = (subject, item) => InventoryCommands.ItemCount(subject, item),
        Equipped = (subject, item) => InventoryCommands.IsEquipped(subject, item),
        HasPerk = (subject, perk) => subject == _pluginStack.RuntimeFormKey(0x14) ? _playerSkills.HasPerk(perk) :
            _scripts.References!.AcquiredPerks(subject).Contains(perk),
        IsDead = DialogueActorDead,
        HealthPercentage = ActorHealthPercentage,
        Sitting = ActorSitting,
        CurrentPackage = CurrentActorPackage,
        Vampire = () => _scripts.ActorQueries.GetVampire(),
        Distance = ReferenceDistance,
        SameCell = IsInSameCell,
        InZone = ReferenceInZone,
        PlayerPlacement = () =>
        {
            var position = _player.GlobalPosition / _player.UnitsToMeters;
            return new FalloutReferencePlacement(_activeCell, [position.X, -position.Z, position.Y], [0, 0, 0]);
        },
        UnitsToMetres = () => _player.UnitsToMeters,
        Native = (subject, condition) => subject == _pluginStack.RuntimeFormKey(0x14) ? EvaluatePlayerNativeCondition(condition) :
            condition.Kind == Function.GetOpenState ? ReferencePresentation().GetOpenState(subject) :
            _scripts.References!.QueryNativeCondition(subject, condition),
    };

    private float EvaluatePlayerNativeCondition(FalloutConditionQuery condition) => condition.Kind switch
    {
        Function.GetActorValue => DialogueActorValue(_pluginStack.RuntimeFormKey(0x14), checked((int)condition.Argument1)),
        Function.GetDead => Vitals.HitPoints == 0 ? 1 : 0,
        Function.GetInCell => _scripts.References!.IsInCell(_pluginStack.RuntimeFormKey(0x14), condition.FormArgument1, _activeCell) ? 1 : 0,
        Function.GetIsSex or Function.GetPCIsSex when condition.Argument1 <= 1 && condition.Argument2 == 0 => _character.Female == (condition.Argument1 == 1) ? 1 : 0,
        Function.GetLevel => SourcePlayerLevel,
        Function.GetSitting => _player.SittingState,
        Function.GetEquipped => InventoryCommands.IsEquipped(_pluginStack.RuntimeFormKey(0x14), condition.FormArgument1) ? 1 : 0,
        Function.IsSneaking => _player.Activity.Sneaking ? 1 : 0,
        Function.IsRunning => _player.Activity.Running ? 1 : 0,
        Function.IsInCombat => IsInCombat(_pluginStack.RuntimeFormKey(0x14)) ? 1 : 0,
        Function.GetHealthPercentage => ActorHealthPercentage(_pluginStack.RuntimeFormKey(0x14)),
        Function.HasPerk => _playerSkills.HasPerk(condition.FormArgument1) ? 1 : 0,
        Function.IsHardcore => _scripts.Session.Hardcore ? 1 : 0,
        Function.PlayerInRegion => _scripts.References!.IsInRegion(_pluginStack.RuntimeFormKey(0x14), condition.FormArgument1,
            SharedConditions.PlayerPlacement?.Invoke(), _player.UnitsToMeters) ? 1 : 0,
        _ => throw new NotSupportedException($"Player condition {condition.Function} has no authoritative native state owner."),
    };
}
