using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class FollowPackageContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-follow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory, "Follow.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Package(1, 330), Package(2, 500), Package(3, 330, location: true), Package(4, 0), Package(5, 330, targetType: 1)));
            using var records = FalloutPluginStack.Load(directory, ["Follow.esm"]);
            FalloutPluginRecord Source(uint id) => records.GetEffective(new("Follow.esm", id));
            var shortFollow = FalloutFollowPackage.Read(Source(1));
            if (shortFollow.Target != new FalloutFormKey("Follow.esm", 0x14) || shortFollow.Distance != 330 ||
                FalloutFollowPackage.Read(Source(2)).Distance != 500 || FalloutScriptPackage.Read(Source(1)).LocationType is not null)
                throw new InvalidOperationException("Follow invented a start location or lost its target/distance.");
            Reject(() => FalloutFollowPackage.Read(Source(3)));
            Reject(() => FalloutFollowPackage.Read(Source(4)));
            Reject(() => FalloutFollowPackage.Read(Source(5)));
            var motion = new FalloutActorPackageMotion(shortFollow.Form, new string('a', 64), "meshes/actor/mtforward.kf",
                new string('b', 64), 3.25, false, [2, 3, 4], [0, 0, 0, 1]);
            var restored = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(motion))!;
            restored.Validate();
            if (restored.Seconds != 3.25 || !restored.Position.SequenceEqual(motion.Position))
                throw new InvalidOperationException("Follow persistence lost motion state.");
            Reject(() => (motion with { Seconds = double.NaN }).Validate());
            Reject(() => (motion with { Rotation = [0, 0, 0, 0] }).Validate());
            Reject(() => (motion with { StartPending = true }).Validate());
            Reject(() => (motion with { PackageSha256 = "changed" }).Validate());
            Console.WriteLine("OPENNV_FOLLOW_PACKAGE_CONTRACT_PASS optionalStart=true sourceTarget=true distances=true coldClock=true malformedRejected=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Package(uint id, int distance, bool location = false, int targetType = 0)
    {
        var data = new byte[12]; data[4] = 1; data[11] = 0xa5;
        var target = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(target, targetType);
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 0x14);
        BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(8), distance); target[15] = 0xcc;
        return Record("PACK", id, Field("EDID", Encoding.ASCII.GetBytes("SourceFollow\0")), Field("PKDT", data),
            Field("PTDT", target), location ? Field("PLDT", new byte[12]) : []);
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
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid follow source/state was accepted.");
    }
}
