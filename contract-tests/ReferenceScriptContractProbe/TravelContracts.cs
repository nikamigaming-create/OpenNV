using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class TravelContracts
{
    internal static void RunOwnedCurrentLocation(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(dataRoot), includeImplicitPlugins: false);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var package = records.GetEffective(new("FalloutNV.esm", 0x10a21c));
        var before = Hash(package.ReadData());
        var travel = FalloutTravelPackage.Read(package, ownsIdleCollection: true);
        Require(travel.LocationType == 2 && travel.Radius == 0 && travel.Flags == 0x801000 && travel.WeaponDrawn &&
            FalloutPackageData.Read(package).SpecificFlags == 0,
            "Owned current-location Travel declaration changed or conflated its unused trailing bytes with behavior.");
        Require(before == Hash(package.ReadData()), "Owned current-location audit changed its read-only source.");
        Console.WriteLine($"OPENNV_OWNED_CURRENT_LOCATION_TRAVEL_PASS package={package.FormKey} sourceSha256={before} locationType=2 weaponDrawn=true sourceUnchanged=true");
        var wellPackage = records.GetEffective(new("FalloutNV.esm", 0x10a21b));
        var wellHash = Hash(wellPackage.ReadData());
        var wellTravel = FalloutTravelPackage.Read(wellPackage, ownsIdleCollection: true);
        Require(wellTravel.LocationType == 0 && wellTravel.Reference == new FalloutFormKey("FalloutNV.esm", 0x10a200) &&
            wellTravel.Flags == 0x0c803004 && wellTravel.Running && wellTravel.WeaponDrawn &&
            wellTravel.ContinueCombat && wellTravel.NoCombatAlert,
            "Owned well Travel lost its reference, movement or combat-policy declarations.");
        wellTravel.RequireCombatContinuation(engaged: false, complete: false);
        Reject(() => wellTravel.RequireCombatContinuation(engaged: true, complete: false));
        Require(wellHash == Hash(wellPackage.ReadData()), "Owned well Travel audit changed its read-only source.");
        Console.WriteLine($"OPENNV_OWNED_TRAVEL_COMBAT_POLICY_PASS package={wellPackage.FormKey} sourceSha256={wellHash} ordinaryTravel=true concurrentCombatExplicitlyRefused=true sourceUnchanged=true");
        var sneakPackage = records.GetEffective(new("FalloutNV.esm", 0x10a217));
        var sneakHash = Hash(sneakPackage.ReadData());
        var sneak = FalloutTravelPackage.Read(sneakPackage, ownsIdleCollection: true);
        Require(sneak.Flags == 0x0c021204 && sneak.Sneaking && !sneak.Running && sneak.ContinueIfPlayerNear &&
            sneak.Reference == new FalloutFormKey("FalloutNV.esm", 0x10a1ff) && sneak.MustReach,
            "Owned sneak Travel lost its actual stance, continuation or destination.");
        foreach (var group in new[] { "sneakmtidle", "sneakmtforward", "sneakmtfastforward" })
        {
            var path = content.ActorAnimations.Find("meshes/characters/_male", group) ?? throw new InvalidDataException("Owned sneak animation is absent.");
            Require(content.TryRead(path, null, out var bytes, out _), "Owned sneak animation source is unreadable.");
            var animation = FalloutNifFile.Read(bytes);
            var sequence = animation.Roots.Select(animation.ReadObject).OfType<FalloutNifControllerSequence>().Single();
            Require(sequence.StopTime > sequence.StartTime && sequence.Frequency > 0, "Owned sneak animation has no finite source clock.");
            if (group != "sneakmtidle")
            {
                var root = sequence.ControlledBlocks.Single(value => value.NodeName == sequence.TargetName && value.ControllerType == "NiTransformController");
                var sampler = new FalloutNifAnimationSampler(animation, root.Interpolator);
                Require(sampler.Sample(sequence.StartTime).Translation != sampler.Sample(sequence.StopTime).Translation,
                    "Owned sneak movement has no source root displacement.");
            }
        }
        Require(sneakHash == Hash(sneakPackage.ReadData()), "Owned sneak Travel audit changed its read-only source.");
        Console.WriteLine($"OPENNV_OWNED_SNEAK_TRAVEL_PASS package={sneak.Form} sourceSha256={sneakHash} sourceStance=true sourceRootMotion=true continuationFlag=true sourceUnchanged=true");
        var markerPackage = records.GetEffective(new("FalloutNV.esm", 0x17267e));
        var markerHash = Hash(markerPackage.ReadData());
        var markerTravel = FalloutTravelPackage.Read(markerPackage);
        using var markerWorld = new FalloutReferenceWorld(records);
        var actor = new FalloutFormKey("FalloutNV.esm", 0x10588e);
        var marker = new FalloutFormKey("FalloutNV.esm", 0x17267d);
        var markerBase = records.GetEffective(markerWorld.Get(marker).Base);
        Require(markerTravel.Reference == marker && markerTravel.Flags == 0x2004 &&
            records.GetEffective(markerWorld.Get(actor).Base).Signature == "CREA" &&
            markerBase.Signature == "IDLM" && FalloutIdleCollection.Read(markerBase).Idles.Count == 5 &&
            FalloutScriptPackage.Read(markerPackage).Idles.Count == 0,
            "Owned Travel spatial marker was conflated with an instruction to use its separate idle collection.");
        var markerProgress = markerTravel.Start(records, markerWorld, actor);
        var markerPlacement = markerWorld.Placement(marker);
        Require(markerProgress.Cell == markerPlacement.Cell && markerProgress.Location.SequenceEqual(markerPlacement.Position) &&
            !markerProgress.Complete && markerHash == Hash(markerPackage.ReadData()),
            "Owned IDLM Travel did not retain its actual source destination or changed owned bytes.");
        Console.WriteLine($"OPENNV_OWNED_IDLM_TRAVEL_PASS package={markerTravel.Form} target={marker} sourceSha256={markerHash} ordinaryLocation=true markerCollectionNotSelected=true sourceUnchanged=true");
    }

    internal static void Run()
    {
        CheckPortalOverlayRetirement();
        var directory = Path.Combine(Path.GetTempPath(), "opennv-travel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Join(Header(),
                Record("CREA", 0x700, Field("ACBS", new byte[24]), Field("PKID", BitConverter.GetBytes(0x400u)), Field("PKID", BitConverter.GetBytes(0x401u))),
                Record("NPC_", 0x702, Field("ACBS", new byte[24])),
                Record("CREA", 0x704, Field("ACBS", Join(BitConverter.GetBytes(0x00400000u), new byte[20]))),
                Record("IDLM", 0x705, Field("IDLF", [0]), Field("IDLC", [1]), Field("IDLT", BitConverter.GetBytes(2f)), Field("IDLA", BitConverter.GetBytes(0x500u))),
                Record("STAT", 0x3b), Record("STAT", 0x701), Record("DOOR", 0x703),
                Global(0x35, 2277), Global(0x36, 0), Global(0x37, 1), Global(0x38, 23), Global(0x39, 4.95f), Global(0x3a, 1),
                Record("CELL", 0x800, Field("DATA", [1])), References(), NavigationMesh(0x600, 0x800),
                Record("CELL", 0x801, Field("DATA", [1])), PortalDestination(), NavigationMesh(0x601, 0x801),
                Record("CELL", 0x802, Field("DATA", [1])), NavigationMesh(0x602, 0x802,
                    [(0x903, 0, 0xabcd), (0x904, 1, 0x1234)]),
                Package(0x400, 0, 0x1404), Package(0x401, 3, 0x1002), Package(0x402, 0, 0x140c), Package(0x403, 2, 0x1002),
                Package(0x406, 0, 0x1002, behavior: 0x20, idle: true), Package(0x407, 0, 2, behavior: 0x20),
                Package(0x408, 0, 0x101002), Package(0x409, 3, 0x101002),
                Package(0x40a, 0, 0x203404),
                Package(0x40b, 2, 0x801000, radius: 0),
                Package(0x40c, 0, 0x0c803004, radius: 0),
                Package(0x40e, 0, 0x0c021204, radius: 0), Package(0x40f, 0, 0x1200, radius: 0),
                Package(0x410, 0, 0x2004, target: 0x907),
                AccompanyPackage(),
                GuardPackage(0x404, 0, 0x14001000, 180, 240), GuardPackage(0x405, 3, 0x10001000, 0, 0),
                Record("IDLE", 0x500, Field("MODL", Text("actor/event.kf")), Field("DATA", [0x54, 0, 0, 0, 0, 0]))));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Base.esm"), Package(0x400, 0, 0x1404, radius: 3, legacy: true)));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
            using var world = new FalloutReferenceWorld(records);
            CheckDoorAssociations(records);
            FalloutFormKey Key(uint id) => new("Base.esm", id);
            var actor = Key(0x900); var npcActor = Key(0x901); var marker = Key(0x902); var record = records.GetEffective(Key(0x400));
            var guard = FalloutGuardPackage.Read(records.GetEffective(Key(0x404)));
            Require(guard.Reference == marker && guard.Target == marker && guard.Radius == 180 && guard.TargetRadius == 240 &&
                !guard.WarnAndAttack && guard.ContinueCombat && !guard.Running,
                "Guard conflated its source location and intrusion radii or lost source flags.");
            var guardStart = guard.Start(records, world, actor);
            guard.Validate(records, world, actor, guardStart);
            Require(guardStart.Location.SequenceEqual([10f, 20f, 30f]) && !guardStart.Complete,
                "Guard did not begin its source marker approach independently of wander radius.");
            Reject(guard.RequireStationaryBehavior);
            FalloutGuardPackage.Read(records.GetEffective(Key(0x405))).RequireStationaryBehavior();
            var guardMotion = new FalloutActorPackageMotion(guard.Form, Hash(records.GetEffective(guard.Form).ReadData()),
                "meshes/actor/mtforward.kf", new string('a', 64), 1.25, false, [1, 2, 3], [0, 0, 0, 1], Guard: guardStart);
            world.Get(actor).PackageMotion = guardMotion;
            var guardSaved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using (var guardCold = new FalloutReferenceWorld(records))
            {
                guardCold.Restore(guardSaved);
                Require(guardCold.Get(actor).PackageMotion?.Guard?.Complete == false &&
                    guardCold.Get(actor).PackageMotion?.Seconds == 1.25,
                    "Cold Guard lost its incomplete approach and source motion clock.");
            }
            Reject(() => (guardMotion with { Travel = guardStart }).Validate());
            using (var invalidGuard = new FalloutReferenceWorld(records))
            {
                Reject(() => invalidGuard.Restore(guardSaved.Select(value => value.Reference == actor ? value with
                { PackageMotion = guardMotion with { Guard = guardStart with { Location = [99, 20, 30] } } } : value).ToArray()));
                Require(invalidGuard.Capture().Count == 0, "Invalid Guard location partially mutated cold state.");
            }
            world.Get(actor).PackageMotion = null;
            var travel = FalloutTravelPackage.Read(record);
            Require(travel.Reference == marker && travel.Radius == 3 && travel.MustReach && travel.OncePerDay && !travel.Running,
                "Travel lost winning fields, adjusted marker or movement flags.");
            foreach (var id in new uint[] { 0x408, 0x409 })
            {
                var headOff = records.GetEffective(Key(id));
                var declaration = FalloutScriptPackage.Read(headOff);
                Require(declaration.Flags == 0x101002 && !declaration.HeadTrackingEnabled &&
                    FalloutTravelPackage.Read(headOff).Flags == declaration.Flags,
                    "Head-Tracking Off was rejected or lost its independent package selector.");
                if (id == 0x409) Require(FalloutEditorTravelPackage.Read(headOff).Flags == declaration.Flags,
                    "Editor Travel did not retain the shared head-tracking selector.");
            }
            Require(FalloutScriptPackage.Read(record).HeadTrackingEnabled, "Ordinary Travel disabled head tracking without its source flag.");
            var hiddenWeaponTravel = records.GetEffective(Key(0x40a));
            Require(!FalloutScriptPackage.Read(hiddenWeaponTravel).WeaponsVisible &&
                FalloutScriptPackage.Read(record).WeaponsVisible && FalloutTravelPackage.Read(hiddenWeaponTravel) is { Running: true, OncePerDay: true, MustReach: true },
                "Weapon visibility selector was rejected, conflated with inventory or lost independent Travel flags.");
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x402))));
            var combatTravel = FalloutTravelPackage.Read(records.GetEffective(Key(0x40c)));
            Require(combatTravel.ContinueCombat && combatTravel.NoCombatAlert && combatTravel.WeaponDrawn && combatTravel.Running,
                "Travel conflated independent combat, weapon and movement flags.");
            combatTravel.RequireCombatContinuation(engaged: false, complete: false);
            combatTravel.RequireCombatContinuation(engaged: true, complete: true);
            travel.RequireCombatContinuation(engaged: true, complete: false);
            Reject(() => combatTravel.RequireCombatContinuation(engaged: true, complete: false));
            var currentLocation = FalloutTravelPackage.Read(records.GetEffective(Key(0x403)));
            Require(currentLocation.LocationType == 2, "Current-location Travel lost its source selector.");
            Reject(() => currentLocation.Start(records, world, npcActor));
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x406))));
            var idleTravel = FalloutTravelPackage.Read(records.GetEffective(Key(0x406)), ownsIdleCollection: true);
            Require(idleTravel.Reference == marker && idleTravel.MustReach,
                "Owned NPC idle collection or skipped behavior prevented marker Travel admission.");
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x407)), ownsIdleCollection: true));
            var start = travel.Start(records, world, actor);
            CheckIdleMarkerTravel(records);
            Require(start.Location.SequenceEqual([10f, 20f, 30f]) && !start.Complete, "Travel did not resolve its actual marker.");
            world.SetPlacement(marker, new(Key(0x800), [40, 50, 60], [0, 0, 0]));
            start = travel.Start(records, world, actor);
            Require(start.Location.SequenceEqual([40f, 50f, 60f]), "Travel ignored an authoritative moved marker.");
            travel.Validate(records, world, actor, start);
            foreach (var creaturePortal in new[]
            {
                start with { RouteCell = Key(0x800) },
                start with { RouteCell = Key(0x800), PortalDoor = Key(0x903) },
                start with { PortalArrival = new(Key(0x903), Key(0x904)) }
            })
            {
                creaturePortal.Validate();
                Reject(() => travel.Validate(records, world, actor, creaturePortal));
            }
            var npcStart = travel.Start(records, world, npcActor);
            var navigation = CellNavigationGraph.LoadOwned(records, Key(0x800));
            npcStart = npcStart with
            {
                NavigationSha256 = navigation.SourceSha256,
                RouteTarget = [12, 0, 18],
                RouteWaypoints = [[2, 0, 3], [12, 0, 18]],
                RouteCursor = 1,
            };
            travel.Validate(records, world, npcActor, npcStart);
            var npcMotion = new FalloutActorPackageMotion(record.FormKey, Hash(record.ReadData()),
                "meshes/npc/mtforward.kf", new string('d', 64), 2.75, false, [11, 12, 13], [0, 0, 0, 1], Travel: npcStart);
            CheckCurrentLocationTravel(records, npcMotion);
            CheckSneakTravel(records, npcMotion);
            world.Get(npcActor).PackageMotion = npcMotion;
            world.Get(npcActor).PackageAssignment = new(record.FormKey, Hash(record.ReadData()), false);
            var npcSaved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using (var npcCold = new FalloutReferenceWorld(records))
            {
                npcCold.Restore(npcSaved);
                var restored = npcCold.Get(npcActor).PackageMotion!;
                var restoredTravel = restored.Travel ?? throw new InvalidDataException("Cold NPC marker Travel is absent.");
                Require(restored.Seconds == 2.75 && restored.Package == record.FormKey &&
                    restoredTravel.RouteCursor == 1 && restoredTravel.NavigationSha256 == navigation.SourceSha256 &&
                    restoredTravel.RouteWaypoints is { } restoredWaypoints && restoredWaypoints.Length == npcStart.RouteWaypoints!.Length &&
                    restoredWaypoints.Zip(npcStart.RouteWaypoints).All(pair => pair.First.SequenceEqual(pair.Second)),
                    "Cold marker Travel lost its native route cursor, winning NAVM identity or package KF clock.");
            }
            var failedRoute = npcStart with
            {
                RouteWaypoints = [], RouteCursor = 0,
                RouteFailure = new("No supported source corridor.", 3, 1.25)
            };
            var failedSaved = npcSaved.Select(value => value.Reference == npcActor ? value with
                { PackageMotion = npcMotion with { Travel = failedRoute } } : value).ToArray();
            using (var failedCold = new FalloutReferenceWorld(records))
            {
                failedCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(failedSaved))!);
                var retainedFailure = failedCold.Get(npcActor).PackageMotion!;
                Require(retainedFailure.Travel!.RouteFailure == failedRoute.RouteFailure &&
                    !retainedFailure.Travel.Complete && retainedFailure.Seconds == npcMotion.Seconds &&
                    failedCold.Get(npcActor).PackageAssignment!.Done == false,
                    "Cold failed Travel dropped its refusal, retry countdown, actual clock or unfinished lifecycle.");
            }
            var pendingRoute = npcStart with { RouteWaypoints = [], RouteCursor = 0, RoutePending = true };
            using (var pendingCold = new FalloutReferenceWorld(records))
            {
                var pendingSaved = npcSaved.Select(value => value.Reference == npcActor ? value with
                    { PackageMotion = npcMotion with { Travel = pendingRoute } } : value).ToArray();
                pendingCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(pendingSaved))!);
                var resumed = pendingCold.Get(npcActor);
                Require(resumed.PackageMotion!.Travel is { RoutePending: true, Complete: false, RouteCursor: 0, RouteWaypoints.Length: 0 } &&
                    resumed.PackageMotion.Seconds == npcMotion.Seconds && resumed.PackageMotion.Position.SequenceEqual(npcMotion.Position) &&
                    resumed.PackageAssignment!.Done == false,
                    "Cold pending search lost its actual pose, animation clock or unfinished lifecycle.");
            }
            foreach (var invalidTravel in new[]
            {
                npcStart with { NavigationSha256 = new string('0', 64) },
                npcStart with { NavigationSha256 = null, RouteTarget = null, RouteWaypoints = null },
                npcStart with { RouteWaypoints = [], RouteCursor = 0 },
                npcStart with { RouteCursor = 3 },
                npcStart with { RoutePending = true },
                pendingRoute with { Complete = true },
                pendingRoute with { NavigationSha256 = null },
                failedRoute with { RoutePending = true },
                failedRoute with { Complete = true },
                failedRoute with { RouteWaypoints = [[1, 2, 3]] },
                failedRoute with { RouteFailure = new("", 1, .5) },
                failedRoute with { RouteFailure = new("Unbound route", 5, .5) },
                failedRoute with { RouteFailure = new("Unbound route", 1, double.NaN) },
            })
            {
                using var refused = new FalloutReferenceWorld(records);
                Reject(() => refused.Restore(npcSaved.Select(value => value.Reference == npcActor ? value with
                { PackageMotion = npcMotion with { Travel = invalidTravel } } : value).ToArray()));
                Require(refused.Capture().Count == 0, "Invalid NPC marker Travel partially restored its reference world.");
            }
            var editor = FalloutTravelPackage.Read(records.GetEffective(Key(0x401)));
            world.SetPlacement(actor, new(Key(0x800), [70, 80, 90], [0, 0, 0]));
            var editorGuard = FalloutGuardPackage.Read(records.GetEffective(Key(0x405)));
            Require(editorGuard.Start(records, world, actor).Location.SequenceEqual([1f, 2f, 3f]),
                "Editor Guard used the moved actor instead of its authored placement.");
            Require(editor.Start(records, world, actor).Location.SequenceEqual([1f, 2f, 3f]), "Editor Travel used a moved actor pose.");
            var globals = FalloutGlobalState.Read(records);
            var clock = new FalloutGameTime(globals, FalloutGameTimeBindings.Read(records),
                new([31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31], "synthetic-calendar"));
            world.MarkPackageStart(actor, record, clock);
            Require(world.PackageEligible(actor, record, clock, record.FormKey, false), "Once-per-day interrupted its current unfinished run.");
            Require(!world.PackageEligible(actor, record, clock, record.FormKey, true), "Once-per-day restarted after completion.");
            globals.Set(Key(0x39), 5.1f); globals.Set(Key(0x38), 2);
            Require(!world.PackageEligible(actor, record, clock, null, false), "Midnight restarted a once-per-day package early.");
            globals.Set(Key(0x39), 5.95f);
            Require(world.PackageEligible(actor, record, clock, null, false), "A full source day failed to release package selection.");
            globals.Set(Key(0x39), 4.95f);
            var selected = FalloutAiPackages.Select(records, Key(0x700), _ => throw new InvalidOperationException("Unexpected condition"),
                clock: clock, eligible: candidate => world.PackageEligible(actor, candidate, clock, null, false));
            Require(selected?.FormKey == Key(0x401), "Authored priority did not pass the ineligible once-per-day candidate.");
            var motion = new FalloutActorPackageMotion(record.FormKey, Hash(record.ReadData()), "meshes/actor/mtidle.kf", new string('a', 64),
                1, false, [70, 80, 90], [0, 0, 0, 1], Travel: start with { Complete = true });
            world.Get(actor).PackageMotion = motion;
            var idle = records.GetEffective(Key(0x500));
            var idleClock = new FalloutIdleAnimationPlayback(0, 2, 1, 2, [(0, "start"), (2, "end")], 0);
            idleClock.Advance(.5);
            world.Get(actor).PackageIdle = new(record.FormKey, Hash(record.ReadData()), "POEA", idle.FormKey, Hash(idle.ReadData()),
                "meshes/actor/event.kf", new string('b', 64), idleClock.Capture());
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            var retained = cold.Get(actor);
            Require(retained.PackageMotion!.Travel!.Complete && retained.PackageStarts.Count == 1 && retained.PackageIdle!.Clock.SourceSeconds == .5,
                "Cold Travel lost arrival, start time or the event idle phase.");
            Reject(() => (motion with { EditorTravel = new(Key(0x800), [1, 2, 3], false) }).Validate());
            foreach (var invalid in new[]
            {
                saved.Select(value => value.Reference == actor ? value with { PackageMotion = motion with { Travel = start with { Location = [10, 20, 30] } } } : value).ToArray(),
                saved.Select(value => value.Reference == actor ? value with { PackageStarts = [retained.PackageStarts[0], retained.PackageStarts[0]] } : value).ToArray(),
                saved.Select(value => value.Reference == actor ? value with { PackageIdle = value.PackageIdle! with { IdleSha256 = new string('c', 64) } } : value).ToArray(),
                saved.Select(value => value.Reference == actor ? value with { PackageMotion = motion with
                { Travel = start with { PortalArrival = new(Key(0x903), Key(0x904)) } } } : value).ToArray(),
                saved.Select(value => value.Reference == actor ? value with { PackageMotion = motion with
                { Travel = start with { Complete = true, RouteCell = Key(0x800) } } } : value).ToArray(),
                saved.Select(value => value.Reference == actor ? value with { PackageMotion = motion with { Travel = start } } : value).ToArray()
            })
            {
                using var refused = new FalloutReferenceWorld(records);
                Reject(() => refused.Restore(invalid));
                Require(refused.Capture().Count == 0, "Invalid Travel partially restored its reference world.");
            }
            Console.WriteLine("OPENNV_TRAVEL_CONTRACT_PASS winningMarker=true movedMarker=true editorPoseDistinct=true oncePerDay24Hours=true selectionPriority=true coldArrivalAndIdle=true invalidSourceRejected=true atomicRestore=true");
            Console.WriteLine("OPENNV_GUARD_APPROACH_CONTRACT_PASS independentRadii=true sourceFlags=true markerAndEditor=true coldMotion=true noInventedWander=true invalidAtomic=true");
            CheckPortalTravel(records, record, npcMotion);
            CheckAccompanyPortal(records, npcMotion);
            CheckCreatureTravelPortal(records, record, npcMotion);
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Base.esm")); File.Delete(Path.Combine(directory, "Patch.esp")); Directory.Delete(directory);
        }
    }

    private static void CheckIdleMarkerTravel(FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        FalloutFormKey Key(uint id) => new("Base.esm", id);
        var travel = FalloutTravelPackage.Read(records.GetEffective(Key(0x410)));
        var declaration = FalloutScriptPackage.Read(records.GetEffective(travel.Form));
        Require(declaration.Idles.Count == 0 && FalloutIdleCollection.Read(records.GetEffective(Key(0x705))).Idles.Count == 1,
            "Travel package idles were conflated with its spatial target's idle collection.");
        foreach (var actor in new[] { Key(0x900), Key(0x901) })
        {
            var progress = travel.Start(records, world, actor);
            Require(progress.Cell == Key(0x800) && progress.Location.SequenceEqual([70f, 80f, 90f]) && !progress.Complete,
                "Travel did not use its IDLM reference's real position for both actor kinds.");
            progress.Location[0] = -99;
            Require(world.Placement(Key(0x907)).Position[0] == 70, "Travel exposed mutable authoritative placement.");
        }
        world.SetPlacement(Key(0x907), new(Key(0x801), [20, 30, 40], [0, 0, 0]));
        var moved = travel.Start(records, world, Key(0x900));
        Require(moved.Cell == Key(0x801) && moved.Location.SequenceEqual([20f, 30f, 40f]),
            "IDLM Travel ignored the shared moved placement.");
        world.Get(Key(0x907)).DeletePending = true;
        Reject(() => travel.Start(records, world, Key(0x900)));
        Console.WriteLine("OPENNV_TRAVEL_IDLM_LOCATION_PASS sourcePlacement=true bothActorKinds=true movedTarget=true collectionNotSelected=true deletedRefused=true");
    }

    private static void CheckSneakTravel(FalloutPluginStack records, FalloutActorPackageMotion template)
    {
        FalloutFormKey Key(uint id) => new("Base.esm", id);
        var source = records.GetEffective(Key(0x40e));
        var sneak = FalloutTravelPackage.Read(source);
        Require(sneak.Sneaking && !sneak.Running && sneak.ContinueIfPlayerNear && sneak.MustReach,
            "Travel conflated sneaking, running and continuation flags.");
        var nearOnly = FalloutTravelPackage.Read(records.GetEffective(Key(0x40f)));
        Require(nearOnly.RetainsSelection(false, true, false) && !nearOnly.RetainsSelection(false, false, false) &&
            !nearOnly.RetainsSelection(false, true, true) && !nearOnly.RetainsSelection(true, true, false) &&
            sneak.RetainsSelection(false, false, false) && !sneak.RetainsSelection(false, true, true),
            "Travel continuation lost the same-cell hold, actual completion or explicit EVP override.");
        Require(sneak.HoldsForPlayer(true) && !sneak.HoldsForPlayer(false),
            "Travel admitted a completion while its ContinueIfPCNear condition held.");
        using var world = new FalloutReferenceWorld(records);
        var actor = Key(0x901);
        Reject(() => nearOnly.Validate(records, world, Key(0x900), nearOnly.Start(records, world, Key(0x900))));
        Require(world.InSameCell(actor, records.RuntimeFormKey(0x14), new(Key(0x800), [100000, 100000, 0], [0, 0, 0]), .01524f) &&
            !world.InSameCell(actor, records.RuntimeFormKey(0x14), new(Key(0x801), [0, 0, 0], [0, 0, 0]), .01524f),
            "ContinueIfPCNear substituted distance for actual cell membership.");
        world.Get(actor).PackageAssignment = new(source.FormKey, Hash(source.ReadData()), false);
        world.Get(actor).PackageMotion = template with
        {
            Package = source.FormKey, PackageSha256 = Hash(source.ReadData()), Animation = "meshes/actor/sneakmtidle.kf",
            Seconds = 7.5, Travel = template.Travel! with { Complete = false, Location = world.Placement(Key(0x902)).Position }
        };
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(saved);
        var retained = cold.Get(actor);
        Require(retained.PackageAssignment?.Done == false && retained.PackageMotion is { Seconds: 7.5, Travel.Complete: false } &&
            retained.PackageMotion.Animation.EndsWith("sneakmtidle.kf", StringComparison.Ordinal),
            "Cold Travel lost its unfinished arrival hold or actual idle animation clock.");
        Console.WriteLine("OPENNV_SNEAK_TRAVEL_CONTRACT_PASS independentFlags=true cellMembership=true explicitEvaluation=true coldUnfinishedIdle=true");
    }

    private static void CheckPortalOverlayRetirement()
    {
        var finite = new FalloutIdleAnimationPlayback(0, 1, 1, 2, [(0, "start"), (1, "end")], 0);
        Require(!RuntimeNativeNpc.PortalOverlayRetired(true, finite, false, 1, null), "Active finite IDLE/ANIO retired early.");
        finite.Advance(.5);
        Require(!finite.Complete && !RuntimeNativeNpc.PortalOverlayRetired(true, finite, false, 1, null),
            "Portal wait bypassed the real finite source clock.");
        finite.Advance(1);
        Require(finite.Complete && !RuntimeNativeNpc.PortalOverlayRetired(true, finite, false, 1, null),
            "Clock completion discarded its still-owned animation/ANIO.");
        Require(RuntimeNativeNpc.PortalOverlayRetired(false, null, false, 0, null), "Ordinary complete retirement remained blocked.");
        var endless = new FalloutIdleAnimationPlayback(0, 1, 1, 0, [], 0);
        Reject(() => RuntimeNativeNpc.PortalOverlayRetired(true, endless, false, 0, null));
        Reject(() => RuntimeNativeNpc.PortalOverlayRetired(true, null, false, 0, null));
        Reject(() => RuntimeNativeNpc.PortalOverlayRetired(false, null, false, 1, null));
        Reject(() => RuntimeNativeNpc.PortalOverlayRetired(false, null, true, 0, null));
        Reject(() => RuntimeNativeNpc.PortalOverlayRetired(false, null, false, 0, "source animation failure"));
        Console.WriteLine("OPENNV_PORTAL_OVERLAY_CONTRACT_PASS finiteClockWait=true actualRetirementRequired=true endlessAndErrorRefused=true orphanAnioAndResponseRefused=true");
    }

    private static void CheckAccompanyPortal(FalloutPluginStack records, FalloutActorPackageMotion template)
    {
        FalloutFormKey Key(uint id) => new("Base.esm", id);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var actor = Key(0x900); var door = Key(0x903); var package = records.GetEffective(Key(0x40d));
        Require(!world.CanNpcOpenDoor(door, actor), "Creature opened a locked door without ownership.");
        world.UnlockReference(door);
        Require(world.CanNpcOpenDoor(door, actor) && !world.CanNpcOpenDoor(door, Key(0x906)),
            "Creature door admission ignored its source CannotOpenDoors flag.");
        Require(world.TravelPortal(actor, Key(0x801)) == door, "Accompany lost the shared directed portal graph.");
        var arrival = world.TravelPortalArrival(door, out var destination);
        var receipt = new FalloutAccompanyPortalArrival(door, destination);
        var state = world.Get(actor);
        state.PackageAssignment = new(package.FormKey, Hash(package.ReadData()), false);
        Reject(() => receipt.Validate(records, world, actor, package));
        world.SetPlacement(actor, arrival);
        state.PackageMotion = template with { Package = package.FormKey, PackageSha256 = Hash(package.ReadData()),
            Travel = null, AccompanyPortal = receipt };
        receipt.Validate(records, world, actor, package);
        Require(world.CurrentPackage(actor) == package.FormKey, "Accompany lost its package during committed portal handoff.");
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(saved);
            Require(cold.Get(actor).PackageMotion?.AccompanyPortal == receipt && cold.Get(actor).PackageAssignment?.Done == false &&
                cold.Placement(actor).Position.SequenceEqual(arrival.Position),
                "Cold Accompany lost its committed arrival, exact placement or unfinished lifecycle.");
        }
        foreach (var invalid in new[]
        {
            saved.Select(value => value.Reference == actor ? value with { Placement = value.Placement! with { Position = [0, 0, 0] } } : value).ToArray(),
            saved.Select(value => value.Reference == actor ? value with { PackageAssignment = value.PackageAssignment! with { Done = true } } : value).ToArray(),
            saved.Select(value => value.Reference == actor ? value with { PackageAssignment = null } : value).ToArray(),
            saved.Select(value => value.Reference == actor ? value with { PackageMotion = value.PackageMotion! with
                { AccompanyPortal = new(door, door) } } : value).ToArray(),
        })
        {
            using var refused = new FalloutReferenceWorld(records);
            Reject(() => refused.Restore(invalid));
            Require(refused.Capture().Count == 0, "Invalid Accompany receipt partially restored the world.");
        }
        Reject(() => (state.PackageMotion! with { Travel = template.Travel }).Validate());
        Console.WriteLine("OPENNV_ACCOMPANY_PORTAL_CONTRACT_PASS sharedDirectedRoute=true sourceDoorCapability=true lockedRefused=true consumedReceipt=true coldPlacement=true unfinishedLifecycle=true malformedAtomic=true");
    }

    private static void CheckCreatureTravelPortal(FalloutPluginStack records, FalloutPluginRecord package,
        FalloutActorPackageMotion template)
    {
        FalloutFormKey Key(uint id) => new("Base.esm", id);
        using var world = new FalloutReferenceWorld(records);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var actor = Key(0x900); var door = Key(0x903); var marker = Key(0x902);
        world.UnlockReference(door);
        world.SetPlacement(marker, new(Key(0x801), [70, 80, 90], [0, 0, 0]));
        var source = FalloutTravelPackage.Read(package);
        var progress = source.Start(records, world, actor);
        var state = world.Get(actor);
        state.PackageAssignment = new(package.FormKey, Hash(package.ReadData()), false);
        state.PackageMotion = template with { Travel = progress };
        source.Validate(records, world, actor, progress);
        Require(world.TravelPortal(actor, progress.Cell) == door,
            "Creature Travel lost its ordinary source portal approach.");
        var placement = world.TravelPortalArrival(door, out var destination);
        var receipt = progress with { PortalArrival = new(door, destination) };
        Reject(() => source.Validate(records, world, actor, receipt));
        world.SetPlacement(actor, placement);
        state.PackageMotion = template with { Travel = receipt };
        source.Validate(records, world, actor, receipt);
        Require(world.CurrentPackage(actor) == package.FormKey && !receipt.Complete,
            "Creature portal handoff lost its package or reported final arrival.");
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(saved);
            Require(cold.Get(actor).PackageMotion?.Travel?.PortalArrival == receipt.PortalArrival &&
                cold.Get(actor).PackageAssignment?.Done == false && cold.Placement(actor).Position.SequenceEqual(placement.Position),
                "Cold creature Travel lost its exact committed placement or unfinished destination.");
        }
        foreach (var invalid in new[]
        {
            saved.Select(value => value.Reference == actor ? value with { PackageAssignment = null } : value).ToArray(),
            saved.Select(value => value.Reference == actor ? value with { PackageAssignment = value.PackageAssignment! with { Done = true } } : value).ToArray(),
            saved.Select(value => value.Reference == actor ? value with { Placement = value.Placement! with { Position = [0, 0, 0] } } : value).ToArray(),
        })
        {
            using var refused = new FalloutReferenceWorld(records);
            Reject(() => refused.Restore(invalid));
            Require(refused.Capture().Count == 0, "Invalid creature Travel receipt partially restored state.");
        }
        Console.WriteLine("OPENNV_CREATURE_TRAVEL_PORTAL_CONTRACT_PASS sharedRoute=true committedReceipt=true coldPlacement=true noFinalArrival=true malformedAtomic=true");
    }

    private static byte[] AccompanyPackage()
    {
        var data = new byte[12]; data[4] = 7;
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x04501000);
        var target = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 0x901u);
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(8), 128);
        return Record("PACK", 0x40d, Field("EDID", Text("AccompanyPortalFixture")), Field("PKDT", data), Field("PTDT", target));
    }

    private static void CheckPortalTravel(FalloutPluginStack records, FalloutPluginRecord package, FalloutActorPackageMotion template)
    {
        FalloutFormKey Key(uint id) => new("Base.esm", id);
        using var world = new FalloutReferenceWorld(records);
        var actor = Key(0x901); var marker = Key(0x902); var door = Key(0x903);
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        Reject(() => world.CurrentPackage(actor));
        Reject(() => world.TravelPortal(actor, Key(0x801)));
        world.UnlockReference(door);
        world.SetPlacement(marker, new(Key(0x801), [70, 80, 90], [0, 0, 0]));
        var source = FalloutTravelPackage.Read(package);
        var progress = source.Start(records, world, actor) with
        {
            RouteCell = Key(0x800), PortalDoor = door,
            NavigationSha256 = world.TravelNavigation(Key(0x800)).SourceSha256,
            RouteTarget = [4, 5, 6], RouteWaypoints = [[1, 2, 3], [4, 5, 6]], RouteCursor = 1
        };
        Require(world.TravelPortal(actor, progress.Cell) == door, "Cross-cell Travel lost its directed source door.");
        source.Validate(records, world, actor, progress);
        var state = world.Get(actor);
        state.PackageAssignment = new(package.FormKey, Hash(package.ReadData()), false);
        state.PackageMotion = template with { Travel = progress };
        var approachSave = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        Reject(() => source.Validate(records, world, actor, progress with { RouteCell = null }));
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(approachSave);
            Require(cold.Get(actor).PackageMotion!.Travel!.PortalDoor == door && cold.Placement(actor).Cell == Key(0x800),
                "Cold portal approach invented an arrival or lost its door.");
        }
        world.Get(door).Enabled = false;
        Reject(() => world.TravelPortal(actor, progress.Cell));
        Require(world.Placement(actor).Cell == Key(0x800), "Rejected door moved its actor.");
        world.Get(door).Enabled = true;
        var arrival = world.TravelPortalArrival(door, out var destination);
        Require(destination == Key(0x904) && arrival.Cell == Key(0x801) && arrival.Position.SequenceEqual([40f, 50f, 60f]),
            "Portal arrival did not preserve the actual XTEL endpoint.");
        world.SetPlacement(actor, arrival);
        var receipt = source.Start(records, world, actor) with { PortalArrival = new(door, destination) };
        state.PackageMotion = template with { Travel = receipt };
        source.Validate(records, world, actor, receipt);
        Require(world.IsResident(actor) && world.CurrentPackage(actor) == package.FormKey,
            "Committed portal handoff lost its current package before residency publication.");
        state.QueryCurrentPackage = () => Key(0x401);
        Require(world.CurrentPackage(actor) == Key(0x401), "Portal receipt overrode the actual native package owner.");
        state.QueryCurrentPackage = null;
        var handoffAssignment = state.PackageAssignment!;
        state.PackageAssignment = handoffAssignment with { Done = true };
        Reject(() => world.CurrentPackage(actor));
        state.PackageAssignment = handoffAssignment with { Sha256 = new string('0', 64) };
        Reject(() => world.CurrentPackage(actor));
        state.PackageAssignment = handoffAssignment;
        state.PackageMotion = template with { Travel = receipt with { PortalArrival = new(door, door) } };
        Reject(() => world.CurrentPackage(actor));
        state.PackageMotion = template with { Travel = progress };
        Reject(() => world.CurrentPackage(actor));
        state.PackageMotion = template with { Travel = receipt };
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using (var cold = new FalloutReferenceWorld(records))
        {
            cold.Restore(saved);
            var restored = cold.Get(actor);
            Require(restored.PackageMotion!.Travel!.PortalArrival == receipt.PortalArrival &&
                !restored.PackageMotion.Travel.Complete && restored.PackageAssignment?.Done == false &&
                cold.TravelPortal(actor, receipt.Cell) is null,
                "Cold portal receipt replayed source travel or completed the final marker.");
            Reject(() => cold.TravelPortal(actor, Key(0x800)));
        }
        foreach (var invalid in new[] { receipt with { Complete = true }, receipt with { PortalArrival = new(door, door) },
            receipt with { RouteCell = Key(0x800) } })
        {
            using var cold = new FalloutReferenceWorld(records);
            Reject(() => cold.Restore(saved.Select(value => value.Reference == actor ? value with
                { PackageMotion = template with { Travel = invalid } } : value).ToArray()));
            Require(cold.Capture().Count == 0, "Invalid portal receipt partially restored its world.");
        }
        Console.WriteLine("OPENNV_TRAVEL_PORTAL_CONTRACT_PASS directed=true lockedAndDisabledRefused=true coldApproach=true coldCommittedArrival=true committedPackageHandoff=true finalMarkerPending=true noReverseInference=true malformedAtomic=true");
    }

    private static void CheckCurrentLocationTravel(FalloutPluginStack records, FalloutActorPackageMotion template)
    {
        FalloutFormKey Key(uint id) => new("Base.esm", id);
        using var world = new FalloutReferenceWorld(records);
        var actor = Key(0x901);
        var package = records.GetEffective(Key(0x40b));
        var source = FalloutTravelPackage.Read(package);
        Require(source.LocationType == 2 && source.WeaponDrawn && source.Radius == 0,
            "Current-location Travel lost its independent weapon flag or location radius.");
        var point = new[] { 20f, 30f, 0f };
        var progress = source.Start(records, world, actor, point);
        point[0] = 99;
        Require(progress.Location.SequenceEqual([20f, 30f, 0f]), "Current-location anchor retained mutable input or used editor placement.");
        progress = progress with
        {
            Complete = true, RouteCell = Key(0x800), NavigationSha256 = world.TravelNavigation(Key(0x800)).SourceSha256,
            RouteTarget = [20, 0, -30], RouteWaypoints = [], RouteCursor = 0
        };
        // Movement after package selection must not relocate its persisted anchor.
        world.SetPlacement(actor, new(Key(0x800), [70, 80, 0], [0, 0, 0]));
        source.Validate(records, world, actor, progress);
        var state = world.Get(actor);
        state.PackageAssignment = new(package.FormKey, Hash(package.ReadData()), true);
        state.PackageMotion = template with { Package = package.FormKey, PackageSha256 = Hash(package.ReadData()), Travel = progress };
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(saved);
        Require(cold.Get(actor).PackageMotion!.Travel!.Location.SequenceEqual([20f, 30f, 0f]) &&
            cold.Get(actor).PackageAssignment?.Done == true && cold.Placement(actor).Position.SequenceEqual([70f, 80f, 0f]),
            "Cold current-location Travel recaptured its anchor or restarted its completed lifecycle.");
        Reject(() => source.Start(records, world, Key(0x900), [1, 2, 3]));
        Reject(() => source.Validate(records, world, actor, progress with { NavigationSha256 = new string('0', 64) }));
        Console.WriteLine("OPENNV_CURRENT_LOCATION_TRAVEL_CONTRACT_PASS startPoseLatched=true weaponDrawn=true coldAnchorPreserved=true completedLifecyclePreserved=true unsupportedCreatureRefused=true");
    }

    private static void CheckDoorAssociations(FalloutPluginStack records)
    {
        FalloutFormKey Key(uint id) => new("Base.esm", id);
        var source = FalloutNavigationMesh.Read(records.GetEffective(Key(0x602)));
        Require(source.Doors.Length == 2 && source.Doors[1] == new FalloutNavigationDoor(Key(0x904), 1, 0x1234),
            "An invalid auxiliary door association was dropped, clamped or lost its unused bytes.");
        var graph = CellNavigationGraph.LoadOwned(records, Key(0x802));
        Require(graph.NavMeshes == 1 && graph.Triangles == 1 && graph.DoorAssociations.Count == 2 &&
            graph.DoorAssociationErrors.Count == 1 && graph.DoorAssociationErrors[0].Contains("Base.esm:000602", StringComparison.OrdinalIgnoreCase) &&
            graph.DoorAssociationErrors[0].Contains("Base.esm:000904", StringComparison.OrdinalIgnoreCase),
            "Auxiliary door divergence discarded valid geometry or lost exact source identities.");
        graph.RequireDoorAssociations(Key(0x903));
        Reject(() => graph.RequireDoorAssociations(Key(0x904)));
        var route = graph.FindPath(new Godot.Vector3(10, 10, 0), new Godot.Vector3(20, 20, 0));
        Require(route.Count == 1 && route[0].DistanceTo(new Godot.Vector3(20, 20, 0)) < .001f,
            "An unrelated invalid door association blocked valid triangle routing.");
        var expectedHash = Hash(Encoding.UTF8.GetBytes(Key(0x602) + ":" + Hash(records.GetEffective(Key(0x602)).ReadData())));
        Require(graph.SourceSha256 == expectedHash, "Navigation identity omitted retained auxiliary source bytes.");
        Console.WriteLine("OPENNV_NAVIGATION_DOOR_ASSOCIATION_CONTRACT_PASS rawRetained=true geometryRetained=true exactDivergence=true invalidDoorRefused=true fullSourceHash=true");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Global(uint id, float value) => Record("GLOB", id, Field("EDID", Text("Global" + id)), Field("FNAM", [(byte)'f']), Field("FLTV", BitConverter.GetBytes(value)));
    private static byte[] References()
    {
        byte[] Reference(string type, uint id, uint baseId, float[] position) => Record(type, id, Field("NAME", BitConverter.GetBytes(baseId)),
            Field("DATA", position.Concat(new float[3]).SelectMany(BitConverter.GetBytes).ToArray()));
        var body = Join(Reference("ACRE", 0x900, 0x700, [1, 2, 3]), Reference("ACHR", 0x901, 0x702, [4, 5, 6]),
            Reference("ACRE", 0x906, 0x704, [1, 2, 3]),
            Reference("REFR", 0x907, 0x705, [70, 80, 90]),
            Reference("REFR", 0x902, 0x3b, [10, 20, 30]),
            Record("REFR", 0x903, Field("NAME", BitConverter.GetBytes(0x703u)),
                Field("XLOC", new byte[20]),
                Field("DATA", new float[] { 20, 30, 40, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray()),
                Field("XTEL", Join(BitConverter.GetBytes(0x904u), new float[] { 40, 50, 60, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray(), new byte[4]))));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24); return group;
    }
    private static byte[] PortalDestination()
    {
        var body = Record("REFR", 0x904, Field("NAME", BitConverter.GetBytes(0x703u)),
            Field("DATA", new float[] { 1, 2, 3, 0, 0, 0 }.SelectMany(BitConverter.GetBytes).ToArray()));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x801); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24); return group;
    }
    private static byte[] NavigationMesh(uint id, uint cell, (uint Door, ushort Triangle, ushort Unused)[]? doors = null)
    {
        var header = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(header, cell);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)(doors?.Length ?? 0));
        var vertices = new byte[36];
        var points = new[] { (0f, 0f, 0f), (100f, 0f, 0f), (0f, 100f, 0f) };
        for (var index = 0; index < points.Length; index++)
        {
            var offset = index * 12;
            BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(offset), points[index].Item1);
            BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(offset + 4), points[index].Item2);
            BinaryPrimitives.WriteSingleLittleEndian(vertices.AsSpan(offset + 8), points[index].Item3);
        }
        var triangle = new byte[16];
        BinaryPrimitives.WriteUInt16LittleEndian(triangle, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(triangle.AsSpan(4), 2);
        BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(6), -1);
        BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(8), -1);
        BinaryPrimitives.WriteInt16LittleEndian(triangle.AsSpan(10), -1);
        return Record("NAVM", id, Field("DATA", header), Field("NVER", BitConverter.GetBytes(11u)),
            Field("NVVX", vertices), Field("NVTR", triangle), Field("NVEX", []), Field("NVCA", []),
            Field("NVDP", (doors ?? []).SelectMany(door => Join(BitConverter.GetBytes(door.Door),
                BitConverter.GetBytes(door.Triangle), BitConverter.GetBytes(door.Unused))).ToArray()));
    }
    private static byte[] Package(uint id, int locationType, uint flags, int radius = 1, ushort behavior = 0, bool idle = false, bool legacy = false, uint target = 0x902)
    {
        var data = new byte[legacy ? 8 : 12]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags); data[4] = 6;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), behavior);
        var location = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(location, locationType);
        BinaryPrimitives.WriteUInt32LittleEndian(location.AsSpan(4), locationType == 0 ? target : 0);
        BinaryPrimitives.WriteInt32LittleEndian(location.AsSpan(8), radius);
        return Record("PACK", id, Field("EDID", Text("Travel" + id)), Field("PKDT", data), Field("PLDT", location),
            Field("PSDT", [255, 255, 0, 255, 0, 0, 0, 0]),
            idle ? Join(Field("IDLC", [1]), Field("IDLA", BitConverter.GetBytes(0x500u))) : [],
            Field("POEA", []), Field("INAM", BitConverter.GetBytes(0x500u)));
    }
    private static byte[] GuardPackage(uint id, int locationType, uint flags, int radius, int targetRadius)
    {
        var data = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags); data[4] = 14;
        var location = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(location, locationType);
        BinaryPrimitives.WriteUInt32LittleEndian(location.AsSpan(4), locationType == 0 ? 0x902u : 0);
        BinaryPrimitives.WriteInt32LittleEndian(location.AsSpan(8), radius);
        var target = new byte[16]; BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 0x902u);
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(8), targetRadius);
        return Record("PACK", id, Field("EDID", Text("Guard" + id)), Field("PKDT", data), Field("PLDT", location), Field("PTDT", target));
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var body = Join(fields); var result = new byte[24 + body.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)body.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id);
        body.CopyTo(result, 24); return result;
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid Travel state was admitted.");
    }
}
