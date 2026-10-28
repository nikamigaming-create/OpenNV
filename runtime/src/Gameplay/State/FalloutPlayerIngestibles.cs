using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutIngestibleActiveEffect(FalloutFormKey Item, int Index, string ItemHash,
    string EffectHash, float Magnitude, double Elapsed, double Duration);
internal sealed record FalloutIngestiblesSnapshot(IReadOnlyList<FalloutIngestibleActiveEffect> Effects);
internal sealed record FalloutIngestibleUseResult(FalloutFormKey Item, bool Consumed, float HealthBefore, float HealthAfter);
internal sealed record FalloutIngestibleUse(FalloutFormKey? Sound, Func<FalloutIngestibleUseResult> Commit);

// The same item transaction and gameplay clock serve flat and wrist UI input.
// Timers store magnitudes selected at consumption, never recomputed on reload.
internal sealed class FalloutPlayerIngestibles(FalloutPluginStack records, FalloutPlayerInventory inventory,
    FalloutPlayerVitals vitals, FalloutBodyPartData bodyParts, Func<int, float> actorValue,
    Func<FalloutFormKey, bool> hasPerk, Func<bool> hardcore, FalloutPlayerActorValues? pools = null)
{
    private readonly Dictionary<FalloutFormKey, FalloutIngestible> _definitions = [];
    private readonly List<FalloutIngestibleActiveEffect> _active = [];
    internal FalloutIngestiblesSnapshot Capture() => new(_active.ToArray());
    private FalloutIngestible Definition(FalloutFormKey form)
    {
        if (!_definitions.TryGetValue(form, out var value))
            _definitions.Add(form, value = records.GetEffective(form).Signature switch
            {
                "ALCH" or "INGR" => FalloutIngestible.Read(records, form),
                "SPEL" => FalloutIngestible.ReadActorEffect(records, form),
                _ => throw new InvalidDataException("Active effect source is neither ingestible, ingredient nor SPEL.")
            });
        return value;
    }

    internal FalloutIngestibleUse Prepare(FalloutFormKey form) => Prepare(form, consume: true);

    // Callers must establish the source and delivery of the actor effect;
    // this owns its selected value changes and their cold continuation only.
    internal FalloutIngestibleUse PrepareActorEffect(FalloutFormKey form) => Prepare(form, consume: false);

    private FalloutIngestibleUse Prepare(FalloutFormKey form, bool consume)
    {
        if (consume && inventory.Item(form) is not { Count: > 0 }) throw new InvalidOperationException("Consumed item is absent from inventory.");
        if (vitals.State.HitPoints == 0) throw new InvalidOperationException("A dead player cannot receive an effect.");
        var signature = records.GetEffective(form).Signature;
        if (consume ? signature is not ("ALCH" or "INGR") : signature != "SPEL")
            throw new InvalidDataException("Effect delivery differs from its source record type.");
        var source = Definition(form);
        if ((source.Flags & ~7) != 0 || source.Script is not null || source.AddictionChance != 0)
            throw new NotSupportedException($"Ingestible {form} requires its flags, script or addiction owner.");
        if (source.Effects.Count > 0 && source.Effects.All(effect => (effect.Flags & 5) == 5))
            throw new NotSupportedException("Poison application requires its equipped-weapon owner.");
        var selected = source.Effects.Where(effect => FalloutCondition.AllPass(effect.Conditions, Condition)).ToArray();
        var before = vitals.State;
        var after = before;
        var timers = new List<FalloutIngestibleActiveEffect>();
        var modifiers = new Dictionary<int, float>();
        var beforePools = pools?.Capture();
        foreach (var effect in selected)
        {
            ValidateEffect(effect);
            var magnitude = FalloutIngestibleApplications.Magnitude(records, source, effect, actorValue);
            if (FalloutIngestibleApplications.Reversible(effect))
                modifiers[effect.ActorValue] = modifiers.GetValueOrDefault(effect.ActorValue) + magnitude;
            else if (effect.EffectiveDuration == 0) after = Apply(after, effect, magnitude);
            else timers.Add(new(form, effect.Index, source.Hash, effect.Hash, magnitude, 0, effect.EffectiveDuration));
            if (FalloutIngestibleApplications.Reversible(effect))
                timers.Add(new(form, effect.Index, source.Hash, effect.Hash, magnitude, 0, effect.EffectiveDuration));
        }
        foreach (var (value, amount) in modifiers)
            if (!float.IsFinite(amount) || !float.IsFinite(pools!.ReadCurrent(value) + amount))
                throw new InvalidDataException("Ingestible modifier exceeds its actor-value pool.");
        after.Validate();
        var revision = inventory.Revision;
        var activeBefore = _active.ToArray();
        var used = false;
        return new(source.Sound, () =>
        {
            if (used || consume && inventory.Revision != revision || !ReferenceEquals(vitals.State, before) ||
                !_active.SequenceEqual(activeBefore) ||
                beforePools is not null && !pools!.Capture().Values.OrderBy(row => row.Key).SequenceEqual(beforePools.Values.OrderBy(row => row.Key)))
                throw new InvalidOperationException("Prepared Aid use is stale or already committed.");
            used = true;
            var transaction = new FalloutInventoryChangeLease(() =>
            {
                foreach (var (value, amount) in modifiers) pools!.AddModifier(value, FalloutActorValuePool.Temporary, amount);
                vitals.Publish(after);
                _active.AddRange(timers);
            }, () =>
            {
                if (beforePools is not null) pools!.Restore(beforePools);
                vitals.Publish(before);
                _active.Clear(); _active.AddRange(activeBefore);
            });
            if (consume) inventory.ConsumeOne(form, transaction);
            else
            {
                using (transaction) { transaction.Commit(); transaction.Complete(); }
            }
            return new(form, consume, before.ExactHitPoints, after.ExactHitPoints);
        });
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds == 0 || _active.Count == 0 || vitals.State.HitPoints == 0) return;
        var before = vitals.State;
        var state = before;
        var beforePools = pools?.Capture();
        var expired = new Dictionary<int, float>();
        var next = new List<FalloutIngestibleActiveEffect>();
        foreach (var timer in _active)
        {
            var effect = Definition(timer.Item).Effects[timer.Index];
            var elapsed = Math.Min(timer.Duration, timer.Elapsed + seconds);
            if (FalloutIngestibleApplications.Reversible(effect))
            {
                if (elapsed == timer.Duration)
                    expired[effect.ActorValue] = expired.GetValueOrDefault(effect.ActorValue) - timer.Magnitude;
            }
            else state = Apply(state, effect, timer.Magnitude * (float)(elapsed - timer.Elapsed));
            if (elapsed < timer.Duration) next.Add(timer with { Elapsed = elapsed });
        }
        state.Validate();
        try
        {
            foreach (var (value, magnitude) in expired) pools!.AddModifier(value, FalloutActorValuePool.Temporary, magnitude);
            vitals.Publish(state);
        }
        catch
        {
            if (beforePools is not null) pools!.Restore(beforePools);
            vitals.Publish(before); throw;
        }
        _active.Clear(); _active.AddRange(next);
    }

    internal void Restore(FalloutIngestiblesSnapshot snapshot)
    {
        if (_active.Count != 0) throw new InvalidOperationException("Ingestible restoration needs a fresh owner.");
        Validate(snapshot);
        foreach (var timer in snapshot.Effects)
        {
            var source = Definition(timer.Item);
            if (timer.Index >= source.Effects.Count || timer.ItemHash != source.Hash ||
                timer.EffectHash != source.Effects[timer.Index].Hash || timer.Duration != source.Effects[timer.Index].EffectiveDuration ||
                Math.Sign(timer.Magnitude) != 0 && Math.Sign(timer.Magnitude) != ((source.Effects[timer.Index].Flags & 4) == 0 ? 1 : -1))
                throw new InvalidDataException("Saved ingestible effect differs from its winning source.");
            ValidateEffect(source.Effects[timer.Index]);
        }
        _active.AddRange(snapshot.Effects);
    }

    internal static void Validate(FalloutIngestiblesSnapshot snapshot)
    {
        if (snapshot.Effects is null || snapshot.Effects.Any(effect => effect is null || effect.Index < 0 ||
            effect.ItemHash is null || effect.EffectHash is null || effect.ItemHash.Length != 64 || effect.EffectHash.Length != 64 ||
            !effect.ItemHash.All(Uri.IsHexDigit) || !effect.EffectHash.All(Uri.IsHexDigit) || !float.IsFinite(effect.Magnitude) ||
            !double.IsFinite(effect.Elapsed) || !double.IsFinite(effect.Duration) || effect.Elapsed < 0 || effect.Elapsed >= effect.Duration))
            throw new InvalidDataException("Saved ingestible timers are invalid.");
    }

    private void ValidateEffect(FalloutIngestibleEffect effect) =>
        FalloutIngestibleApplications.Validate(effect, value => pools is not null && value is >= 5 and <= 11);

    private float Condition(FalloutCondition condition) => condition.Function switch
    {
        586 when condition.Argument1 == 0 && condition.Argument2 == 0 => hardcore() ? 1 : 0,
        449 when condition.Argument2 == 0 => hasPerk(condition.FormArgument1) ? 1 : 0,
        _ => throw new NotSupportedException($"Ingestible condition {condition.Function} has no owner.")
    };

    private GameplayVitals Apply(GameplayVitals state, FalloutIngestibleEffect effect, float amount)
    {
        if (!float.IsFinite(amount)) throw new InvalidDataException("Ingestible effect increment is invalid.");
        if (effect.ActorValue == 54)
        {
            var radiation = state.RadiationRads - amount;
            if (!float.IsFinite(radiation)) throw new InvalidDataException("Ingestible radiation overflowed.");
            return state with { RadiationRads = Math.Max(0, radiation) };
        }
        var health = Math.Clamp(state.ExactHitPoints + amount, 0, state.MaximumHitPoints);
        var displayed = (int)MathF.Ceiling(health);
        state = state with { HitPoints = displayed, HitPointFraction = displayed - health };
        if (effect.Archetype != 34) return state;
        var limbs = new Dictionary<byte, float>(state.LimbDamage ?? new Dictionary<byte, float>());
        foreach (var part in bodyParts.Parts.Where(part => part.ActorValue is >= 25 and <= 30 && part.HealthPercent > 0))
        {
            var capacity = state.MaximumHitPoints * part.HealthPercent / 100;
            var damage = Math.Clamp(limbs.GetValueOrDefault(part.Type), 0, capacity);
            limbs[part.Type] = Math.Clamp(damage - capacity * amount / 100, 0, capacity);
        }
        return state with { LimbDamage = limbs };
    }
}
