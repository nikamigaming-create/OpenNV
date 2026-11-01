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
        // Exercise winning startup scripts through their shared mutation owners.
        var selected = new HashSet<FalloutFormKey> { new("GunRunnersArsenal.esm", 0x93c), new("LonesomeRoad.esm", 0x3603) };
        // Zero globally disables recurrence timing and keeps GetSecondsPassed at
        // zero; this fixture must advance the source startup timer naturally.
        var scripts = new FalloutQuestScripts(records, quests, selected, inventory, globals, defaultProcessingDelay: .5f, references: world);
        var inventoryCommands = new FalloutInventoryCommands(records, world, inventory, () => values.Source.Level, globals);
        var exteriorCell = new FalloutFormKey("FalloutNV.esm", 0x846ea);
        if (FalloutCellSceneReader.ReadDefinition(records, exteriorCell).Worldspace is null)
            throw new InvalidDataException("Owned startup fixture has no source exterior cell.");
        bool InCell(FalloutFormKey reference, FalloutFormKey cell) => world.IsInCell(reference, cell, exteriorCell);
        FalloutQuestStages? stages = null;
        var referenceScripts = new FalloutReferenceScripts(records, world, quests,
            new((_, _) => false, effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.SetStage) stages!.Enter(effect.Target!.Value, effect.Stage);
                else if (effect.Kind == FalloutReferenceEffectKind.Message)
                {
                    // Match the production effect dispatcher: source script and
                    // reference caller qualify MESG formatting/conditions and
                    // the ordinary persistent notification/message owner.
                    var owner = records.GetEffective(effect.Source);
                    var caller = owner.Signature == "QUST" ? FalloutScriptLocals.AttachedScript(records, owner)?.FormKey :
                        world.Get(effect.Source).Script?.Record.FormKey;
                    scripts.ShowMessage(effect.Target ?? throw new InvalidDataException("Owned startup ShowMessage target is absent."),
                        caller, owner.Signature == "QUST" ? null : effect.Source);
                }
                else throw new NotSupportedException($"Owned startup selected an unprovided {effect.Kind} effect host.");
            }, Globals: globals, PlayerLevel: () => values.Source.Level, Inventory: inventoryCommands, IsInCell: InCell));
        stages = new(records, quests, referenceScripts.StageSteps, quests.Evaluate);
        var host = new FalloutQuestScriptHost((quest, stage) => () => stages!.Enter(quest, stage),
            name => Read(name, FalloutActorValueRead.Current), ExecuteProgram: referenceScripts.ExecuteProgram,
            ReadPlayerActorValue: Read, ChangePlayerActorValue: values.Change, Inventory: inventoryCommands, IsInCell: InCell);
        foreach (var quest in selected)
        {
            var before = world.FormLists.Revision;
            scripts.AdvanceClaimed(quest, 0, host);
            var state = scripts.Capture().Instances.Single(instance => instance.Quest == quest);
            if (world.FormLists.Revision == before || state.Error is not null || state.Executions == 0)
                throw new InvalidDataException($"Owned startup {quest} did not execute its list prefix: {state.Error}");
        }
        var startup = new FalloutFormKey("LonesomeRoad.esm", 0x3603);
        for (var index = 0; index < 16 && quests.Stage(startup) < 10; ++index) scripts.AdvanceClaimed(startup, .5, host);
        var leveled = JsonSerializer.Deserialize<FalloutLeveledItemsSnapshot>(JsonSerializer.Serialize(world.LeveledItems.Capture()))!;
        if (quests.Stage(startup) != 10 || leveled.Lists.Sum(list => list.Added.Count) != 4 || stages.HasUnfinishedResults ||
            !world.MapMarker(new("FalloutNV.esm", 0x157f7e)).Visible)
            throw new InvalidDataException($"Owned startup did not complete its source leveled mutations and map reveal: " +
                $"stage={quests.Stage(startup)}, added={leveled.Lists.Sum(list => list.Added.Count)}, " +
                $"unfinishedResults={stages.HasUnfinishedResults}, markerVisible={world.MapMarker(new("FalloutNV.esm", 0x157f7e)).Visible}; " +
                $"stageErrors={JsonSerializer.Serialize(stages.Errors)}, " +
                $"scripts={JsonSerializer.Serialize(scripts.Capture().Instances.Where(instance => selected.Contains(instance.Quest)))}.");
        var savedFlags = JsonSerializer.Deserialize<FalloutActorBaseFlagsSnapshot>(JsonSerializer.Serialize(world.ActorBaseFlags.Capture()))!;
        if (savedFlags.Changes.Count == 0)
            throw new InvalidDataException("Owned startup did not execute its authored essential-flag writes.");
        var saved = JsonSerializer.Deserialize<FalloutFormListsSnapshot>(JsonSerializer.Serialize(world.FormLists.Capture()))!;
        if (saved.Lists.Count == 0 || saved.Lists.All(list => list.Added.Count == 0))
            throw new InvalidDataException("Owned startup did not execute its authored persistent list effects.");
        using var restored = new FalloutReferenceWorld(records);
        restored.FormLists.Restore(saved);
        restored.ActorBaseFlags.Restore(savedFlags);
        restored.LeveledItems.Restore(leveled);
        if (JsonSerializer.Serialize(restored.LeveledItems.Capture()) != JsonSerializer.Serialize(leveled))
            throw new InvalidDataException("Owned scripted leveled entries changed on cold restore.");
        if (!restored.ActorBaseFlags.Capture().Changes.SequenceEqual(savedFlags.Changes))
            throw new InvalidDataException("Owned essential flags changed on cold restore.");
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
        Console.WriteLine($"OPENNV_OWNED_MUTABLE_SCRIPT_STATE_PASS startupInvocations={selected.Count} completedInvocations={selected.Count} changedLists={saved.Lists.Count} " +
            $"addedForms={saved.Lists.Sum(list => list.Added.Count)} playerAiValues=6 sourceOwner={source.Owner} coldLists=true " +
            $"playerInSource={playerInSource} enginePlayerRoundtrip=true essentialBases={savedFlags.Changes.Count} coldEssentialFlags=true " +
            $"leveledEntries={leveled.Lists.Sum(list => list.Added.Count)} sourceMapReveal=true coldLeveled=true compiledVm=unimplemented gameplay=unverified");

        double Read(string name, FalloutActorValueRead kind) => FalloutPlayerActorValues.HasPool(name)
            ? values.Read(FalloutPlayerActorValues.PoolValue(name), kind)
            : skills.IsSkill(name) ? skills.ReadSkill(name, kind)
            : throw new NotSupportedException($"Owned startup requires the actual {name} value owner.");
    }
}
