using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    internal FalloutFormKey TouchEffectTarget => _state.Reference;
    internal Godot.Node3D TouchEffectBody => _actor;
    internal void ReceiveHostileTouch(FalloutFormKey attacker) => Provoke(attacker);

    private string? _weaponTouchError;

    internal Action PrepareTouchHealthDamage(float amount, FalloutFormKey attacker, int level, FalloutGlobalState globals)
    {
        if (!float.IsFinite(amount) || amount < 0) throw new InvalidDataException("Touch health magnitude is invalid.");
        var torso = _world.BodyParts(_state.Reference).Parts.Single(part => part.Type == 0);
        _ = _world.Health(_state.Reference);
        var used = false;
        return () =>
        {
            if (used) throw new InvalidOperationException("Touch health delivery was already consumed.");
            used = true;
            if (Dead || !EffectiveEnabled || amount == 0) return;
            if (amount >= _world.Health(_state.Reference).Current) PrepareDeath();
            var hit = _world.DamageActor(_state.Reference, attacker, torso.Type, amount, 0, level, globals, healthOnly: true);
            if (hit.Died) BeginDeath();
            else if (hit.HealthDamage > 0)
            {
                Provoke(attacker);
                RequestHitReaction(hit, -1);
            }
        };
    }

    private void ReceiveWeaponTouchEffects(FalloutFormKey attacker, FalloutFormKey weapon, int level, FalloutGlobalState globals)
    {
        try
        {
            if (Dead) return;
            var player = _context?.Player() ?? throw new NotSupportedException("Weapon touch has no shared condition host.");
            var selected = player.SelectWeaponTouchEffects(_state.Reference, attacker, weapon);
            if (selected is null || selected.Effects.Count == 0) { _weaponTouchError = null; return; }
            var amount = FalloutWeaponTouchEffects.HealthDamage(selected);
            var health = PrepareTouchHealthDamage(amount, attacker, level, globals);
            var values = selected with { Effects = selected.Effects.Where(effect => effect.ActorValue != 16).ToArray() };
            var apply = values.Effects.Count == 0 ? null : _world.PrepareActorTouchEnchantment(_state.Reference, values,
                _ => throw new InvalidOperationException("Selected weapon effect retained an unevaluated condition."));
            health();
            apply?.Invoke();
            if (!Dead && selected.Effects.Any(effect => (effect.Flags & 1) != 0)) ReceiveHostileTouch(attacker);
            _weaponTouchError = null;
        }
        catch (Exception error)
        {
            // The ordinary weapon hit has already been consumed and recorded.
            // Retain a distinct effect error; never retry the damage transaction.
            _weaponTouchError = error.Message;
            Godot.GD.PushError($"OPENNV_WEAPON_TOUCH_UNBOUND target={_state.Reference} caster={attacker} weapon={weapon} {error.Message}");
        }
    }
}
