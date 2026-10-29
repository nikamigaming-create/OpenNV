using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;

internal static class IdleMarkerReservationContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-idle-marker-reservations-").FullName;
        var path = Path.Combine(directory, "Markers.esm");
        try
        {
            byte[] Reference(string signature, uint id, uint basis) => Record(signature, id,
                Field("NAME", U32(basis)), Field("DATA", new byte[24]));
            var references = Join(Reference("ACHR", 0x900, 0x700), Reference("ACRE", 0x901, 0x701),
                Reference("REFR", 0x902, 0x710), Reference("REFR", 0x903, 0x710), Reference("REFR", 0x904, 0x711),
                Record("REFR", 0x905, Field("NAME", [0, 1]), Field("DATA", new byte[24])),
                Reference("REFR", 0x906, 0x700), Reference("ACHR", 0x907, 0x701), Reference("ACHR", 0x908, 0x700));
            var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
            BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); references.CopyTo(group, 24);
            var packageData = new byte[12]; packageData[4] = 12;
            var location = new byte[12]; location[0] = 3;
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 0x700), Record("CREA", 0x701), Record("STAT", 0x711),
                Record("PACK", 0x720, Field("EDID", Encoding.ASCII.GetBytes("MarkerSandbox\0")),
                    Field("PKDT", packageData), Field("PLDT", location), Field("IDLC", [0]), Field("POBA", [])),
                Record("IDLM", 0x710, Field("IDLF", [0]), Field("IDLC", [1]), Field("IDLA", U32(0x730)), Field("IDLT", BitConverter.GetBytes(0f))),
                Record("IDLE", 0x730), Record("IDLE", 0x731),
                Record("CELL", 0x800, Field("DATA", [1])), group));
            using var records = FalloutPluginStack.Load(directory, ["Markers.esm"]);
            using var world = new FalloutReferenceWorld(records);
            FalloutFormKey Key(uint id) => new("Markers.esm", id);
            var actor = Key(0x900); var other = Key(0x901); var first = Key(0x902); var second = Key(0x903);
            var observations = new HashSet<(FalloutFormKey, int)>();
            bool Occupied(FalloutFormKey marker, int slot)
            {
                Require(slot == 0, "Idle marker occupancy queried a fabricated slot.");
                return observations.Contains((marker, slot));
            }
            _ = world.Get(actor); _ = world.Get(other); _ = world.Get(first); _ = world.Get(second);
            var instances = world.InstanceCount;
            Require(world.IdleMarkerAvailable(first, 0, actor, Occupied) &&
                world.ReserveIdleMarker(first, 0, actor, Occupied) && world.OwnsIdleMarker(first, 0, actor),
                "An empty source marker could not be reserved before travel.");
            Require(!world.IdleMarkerAvailable(first, 0, actor, Occupied) &&
                !world.IdleMarkerAvailable(first, 0, other, Occupied) &&
                world.ReserveIdleMarker(first, 0, actor, Occupied) &&
                !world.ReserveIdleMarker(first, 0, other, Occupied),
                "Marker candidate availability confused an owned reservation with an empty slot.");
            Require(world.IdleMarkerAvailable(second, 0, other, Occupied) &&
                world.ReserveIdleMarker(second, 0, other, Occupied),
                "Two placements sharing an IDLM base incorrectly shared capacity.");
            world.ReleaseIdleMarker(first, 0, other);
            Require(world.OwnsIdleMarker(first, 0, actor) && !world.ReserveIdleMarker(first, 0, other, Occupied),
                "A competing actor stole or released the existing source reservation.");
            observations.Add((first, 0));
            Require(!world.ReserveIdleMarker(first, 0, actor, Occupied) && world.OwnsIdleMarker(first, 0, actor),
                "Physical occupancy was bypassed by an idempotent reserve or cleared the retained reservation.");
            world.ReleaseIdleMarker(first, 0, actor);
            Require(!world.OwnsIdleMarker(first, 0, actor) && !world.IdleMarkerAvailable(first, 0, other, Occupied) &&
                !world.ReserveIdleMarker(first, 0, other, Occupied),
                "Releasing a reservation also invented empty physical occupancy.");
            observations.Remove((first, 0));
            Require(world.IdleMarkerAvailable(first, 0, other, Occupied) &&
                world.ReserveIdleMarker(first, 0, other, Occupied) && world.InstanceCount == instances,
                "Cleared occupancy/reservation failed to admit the next actual actor or invented a proxy.");
            world.ReleaseIdleMarker(first, 0, actor);
            Require(world.OwnsIdleMarker(first, 0, other), "A stale wrong-actor release removed the replacement owner.");
            world.ReleaseIdleMarker(first, 0, other); world.ReleaseIdleMarker(second, 0, other);
            foreach (var participant in new[] { world.Get(first), world.Get(actor) })
            {
                participant.Enabled = false; Refused(); participant.Enabled = true;
                participant.Deleted = true; Refused(); participant.Deleted = false;
                participant.DeletePending = true; Refused(); participant.DeletePending = false;
                participant.Taken = true; Refused(); participant.Taken = false;
            }
            void Refused() => Require(!world.IdleMarkerAvailable(first, 0, actor, Occupied) &&
                !world.ReserveIdleMarker(first, 0, actor, Occupied) && !world.OwnsIdleMarker(first, 0, actor),
                "An unavailable actor or marker acquired a reservation.");
            Reject(() => world.ReserveIdleMarker(first, 1, actor, Occupied));
            Reject(() => world.ReserveIdleMarker(Key(0x710), 0, actor, Occupied));
            Reject(() => world.ReserveIdleMarker(Key(0x904), 0, actor, Occupied));
            Reject(() => world.ReserveIdleMarker(Key(0x905), 0, actor, Occupied));
            Reject(() => world.ReserveIdleMarker(first, 0, first, Occupied));
            Reject(() => world.ReserveIdleMarker(first, 0, Key(0x906), Occupied));
            Reject(() => world.ReserveIdleMarker(first, 0, Key(0x907), Occupied));
            try
            {
                _ = world.ReserveIdleMarker(first, 0, actor, null!);
                throw new InvalidDataException("Missing occupancy owner was treated as an empty marker.");
            }
            catch (ArgumentNullException) { }
            try
            {
                _ = world.ReserveIdleMarker(first, 0, actor, (_, _) => throw new NotSupportedException("occupancy unavailable"));
                throw new InvalidDataException("Unobserved occupancy was treated as an empty marker.");
            }
            catch (NotSupportedException error) when (error.Message == "occupancy unavailable") { }
            Require(!world.OwnsIdleMarker(first, 0, actor), "Failed occupancy observation partially acquired a marker.");
            using (var source = new FalloutReferenceWorld(records))
            {
                string Hash(uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
                var participant = source.Get(actor); var competitor = Key(0x908);
                _ = source.Get(competitor); _ = source.Get(first);
                participant.Animation.Change("meshes/fixture/idle.kf", new string('a', 64));
                participant.Animation.Advance(.5);
                var assignment = new FalloutActorPackageAssignment(Key(0x720), Hash(0x720), false);
                participant.PackageAssignment = assignment;
                participant.PackageMotion = new(Key(0x720), Hash(0x720), "meshes/fixture/idle.kf", new string('a', 64),
                    .5, false, [0, 0, 0], [0, 0, 0, 1], Sandbox: new(FalloutSandboxActivity.IdleMarker, 9, 1,
                        new FalloutSandboxPreferences().Capture(), first));
                participant.SandboxMarker = new(assignment, 0, null, null, null, 123, 0, null, Key(0x720),
                    new(false, false, false, false, false, false, 0), null, first, Key(0x710), Hash(0x710),
                    FalloutSandboxMarkerPhase.Approach, null);
                Require(source.ReserveIdleMarker(first, 0, actor, source.IdleMarkerOccupied), "Source continuation did not own its marker.");
                var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(source.Capture()))!;
                using var cold = new FalloutReferenceWorld(records);
                cold.Restore(snapshots); // Its internal validation world has disposed before this returns.
                Require(cold.OwnsIdleMarker(first, 0, actor) &&
                    cold.Get(actor).SandboxMarker?.Marker == first &&
                    !cold.ReserveIdleMarker(first, 0, competitor, cold.IdleMarkerOccupied),
                    "Cold world lost its validated marker claim or admitted a competing owner after temporary-world disposal.");
                var retained = snapshots.Single(value => value.Reference == actor);
                using var conflict = new FalloutReferenceWorld(records);
                Reject(() => conflict.Restore(snapshots.Select(value => value.Reference == competitor
                    ? retained with { Reference = competitor } : value).ToArray()));
                Require(conflict.InstanceCount == 0 && !conflict.OwnsIdleMarker(first, 0, actor),
                    "Conflicting cold marker continuations partially published actors or reservations.");
                var markerPrefix = participant.SandboxMarker! with { Phase = FalloutSandboxMarkerPhase.Dispatch, NativePhase = 2 };
                Require(RuntimeNativeNpc.SandboxMarkerProcedure(markerPrefix.NativePhase.Value) == 0 &&
                    RuntimeNativeNpc.ObserveSandboxMarkerNativePhase(2, movementPending: false, hasSelectedIdle: false) == 2 &&
                    RuntimeNativeNpc.ObserveSandboxMarkerNativePhase(2, movementPending: true, hasSelectedIdle: true) == 2,
                    "Marker selection or movement alone invented active native phase three before first eligibility.");
                participant.SandboxMarker = markerPrefix;
                var dispatchBytes = JsonSerializer.Serialize(source.Capture());
                using var dispatchCold = new FalloutReferenceWorld(records);
                dispatchCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(dispatchBytes)!);
                var restoredMarker = dispatchCold.Get(actor).SandboxMarker!;
                Require(restoredMarker.NativePhase == 2 && RuntimeNativeNpc.SandboxMarkerProcedure(restoredMarker.NativePhase.Value) == 0 &&
                    JsonSerializer.Serialize(dispatchCold.Capture()) == dispatchBytes,
                    "Cold marker dispatch promoted its native phase or replayed source selection.");
                var observedPhase = RuntimeNativeNpc.ObserveSandboxMarkerNativePhase(2, movementPending: false, hasSelectedIdle: true);
                Require(observedPhase == 3 && RuntimeNativeNpc.SandboxMarkerProcedure(observedPhase.Value) == 46 &&
                    RuntimeNativeNpc.ObserveSandboxMarkerNativePhase(3, movementPending: false, hasSelectedIdle: true) == 3,
                    "A subsequent observed marker IDLE lost its source phase-three procedure.");
                Reject(() => (markerPrefix with { NativePhase = observedPhase }).Validate(records, source, participant));
                // Process history may come from a different marker. The
                // native observation runs before this marker filters IDLEs.
                participant.SandboxIdleSelection = new(0, Key(0x731));
                participant.SandboxMarker = markerPrefix with { NativePhase = observedPhase };
                var activeBytes = JsonSerializer.Serialize(source.Capture());
                using var activeCold = new FalloutReferenceWorld(records);
                activeCold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(activeBytes)!);
                Require(activeCold.Get(actor).SandboxMarker!.NativePhase == 3 &&
                    activeCold.Get(actor).SandboxIdleSelection!.SelectedIdle == Key(0x731) &&
                    !FalloutIdleCollection.Read(records.GetEffective(Key(0x710))).Idles.Contains(Key(0x731)) &&
                    JsonSerializer.Serialize(activeCold.Capture()) == activeBytes,
                    "Cold marker dispatch lost its inherited process IDLE/observed phase or rebound it to the new marker's eligibility list.");
                Reject(() => (markerPrefix with { Phase = FalloutSandboxMarkerPhase.Approach }).Validate());
                Reject(() => (markerPrefix with { NativePhase = 1 }).Validate());
                var legacyJson = JsonSerializer.SerializeToNode(markerPrefix with
                { Phase = FalloutSandboxMarkerPhase.Playback, NativePhase = null })!.AsObject();
                legacyJson.Remove(nameof(FalloutActorSandboxMarkerContinuation.NativePhase));
                var legacyMarker = legacyJson.Deserialize<FalloutActorSandboxMarkerContinuation>()!;
                legacyMarker.Validate();
                Require(legacyMarker.NativePhase is null &&
                    RuntimeNativeNpc.ObserveSandboxMarkerNativePhase(legacyMarker.NativePhase, movementPending: true, hasSelectedIdle: true) is null &&
                    RuntimeNativeNpc.ObserveSandboxMarkerNativePhase(legacyMarker.NativePhase, movementPending: false, hasSelectedIdle: false) is null &&
                    RuntimeNativeNpc.ObserveSandboxMarkerNativePhase(legacyMarker.NativePhase, movementPending: false, hasSelectedIdle: true) == 3,
                    "An absent native marker phase was invented at load or failed to earn its next actual selected-IDLE observation.");
                Reject(() => RuntimeNativeNpc.SandboxMarkerProcedure(1));
            }
            Console.WriteLine("OPENNV_IDLE_MARKER_RESERVATION_CONTRACT_PASS placedSource=true capacityOne=true separatePlacements=true " +
                "occupancyAndReservationIndependent=true ownerGuard=true disabledDeletedTakenRefused=true unknownOccupancyRefused=true coldContinuation=true atomicConflict=true firstDispatchProcedure0=true inheritedDispatchProcedure46=true coldNativePhase=true absentPhaseRemainsUnknownUntilObserved=true gameplay=unverified");
        }
        finally { File.Delete(path); Directory.Delete(directory, recursive: false); }
    }

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6);
        return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24);
        return bytes;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private static void Reject(Action request)
    {
        try { request(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid idle marker reservation source was admitted.");
    }
}
