using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class OwnedBottleWeaponProbe
{
    internal static void Run(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        FalloutFormKey Key(uint id) => new("FalloutNV.esm", id);
        var source = records.GetEffective(Key(0x10a1ef));
        var targets = new uint[] { 0x10a209, 0x10a208, 0x10a207 }.Select(Key).ToArray();
        var placed = targets.Select(records.GetEffective).ToArray();
        var targetBase = records.GetEffective(Key(0x10a1f6));
        if (source.Signature != "SCPT" || FalloutDialogueTopic.RequiredForm(targetBase, "SCRI") != source.FormKey ||
            placed.Any(record => record.Signature != "REFR" || FalloutDialogueTopic.RequiredForm(record, "NAME") != targetBase.FormKey))
            throw new InvalidDataException("Owned bottle script or placed targets changed.");
        var bindings = new FalloutScriptBindings(records, placed[0], source, source.ReadSubrecords());
        var quest = bindings.Form("VCG02").FormKey;
        var count = bindings.Variable("VCG02.nTargetCount");
        var tutorial = bindings.Form("CGTutorial").FormKey;
        var sunny = bindings.Form("SunnyREF").FormKey;
        var reaction = bindings.Form("VCG02SunnyShotReaction").FormKey;
        var camera = bindings.Form("NVWeapMS22Camera").FormKey;
        var rifle = Key(0x7ea24);
        if (FalloutWeaponPresentation.ReadScriptAnimationType(records.GetEffective(rifle)) is not (> 3 and < 9))
            throw new InvalidDataException("Owned practice rifle no longer meets the source weapon-type predicate.");
        var hashes = placed.Concat([source, targetBase, records.GetEffective(camera), records.GetEffective(rifle)])
            .Select(record => (Record: record, Hash: SHA256.HashData(record.ReadData()))).ToArray();
        var cell = FalloutCellSceneReader.ParentCell(placed[0]) ?? throw new InvalidDataException("Owned bottle has no source cell.");
        if (placed.Any(record => FalloutCellSceneReader.ParentCell(record) != cell))
            throw new InvalidDataException("Selected source bottles no longer share a cell.");
        var scene = FalloutCellSceneReader.Read(records, cell);

        // These isolated owners test authored OnHitWith execution. They do not
        // reach the tutorial through gameplay or touch a live campaign/save.
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(scene);
        var quests = new FalloutQuestState(records);
        quests.SetRunning(quest, true);
        var player = new FalloutPlayerInventory();
        player.Add(records, rifle, 1, 1, true);
        player.Add(records, camera, 1, 1, true);
        var handling = new FalloutWeaponHandling(player);
        var effects = new List<FalloutReferenceScriptEffect>();
        var scripts = Scripts(world, quests, player, handling, effects);
        player.Equip(records, rifle);
        handling.SetDrawn(false);
        Hit(world, scripts, targets[0]);
        Check(quests.Variable(quest, count.Index) == 0 && effects.Count == 0, "Holstered weapon passed the owned bottle guard.");
        handling.SetDrawn(true);
        player.Equip(records, camera);
        Hit(world, scripts, targets[0]);
        Check(quests.Variable(quest, count.Index) == 0 && effects.Count == 0, "Equipped camera passed the owned bottle guard.");
        player.Equip(records, rifle);
        Hit(world, scripts, targets[0]);
        Check(quests.Variable(quest, count.Index) == 1 && effects.Count == 2, "Drawn rifle did not execute the source bottle result.");
        Hit(world, scripts, targets[0]);
        Check(quests.Variable(quest, count.Index) == 1 && effects.Count == 2, "A second hit counted the same source bottle twice.");

        var savedReferences = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        var savedQuests = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!;
        var savedInventory = player.Capture();
        var savedHandling = handling.Capture();
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(savedReferences);
        cold.LoadCell(scene);
        var coldQuests = new FalloutQuestState(records);
        coldQuests.Restore(savedQuests);
        var coldPlayer = new FalloutPlayerInventory();
        coldPlayer.Restore(savedInventory.Inventory, savedInventory.EquippedRuntimeFormIds.ToArray(), savedInventory.InventoryRandomState);
        var coldHandling = new FalloutWeaponHandling(coldPlayer);
        coldHandling.Restore(savedHandling, _ => throw new InvalidOperationException("Fixture has no magazine state."));
        var coldEffects = new List<FalloutReferenceScriptEffect>(effects);
        var coldScripts = Scripts(cold, coldQuests, coldPlayer, coldHandling, coldEffects);
        foreach (var target in targets.Skip(1))
        {
            Hit(world, scripts, target);
            Hit(cold, coldScripts, target);
        }
        Check(quests.Variable(quest, count.Index) == 3 && quests.Objective(quest, 10).Completed &&
            effects.Count == 7 && effects.SequenceEqual(coldEffects) &&
            JsonSerializer.Serialize(quests.Capture()) == JsonSerializer.Serialize(coldQuests.Capture()) &&
            JsonSerializer.Serialize(world.Capture()) == JsonSerializer.Serialize(cold.Capture()),
            "Three authored bottle hits lost their result, effect order or cold continuation.");
        Check(hashes.All(pair => pair.Hash.SequenceEqual(SHA256.HashData(pair.Record.ReadData()))), "Owned source bytes changed.");
        Console.WriteLine($"OPENNV_OWNED_BOTTLE_WEAPON_PASS script={source.FormKey} rifle={rifle} targets=3 drawn=true holsteredGuard=true cameraGuard=true " +
            "countOnce=true objective10=true cold=true sourceReadonly=true fixture=isolated-source-script campaign=unverified parity=unverified");

        FalloutReferenceScripts Scripts(FalloutReferenceWorld owner, FalloutQuestState state, FalloutPlayerInventory inventory,
            FalloutWeaponHandling weaponHandling, List<FalloutReferenceScriptEffect> output)
        {
            var commands = new FalloutInventoryCommands(records, owner, inventory, () => 1, weaponDrawn: reference =>
                reference == records.RuntimeFormKey(0x14) ? weaponHandling.Drawn : throw new InvalidDataException("Bottle queried a non-player draw owner."));
            return new(records, owner, state, new((_, _) => false, effect =>
            {
                if (effect.Kind == FalloutReferenceEffectKind.SayTo)
                    Check(targets.Contains(effect.Source) && effect.Target == sunny && effect.Argument == records.RuntimeFormKey(0x14) &&
                        effect.Topic == reaction, "Bottle changed its source speech request.");
                else if (effect.Kind == FalloutReferenceEffectKind.SetStage)
                {
                    Check(effect.Target == tutorial && effect.Stage == 62, "Bottle changed its tutorial stage request.");
                    state.EnterStage(tutorial, 62);
                }
                else Check(effect.Kind == FalloutReferenceEffectKind.EvaluatePackages && targets.Contains(effect.Source) &&
                    effect.Target == sunny, "Bottle emitted an unexpected effect.");
                output.Add(effect);
            }, Inventory: commands));
        }

        void Hit(FalloutReferenceWorld owner, FalloutReferenceScripts executor, FalloutFormKey target)
        {
            owner.HitEvents.Mark(target, records.RuntimeFormKey(0x14), rifle, FalloutReferenceHitKind.Projectile);
            var batch = owner.HitEvents.SnapshotPending(target);
            var results = executor.DispatchFrame(target, batch.Events, 1.0 / 60);
            if (results.Any(result => result.Error is not null) || results.Sum(result => result.Blocks) != 1)
                throw new InvalidDataException("Owned bottle script failed: " + string.Join(';', results.Select(result => result.Error)));
            owner.HitEvents.Consume(batch);
        }
    }

    private static void Check(bool valid, string error) { if (!valid) throw new InvalidDataException(error); }
}
