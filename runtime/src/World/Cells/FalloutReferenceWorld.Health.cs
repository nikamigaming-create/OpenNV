using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorInjury(bool Dead, FalloutFormKey? Killer,
    IReadOnlyDictionary<byte, float> LimbDamage, bool DeathInventoryGranted = false);

internal sealed record FalloutActorHit(FalloutFormKey Reference, byte Part, float HealthBefore, float HealthAfter,
    float HealthDamage, float LimbDamage, bool Died, bool Dead, uint ImpactMaterial);

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutActorHealthSource> _healthSources = [];
    private readonly Dictionary<FalloutFormKey, FalloutBodyPartData> _bodyParts = [];

    internal FalloutActorHealthSource HealthSource(FalloutFormKey reference)
    {
        var actor = Actor(reference);
        if (!_healthSources.TryGetValue(actor.Base, out var source))
            _healthSources.Add(actor.Base, source = FalloutActorHealthSource.Read(records, actor.Base));
        return source;
    }

    internal FalloutBodyPartData BodyParts(FalloutFormKey reference)
    {
        var form = HealthSource(reference).BodyParts;
        if (!_bodyParts.TryGetValue(form, out var data))
            _bodyParts.Add(form, data = FalloutBodyPartData.Read(records.GetEffective(form)));
        return data;
    }

    internal FalloutActorValue Health(FalloutFormKey reference)
    {
        var actor = Actor(reference);
        if (!actor.ActorValues.TryGetValue("health", out var health))
        {
            health = new(HealthSource(reference).Health);
            actor.ActorValues.Add("health", health);
            actor.Injury ??= new(health.Base == 0, null, new Dictionary<byte, float>());
        }
        return health;
    }

    internal bool IsDead(FalloutFormKey reference) => Actor(reference).Injury?.Dead == true;

    // The hit resolver supplies damage after weapon/armor rules. Reference state
    // owns modifier pools and the death transition, never the rendered actor.
    internal FalloutActorHit DamageActor(FalloutFormKey reference, FalloutFormKey attacker, byte partType,
        float damage, float limbMultiplier, int level, FalloutGlobalState? globals = null)
    {
        if (!float.IsFinite(damage) || damage < 0 || !float.IsFinite(limbMultiplier) || limbMultiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(damage));
        var source = HealthSource(reference);
        var part = BodyParts(reference).Parts.SingleOrDefault(value => value.Type == partType) ??
            throw new InvalidDataException("Hit body part is absent from the actor's source table.");
        if (source.Essential) throw new NotSupportedException("Essential actor knockdown/recovery is not bound.");
        var actor = Actor(reference);
        var health = Health(reference);
        var injury = actor.Injury!;
        var healthDamage = source.Invulnerable || injury.Dead ? 0 : damage * part.DamageMultiplier;
        var limbDamage = source.Invulnerable ? 0 : damage * limbMultiplier;
        var changed = health with { Damage = health.Damage - healthDamage };
        var limbs = new Dictionary<byte, float>(injury.LimbDamage);
        limbs[partType] = limbs.GetValueOrDefault(partType) + limbDamage;
        if (!changed.IsFinite || !float.IsFinite(limbs[partType])) throw new InvalidDataException("Actor damage exceeds finite storage.");
        var died = !injury.Dead && changed.Current <= 0;
        // Expand the source death list once, before publishing the transition.
        // Inventory.Add is atomic and retains its own leveled-list random stream.
        if (died && !injury.DeathInventoryGranted && source.DeathItem is { } item)
            Inventory(reference, level, globals).Contents.Add(records, item, 1, level, true, globals);
        actor.ActorValues["health"] = changed;
        actor.Injury = new(injury.Dead || died, died ? attacker : injury.Killer, limbs, injury.DeathInventoryGranted || died);
        return new(reference, partType, health.Current, changed.Current, healthDamage, limbDamage, died, actor.Injury.Dead, source.ImpactMaterial);
    }

    private void RestoreInjury(FalloutReferenceInstance actor, FalloutActorInjury injury)
    {
        if (injury.LimbDamage is null || injury.LimbDamage.Any(pair => pair.Key > 14 || !float.IsFinite(pair.Value) || pair.Value < 0) ||
            !injury.Dead && (injury.Killer is not null || injury.DeathInventoryGranted) ||
            !actor.ActorValues.TryGetValue("health", out var health) || !health.IsFinite || injury.Dead != (health.Current <= 0))
            throw new InvalidDataException("Saved actor injury is inconsistent with its health.");
        var body = BodyParts(actor.Reference);
        if (injury.LimbDamage.Keys.Any(key => !body.Parts.Any(part => part.Type == key)))
            throw new InvalidDataException("Saved limb is absent from the winning body-part source.");
        actor.Injury = injury with { LimbDamage = new Dictionary<byte, float>(injury.LimbDamage) };
    }
}
