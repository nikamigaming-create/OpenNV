using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

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
                Reference("REFR", 0x906, 0x700), Reference("ACHR", 0x907, 0x701));
            var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
            BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); references.CopyTo(group, 24);
            File.WriteAllBytes(path, Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("NPC_", 0x700), Record("CREA", 0x701), Record("STAT", 0x711),
                Record("IDLM", 0x710, Field("IDLF", [0]), Field("IDLC", [0]), Field("IDLT", BitConverter.GetBytes(0f))),
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
            Console.WriteLine("OPENNV_IDLE_MARKER_RESERVATION_CONTRACT_PASS placedSource=true capacityOne=true separatePlacements=true " +
                "occupancyAndReservationIndependent=true ownerGuard=true disabledDeletedTakenRefused=true unknownOccupancyRefused=true gameplay=unverified");
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
