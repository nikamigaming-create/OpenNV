using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Content.Definitions;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static class OwnedMutableScriptStateProbe
{
    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(root), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var globals = FalloutGlobalState.Read(records);
        var values = new FalloutPlayerActorValues(records);
        var source = FalloutActorAiData.Read(records, values.Source.Player);
        var inventory = world.Inventory(records.RuntimeFormKey(FalloutEngineForms.PlayerReference), values.Source.Level, globals).Contents;
        var race = FalloutDialogueTopic.RequiredForm(records.GetEffective(values.Source.Player), "RNAM");
        var skills = new FalloutPlayerSkills(records, () => values.BaseSpecial, _ => false, () => [], globals, inventory,
            values.Source.Player, () => race, () => false, actorValues: values);
        values.BindConstantModifiers(skills.Modifiers);
        foreach (var id in new[] { FalloutActorValueId.Aggression, FalloutActorValueId.Confidence, FalloutActorValueId.Energy,
            FalloutActorValueId.Responsibility, FalloutActorValueId.Mood, FalloutActorValueId.Assistance })
        {
            if (values.ReadBase((int)id) != source.Value(id))
                throw new InvalidDataException("Owned player AI-data pool differs from its winning template group.");
            _ = values.ReadCurrent((int)id); // Execute the ordinary constant-effect owner as well.
        }
        var quests = new FalloutQuestState(records);
        // This source startup executes list additions before its separate essential-state boundary.
        // Lonesome Road reaches an unowned leveled-list mutation before its FormList prefix.
        var selected = new HashSet<FalloutFormKey> { new("GunRunnersArsenal.esm", 0x93c) };
        var scripts = new FalloutQuestScripts(records, quests, selected, inventory, globals, defaultProcessingDelay: 0, references: world);
        var host = new FalloutQuestScriptHost((_, _) => throw new NotSupportedException("Owned startup selected an unprovided stage-result host."),
            name => Read(name, FalloutActorValueRead.Current), ReadPlayerActorValue: Read, ChangePlayerActorValue: values.Change,
            Inventory: new(records, world, inventory, () => values.Source.Level, globals));
        var boundaries = new List<string>();
        foreach (var quest in selected)
        {
            var before = world.FormLists.Revision;
            try { scripts.AdvanceClaimed(quest, 0, host); }
            catch (NotSupportedException error) when (error.Message.StartsWith("Reached script command SetEssential ", StringComparison.Ordinal))
            {
                boundaries.Add($"{quest}:SetEssential"); // Separate actor-base flag owner; no default or continuation.
            }
            var state = scripts.Capture().Instances.Single(instance => instance.Quest == quest);
            if (world.FormLists.Revision == before || state.Error is not null && !boundaries.Any(value => value.StartsWith(quest + ":", StringComparison.Ordinal)))
                throw new InvalidDataException($"Owned startup {quest} did not execute its list prefix: {state.Error}");
        }
        var saved = JsonSerializer.Deserialize<FalloutFormListsSnapshot>(JsonSerializer.Serialize(world.FormLists.Capture()))!;
        if (saved.Lists.Count == 0 || saved.Lists.All(list => list.Added.Count == 0))
            throw new InvalidDataException("Owned startup did not execute its authored persistent list effects.");
        using var restored = new FalloutReferenceWorld(records);
        restored.FormLists.Restore(saved);
        foreach (var list in saved.Lists)
            if (!restored.FormLists.Entries(list.Form).SequenceEqual(world.FormLists.Entries(list.Form)))
                throw new InvalidDataException("Owned startup list order changed on cold restore.");
        var playerReference = records.RuntimeFormKey(FalloutEngineForms.PlayerReference);
        var playerInSource = records.TryGetEffective(playerReference, out _);
        var isolated = new FalloutFormListState(records);
        isolated.Add(saved.Lists[0].Form, playerReference);
        var playerSaved = JsonSerializer.Deserialize<FalloutFormListsSnapshot>(JsonSerializer.Serialize(isolated.Capture()))!;
        var playerRestored = new FalloutFormListState(records); playerRestored.Restore(playerSaved);
        if (!playerRestored.Entries(saved.Lists[0].Form).Contains(playerReference))
            throw new InvalidDataException("Owned engine player identity did not survive list persistence.");
        Console.WriteLine($"OPENNV_OWNED_MUTABLE_SCRIPT_STATE_PASS startupPrefixes={selected.Count} completedQuests={selected.Count - boundaries.Count} changedLists={saved.Lists.Count} " +
            $"addedForms={saved.Lists.Sum(list => list.Added.Count)} playerAiValues=6 sourceOwner={source.Owner} coldLists=true " +
            $"playerInSource={playerInSource} enginePlayerRoundtrip=true boundaries={string.Join(',', boundaries)} compiledVm=unimplemented gameplay=unverified");

        double Read(string name, FalloutActorValueRead kind) => FalloutPlayerActorValues.HasPool(name)
            ? values.Read(FalloutPlayerActorValues.PoolValue(name), kind)
            : skills.IsSkill(name) ? skills.ReadSkill(name, kind)
            : throw new NotSupportedException($"Owned startup requires the actual {name} value owner.");
    }
}
