using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class OwnedIngestibleProbe
{
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
        var label = FalloutGameSettingStrings.Read(records, "sInventoryUse");
        if (string.IsNullOrWhiteSpace(label)) throw new InvalidDataException("Owned Aid action label is absent.");
        Console.WriteLine("OPENNV_OWNED_INGESTIBLE_PASS stimpak=true normalInstant=true hardcoreTimed=true medicine=true consumeSound=true coldRemainder=true nativeUse=separate");
    }
}
