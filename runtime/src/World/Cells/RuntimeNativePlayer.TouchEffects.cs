using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private Func<FalloutFormKey, FalloutFormKey, FalloutIngestible, Action>? _prepareTouchEnchantment;
    private Func<FalloutFormKey, FalloutFormKey, FalloutCondition, float>? _weaponTouchCondition;
    private Action<float>? _weaponTouchHealthDamage;
    private readonly Dictionary<FalloutFormKey, FalloutIngestible?> _weaponTouchSources = [];

    internal void ConfigureWeaponTouchEffects(Func<FalloutFormKey, FalloutFormKey, FalloutCondition, float> condition,
        Action<float> healthDamage)
    {
        if (_weaponTouchCondition is not null) throw new InvalidOperationException("Weapon touch delivery is already bound.");
        _weaponTouchCondition = condition;
        _weaponTouchHealthDamage = healthDamage;
    }

    internal FalloutIngestible? SelectWeaponTouchEffects(FalloutFormKey target, FalloutFormKey caster, FalloutFormKey weapon)
    {
        if (!_weaponTouchSources.TryGetValue(weapon, out var source))
            _weaponTouchSources.Add(weapon, source = FalloutWeaponTouchEffects.Read(_presentationRecords!, weapon));
        return source is null ? null : SelectDeliveredTouchEffects(target, caster, source);
    }

    internal FalloutIngestible SelectDeliveredTouchEffects(FalloutFormKey target, FalloutFormKey caster,
        FalloutIngestible source, bool areaDelivered = false) => FalloutWeaponTouchEffects.Select(source, condition =>
            (_weaponTouchCondition ?? throw new NotSupportedException("Weapon touch conditions have no authoritative host."))(target, caster, condition), areaDelivered);

    internal Action PrepareTouchHealthDamage(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0) throw new InvalidDataException("Touch health magnitude is invalid.");
        var damage = _weaponTouchHealthDamage ?? throw new NotSupportedException("Touch health has no damage owner.");
        var used = false;
        return () =>
        {
            if (used) throw new InvalidOperationException("Touch health delivery was already consumed.");
            used = true;
            if (IsDefeated?.Invoke() != true && amount > 0) damage(amount);
        };
    }

    internal void ReceiveWeaponTouchEffects(FalloutFormKey caster, FalloutFormKey weapon)
    {
        try
        {
            if (IsDefeated?.Invoke() == true) return;
            var target = _presentationRecords!.RuntimeFormKey(0x14);
            if (SelectWeaponTouchEffects(target, caster, weapon) is not { } selected || selected.Effects.Count == 0)
            { _shotEffectErrors.Remove("received-weapon-touch"); return; }
            var amount = FalloutWeaponTouchEffects.HealthDamage(selected);
            var health = PrepareTouchHealthDamage(amount);
            var values = selected with { Effects = selected.Effects.Where(effect => effect.ActorValue != 16).ToArray() };
            var apply = values.Effects.Count == 0 ? null : PrepareTouchEnchantment(target, caster, values);
            health();
            if (IsDefeated?.Invoke() != true) apply?.Invoke();
            _shotEffectErrors.Remove("received-weapon-touch");
        }
        catch (Exception error)
        {
            _shotEffectErrors["received-weapon-touch"] = error.Message;
            Godot.GD.PushError($"OPENNV_WEAPON_TOUCH_UNBOUND target=Player caster={caster} weapon={weapon} {error.Message}");
        }
    }

    internal void ConfigureTouchEnchantments(Func<FalloutFormKey, FalloutFormKey, FalloutIngestible, Action> prepare)
    {
        if (_prepareTouchEnchantment is not null) throw new InvalidOperationException("Touch enchantment delivery is already bound.");
        _prepareTouchEnchantment = prepare;
    }

    internal Action PrepareTouchEnchantment(FalloutFormKey target, FalloutFormKey caster, FalloutIngestible source) =>
        (_prepareTouchEnchantment ?? throw new NotSupportedException("Touch enchantments have no authoritative value/condition host."))(target, caster, source);
}
