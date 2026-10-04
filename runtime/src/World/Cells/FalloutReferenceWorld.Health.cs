using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorInjury(bool Dead, FalloutFormKey? Killer,
    IReadOnlyDictionary<byte, float> LimbDamage, bool DeathInventoryGranted = false, IReadOnlyList<byte>? SeveredParts = null,
    bool DeathEventPending = false, double DeathEventElapsed = 0);

internal sealed record FalloutActorHit(FalloutFormKey Reference, byte Part, float HealthBefore, float HealthAfter,
    float HealthDamage, float LimbDamage, bool Died, bool Dead, uint ImpactMaterial);

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutActorHealthSource> _healthSources = [];
    private readonly Dictionary<FalloutFormKey, FalloutBodyPartData> _bodyParts = [];

    internal FalloutActorHealthSource HealthSource(FalloutFormKey reference)
    {
        var actor = Actor(reference);
        if (!_healthSources.TryGetValue(actor.Reference, out var source))
            _healthSources.Add(actor.Reference, source = FalloutActorHealthSource.Read(records, actor.Base, actor.Templates));
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

    internal int GetDeadCount(FalloutFormKey actorBase)
    {
        if (records.GetEffective(actorBase).Signature is not ("NPC_" or "CREA"))
            throw new InvalidDataException("GetDeadCount requires an actor base, not a placed reference.");
        return _instances.Values.Where(actor => actor.Base == actorBase).Aggregate(0,
            (count, actor) => checked(count + actor.DeathCount));
    }

    internal bool PlayerInCombat() => ResidentInstances.Any(actor =>
        actor.Engagement is { Action: not "idle" } engagement && engagement.Target == _enginePlayer &&
        IsEnabled(actor.Reference) && actor.Injury?.Dead != true);

    internal bool IsInCombat(FalloutFormKey reference, Func<bool>? playerCombat = null)
    {
        if (reference == records.RuntimeFormKey(0x14))
            return (playerCombat ?? throw new NotSupportedException("Player combat query has no engagement owner."))();
        var actor = Actor(reference);
        return IsEnabled(reference) && actor.Injury?.Dead != true && actor.Engagement is { Action: not "idle" };
    }

    internal void InitializeSourceCorpse(FalloutFormKey reference)
    {
        var actor = Actor(reference);
        if (actor.Injury is null && FalloutActorHealthSource.StartsDead(records, actor.Base, actor.Templates))
            _ = Health(reference);
    }

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
        if (died) injury = BeginActorDeath(actor, source, injury, attacker, level, globals);
        actor.ActorValues["health"] = changed;
        actor.Injury = injury with
        {
            LimbDamage = limbs
        };
        return new(reference, partType, health.Current, changed.Current, healthDamage, limbDamage, died, actor.Injury.Dead, source.ImpactMaterial);
    }

    internal bool KillActor(FalloutFormKey reference, FalloutFormKey? killer, int level, FalloutGlobalState? globals = null)
    {
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        var actor = Actor(reference);
        var source = HealthSource(reference);
        if (source.Essential) throw new NotSupportedException("Essential actor knockdown/recovery is not bound.");
        if (killer is { } known && records.RuntimeFormId(known) != 0x14) _ = Actor(known);
        var health = Health(reference);
        var injury = actor.Injury!;
        if (injury.Dead) return false;
        // Kill is a death transition, not a weapon hit. It preserves existing
        // limb damage and does not generate hit effects or destruction damage.
        var changed = health with { Damage = -(health.Base + health.Permanent + health.Temporary) };
        if (!changed.IsFinite) throw new InvalidDataException("Script death exceeds finite health storage.");
        injury = BeginActorDeath(actor, source, injury, killer, level, globals);
        actor.ActorValues["health"] = changed;
        actor.Injury = injury;
        return true;
    }

    private FalloutActorInjury BeginActorDeath(FalloutReferenceInstance actor, FalloutActorHealthSource source,
        FalloutActorInjury injury, FalloutFormKey? killer, int level, FalloutGlobalState? globals)
    {
        var deathCount = checked(actor.DeathCount + 1);
        // Expand once before publishing health/death. Inventory.Add is atomic
        // and uses the same retained leveled-list stream as weapon deaths.
        if (!injury.DeathInventoryGranted && source.DeathItem is { } item)
            Inventory(actor.Reference, level, globals).Contents.Add(records, item, 1, level, true, globals);
        actor.DeathCount = deathCount;
        actor.HitReaction = null;
        return injury with
        {
            Dead = true,
            Killer = killer,
            DeathInventoryGranted = true,
            DeathEventPending = true,
            DeathEventElapsed = 0
        };
    }

    internal bool AdvanceDeathEvent(FalloutFormKey reference, double seconds, double delay, bool speaking)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || !double.IsFinite(delay) || delay < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        var actor = Actor(reference);
        if (actor.Injury is not { DeathEventPending: true } injury) return false;
        var elapsed = Math.Min(delay, injury.DeathEventElapsed + seconds);
        var ready = elapsed >= delay && !speaking;
        // Consume before dispatch: a failing source block retains its prefix
        // and error, and a saved corpse must never execute that prefix again.
        actor.Injury = injury with { DeathEventElapsed = elapsed, DeathEventPending = !ready };
        return ready;
    }

    private void RestoreInjury(FalloutReferenceInstance actor, FalloutActorInjury injury)
    {
        if (injury.LimbDamage is null || injury.LimbDamage.Any(pair => pair.Key > 14 || !float.IsFinite(pair.Value) || pair.Value < 0) ||
            !injury.Dead && (injury.Killer is not null || injury.DeathInventoryGranted || injury.DeathEventPending || injury.DeathEventElapsed != 0) ||
            !double.IsFinite(injury.DeathEventElapsed) || injury.DeathEventElapsed < 0 ||
            !actor.ActorValues.TryGetValue("health", out var health) || !health.IsFinite || injury.Dead != (health.Current <= 0))
            throw new InvalidDataException("Saved actor injury is inconsistent with its health.");
        var body = BodyParts(actor.Reference);
        if (injury.LimbDamage.Keys.Any(key => !body.Parts.Any(part => part.Type == key)))
            throw new InvalidDataException("Saved limb is absent from the winning body-part source.");
        if (injury.SeveredParts is { } severed && (!injury.Dead && severed.Count != 0 ||
            severed.Distinct().Count() != severed.Count || severed.Any(type => !body.Parts.Any(part => part.Type == type && (part.Flags & 1) != 0))))
            throw new InvalidDataException("Saved severed limb is invalid for its source body.");
        actor.Injury = injury with { LimbDamage = new Dictionary<byte, float>(injury.LimbDamage), SeveredParts = injury.SeveredParts?.ToArray() };
    }

    internal void SeverLimb(FalloutFormKey reference, byte type)
    {
        var actor = Actor(reference);
        if (actor.Injury is not { Dead: true } injury) throw new InvalidOperationException("Limb separation requires a dead actor.");
        if (!BodyParts(reference).Parts.Any(part => part.Type == type && (part.Flags & 1) != 0))
            throw new NotSupportedException("The source body part is not severable.");
        var parts = injury.SeveredParts ?? [];
        if (!parts.Contains(type)) actor.Injury = injury with { SeveredParts = parts.Append(type).Order().ToArray() };
    }
}
