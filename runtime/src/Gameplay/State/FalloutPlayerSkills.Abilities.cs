using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerSkills
{
    private IFalloutAbilityScriptLifetime? _scriptAbilities;
    internal void BindAbilityScripts(IFalloutAbilityScriptLifetime owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_scriptAbilities is not null) throw new InvalidOperationException("Player ability script lifecycle is already bound.");
        _scriptAbilities = owner;
    }
    internal IReadOnlyList<FalloutFormKey> SelectedConstantEffects() => ConstantEffects().Distinct().ToArray();

    internal void RequireFatigueKnockoutEffects()
    {
        foreach (var source in SelectedConstantEffects())
            if (!_abilities.TryGetConstantModifiers(source, out var modifiers) || _abilities.Scripts(source).Count != 0 ||
                modifiers.Any(modifier => modifier.ActorValue == 47))
                throw new NotSupportedException($"Player knockout effect {source} requires its active paralysis/effect owner.");
    }
    internal float AbilityCondition(FalloutCondition condition) => Condition(condition);
    private IReadOnlyList<FalloutAbilityModifier> OwnedModifiers(FalloutFormKey form) => _abilities.Spell(form, _scriptAbilities);
    private void SynchronizeAbilityScripts() => _scriptAbilities?.Synchronize();
}
