using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class OwnedPlayerAbilityScriptProbe
{
    internal static void Run(string dataRoot, string spellIdentity)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var separator = spellIdentity.LastIndexOf(':');
        if (separator <= 0) throw new ArgumentException("Expected plugin:hex-object-id ability identity.");
        var spell = new FalloutFormKey(spellIdentity[..separator], Convert.ToUInt32(spellIdentity[(separator + 1)..], 16));
        var actor = new FalloutPlayerActorValues(records);
        var race = FalloutDialogueTopic.RequiredForm(records.GetEffective(actor.Source.Player), "RNAM");
        using var world = new FalloutReferenceWorld(records);
        var skills = Skills(actor);
        var selected = false;
        var effects = Bind(actor, skills, world);
        var before = skills.SkillOrder.ToDictionary(value => value, value => skills.ReadSkill(value, FalloutActorValueRead.Permanent));
        selected = true;
        _ = actor.ReadCurrent(5);
        var skillState = skills.CaptureValues();
        var effectState = effects.Capture();
        if (effectState.Effects.Count == 0 || effectState.Effects.Any(effect => !effect.Started || effect.Error is not null))
            throw new InvalidDataException("Selected source ability did not complete its actual Start owners.");
        var after = skills.SkillOrder.ToDictionary(value => value, value => skills.ReadSkill(value, FalloutActorValueRead.Permanent));
        for (var iteration = 0; iteration < 20; iteration++) _ = actor.ReadCurrent(5 + iteration % 7);
        if (JsonSerializer.Serialize(skillState) != JsonSerializer.Serialize(skills.CaptureValues()) ||
            JsonSerializer.Serialize(effectState) != JsonSerializer.Serialize(effects.Capture()) || world.InstanceCount != 0)
            throw new InvalidDataException("Repeated source queries replayed Start or fabricated a world actor.");

        var coldActor = new FalloutPlayerActorValues(records, actor.Capture());
        var coldSkills = Skills(coldActor);
        coldSkills.RestoreValues(JsonSerializer.Deserialize<FalloutPlayerSkillValuesSnapshot>(JsonSerializer.Serialize(skillState))!);
        using var coldWorld = new FalloutReferenceWorld(records);
        var coldEffects = Bind(coldActor, coldSkills, coldWorld,
            JsonSerializer.Deserialize<FalloutPlayerAbilityScriptsSnapshot>(JsonSerializer.Serialize(effectState))!);
        _ = coldActor.ReadCurrent(5);
        if (!after.OrderBy(pair => pair.Key).SequenceEqual(coldSkills.SkillOrder.ToDictionary(value => value,
                value => coldSkills.ReadSkill(value, FalloutActorValueRead.Permanent)).OrderBy(pair => pair.Key)) ||
            JsonSerializer.Serialize(skillState) != JsonSerializer.Serialize(coldSkills.CaptureValues()) ||
            JsonSerializer.Serialize(effectState) != JsonSerializer.Serialize(coldEffects.Capture()) || coldWorld.InstanceCount != 0)
            throw new InvalidDataException("Cold source ability changed locals, skill pools or its completed Start.");
        Console.WriteLine("OPENNV_OWNED_PLAYER_ABILITY_START_PASS " + JsonSerializer.Serialize(new
        {
            spell = spell.ToString(),
            effects = effectState.Effects.Select(effect => new { effect.EffectOrdinal, effect.Script, effect.ScriptSha256, effect.Started, effect.Locals }),
            changes = after.Where(pair => pair.Value != before[pair.Key]).Select(pair => new { actorValue = pair.Key, before = before[pair.Key], after = pair.Value }),
            skillPools = skillState.Pools,
            once = true,
            cold = true,
            actualPlayerReference = "00000014",
            proxyActors = world.InstanceCount + coldWorld.InstanceCount,
            boundary = "selected-source-start-and-cold-state;ordinary-input-and-compiled-parity-independent"
        }));

        FalloutPlayerSkills Skills(FalloutPlayerActorValues values) => new(records, () => values.BaseSpecial,
            _ => false, () => [], null, new FalloutPlayerInventory(), values.Source.Player, () => race, () => false,
            actorValues: values);
        FalloutPlayerAbilityScripts Bind(FalloutPlayerActorValues values, FalloutPlayerSkills owner, FalloutReferenceWorld references,
            FalloutPlayerAbilityScriptsSnapshot? restore = null)
        {
            var lifetime = new FalloutPlayerAbilityScripts(records,
                () => owner.SelectedConstantEffects().Concat(selected ? [spell] : Array.Empty<FalloutFormKey>()).Distinct().ToArray(),
                owner.AbilityCondition, restore);
            var executor = new FalloutReferenceScripts(records, references, new FalloutQuestState(records),
                new((_, _) => throw new NotSupportedException("Selected ability requires a native furniture query."),
                    _ => throw new NotSupportedException("Selected ability requires a native presentation command."),
                    ReadActorValue: (reference, name, kind) =>
                    {
                        RequirePlayer(reference);
                        return owner.IsSkill(name) ? owner.ReadSkill(name, kind) : values.Read(FalloutPlayerActorValues.SpecialValue(name), kind);
                    }, ChangeActorValue: (reference, name, operation, value) =>
                    {
                        RequirePlayer(reference);
                        if (owner.IsSkill(name)) owner.ChangeSkill(name, operation, value); else values.Change(name, operation, value);
                    }));
            lifetime.BindExecutor(executor.ExecuteActiveEffect);
            owner.BindAbilityScripts(lifetime);
            values.BindConstantModifiers(owner.Modifiers);
            values.BindAbilityLifecycle(lifetime.Synchronize);
            return lifetime;
        }
        void RequirePlayer(FalloutFormKey reference)
        {
            if (records.RuntimeFormId(reference) != FalloutPlayerActorValues.PlayerReference)
                throw new NotSupportedException("Selected effect requires a separate actor value owner.");
        }
    }
}
