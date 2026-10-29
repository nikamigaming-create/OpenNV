using System.Text.Json;
using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedIngestibleProbe
{
    internal static void RunActorInventory(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var actor = new FalloutFormKey("FalloutNV.esm", 0x15a796);
        var globals = FalloutGlobalState.Read(records);
        var inventory = world.Inventory(actor, 1, globals);
        var instance = world.Get(actor);
        var owner = FalloutActorTemplateOwner.Resolve(records, records.GetEffective(instance.Base), 256, instance.Templates);
        var source = owner.ReadSubrecords().Where(field => field.Signature == "CNTO").Select(field =>
        {
            if (field.Data.Length != 8) throw new InvalidDataException("Source inventory entry is malformed.");
            var item = owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span));
            return new { item = item.ToString(), signature = records.GetEffective(item).Signature,
                count = BinaryPrimitives.ReadInt32LittleEndian(field.Data.Span[4..]) };
        }).ToArray();
        var foods = inventory.Contents.Items.Where(item => world.IsActorFood(item.FormKey))
            .Select(item => new { item = item.FormKey.ToString(), item.Count }).ToArray();
        var possible = new HashSet<FalloutFormKey>();
        void Expand(FalloutFormKey key)
        {
            if (!possible.Add(key)) return;
            var record = records.GetEffective(key);
            if (record.Signature != "LVLI") return;
            foreach (var entry in record.ReadSubrecords().Where(field => field.Signature == "LVLO"))
            {
                if (entry.Data.Length != 12) throw new InvalidDataException("Leveled food source entry has an invalid extent.");
                Expand(record.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(entry.Data.Span[4..])));
            }
        }
        foreach (var entry in owner.ReadSubrecords().Where(field => field.Signature == "CNTO"))
            Expand(owner.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(entry.Data.Span)));
        var candidateFoods = possible.Where(key => records.GetEffective(key).Signature == "ALCH" && world.IsActorFood(key))
            .Select(key => FalloutIngestible.Read(records, key)).Select(value => new
            {
                item = value.Form.ToString(), value.Flags, script = value.Script?.ToString(),
                withdrawal = value.Withdrawal?.ToString(), value.AddictionChance,
                poisonOnly = value.Effects.Count > 0 && value.Effects.All(effect => (effect.Flags & 5) == 5)
            }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned-actor-food-source", actor = actor.ToString(),
            sourceOwner = owner.FormKey.ToString(), source, resolvedFoods = foods, candidateFoods,
            random = "fresh-source-resolution", retailOrdering = "unverified", gameplay = "separate" }));
    }

    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        Run(records, content);
    }

    internal static void Run(FalloutPluginStack records, RuntimeLiveContentSource content)
    {
        var form = FalloutDialogueTopic.Find(records, "ALCH", "Stimpak").FormKey;
        var player = records.RuntimeFormKey(7);
        var source = FalloutIngestible.Read(records, form);
        if (FalloutActorFood.IsFood(records, form)) throw new InvalidDataException("Owned Stimpak was admitted as actor food.");
        var foods = records.EffectiveRecords("ALCH").Count(record => FalloutActorFood.IsFood(records, record.FormKey));
        if (foods == 0) throw new InvalidDataException("Owned ingestibles supplied no actor food.");
        Console.WriteLine($"OPENNV_OWNED_ACTOR_FOOD_PASS ingestibles=true foodCount={foods} medicineExcluded=true gameplay=unverified");
        var body = FalloutBodyPartData.Read(records.GetEffective(records.RuntimeFormKey(0x1d)));
        var waterSpell = FalloutDialogueTopic.Find(records, "SPEL", "WaterHeal1Purified").FormKey;
        var waterSource = FalloutIngestible.ReadActorEffect(records, waterSpell);
        var waterVitals = new FalloutPlayerVitals(records, player, new(5, 5, 5, 5, 5, 5, 5));
        waterVitals.Damage(50);
        var waterBefore = waterVitals.State.ExactHitPoints;
        var waterInventory = new FalloutPlayerInventory();
        var waterOwner = new FalloutPlayerIngestibles(records, waterInventory, waterVitals, body, _ => 50, _ => false, () => false);
        if (waterOwner.PrepareActorEffect(waterSpell).Commit().Consumed || waterInventory.Item(waterSpell) is not null)
            throw new InvalidDataException("Owned water effect unexpectedly consumed inventory.");
        waterOwner.Advance(2);
        var waterColdVitals = new FalloutPlayerVitals(records, player, new(5, 5, 5, 5, 5, 5, 5), waterVitals.State);
        var waterCold = new FalloutPlayerIngestibles(records, waterInventory, waterColdVitals, body, _ => 50, _ => false, () => false);
        waterCold.Restore(JsonSerializer.Deserialize<FalloutIngestiblesSnapshot>(JsonSerializer.Serialize(waterOwner.Capture()))!);
        waterOwner.Advance(100); waterCold.Advance(100);
        var healingEffect = waterSource.Effects.Single(effect => effect.ActorValue == 16);
        var waterExpected = waterBefore + healingEffect.Magnitude * healingEffect.EffectiveDuration;
        if (waterVitals.State.ExactHitPoints != waterExpected || waterColdVitals.State.ExactHitPoints != waterExpected ||
            waterOwner.Capture().Effects.Count != 0)
            throw new InvalidDataException("Owned water actor effect lost its duration or cold remainder.");
        var hardcoreWater = new FalloutPlayerIngestibles(records, waterInventory, waterVitals, body, _ => 50, _ => false, () => true);
        try { hardcoreWater.PrepareActorEffect(waterSpell); throw new InvalidDataException("Unsupported dehydration effect was admitted."); }
        catch (NotSupportedException) { }
        if (waterVitals.State.ExactHitPoints != waterExpected || hardcoreWater.Capture().Effects.Count != 0)
            throw new InvalidDataException("Unsupported dehydration partially applied water healing.");
        Console.WriteLine("OPENNV_OWNED_WATER_EFFECT_PASS nonconsuming=true timedHealth=true coldRemainder=true unsupportedDehydrationRefused=true delivery=unverified");
        var expectedMagnitude = source.Effects.Single(effect => effect.Archetype == 34 && effect.Magnitude == 30).Magnitude *
            (FalloutGameSettingFloats.Read(records, "fMagicMedicineSkillBase") + FalloutGameSettingFloats.Read(records, "fMagicMedicineSkillMult") * .5f);
        foreach (var hardcore in new[] { false, true })
        {
            var inventory = new FalloutPlayerInventory(); inventory.Add(records, form, 2, 1, true);
            var vitals = new FalloutPlayerVitals(records, player, new(5, 5, 5, 5, 5, 5, 5));
            vitals.Damage(100);
            var before = vitals.State.ExactHitPoints;
            var owner = new FalloutPlayerIngestibles(records, inventory, vitals, body, _ => 50, _ => false, () => hardcore);
            var use = owner.Prepare(form);
            if (use.Sound != source.Sound || use.Sound is null) throw new InvalidDataException("Owned consumption lost its sound.");
            var sound = FalloutSoundRecordReader.Read(records.GetEffective(use.Sound.Value));
            if (sound.HasExactFile ? !content.TryResolve(sound.LogicalPath, null, out _) :
                !content.ResourcePathsUnder(sound.LogicalPath).Any(path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)))
                throw new FileNotFoundException("Owned consumption sound is unavailable.");
            use.Commit();
            if (inventory.Item(form)!.Count != 1 || (owner.Capture().Effects.Count == 0) == hardcore)
                throw new InvalidDataException("Owned Stimpak lost its condition-selected timing or item transaction.");
            owner.Advance(1.5);
            var restoredVitals = new FalloutPlayerVitals(records, player, new(5, 5, 5, 5, 5, 5, 5),
                JsonSerializer.Deserialize<GameplayVitals>(JsonSerializer.Serialize(vitals.State))!);
            var cold = new FalloutPlayerIngestibles(records, inventory, restoredVitals, body, _ => 50, _ => false, () => hardcore);
            cold.Restore(JsonSerializer.Deserialize<FalloutIngestiblesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!);
            owner.Advance(4.5); cold.Advance(4.5);
            if (MathF.Abs(vitals.State.ExactHitPoints - before - expectedMagnitude) > .001f ||
                restoredVitals.State.ExactHitPoints != vitals.State.ExactHitPoints || owner.Capture().Effects.Count != 0)
                throw new InvalidDataException("Owned Stimpak timing or cold healing total diverged.");
        }
        var cactus = FalloutDialogueTopic.Find(records, "ALCH", "NVFreshBarrelCactusFruit").FormKey;
        var foodInventory = new FalloutPlayerInventory(); foodInventory.Add(records, cactus, 1, 1, true);
        var foodPools = new FalloutPlayerActorValues(records); foodPools.BindConstantModifiers((_, _) => []);
        var foodVitals = new FalloutPlayerVitals(records, player, new(5, 5, 5, 5, 5, 5, 5));
        foodVitals.Damage(50);
        var foodHealth = foodVitals.State.ExactHitPoints;
        var endurance = foodPools.ReadCurrent(7);
        var food = new FalloutPlayerIngestibles(records, foodInventory, foodVitals, body,
            _ => 50, _ => false, () => false, foodPools);
        food.Prepare(cactus).Commit();
        if (foodInventory.Item(cactus) is { Count: > 0 } || foodPools.ReadCurrent(7) != endurance - 1)
            throw new InvalidDataException("Owned cactus lost its consumption or temporary Endurance penalty.");
        food.Advance(5);
        var restoreHealth = FalloutIngestible.Read(records, cactus).Effects.Single(effect => effect.ActorValue == 16);
        var expectedFood = restoreHealth.Magnitude * restoreHealth.EffectiveDuration *
            (FalloutGameSettingFloats.Read(records, "fMagicSurvivalSkillBase") +
                FalloutGameSettingFloats.Read(records, "fMagicSurvivalSkillMult") * .5f);
        if (MathF.Abs(foodVitals.State.ExactHitPoints - foodHealth - expectedFood) > .001f || foodPools.ReadCurrent(7) != endurance - 1)
            throw new InvalidDataException("Owned food timed healing or independent modifier lifetime diverged.");
        food.Advance(115);
        if (foodPools.ReadCurrent(7) != endurance || food.Capture().Effects.Count != 0)
            throw new InvalidDataException("Owned cactus modifier did not expire once at its source duration.");
        Console.WriteLine("OPENNV_OWNED_FOOD_EFFECT_PASS cactus=true survivalScale=true timedHealth=true temporaryEndurance=true hardcoreConditions=false nativeEat=unverified");
        var meat = FalloutDialogueTopic.Find(records, "ALCH", "NVGeckoMeat").FormKey;
        var meatInventory = new FalloutPlayerInventory(); meatInventory.Add(records, meat, 1, 1, true);
        var meatVitals = new FalloutPlayerVitals(records, player, new(5, 5, 5, 5, 5, 5, 5));
        meatVitals.Damage(50);
        var meatBefore = meatVitals.State.ExactHitPoints;
        var meatOwner = new FalloutPlayerIngestibles(records, meatInventory, meatVitals, body,
            value => value == 20 ? 25 : 50, _ => false, () => false, foodPools);
        var meatEffects = FalloutIngestible.Read(records, meat).Effects;
        var rads = meatEffects.Single(effect => effect.ActorValue == 54);
        meatOwner.Prepare(meat).Commit(); meatOwner.Advance(60);
        var healing = meatEffects.Single(effect => effect.ActorValue == 16);
        var healingScale = FalloutGameSettingFloats.Read(records, "fMagicSurvivalSkillBase") +
            FalloutGameSettingFloats.Read(records, "fMagicSurvivalSkillMult") * .5f;
        if (meatInventory.Item(meat) is { Count: > 0 } ||
            MathF.Abs(meatVitals.State.RadiationRads - rads.Magnitude * Math.Max(1, rads.EffectiveDuration) * .75f) > .001f ||
            MathF.Abs(meatVitals.State.ExactHitPoints - meatBefore - healing.Magnitude * Math.Max(1, healing.EffectiveDuration) * healingScale) > .001f)
            throw new InvalidDataException("Owned meat consumption lost health or resistance-adjusted radiation.");
        Console.WriteLine("OPENNV_OWNED_MEAT_EFFECT_PASS consume=true health=true radiation=true sourceResistance=true native=unverified");
        var label = FalloutGameSettingStrings.Read(records, "sInventoryUse");
        if (string.IsNullOrWhiteSpace(label)) throw new InvalidDataException("Owned Aid action label is absent.");
        Console.WriteLine("OPENNV_OWNED_INGESTIBLE_PASS stimpak=true normalInstant=true hardcoreTimed=true medicine=true consumeSound=true coldRemainder=true nativeUse=separate");
    }
}
