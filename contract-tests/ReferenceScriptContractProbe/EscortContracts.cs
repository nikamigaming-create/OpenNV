using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class EscortContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-escort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Join(Header(), Package(1, 200), Package(2, 0),
                Package(3, 200, targetType: 1), Package(4, 200, radius: -1), Package(5, 200, duplicate: true),
                Package(6, 200, target: 0), Package(7, 200, flags: 0x8), Package(8, 200, distanceSize: 8),
                FaultFixture()));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Base.esm"), Package(1, 320, target: 0x01000015, destination: 0x01000030)));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
            FalloutPluginRecord Source(uint id) => records.GetEffective(new("Base.esm", id));
            var source = Source(1);
            var escort = FalloutEscortPackage.Read(source);
            Require(escort.Distance == 320 && escort.Target == new FalloutFormKey("Patch.esp", 0x15) &&
                escort.Destination == new FalloutFormKey("Patch.esp", 0x30), "Escort lost winning override or adjusted reference identities.");
            Require(FalloutEscortPackage.Read(Source(2)).Distance == 0, "Zero authored distance was replaced with an invented default.");
            foreach (var id in new uint[] { 3, 4, 5, 6, 7, 8 }) Reject(() => FalloutEscortPackage.Read(Source(id)));
            var initial = new FalloutEscortProgress(false, false);
            var acquire = escort.Advance(initial, 400, 800, 900, false);
            Require(acquire.Action == FalloutEscortAction.ApproachTarget && !acquire.Progress.TargetAcquired, "Escort began leading before reaching its target.");
            var lead = escort.Advance(initial, 100, 800, 900, false);
            Require(lead.Action == FalloutEscortAction.Lead && lead.Progress.TargetAcquired, "Escort did not begin leading its nearby target.");
            var waiting = escort.Advance(lead.Progress, 400, 800, 900, false);
            Require(waiting.Action == FalloutEscortAction.WaitForTarget, "Escort abandoned its out-of-range trailing target.");
            Require(escort.Advance(waiting.Progress, 400, 800, 200, false).Action == FalloutEscortAction.Lead,
                "Escort refused to move when the distant target was already closer to the destination.");
            Require(escort.Advance(waiting.Progress, 100, 800, 900, false).Action == FalloutEscortAction.Lead,
                "Escort did not resume when its target caught up.");
            Require(escort.Advance(waiting.Progress, 400, 0, 400, true).Action == FalloutEscortAction.WaitForTarget,
                "Leader arrival completed an escort whose target remained behind.");
            var completed = escort.Advance(waiting.Progress, 100, 0, 100, true);
            Require(completed.Action == FalloutEscortAction.Complete && completed.Progress.Complete,
                "Observed supported destination arrival did not complete the escort.");
            var motion = new FalloutActorPackageMotion(escort.Form, Convert.ToHexString(SHA256.HashData(source.ReadData())),
                "meshes/actor/mtidle.kf", new('a', 64), 2.5, false, [1, 2, 3], [0, 0, 0, 1], Escort: waiting.Progress);
            var restored = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(motion))!;
            restored.Validate();
            Require(escort.Advance(restored.Escort!, 400, 800, 900, false).Action == FalloutEscortAction.WaitForTarget &&
                restored.Seconds == motion.Seconds && restored.Position.SequenceEqual(motion.Position),
                "Cold escort forgot target acquisition, animation clock or native pose.");
            var events = new List<string>();
            var declaration = FalloutScriptPackage.Read(source);
            var lifecycle = new FalloutPackageEvents((_, kind) => events.Add(kind));
            lifecycle.Restore(declaration, false); lifecycle.Complete(); lifecycle.Complete();
            Require(events.SequenceEqual(["POEA"]), "Cold active escort replayed its start or completion event.");
            events.Clear();
            var finished = new FalloutPackageEvents((_, kind) => events.Add(kind));
            finished.Restore(declaration, true); finished.Complete();
            Require(events.Count == 0, "Cold completed escort replayed a consumed package event.");
            FalloutPackageEvents reentrant = null!;
            var next = FalloutScriptPackage.Read(Source(2));
            reentrant = new((_, kind) => { if (kind == "POEA") reentrant.Change(next); });
            reentrant.Change(declaration); reentrant.Complete();
            Require(reentrant.Active == next && !reentrant.Done, "A completion result incorrectly completed its newly selected package.");
            Reject(() => (restored with { Escort = new(false, true) }).Validate());
            Reject(() => escort.Advance(initial, float.NaN, 0, 0, false));
            CheckColdPackageFault(records, motion);
            Console.WriteLine("OPENNV_ESCORT_CONTRACT_PASS winningReferences=true sourceDistance=true acquireLeadWait=true targetAhead=true observedArrival=true coldProgress=true eventNoReplay=true retainedFaultPrefix=true uncompletedResultRefused=true malformedRejected=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Header(string? master = null)
    {
        var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return Record("TES4", 0, Field("HEDR", data), master is null ? [] : Field("MAST", Encoding.ASCII.GetBytes(master + '\0')),
            master is null ? [] : Field("DATA", new byte[8]));
    }
    private static byte[] Package(uint id, uint distance, int targetType = 0, int radius = 0, bool duplicate = false,
        uint target = 0x14, uint destination = 0x30, uint flags = 0x401002, int distanceSize = 4)
    {
        var data = new byte[12]; data[4] = 2; data[5] = 0xcd; data[11] = 0xa5; BinaryPrimitives.WriteUInt32LittleEndian(data, flags);
        var location = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(location.AsSpan(4), destination);
        BinaryPrimitives.WriteInt32LittleEndian(location.AsSpan(8), radius);
        var targets = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(targets, targetType);
        BinaryPrimitives.WriteUInt32LittleEndian(targets.AsSpan(4), target);
        var spacing = new byte[distanceSize]; BinaryPrimitives.WriteUInt32LittleEndian(spacing, distance);
        return Record("PACK", id, Field("EDID", Encoding.ASCII.GetBytes("SourceEscort\0")), Field("PKDT", data),
            Field("PLDT", location), Field("PTDT", targets), Field("PKE2", spacing), duplicate ? Field("PKE2", spacing) : []);
    }
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported escort source or progress was accepted.");
    }
}
