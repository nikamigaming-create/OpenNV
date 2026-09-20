using OpenNV.Runtime.Content;

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
    Func<FalloutFormKey, bool> hasPerk, Func<bool> hardcore)
{
    private readonly Dictionary<FalloutFormKey, FalloutIngestible> _definitions = [];
    private readonly List<FalloutIngestibleActiveEffect> _active = [];
    private readonly Dictionary<string, float> _settings = [];
    internal FalloutIngestiblesSnapshot Capture() => new(_active.ToArray());
    private FalloutIngestible Definition(FalloutFormKey form)
    {
        if (!_definitions.TryGetValue(form, out var value)) _definitions.Add(form, value = FalloutIngestible.Read(records, form));
        return value;
    }

    internal FalloutIngestibleUse Prepare(FalloutFormKey form)
    {
        if (inventory.Item(form) is not { Count: > 0 }) throw new InvalidOperationException("Consumed item is absent from inventory.");
        if (vitals.State.HitPoints == 0) throw new InvalidOperationException("A dead player cannot consume Aid.");
        var source = Definition(form);
        if ((source.Flags & ~7) != 0 || source.Script is not null || source.AddictionChance != 0)
            throw new NotSupportedException($"Ingestible {form} requires its flags, script or addiction owner.");
        if (source.Effects.All(effect => (effect.Flags & 5) == 5))
            throw new NotSupportedException("Poison application requires its equipped-weapon owner.");
        var selected = source.Effects.Where(effect => FalloutCondition.AllPass(effect.Conditions, Condition)).ToArray();
        var before = vitals.State;
        var after = before;
        var timers = new List<FalloutIngestibleActiveEffect>();
        foreach (var effect in selected)
        {
            ValidateEffect(effect);
            var magnitude = Magnitude(source, effect);
            if (effect.EffectiveDuration == 0) after = Apply(after, effect, magnitude);
            else timers.Add(new(form, effect.Index, source.Hash, effect.Hash, magnitude, 0, effect.EffectiveDuration));
        }
        after.Validate();
        var revision = inventory.Revision;
        var used = false;
        return new(source.Sound, () =>
        {
            if (used || inventory.Revision != revision || !ReferenceEquals(vitals.State, before))
                throw new InvalidOperationException("Prepared Aid use is stale or already committed.");
            used = true;
            inventory.Remove(form, 1, true);
            vitals.Publish(after);
            _active.AddRange(timers);
            return new(form, true, before.ExactHitPoints, after.ExactHitPoints);
        });
    }

    internal void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds == 0 || _active.Count == 0 || vitals.State.HitPoints == 0) return;
        var state = vitals.State;
        var next = new List<FalloutIngestibleActiveEffect>();
        foreach (var timer in _active)
        {
            var effect = Definition(timer.Item).Effects[timer.Index];
            var elapsed = Math.Min(timer.Duration, timer.Elapsed + seconds);
            state = Apply(state, effect, timer.Magnitude * (float)(elapsed - timer.Elapsed));
            if (elapsed < timer.Duration) next.Add(timer with { Elapsed = elapsed });
        }
        state.Validate();
        vitals.Publish(state);
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

    private static void ValidateEffect(FalloutIngestibleEffect effect)
    {
        if (effect.Archetype is not (0 or 34) || effect.ActorValue != 16 ||
            effect.Area != 0 || effect.Range != 0 || (effect.Flags & 0x10) == 0 ||
            (effect.Flags & ~0x1803f5u) != 0 || effect.AdditionalPresentation ||
            effect.Conditions.Any(condition => condition.Function is not (449 or 586)))
            throw new NotSupportedException($"Ingestible effect {effect.Form} requires its archetype, actor-value, recovery, condition or presentation owner.");
    }

    private float Magnitude(FalloutIngestible source, FalloutIngestibleEffect effect)
    {
        var result = (effect.Flags & 0x100) != 0 ? 0 : effect.Magnitude;
        if ((effect.Flags & 5) == 0)
        {
            if ((source.Flags & 2) != 0) result *= SkillMultiplier("Survival", 44);
            if ((source.Flags & 4) != 0) result *= SkillMultiplier("Medicine", 37);
        }
        result *= (effect.Flags & 4) == 0 ? 1 : -1;
        return float.IsFinite(result) ? result : throw new InvalidDataException("Ingestible magnitude is not finite.");
    }

    private float SkillMultiplier(string name, int skill)
    {
        float Setting(string suffix)
        {
            var key = "fMagic" + name + "Skill" + suffix;
            if (!_settings.TryGetValue(key, out var value)) _settings.Add(key, value = FalloutGameSettingFloats.Read(records, key));
            return value;
        }
        var level = actorValue(skill);
        if (!float.IsFinite(level) || level < 0) throw new InvalidDataException("Ingestible skill magnitude is invalid.");
        return Setting("Base") + Setting("Mult") * level / 100;
    }

    private float Condition(FalloutCondition condition) => condition.Function switch
    {
        586 when condition.Argument1 == 0 && condition.Argument2 == 0 => hardcore() ? 1 : 0,
        449 when condition.Argument2 == 0 => hasPerk(condition.FormArgument1) ? 1 : 0,
        _ => throw new NotSupportedException($"Ingestible condition {condition.Function} has no owner.")
    };

    private GameplayVitals Apply(GameplayVitals state, FalloutIngestibleEffect effect, float amount)
    {
        if (!float.IsFinite(amount)) throw new InvalidDataException("Ingestible effect increment is invalid.");
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
