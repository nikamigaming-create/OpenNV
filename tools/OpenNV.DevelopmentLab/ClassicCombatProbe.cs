using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Fallout2.Native;

internal static class ClassicCombatProbe
{
    internal static int MapSpace(string install, string campaign, string output)
    {
        using IFalloutClassicOwnedSource source = campaign == "fallout-2" ? Fo2NativeOwnedSource.LoadInstall(install) :
            OpenNV.Runtime.Content.Fallout1OwnedContentSource.LoadInstall(install);
        using var catalog = new ClassicMapCatalog(source, campaign);
        var summaries = new List<object>();
        foreach (var path in catalog.Maps)
        {
            try
            {
                var map = OpenNV.Runtime.Content.Fallout1NativeMapReader.Read(catalog.Read(path, out _));
                summaries.Add(new { path, map.MapIndex, map.EnteringTile, map.EnteringElevation,
                    elevations = map.Elevations.Select(pair => new { elevation = pair.Key, squares = pair.Value.Length,
                        empty = pair.Value.Count(value => (value & 0xfff) == 1) }).ToArray() });
            }
            catch (Exception error) { summaries.Add(new { path, error = error.Message }); }
        }
        var level = catalog.Load(catalog.StartingMap);
        var names = OpenNV.Runtime.Content.Fallout1NativeLists.Read(catalog.Read("art/tiles/tiles.lst", out _));
        var palette = catalog.Read("color.pal", out _);
        var tileArt = level.Map.Elevations.Values.SelectMany(values => values).Select(value => (int)(value & 0xfff)).Distinct().Order()
            .Where(id => id != 1).Select(id =>
            {
                var frame = OpenNV.Runtime.Content.Fallout1NativeFrmReader.ReadFrame(catalog.Read("art/tiles/" + names[id], out _), 0, 0);
                return new { id, art = names[id], frame.Width, frame.Height,
                    transparent = frame.PaletteIndexes.Count(index => index == 0),
                    opaqueBlack = frame.PaletteIndexes.Count(index => index != 0 && palette[index * 3] == 0 && palette[index * 3 + 1] == 0 && palette[index * 3 + 2] == 0) };
            }).ToArray();
        var floors = level.Map.Elevations.Select(pair =>
        {
            var navigation = new ClassicMapNavigation(level.Map, level.Objects, pair.Key);
            return new { elevation = pair.Key, floorIds = pair.Value.Select(value => (int)(value & 0xfff)).ToArray(),
                blocked = navigation.Blocked.Order().ToArray(), walkable = navigation.Walkable.Order().ToArray(),
                floorBacked = navigation.FloorBacked.Order().ToArray() };
        }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(new { campaign, profile = catalog.ProfileId,
            selectedMap = level.Path, level.Sha256, floors, tileNames = names, tileArt,
            objects = level.Objects.TopLevelObjects.Select(row => new { row.Serial, row.Pid, row.Fid, row.Tile, row.Elevation,
                row.Rotation, row.Flags, row.ScriptId, row.Prototype.ObjectType, row.Prototype.LogicalPath,
                world = row.Tile < 0 ? null : new[] { OpenNV.Runtime.Campaigns.Fallout1.Fo1HexMath.Center(row.Tile).X, 0f,
                    OpenNV.Runtime.Campaigns.Fallout1.Fo1HexMath.Center(row.Tile).Z } }).ToArray(), catalog = summaries }));
        Console.WriteLine($"OPENNV_CLASSIC_MAP_SPACE campaign={campaign} maps={summaries.Count} selected={level.Path} nativeAcceptance=false");
        return 0;
    }

    internal static int Run(string install)
    {
        using var source = Fo2NativeOwnedSource.LoadInstall(install);
        using var catalog = new ClassicMapCatalog(source, "fallout-2");
        using var randomJson = JsonDocument.Parse(File.ReadAllBytes("runtime/config/classic-retail-random-fo2-1.02-v1.json"));
        var randomContract = ClassicRetailRandomContract.Parse(randomJson.RootElement);
        var first = OpenNV.Runtime.Content.ClassicPremadeReader.Load(catalog.Campaign, path => catalog.Read(path, out _))[0];
        var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, catalog.Campaign, catalog.ProfileId,
            first.Id, first.GcdSha256, first.BiographySha256, first.PortraitSha256, first.Character, null);
        var started = ClassicPlayerSession.Begin(catalog, choice, randomContract);
        Console.WriteLine(JsonSerializer.Serialize(new { entry = started.MapPath, actorFailures = started.Actors.Failures.ToArray() }));
        foreach (var path in new[] { "maps/artemple.map", "maps/arcaves.map", "maps/arbridge.map" })
        {
            var level = catalog.Load(path);
            if (path != started.MapPath)
            {
                var navigation = new ClassicMapNavigation(level.Map, level.Objects, level.Map.EnteringElevation);
                var entry = new ClassicPlayerSession(catalog, path, choice, tile: navigation.Walkable.Order().First(),
                    initialization: started.Initialization, randomContract: randomContract, savedRandom: started.CampaignRandom, freshCampaign: true);
                Console.WriteLine(JsonSerializer.Serialize(new { inspectedEntry = path, actorFailures = entry.Actors.Failures.ToArray(), traits = entry.Actors.Save().Traits }));
            }
            var names = OpenNV.Runtime.Content.Fallout1NativeLists.Read(catalog.Read("scripts/scripts.lst", out _));
            foreach (var group in level.Objects.TopLevelObjects.Where(row => row.Prototype.ObjectType == 1).GroupBy(row =>
                (row.Elevation, row.Pid, row.ScriptId, row.Prototype.LogicalPath)))
            {
                var row = group.First(); var bytes = catalog.Read(row.Prototype.LogicalPath!, out _);
                int Word(int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
                Console.WriteLine(JsonSerializer.Serialize(new { path, group.Key.Elevation, group.Key.Pid,
                    group.Key.ScriptId, script = row.ScriptId == uint.MaxValue ? null : names[level.Map.LiveScripts.Single(slot => slot.ScriptId == row.ScriptId).ScriptProgramIndex], prototypeScript = Word(28), flags = Word(44), count = group.Count(),
                    ai = row.InstanceValues[5], team = row.InstanceValues[6], hp = row.InstanceValues[8], maximumAp = Word(80) + Word(220),
                    intelligence = Word(64) + Word(204), firstSerial = row.Serial, firstTile = row.Tile,
                    inventory = row.Inventory.Count, sourceFid = row.Fid, instance = row.InstanceValues }));
            }
        }
        Console.WriteLine(System.Text.Encoding.Latin1.GetString(catalog.Read("data/ai.txt", out _)));
        return 0;
    }
    // Explicit in-memory synthetic variation of owned reader inputs. This is
    // a transaction test, never a native campaign/ordinary encounter claim.
    internal static int Contract(string install)
    {
        if (ClassicCritterInstance.ScriptState(0, 10, 0) != 0 ||
            ClassicCritterInstance.ScriptState(4, 10, 0) != 4 ||
            ClassicCritterInstance.ScriptState(0, 10, 48) != 2 ||
            ClassicCritterInstance.ScriptState(1, 10, 0) != 2 ||
            ClassicCritterInstance.ScriptState(0x8000, 10, 0) != 2 ||
            ClassicCritterInstance.ScriptState(0x80, 0, 48) != 1)
            throw new InvalidDataException("Critter script-state result/animation mapping changed.");
        using var source = Fo2NativeOwnedSource.LoadInstall(install);
        using var catalog = new ClassicMapCatalog(source, "fallout-2");
        var level = catalog.Load("maps/arcaves.map");
        var target = level.Objects.TopLevelObjects.First(row => row.Prototype.ObjectType == 1 && row.Inventory.Count == 0 && row.InstanceValues[8] == 6);
        var map = (byte[])catalog.Read(level.Path, out _).Clone();
        foreach (var row in level.Objects.TopLevelObjects.Where(row => row.Prototype.ObjectType == 1))
        {
            if (row.BaseLayout != "full-21") throw new NotSupportedException("Fixture requires the qualified full MAP layout.");
            BinaryPrimitives.WriteInt32BigEndian(map.AsSpan(row.SourceOffset + 16 * 4), -1);
            if (row.Serial != target.Serial) BinaryPrimitives.WriteUInt32BigEndian(map.AsSpan(row.SourceOffset + 9 * 4), row.Flags | 1);
        }
        var pro = (byte[])catalog.Read(target.Prototype.LogicalPath!, out _).Clone();
        BinaryPrimitives.WriteInt32BigEndian(pro.AsSpan(28), -1);
        using var variation = new ClassicMapCatalog(new Variation(source, level.Path, map, target.Prototype.LogicalPath!, pro), "fallout-2");
        T Load<T>(string name, Func<JsonElement, T> parse)
        { using var json = JsonDocument.Parse(File.ReadAllBytes("runtime/config/" + name)); return parse(json.RootElement); }
        var random = Load("classic-retail-random-fo2-1.02-v1.json", ClassicRetailRandomContract.Parse);
        var modes = Load("classic-attack-modes-fo2-1.02-v1.json", ClassicAttackModeCatalog.Parse);
        var execution = new ClassicCombatContracts(Load("classic-skill-fo2-1.02-v1.json", ClassicSkillContract.Parse),
            Load("classic-critical-selection-fo2-1.02-v1.json", ClassicCriticalSelectionContract.Parse),
            Load("classic-retail-damage-fo2-1.02-v1.json", ClassicRetailDamageContract.Parse));
        var first = OpenNV.Runtime.Content.ClassicPremadeReader.Load(catalog.Campaign, path => catalog.Read(path, out _))[0];
        var profile = first.Character with { Traits = [], Special = [8, 8, 5, 4, 4, 6, 5], TaggedSkills = [3, 4, 5] };
        var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, "fallout-2", source.ProfileId,
            null, null, null, null, profile, null);
        var start = ClassicCampaignInitialization.Execute(variation, choice, random);
        var front = ClassicHexGrid.Neighbor(target.Tile, target.Rotation);
        var initial = start with { Map = level.Path, Light = 100, Items = [] };
        var player = new ClassicPlayerSession(variation, level.Path, choice, target.Elevation, front,
            initialization: initial, randomContract: random, combatModes: modes, combatContracts: execution, freshCampaign: true);
        var art = new ClassicArtCache(path => variation.Read(path, out _));
        void Bind(ClassicPlayerSession state) => state.Combat.BindActionArt(() => "art/critters/hmwarraa.frm", fid => art.Critter(fid, 0).Path);
        Bind(player);
        var before = player.HitPoints;
        var request = player.Combat.RequestPlayerAttack(target.Serial);
        if (player.Combat.PendingAction is not { } action) throw new InvalidOperationException(request);
        player.Combat.AdvanceAction(action.Duration);
        if (player.Combat.Phase != ClassicCombatPhase.PlayerTurn) throw new InvalidOperationException(player.Combat.Failure);
        player.Combat.EndTurn();
        var safety = 0;
        while (player.Combat.Phase == ClassicCombatPhase.AttackPending && safety++ < 12)
            player.Combat.AdvanceAction(player.Combat.PendingAction!.Duration);
        if (player.Combat.Phase != ClassicCombatPhase.PlayerTurn || player.Combat.Save().Actions!.All(row => row.ActorHandle == int.MaxValue))
            throw new InvalidOperationException("Enemy response did not complete: " + player.Combat.Failure);
        for (var attempts = 0; player.Combat.Active == true && attempts < 50; attempts++)
        {
            if (player.Combat.PlayerActionPoints < 3) player.Combat.EndTurn();
            else player.Combat.RequestPlayerAttack(target.Serial);
            while (player.Combat.Phase == ClassicCombatPhase.AttackPending)
                player.Combat.AdvanceAction(player.Combat.PendingAction!.Duration);
        }
        var state = player.Combat.Save();
        if (player.Combat.Phase != ClassicCombatPhase.Exploration || !player.Combat.Dead(target.Serial) ||
            state.Deaths is not [{ }] || player.Experience <= 0 || player.Walkable.Contains(target.Tile) == false || player.HitPoints > before)
            throw new InvalidOperationException("Lethal consequences/exit did not complete: " + player.Combat.Failure);
        var restored = new ClassicCombatEncounter(player, modes, false, state, execution);
        restored.BindActionArt(() => "art/critters/hmwarraa.frm", fid => art.Critter(fid, 0).Path);
        if (JsonSerializer.Serialize(restored.Save()) != JsonSerializer.Serialize(state)) throw new InvalidOperationException("Restored combat prefix changed.");
        Console.WriteLine($"OPENNV_CLASSIC_COMBAT_TRANSACTION_PASS syntheticOwnedVariation=true ordinaryCampaign=false enemyActions={state.Actions!.Count(row => row.ActorHandle != int.MaxValue)} hp={player.HitPoints} xp={player.Experience} death=true exit=true");
        return 0;
    }

    private sealed class Variation(IFalloutClassicOwnedSource source, string mapPath, byte[] map, string proPath, byte[] pro) : IFalloutClassicOwnedSource
    {
        public string ProfileId => source.ProfileId;
        public byte[] Read(string path, out int index)
        {
            var canonical = ClassicMapCatalog.Canonical(path);
            if (canonical == mapPath) { index = -1; return map; }
            if (canonical == proPath) { index = -1; return pro; }
            return source.Read(path, out index);
        }
        public IReadOnlyList<string> EffectiveLogicalPaths(string prefix, string extension) => source.EffectiveLogicalPaths(prefix, extension);
        public void Dispose() { }
    }

}
