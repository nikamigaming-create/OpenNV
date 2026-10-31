using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Serialization;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Gameplay.State;

// The retained controller contribution is a velocity rate and a remaining
// lifetime. Collision constrains the resulting motion; hits never set position.
internal sealed record FalloutHitKnockback(float RateX, float RateY, float RateZ, float RemainingSeconds)
{
    [JsonIgnore] internal Vector3 Rate => new(RateX, RateY, RateZ);
    [JsonIgnore] internal Vector3 Velocity => Rate * RemainingSeconds;

    internal static void Validate(FalloutHitKnockback? state)
    {
        if (state is not null && (!float.IsFinite(state.RateX) || !float.IsFinite(state.RateY) ||
            !float.IsFinite(state.RateZ) || !float.IsFinite(state.RemainingSeconds) || state.RemainingSeconds <= 0 ||
            !float.IsFinite(state.Rate.LengthSquared()) || state.Rate.LengthSquared() <= 0))
            throw new InvalidDataException("Hit knockback has invalid retained motion.");
    }

    internal static bool NotPushable(FalloutPluginRecord actor)
    {
        var acbs = actor.ReadSubrecords().Single(field => field.Signature == "ACBS").Data.Span;
        if (acbs.Length != 24) throw new InvalidDataException("Actor knockback has invalid ACBS data.");
        return ((FalloutActorBaseFlags)BinaryPrimitives.ReadUInt32LittleEndian(acbs) & FalloutActorBaseFlags.NotPushable) != 0;
    }

    internal static FalloutHitKnockback? Request(FalloutPluginStack records, float agility, float weaponBaseDamage,
        Vector3 direction, float unitsToMeters, FalloutHitKnockback? current)
    {
        // Native hit-payload weapon damage already includes skill/condition
        // and precedes armor mitigation; it is not raw WEAP DATA damage.
        Validate(current);
        if (!float.IsFinite(agility) || !float.IsFinite(weaponBaseDamage) || weaponBaseDamage < 0 ||
            weaponBaseDamage >= int.MaxValue || !float.IsFinite(direction.LengthSquared()) ||
            !float.IsFinite(unitsToMeters) || unitsToMeters <= 0)
            throw new InvalidDataException("Hit knockback inputs are not finite and source-valid.");
        float Setting(string name)
        {
            var value = FalloutGameSettingFloats.Read(records, name);
            return float.IsFinite(value) ? value : throw new InvalidDataException($"Knockback setting {name} is not finite.");
        }
        var duration = Setting("fKnockbackTime");
        var maximum = Setting("fKnockbackForceMax");
        if (duration <= 0 || maximum < 0) throw new NotSupportedException("Nonpositive knockback time or negative force limit is unbound.");
        var force = (Setting("fKnockbackAgilBase") + 10 * agility * Setting("fKnockbackAgilMult")) *
            (Setting("fKnockbackDamageBase") + (int)weaponBaseDamage * Setting("fKnockbackDamageMult"));
        force = Math.Min(force, maximum);
        if (!float.IsFinite(force)) throw new InvalidDataException("Hit knockback force is not finite.");
        if (direction.LengthSquared() == 0) return current;
        var rate = Vector3.Normalize(direction) * (force * unitsToMeters / duration);
        if (rate.LengthSquared() <= (current?.Rate.LengthSquared() ?? 0)) return current;
        var result = new FalloutHitKnockback(rate.X, rate.Y, rate.Z, duration);
        Validate(result);
        return result;
    }

    internal FalloutHitKnockback? Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new InvalidDataException("Knockback elapsed time is invalid.");
        var remaining = RemainingSeconds - (float)seconds;
        return remaining > 0 ? this with { RemainingSeconds = remaining } : null;
    }
}
