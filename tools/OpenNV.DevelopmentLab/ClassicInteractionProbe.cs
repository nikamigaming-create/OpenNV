using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

internal static class ClassicInteractionProbe
{
    internal static int Run(string install, string campaign, string[] maps)
    {
        using IFalloutClassicOwnedSource source = campaign == "fallout-1"
            ? Fallout1OwnedContentSource.LoadInstall(install) : Fo2NativeOwnedSource.LoadInstall(install);
        using var catalog = new ClassicMapCatalog(source, campaign);
        var first = ClassicPremadeReader.Load(campaign, path => catalog.Read(path, out _))[0];
        var choice = new ClassicCharacterDraft(ClassicCharacterDraft.CurrentSchema, campaign, catalog.ProfileId,
            first.Id, first.GcdSha256, first.BiographySha256, first.PortraitSha256, first.Character, null);
        var temporary = Path.Combine(Path.GetTempPath(), "opennv-door-" + Guid.NewGuid().ToString("N") + ".json");
        var tested = 0;
        var all = maps is ["--all"];
        if (!all) ClassicMessageProbe.Run();
        if (all) maps = catalog.Maps.ToArray();
        var admitted = 0; var scriptFailures = 0; var empty = 0; var errors = new List<string>(); var descriptions = 0;
        try
        {
            if (campaign == "fallout-1" && !all)
            {
                var sourcePlayer = new ClassicPlayerSession(catalog, catalog.StartingMap, choice);
                sourcePlayer.Save(temporary);
                var current = JsonSerializer.Deserialize<ClassicPlayerSave>(File.ReadAllBytes(temporary))!;
                var oldHash = ClassicPremadeReader.Hash(catalog.ReadForNativeSaveRecovery(current.MapPath));
                var prior = current with { Schema = "opennv-classic-player/v3", MapSha256 = oldHash, Doors = null };
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(prior));
                var recovered = ClassicPlayerSession.Restore(catalog, temporary);
                if (recovered.Tile != current.Tile || recovered.HitPoints != current.HitPoints || recovered.CompletedSteps != current.CompletedSteps)
                    throw new InvalidOperationException("Verified archive correction lost player state.");
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(prior with { MapSha256 = new string('0', 64) }));
                try { _ = ClassicPlayerSession.Restore(catalog, temporary); throw new InvalidOperationException("Unknown historical source hash was accepted."); }
                catch (InvalidDataException) { }
                Console.WriteLine("PASS verified old-decoder save recovery retains player state and rejects arbitrary source hashes.");
            }
            foreach (var path in maps.Length == 0 ? [campaign == "fallout-1" ? "maps/v13ent.map" : "maps/arcaves.map"] : maps)
            {
                var level = catalog.Load(path);
                foreach (var elevation in level.Map.Elevations.Keys)
                {
                    var navigation = new ClassicMapNavigation(level.Map, level.Objects, elevation);
                    if (navigation.Walkable.Count == 0)
                    {
                        if (level.Objects.TopLevelObjects.Any(row => row.Elevation == elevation && ClassicDoorWorld.IsDoor(row)))
                            errors.Add(path + ":" + elevation + " has doors but no walkable source floor.");
                        empty++; continue;
                    }
                    var fixtureTile = navigation.Walkable.Order().First();
                    ClassicPlayerSession inspection;
                    try { inspection = new ClassicPlayerSession(catalog, path, choice, elevation, fixtureTile); }
                    catch (Exception error) when (all) { errors.Add(path + ":" + elevation + " " + error.Message); continue; }
                    admitted += inspection.Doors.Serials.Count;
                    scriptFailures += inspection.Doors.Failures.Count;
                    if (all) continue;
                    foreach (var serial in inspection.Doors.Serials)
                    {
                        var definition = inspection.Doors.Definition(serial);
                        Console.WriteLine(JsonSerializer.Serialize(new
                        {
                            path,
                            elevation,
                            serial,
                            definition.Script,
                            definition.Frames,
                            definition.ActionFrame,
                            definition.Fps,
                            definition.CanUse,
                            pose = inspection.Doors.Pose(serial)
                        }));
                        var adjacent = ClassicHexGrid.Neighbors(definition.Object.Tile).FirstOrDefault(navigation.Walkable.Contains, -1);
                        if (adjacent < 0) continue;
                        var player = new ClassicPlayerSession(catalog, path, choice, elevation, adjacent);
                        _ = player.Doors.TakeMessages();
                        try
                        {
                            player.Doors.Examine(serial);
                            var messages = player.Doors.TakeMessages();
                            if (messages.Length == 0) throw new InvalidOperationException("Door examination produced no source text.");
                            descriptions++;
                            Console.WriteLine(JsonSerializer.Serialize(new { examine = serial, messages }));
                        }
                        catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or FileNotFoundException)
                        { Console.WriteLine($"UNBOUND EXAMINE {definition.Script}:{serial}: {error.Message}"); }
                        if (definition.Program is not null)
                        {
                            _ = player.Doors.TakeMessages();
                            var snapshot = JsonSerializer.Serialize(player.Doors.Save());
                            try
                            {
                                player.Doors.Use(serial);
                                Console.WriteLine(JsonSerializer.Serialize(new { scriptUse = definition.Script, serial, messages = player.Doors.TakeMessages(), pose = player.Doors.Pose(serial) }));
                            }
                            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or FileNotFoundException)
                            {
                                if (error is NotSupportedException or InvalidDataException || error.Message.StartsWith("Classic INT", StringComparison.Ordinal))
                                    if (snapshot != JsonSerializer.Serialize(player.Doors.Save()) || player.Doors.TakeMessages().Length != 0)
                                        throw new InvalidOperationException("Rejected door script published partial state or messages.");
                                Console.WriteLine($"UNBOUND USE {definition.Script}:{serial}: {error.Message}");
                            }
                            continue;
                        }
                        if (!definition.CanUse) continue;
                        var before = player.Doors.Pose(serial);
                        player.Doors.Use(serial); player.Doors.Advance(0.5 / definition.Fps);
                        var partial = player.Doors.Pose(serial); player.Save(temporary);
                        var cold = ClassicPlayerSession.Restore(catalog, temporary);
                        if (cold.Doors.Pose(serial) != partial) throw new InvalidOperationException("Mid-animation door state did not survive reload.");
                        cold.Doors.Advance(definition.Frames / (double)definition.Fps);
                        var terminal = cold.Doors.Pose(serial);
                        if (terminal.Open == before.Open || terminal.Direction != 0 || terminal.Frame != (terminal.Open ? definition.Frames - 1 : 0))
                            throw new InvalidOperationException("Door did not reach its source terminal frame.");
                        if (terminal.Open && navigation.FloorBacked.Contains(definition.Object.Tile) &&
                            !level.Objects.TopLevelObjects.Any(row => row.Serial != serial && row.Elevation == elevation &&
                                row.Tile == definition.Object.Tile && row.Prototype.ObjectType is 1 or 2 or 3 && (row.Flags & 0x11) == 0) &&
                            !cold.Walkable.Contains(definition.Object.Tile)) throw new InvalidOperationException("Open door still blocks its source hex.");
                        cold.Doors.Use(serial); cold.Doors.Advance(definition.Frames / (double)definition.Fps); cold.Save(temporary);
                        var closed = ClassicPlayerSession.Restore(catalog, temporary);
                        if (closed.Doors.Pose(serial).Open != before.Open) throw new InvalidOperationException("Close/open reversal did not persist.");
                        var saved = JsonSerializer.Deserialize<ClassicPlayerSave>(File.ReadAllBytes(temporary))!;
                        var changed = saved.Doors!.Doors.Select(row => row.Serial == serial && row.Map == path ? row with { Identity = new string('0', 64) } : row).ToArray();
                        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(saved with { Doors = saved.Doors with { Doors = changed } }));
                        try { _ = ClassicPlayerSession.Restore(catalog, temporary); throw new InvalidOperationException("Changed source door accepted."); }
                        catch (InvalidDataException) { }
                        tested++;
                    }
                    foreach (var failure in inspection.Doors.Failures) Console.WriteLine("UNBOUND " + failure);
                }
            }
            if (all)
            {
                foreach (var error in errors) Console.WriteLine("FAILED " + error);
                Console.WriteLine($"Door source admission: {campaign} maps={maps.Length} doors={admitted} emptyElevations={empty} mapFailures={errors.Count} unboundScriptInitializers={scriptFailures}; this is not script or campaign completion.");
                return errors.Count == 0 ? 0 : 1;
            }
            if (tested == 0) throw new InvalidOperationException("No ordinary source doors were exercised.");
            if (descriptions == 0) throw new InvalidOperationException("No source door descriptions were exercised.");
            Console.WriteLine($"PASS {campaign}: {tested} owned door open/close clocks, collision, cold/mid-animation restore and source-change rejection. Fixture placement; not campaign travel.");
            return 0;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
