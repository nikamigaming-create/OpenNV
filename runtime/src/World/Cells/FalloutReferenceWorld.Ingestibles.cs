using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorIngestiblesSnapshot(IReadOnlyList<FalloutIngestibleActiveEffect> Effects,
    string? Failure = null)
{
    internal FalloutActorIngestiblesSnapshot Copy() => new(Effects.ToArray(), Failure);
    internal static void Validate(FalloutActorIngestiblesSnapshot state)
    {
        FalloutPlayerIngestibles.Validate(new(state.Effects));
        if (state.Failure is not null && string.IsNullOrWhiteSpace(state.Failure))
            throw new InvalidDataException("Actor ingestible failure is empty.");
    }
}

internal sealed partial class FalloutReferenceWorld
{
    private Func<bool>? _readHardcore;
    internal bool PlayerHardcore => (_readHardcore ?? throw new NotSupportedException("Actor ingestible conditions have no session difficulty owner."))();
    internal void BindHardcore(Func<bool> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_readHardcore is not null) throw new InvalidOperationException("Actor hardcore conditions are already bound.");
        _readHardcore = read;
    }
    private readonly Dictionary<FalloutFormKey, FalloutIngestible> _actorIngestibles = [];
    private FalloutIngestible ActorIngestible(FalloutFormKey item)
    {
        if (!_actorIngestibles.TryGetValue(item, out var source))
            _actorIngestibles.Add(item, source = FalloutIngestible.Read(records, item));
        return source;
    }

    internal bool ActorMayEatWorldFood(FalloutFormKey actorReference, FalloutFormKey foodReference)
    {
        var actor = Actor(actorReference); var food = Get(foodReference);
        if (!CanActivate(foodReference) || IsDead(actorReference) || !IsActorFood(food.Base)) return false;
        if (food.Script is not null) throw new NotSupportedException("Scripted food acquisition requires its activation event owner.");
        var ownership = Ownership(foodReference);
        if (ownership.Global is not null) throw new NotSupportedException("Conditional food ownership requires its live global access owner.");
        if (ownership.Owner is not { } owner) return true;
        if (owner == actorReference || owner == actor.Base) return true;
        var declaration = records.GetEffective(owner);
        if (declaration.Signature == "FACT")
            return ActorFactions(actorReference).GetValueOrDefault(owner, (sbyte)-1) >= Math.Max(0, ownership.FactionRank ?? 0);
        return false;
    }

    internal FalloutIngestibleUse PrepareActorIngestible(FalloutFormKey reference, FalloutFormKey item,
        int level, FalloutGlobalState? globals, bool hardcore, FalloutFormKey? worldFood = null)
    {
        if (records.RuntimeFormId(reference) == 0x14)
            throw new InvalidOperationException("The engine player has its own ingestible owner.");
        var actor = Actor(reference); var health = Health(reference);
        if (!IsEnabled(reference) || IsDead(reference) || actor.Ingestibles?.Failure is { })
            throw new InvalidOperationException("Actor cannot begin ingesting in its current state.");
        var source = ActorIngestible(item);
        if ((source.Flags & ~7) != 0 || source.Script is not null || source.AddictionChance != 0 ||
            source.Effects.Count > 0 && source.Effects.All(effect => (effect.Flags & 5) == 5))
            throw new NotSupportedException($"Actor ingestible {item} requires its script, addiction, flags or poison owner: " +
                $"flags=0x{source.Flags:x2} script={source.Script?.ToString() ?? "none"} " +
                $"withdrawal={source.Withdrawal?.ToString() ?? "none"} addictionChance={source.AddictionChance:R} " +
                $"poisonOnly={source.Effects.Count > 0 && source.Effects.All(effect => (effect.Flags & 5) == 5)}.");
        var inventory = Inventory(reference, level, globals).Contents;
        FalloutReferenceInstance? food = null;
        if (worldFood is { } foodReference)
        {
            if (!ActorMayEatWorldFood(reference, foodReference)) throw new InvalidOperationException("World food is not available to this actor.");
            food = Get(foodReference);
            var count = records.GetEffective(foodReference).ReadSubrecords().Where(field => field.Signature == "XCNT").ToArray();
            if (food.Base != item || count.Length > 1 || count.Length == 1 &&
                (count[0].Data.Length != 4 || BinaryPrimitives.ReadInt32LittleEndian(count[0].Data.Span) != 1))
                throw new NotSupportedException("World food needs its exact single-reference consumption owner.");
        }
        else if (inventory.Item(item) is not { Count: > 0 }) throw new InvalidOperationException("Actor inventory contains no selected food.");
        var effects = source.Effects.Where(effect => FalloutCondition.AllPass(effect.Conditions, condition =>
            condition.RunOn == 0 ? condition.Function switch
            {
                586 when condition.Argument1 == 0 && condition.Argument2 == 0 => hardcore ? 1 : 0,
                449 when condition.Argument2 == 0 => AcquiredPerks(reference).Contains(condition.FormArgument1) ? 1 : 0,
                _ => throw new NotSupportedException("Actor ingestible condition is unbound.")
            } : throw new NotSupportedException("Actor ingestible condition has an unbound subject."))).ToArray();
        var beforeValues = new Dictionary<string, FalloutActorValue>(actor.ActorValues);
        var afterValues = new Dictionary<string, FalloutActorValue>(beforeValues);
        var beforeInjury = actor.Injury!; var afterInjury = beforeInjury;
        var before = actor.Ingestibles;
        var active = before?.Effects.ToList() ?? [];
        foreach (var effect in effects)
        {
            FalloutIngestibleApplications.Validate(effect, value => value is >= 5 and <= 11);
            var magnitude = FalloutIngestibleApplications.Magnitude(records, source, effect,
                value => value == 20 ? ActorRadiationResistance(reference, level, globals) :
                    ActorValue(reference, FalloutActorValue.SourceSlot(value)), player: false);
            if (FalloutIngestibleApplications.Reversible(effect)) Modify(effect.ActorValue, magnitude);
            else if (effect.EffectiveDuration == 0) ApplyActorIngestibleIncrement(reference, afterValues, ref afterInjury, effect, magnitude);
            if (effect.EffectiveDuration > 0) active.Add(new(item, effect.Index, source.Hash, effect.Hash, magnitude, 0, effect.EffectiveDuration));
        }
        var revision = inventory.Revision; var used = false;
        var after = new FalloutActorIngestiblesSnapshot(active.ToArray());
        return new(source.Sound, () =>
        {
            if (used || inventory.Revision != revision || !ReferenceEquals(actor.Injury, beforeInjury) ||
                !ReferenceEquals(actor.Ingestibles, before) ||
                !actor.ActorValues.OrderBy(row => row.Key).SequenceEqual(beforeValues.OrderBy(row => row.Key)) ||
                worldFood is { } current && !ActorMayEatWorldFood(reference, current))
                throw new InvalidOperationException("Prepared actor ingestion is stale or already consumed.");
            used = true;
            using var transaction = new FalloutInventoryChangeLease(() =>
            {
                PublishActorIngestibleValues(actor, afterValues); actor.Injury = afterInjury; actor.Ingestibles = after;
                if (food is not null) food.Taken = true;
            }, () =>
            {
                PublishActorIngestibleValues(actor, beforeValues); actor.Injury = beforeInjury; actor.Ingestibles = before;
                if (food is not null) food.Taken = false;
            });
            if (worldFood is null) inventory.ConsumeOne(item, transaction); else inventory.ConsumeWorldItem(transaction);
            return new(item, true, health.Current, afterValues["health"].Current);
        });

        void Modify(int value, float magnitude)
        {
            var slot = FalloutActorValue.SourceSlot(value);
            var previous = afterValues.GetValueOrDefault(slot, new(SourceActorValue(actor, slot)));
            var changed = previous with { Temporary = previous.Temporary + magnitude };
            if (!changed.IsFinite) throw new InvalidDataException("Actor ingestible modifier is not finite.");
            afterValues[slot] = changed;
        }
    }

    private static void PublishActorIngestibleValues(FalloutReferenceInstance actor, Dictionary<string, FalloutActorValue> values)
    { actor.ActorValues.Clear(); foreach (var (key, value) in values) actor.ActorValues.Add(key, value); }

    private void ApplyActorIngestibleIncrement(FalloutFormKey reference, Dictionary<string, FalloutActorValue> values,
        ref FalloutActorInjury injury, FalloutIngestibleEffect effect, float amount)
    {
        if (effect.ActorValue == 54)
        {
            var previous = values.GetValueOrDefault("radiationrads", new(0));
            var radiation = previous.Current - amount;
            if (!float.IsFinite(radiation)) throw new InvalidDataException("Actor radiation overflowed.");
            values["radiationrads"] = previous with
            { Damage = Math.Max(0, radiation) - previous.Base - previous.Permanent - previous.Temporary };
            return;
        }
        var health = values["health"]; var maximum = health.Base + health.Permanent + health.Temporary;
        if (!float.IsFinite(amount) || !float.IsFinite(maximum) || maximum <= 0 || health.Current + amount <= 0)
            throw new NotSupportedException("Actor ingestible increment requires its lethal or invalid-health transition owner.");
        var changed = health with { Damage = Math.Clamp(health.Damage + amount, -maximum, 0) };
        if (!changed.IsFinite) throw new InvalidDataException("Actor ingestible health is not finite.");
        values["health"] = changed;
        if (effect.Archetype != 34) return;
        var limbs = new Dictionary<byte, float>(injury.LimbDamage);
        foreach (var part in BodyParts(reference).Parts.Where(part => part.ActorValue is >= 25 and <= 30 && part.HealthPercent > 0))
        {
            var capacity = health.Base * part.HealthPercent / 100;
            limbs[part.Type] = Math.Clamp(limbs.GetValueOrDefault(part.Type) - capacity * amount / 100, 0, capacity);
        }
        injury = injury with { LimbDamage = limbs };
    }

    private readonly FalloutAbilityModifiers _ingestibleAbilities = new(records);
    private float ActorRadiationResistance(FalloutFormKey reference, int level, FalloutGlobalState? globals)
    {
        var actor = Actor(reference);
        var resistance = (ActorValue(reference, "endurance") + records.NumericSettings.Float("fAVDRadResistEnduranceOffset")) *
            records.NumericSettings.Float("fAVDRadResistEnduranceMult");
        var effects = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(actor.Base), 8, actor.Templates);
        foreach (var field in effects.ReadSubrecords().Where(field => field.Signature == "SPLO"))
        {
            if (field.Data.Length != 4) throw new InvalidDataException("Actor resistance ability identity is invalid.");
            var form = effects.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            if (form is { } spell) Add(spell);
        }
        foreach (var armor in EquippedArmor(reference, level, globals))
        {
            var form = records.GetEffective(armor);
            foreach (var field in form.ReadSubrecords().Where(field => field.Signature == "EITM"))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Armor resistance effect identity is invalid.");
                var enchantment = form.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
                if (enchantment is { } spell) Add(spell);
            }
        }
        foreach (var modifier in PerkModifiers(reference).Where(value => value.ActorValue == 20)) Apply(modifier);
        return float.IsFinite(resistance) ? resistance : throw new InvalidDataException("Actor radiation resistance is not finite.");

        void Add(FalloutFormKey spell)
        {
            foreach (var modifier in _ingestibleAbilities.Spell(spell).Where(value => value.ActorValue == 20)) Apply(modifier);
        }
        void Apply(FalloutAbilityModifier modifier)
        {
            if (modifier.Conditions.Count != 0)
                throw new NotSupportedException("Conditional actor radiation resistance needs its live condition owner.");
            resistance += modifier.Amount;
        }
    }

    internal void ValidateActorIngestibles(FalloutFormKey reference, FalloutActorIngestiblesSnapshot state)
    {
        var actor = Actor(reference); FalloutActorIngestiblesSnapshot.Validate(state);
        if (state.Effects.Count > 0 && (actor.Injury is null || !actor.ActorValues.ContainsKey("health")))
            throw new InvalidDataException("Actor ingestible effects have no retained health/injury owner.");
        foreach (var timer in state.Effects)
        {
            var source = ActorIngestible(timer.Item);
            if (timer.Index >= source.Effects.Count || source.Hash != timer.ItemHash || source.Effects[timer.Index].Hash != timer.EffectHash ||
                source.Effects[timer.Index].EffectiveDuration != timer.Duration ||
                Math.Sign(timer.Magnitude) != 0 && Math.Sign(timer.Magnitude) != ((source.Effects[timer.Index].Flags & 4) == 0 ? 1 : -1))
                throw new InvalidDataException("Actor ingestible receipt differs from winning source effects.");
            FalloutIngestibleApplications.Validate(source.Effects[timer.Index], value => value is >= 5 and <= 11);
            if (FalloutIngestibleApplications.Reversible(source.Effects[timer.Index]) &&
                !actor.ActorValues.ContainsKey(FalloutActorValue.SourceSlot(source.Effects[timer.Index].ActorValue)))
                throw new InvalidDataException("Actor ingestible modifier has no retained value pool.");
        }
    }

    internal void ValidateEatProgress(FalloutFormKey reference, FalloutActorEatProgress meal)
    {
        _ = Actor(reference); meal.Validate();
        if (ActorIngestible(meal.Item).Hash != meal.ItemSha256 || !IsActorFood(meal.Item))
            throw new InvalidDataException("Retained Eat item differs from its source food.");
        if (meal.WorldReference is { } food && Get(food).Base != meal.Item)
            throw new InvalidDataException("Retained world food differs from its source reference.");
        if (meal.Idle is { } idle)
        {
            var record = records.GetEffective(idle);
            if (record.Signature != "IDLE" || !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(record.ReadData()))
                .Equals(meal.IdleSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Retained Eat IDLE differs from its winning source.");
            FalloutActorIdleObjectBinding.ValidateSources(records, FalloutActorIdleSource.Resolve(records, record), meal.Objects);
        }
    }

    internal void AdvanceActorIngestibles(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (seconds == 0) return;
        foreach (var actor in _instances.Values.Where(actor => actor.Ingestibles is { Failure: null, Effects.Count: > 0 }).ToArray())
        {
            if (IsDead(actor.Reference)) continue;
            var before = actor.Ingestibles!;
            try
            {
                var values = new Dictionary<string, FalloutActorValue>(actor.ActorValues); var injury = actor.Injury!;
                var next = new List<FalloutIngestibleActiveEffect>();
                foreach (var timer in before.Effects)
                {
                    var effect = ActorIngestible(timer.Item).Effects[timer.Index];
                    var elapsed = Math.Min(timer.Duration, timer.Elapsed + seconds);
                    if (FalloutIngestibleApplications.Reversible(effect))
                    {
                        if (elapsed == timer.Duration)
                        {
                            var slot = FalloutActorValue.SourceSlot(effect.ActorValue); var value = values[slot];
                            values[slot] = value with { Temporary = value.Temporary - timer.Magnitude };
                            if (!values[slot].IsFinite) throw new InvalidDataException("Actor effect expiry is not finite.");
                        }
                    }
                    else ApplyActorIngestibleIncrement(actor.Reference, values, ref injury, effect, timer.Magnitude * (float)(elapsed - timer.Elapsed));
                    if (elapsed < timer.Duration) next.Add(timer with { Elapsed = elapsed });
                }
                PublishActorIngestibleValues(actor, values); actor.Injury = injury; actor.Ingestibles = new(next.ToArray());
            }
            catch (Exception error) when (error is NotSupportedException or InvalidDataException or InvalidOperationException)
            { actor.Ingestibles = before with { Failure = error.Message }; }
        }
    }
}
