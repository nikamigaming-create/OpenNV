using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

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
                Package(1, 330), Package(2, 500), Package(3, 330, location: true), Package(4, 0), Package(5, 330, targetType: 1),
                Package(6, 128, procedure: 7, flags: 0x04501000), Package(7, 128, procedure: 7, flags: 0x04501001),
                Package(8, 128, location: true, procedure: 7), Package(9, 0, procedure: 7),
                Package(10, 128, targetType: 1, procedure: 7), Package(11, 128, procedure: 7, specificFlags: 1),
                Package(12, 128, procedure: 7, flags: 0x3000)));
            using var records = FalloutPluginStack.Load(directory, ["Follow.esm"]);
            FalloutPluginRecord Source(uint id) => records.GetEffective(new("Follow.esm", id));
            var shortFollow = FalloutFollowPackage.Read(Source(1));
            if (shortFollow.Target != new FalloutFormKey("Follow.esm", 0x14) || shortFollow.Distance != 330 ||
                FalloutFollowPackage.Read(Source(2)).Distance != 500 || FalloutScriptPackage.Read(Source(1)).LocationType is not null)
                throw new InvalidOperationException("Follow invented a start location or lost its target/distance.");
            Reject(() => FalloutFollowPackage.Read(Source(3)));
            Reject(() => FalloutFollowPackage.Read(Source(4)));
            Reject(() => FalloutFollowPackage.Read(Source(5)));
            var accompany = FalloutFollowPackage.ReadAccompany(Source(6));
            if (!accompany.Accompany || !accompany.DefensiveCombat || accompany.Running ||
                accompany.Target != shortFollow.Target || accompany.Distance != 128 || accompany.Flags != 0x04501000 ||
                accompany.AllowsUnprovokedCombat(null) || accompany.AllowsUnprovokedCombat(Source(1).FormKey) ||
                !accompany.AllowsUnprovokedCombat(accompany.Target) || !shortFollow.AllowsUnprovokedCombat(null) ||
                !FalloutFollowPackage.ReadAccompany(Source(12)).Running)
                throw new InvalidOperationException("Accompany lost its target, distance or defensive target protection policy.");
            Reject(() => FalloutFollowPackage.ReadAccompany(Source(1)));
            Reject(() => FalloutFollowPackage.Read(Source(6)));
            foreach (var id in new uint[] { 7, 8, 9, 10, 11 }) Reject(() => FalloutFollowPackage.ReadAccompany(Source(id)));
            var leader = Godot.Vector3.Zero; var goal = new Godot.Vector3(100, 0, 0);
            var lateral = RuntimeNativeCreature.AccompanyApproach(new(100, 0, 50), leader, goal, 2);
            var behind = RuntimeNativeCreature.AccompanyApproach(new(-10, 0, 0), leader, goal, 2);
            if (lateral.Destination != leader || lateral.Distance != 2 ||
                behind.Destination != goal || behind.Distance != 98)
                throw new InvalidOperationException("Accompany destination knowledge lost the actual leader distance.");
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
            var accompanyMotion = motion with { Package = accompany.Form };
            var coldAccompany = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(accompanyMotion))!;
            coldAccompany.Validate();
            if (coldAccompany.Package != accompany.Form || coldAccompany.Seconds != accompanyMotion.Seconds ||
                !coldAccompany.Position.SequenceEqual(accompanyMotion.Position))
                throw new InvalidOperationException("Accompany cold motion lost its source identity, clock or pose.");
            Console.WriteLine("OPENNV_FOLLOW_PACKAGE_CONTRACT_PASS optionalStart=true sourceTarget=true distances=true coldClock=true accompany=true defensiveTarget=true malformedRejected=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Package(uint id, int distance, bool location = false, int targetType = 0,
        byte procedure = 1, uint flags = 0, ushort specificFlags = 0)
    {
        var data = new byte[12]; data[4] = procedure; data[11] = 0xa5;
        BinaryPrimitives.WriteUInt32LittleEndian(data, flags);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), specificFlags);
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
