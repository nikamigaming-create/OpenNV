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
        var timerCandidates = 0; var timed = 0;
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
                            var used = false;
                            var timerCandidate = campaign == "fallout-1" && definition.Script == "scripts/metldoor.int" &&
                                definition.CanUse && player.Doors.Pose(serial) is { Open: false, Locked: false };
                            if (timerCandidate) timerCandidates++;
                            try
                            {
                                player.Doors.Use(serial);
                                used = true;
                                Console.WriteLine(JsonSerializer.Serialize(new { scriptUse = definition.Script, serial, messages = player.Doors.TakeMessages(), pose = player.Doors.Pose(serial) }));
                            }
                            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or NotSupportedException or FileNotFoundException)
                            {
                                if (error is NotSupportedException or InvalidDataException || error.Message.StartsWith("Classic INT", StringComparison.Ordinal))
                                    if (snapshot != JsonSerializer.Serialize(player.Doors.Save()) || player.Doors.TakeMessages().Length != 0)
                                        throw new InvalidOperationException("Rejected door script published partial state or messages.");
                                Console.WriteLine($"UNBOUND USE {definition.Script}:{serial}: {error.Message}");
                            }
                            if (used && timerCandidate && player.Doors.Pose(serial).Open)
                            { CheckMetalDoorTimer(player, serial, temporary, catalog); timed++; }
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
            if (timed != timerCandidates) throw new InvalidOperationException($"Only {timed}/{timerCandidates} source metal-door timer fixtures passed.");
            Console.WriteLine($"Native scripted timer fixtures: passed={timed} candidates={timerCandidates}; fixture placement, not campaign travel.");
            Console.WriteLine($"PASS {campaign}: {tested} owned door open/close clocks, collision, cold/mid-animation restore and source-change rejection. Fixture placement; not campaign travel.");
            return 0;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void CheckMetalDoorTimer(ClassicPlayerSession player, int serial, string temporary, ClassicMapCatalog catalog)
    {
        var scheduled = player.Doors.Save().Clock!;
        var pending = scheduled.Timers.Pending.Single(row => row.TargetHandle == serial + 1);
        if (pending.DueTick - scheduled.Timers.CurrentTick != 30 || pending.FixedParameter != 1)
            throw new InvalidOperationException("Owned metal-door use did not schedule its source three-second callback.");
        player.Doors.Advance(.05); player.Save(temporary);
        var saved = JsonSerializer.Deserialize<ClassicPlayerSave>(File.ReadAllBytes(temporary))!;
        var cold = ClassicPlayerSession.Restore(catalog, temporary);
        if (JsonSerializer.Serialize(cold.Doors.Save().Clock) != JsonSerializer.Serialize(player.Doors.Save().Clock))
            throw new InvalidOperationException("Native script clock or queue changed across cold continuation.");
        cold.Doors.Advance(.05);
        while (cold.Doors.Save().Clock!.Timers.CurrentTick < pending.DueTick)
        {
            cold.Doors.Advance(.1);
            if (cold.Doors.Save().Clock!.Failure is { } failure) throw new InvalidOperationException(failure);
        }
        if (cold.Doors.Pose(serial).Open)
            throw new InvalidOperationException("Owned metal-door timed callback did not invoke source use and close.");
        var closing = cold.Doors.Save().Clock!;
        if (closing.Timers.Pending.Single(row => row.TargetHandle == serial + 1).DueTick != pending.DueTick + 30)
            throw new InvalidOperationException("Nested source use failed to schedule its next callback from dispatch time.");
        for (var tick = 0; tick < 30; tick++) cold.Doors.Advance(.1);
        if (cold.Doors.Save().Clock is not { Failure: null, Timers.Pending.Count: 0 } || cold.Doors.Pose(serial).Open)
            throw new InvalidOperationException("Closed source door left a timer or callback failure behind.");

        var legacy = saved with { Schema = "opennv-classic-player/v5", Doors = saved.Doors! with { ExternalOwners = null } };
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(legacy));
        var upgraded = ClassicPlayerSession.Restore(catalog, temporary).Doors.Save();
        if (JsonSerializer.Serialize(upgraded.Clock) != JsonSerializer.Serialize(saved.Doors!.Clock) ||
            JsonSerializer.Serialize(upgraded.Externals) != JsonSerializer.Serialize(saved.Doors.Externals) || upgraded.ExternalOwners!.Count != 0)
            throw new InvalidOperationException("Legacy v5 upgrade changed pending time/values or invented export ownership.");

        foreach (var invalid in new[]
        {
            saved with { Doors = saved.Doors! with { Clock = null } },
            saved with { Doors = saved.Doors! with { ExternalOwners = null } },
            saved with { Doors = saved.Doors! with { ExternalOwners = new Dictionary<string, string> { ["orphan"] = "unknown-module" } } },
            saved with { Doors = saved.Doors! with { Clock = saved.Doors.Clock! with { Map = "maps/changed.map" } } },
            saved with { Doors = saved.Doors! with { Clock = saved.Doors.Clock! with
                { Timers = saved.Doors.Clock.Timers with { Pending = [pending with { TargetHandle = int.MaxValue }] } } } }
        })
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(invalid));
            try { _ = ClassicPlayerSession.Restore(catalog, temporary); throw new Exception("Incomplete/misbound native timer save accepted."); }
            catch (InvalidDataException) { }
        }
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(saved));
        var cadence = ClassicPlayerSession.Restore(catalog, temporary);
        for (var frame = 0; frame < 60; frame++) cadence.Doors.Advance(1.0 / 60);
        if (cadence.Doors.Save().Clock!.Timers.CurrentTick != saved.Doors!.Clock!.Timers.CurrentTick + 10)
            throw new InvalidOperationException("Fractional frames changed the source script clock cadence.");
        var stalled = ClassicPlayerSession.Restore(catalog, temporary);
        stalled.Doors.Advance(1);
        if (stalled.Doors.Save().Clock!.Timers.CurrentTick != saved.Doors.Clock.Timers.CurrentTick + 1)
            throw new InvalidOperationException("A stalled frame incorrectly caught up multiple game ticks.");
        var manual = ClassicPlayerSession.Restore(catalog, temporary);
        for (var tick = 0; tick < 10; tick++) manual.Doors.Advance(.1);
        manual.Doors.Use(serial);
        var replaced = manual.Doors.Save().Clock!;
        if (replaced.Timers.Pending is not [{ } replacement] || replacement.Sequence == pending.Sequence ||
            replacement.DueTick != replaced.Timers.CurrentTick + 30 || manual.Doors.Pose(serial).Open)
            throw new InvalidOperationException("Repeated ordinary use did not replace the live source timer.");
        var transition = ClassicPlayerSession.Restore(catalog, temporary);
        transition.Doors.Leave(transition.MapPath);
        if (transition.Doors.Save().Clock!.Timers.Pending.Count != saved.Doors!.Clock!.Timers.Pending.Count)
            throw new InvalidOperationException("Same-map transition cancelled timers.");
        transition.Doors.Leave("maps/another-map.map");
        if (transition.Doors.Save().Clock!.Timers.Pending.Count != 0 ||
            transition.Doors.Save().Clock!.Timers.CurrentTick != saved.Doors.Clock.Timers.CurrentTick)
            throw new InvalidOperationException("Map departure failed to cancel script timers while preserving time.");

        // Inject a retained owner fault in this fixture, never into a user's save.
        var faulted = saved with { Doors = saved.Doors with { Doors = saved.Doors.Doors.Select(row =>
            row.Map == player.MapPath && row.Serial == serial ? row with { Failure = "Synthetic callback owner fault" } : row).ToArray() } };
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(faulted));
        var failed = ClassicPlayerSession.Restore(catalog, temporary);
        for (var tick = 0; tick < 30; tick++) failed.Doors.Advance(.1);
        var failedClock = failed.Doors.Save().Clock!;
        if (failedClock.Failure is null || !failedClock.Timers.Pending.Any(row => row.Sequence == pending.Sequence))
            throw new InvalidOperationException("Failed callback lost its pending work or diagnostic.");
        failed.Save(temporary);
        var failedCold = ClassicPlayerSession.Restore(catalog, temporary);
        failedCold.Doors.Advance(1);
        if (JsonSerializer.Serialize(failedCold.Doors.Save().Clock) != JsonSerializer.Serialize(failedClock))
            throw new InvalidOperationException("Cold callback fault resumed or discarded queue state.");
        Console.WriteLine($"PASS native owned scripted timer serial={serial} sourceUse=true cold=true nestedUse=true transition=true retainedFault=true fixture=true campaign=false");
    }
}
