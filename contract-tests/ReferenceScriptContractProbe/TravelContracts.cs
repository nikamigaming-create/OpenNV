using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class TravelContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-travel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Join(Header(),
                Record("CREA", 0x700, Field("ACBS", new byte[24]), Field("PKID", BitConverter.GetBytes(0x400u)), Field("PKID", BitConverter.GetBytes(0x401u))),
                Record("NPC_", 0x702, Field("ACBS", new byte[24])),
                Record("STAT", 0x3b), Record("STAT", 0x701),
                Global(0x35, 2277), Global(0x36, 0), Global(0x37, 1), Global(0x38, 23), Global(0x39, 4.95f), Global(0x3a, 1),
                Record("CELL", 0x800, Field("DATA", [1])), References(), NavigationMesh(0x600, 0x800),
                Package(0x400, 0, 0x1404), Package(0x401, 3, 0x1002), Package(0x402, 0, 0x140c), Package(0x403, 2, 0x1002),
                Package(0x406, 0, 0x1002, behavior: 0x20, idle: true), Package(0x407, 0, 2, behavior: 0x20),
                Package(0x408, 0, 0x101002), Package(0x409, 3, 0x101002),
                Package(0x40a, 0, 0x203404),
                GuardPackage(0x404, 0, 0x14001000, 180, 240), GuardPackage(0x405, 3, 0x10001000, 0, 0),
                Record("IDLE", 0x500, Field("MODL", Text("actor/event.kf")), Field("DATA", [0x54, 0, 0, 0, 0, 0]))));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Base.esm"), Package(0x400, 0, 0x1404, radius: 3, legacy: true)));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
            using var world = new FalloutReferenceWorld(records);
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
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x403))));
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x406))));
            var idleTravel = FalloutTravelPackage.Read(records.GetEffective(Key(0x406)), ownsIdleCollection: true);
            Require(idleTravel.Reference == marker && idleTravel.MustReach,
                "Owned NPC idle collection or skipped behavior prevented marker Travel admission.");
            Reject(() => FalloutTravelPackage.Read(records.GetEffective(Key(0x407)), ownsIdleCollection: true));
            var start = travel.Start(records, world, actor);
            Require(start.Location.SequenceEqual([10f, 20f, 30f]) && !start.Complete, "Travel did not resolve its actual marker.");
            world.SetPlacement(marker, new(Key(0x800), [40, 50, 60], [0, 0, 0]));
            start = travel.Start(records, world, actor);
            Require(start.Location.SequenceEqual([40f, 50f, 60f]), "Travel ignored an authoritative moved marker.");
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
            foreach (var invalidTravel in new[]
            {
                npcStart with { NavigationSha256 = new string('0', 64) },
                npcStart with { NavigationSha256 = null, RouteTarget = null, RouteWaypoints = null },
                npcStart with { RouteWaypoints = [], RouteCursor = 0 },
                npcStart with { RouteCursor = 3 },
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
                saved.Select(value => value.Reference == actor ? value with { PackageMotion = motion with { Travel = start } } : value).ToArray()
            })
            {
                using var refused = new FalloutReferenceWorld(records);
                Reject(() => refused.Restore(invalid));
                Require(refused.Capture().Count == 0, "Invalid Travel partially restored its reference world.");
            }
            Console.WriteLine("OPENNV_TRAVEL_CONTRACT_PASS winningMarker=true movedMarker=true editorPoseDistinct=true oncePerDay24Hours=true selectionPriority=true coldArrivalAndIdle=true invalidSourceRejected=true atomicRestore=true");
            Console.WriteLine("OPENNV_GUARD_APPROACH_CONTRACT_PASS independentRadii=true sourceFlags=true markerAndEditor=true coldMotion=true noInventedWander=true invalidAtomic=true");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Base.esm")); File.Delete(Path.Combine(directory, "Patch.esp")); Directory.Delete(directory);
        }
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
            Reference("REFR", 0x902, 0x3b, [10, 20, 30]));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24); return group;
    }
    private static byte[] NavigationMesh(uint id, uint cell)
    {
        var header = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(header, cell);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 1);
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
            Field("NVVX", vertices), Field("NVTR", triangle), Field("NVEX", []), Field("NVCA", []), Field("NVDP", []));
    }
    private static byte[] Package(uint id, int locationType, uint flags, int radius = 1, ushort behavior = 0, bool idle = false, bool legacy = false)
    {
        var data = new byte[legacy ? 8 : 12]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags); data[4] = 6;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), behavior);
        var location = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(location, locationType);
        BinaryPrimitives.WriteUInt32LittleEndian(location.AsSpan(4), locationType == 0 ? 0x902u : 0);
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
