using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class PlayerScriptPackageContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-player-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Packages.esm"), Join(Header(), Package(0x100, 0x200, 0),
                Package(0x101, 0x201, 5), Package(0x102, 0x201, 5, BitConverter.GetBytes(1u)),
                Package(0x103, 0x201, 5, [1, 0]), Package(0x104, 0x201, 5, BitConverter.GetBytes(uint.MaxValue)),
                Package(0x105, 0x201, 5, BitConverter.GetBytes(2u)),
                Record("REFR", 0x200), Record("REFR", 0x201), Record("IDLE", 0x300)));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Packages.esm"), Package(0x100, 0x201, 3)));
            using var records = FalloutPluginStack.Load(directory, ["Packages.esm", "Patch.esp"]);
            var package = FalloutScriptPackage.Read(records.GetEffective(Key(0x100)));
            var exact = FalloutScriptPackage.Read(records.GetEffective(Key(0x101))) with { LocationRadius = 0 };
            Require(package is { Procedure: 6, LocationType: 0, LocationRadius: 3 } && package.LocationReference == Key(0x201),
                "Winning master-adjusted player package location was lost.");
            Require(FalloutScriptPackage.Read(records.GetEffective(Key(0x102))).Idles.SequenceEqual(package.Idles),
                "Four-byte idle count lost its source list or adjusted identity.");
            foreach (var badCount in new[] { 0x103u, 0x104u, 0x105u })
                Reject(() => FalloutScriptPackage.Read(records.GetEffective(Key(badCount))));
            Require(package.ContainsReferenceLocation(Key(0x400), [2, 0, 0], Key(0x400), [0, 0, 0]) &&
                package.ContainsReferenceLocation(Key(0x400), [3, 0, 0], Key(0x400), [0, 0, 0]) &&
                !package.ContainsReferenceLocation(Key(0x400), [3.001f, 0, 0], Key(0x400), [0, 0, 0]) &&
                !package.ContainsReferenceLocation(Key(0x401), [0, 0, 0], Key(0x400), [0, 0, 0]),
                "Package radius boundary or cell identity was ignored.");
            Require(exact.ContainsReferenceLocation(Key(0x400), [1, 2, 3], Key(0x400), [1, 2, 3]) &&
                !exact.ContainsReferenceLocation(Key(0x400), [1, 2, 3.001f], Key(0x400), [1, 2, 3]),
                "Zero radius admitted an unreached destination.");
            Reject(() => package.ContainsReferenceLocation(Key(0x400), [float.NaN, 0, 0], Key(0x400), [0, 0, 0]));
            var saved = new FalloutPlayerScriptPackageSnapshot(package.Form,
                Convert.ToHexString(SHA256.HashData(records.GetEffective(package.Form).ReadData())), Key(0x300),
                new string('A', 64), 1, true, false, .375, 0);
            var session = new FalloutScriptSession(); session.PublishPlayerPackage(saved);
            var cold = new FalloutScriptSession();
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(session.Capture()))!);
            Require(cold.PlayerPackage == saved, "Cold player package lost assignment, phase, cursor, hash or elapsed time.");
            var changing = saved with { EventKind = "POCA", PendingPackage = Key(0x101), PendingPackageSha256 = new string('B', 64) };
            cold.PublishPlayerPackage(changing);
            cold.Restore(JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(cold.Capture()))!);
            Require(cold.PlayerPackage == changing && cold.PlayerPackage.Phase == "POCA", "Cold change phase lost its pending source assignment.");
            foreach (var invalidChange in new[] { changing with { PendingPackage = null }, changing with { PendingPackageSha256 = null },
                changing with { PendingPackageSha256 = "bad" }, changing with { EventKind = "POBA" }, changing with { PackageEvent = false },
                changing with { EventKind = "POEA" }, changing with { Idle = null, AnimationSha256 = null, PackageEvent = false } })
            {
                Reject(() => cold.PublishPlayerPackage(invalidChange));
                Require(cold.PlayerPackage == changing, "Rejected change phase partially replaced saved assignment.");
            }
            cold.PublishPlayerPackage(saved);
            foreach (var invalid in new[] { saved with { Cursor = -1 }, saved with { Elapsed = double.NaN },
                saved with { Idle = null }, saved with { AnimationSha256 = null }, saved with { PackageSha256 = "bad" },
                saved with { Wait = -1 }, saved with { Complete = true } })
            {
                Reject(() => cold.PublishPlayerPackage(invalid));
                Require(cold.PlayerPackage == saved, "Rejected package state partially published.");
                Reject(() => cold.Restore(cold.Capture() with { PlayerPackage = invalid }));
                Require(cold.PlayerPackage == saved, "Rejected cold package state replaced live assignment.");
            }
            var legacy = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>("{\"Hardcore\":false,\"AutoDisplayObjectives\":true,\"Achievements\":[]}")!;
            Require(legacy.PlayerPackage is null, "Legacy session acquired a script package.");
            cold.PublishPlayerPackage(changing);
            cold.PublishPlayerPackage(null);
            Require(cold.Capture().PlayerPackage is null, "Package removal did not clear saved assignment.");
            var removed = JsonSerializer.Deserialize<FalloutScriptSessionSnapshot>(JsonSerializer.Serialize(cold.Capture()))!;
            cold.PublishPlayerPackage(changing); cold.Restore(removed);
            Require(cold.PlayerPackage is null, "Cold removal restored a stale change or pending assignment.");
            File.WriteAllBytes(Path.Combine(directory, "Bad.esp"), Join(Header("Packages.esm"), Package(0x100, 0, 0)));
            using (var bad = FalloutPluginStack.Load(directory, ["Packages.esm", "Bad.esp"]))
                Reject(() => FalloutScriptPackage.Read(bad.GetEffective(Key(0x100))));
            File.WriteAllBytes(Path.Combine(directory, "Bad.esp"), Join(Header("Packages.esm"), Package(0x100, 0x200, -1)));
            using (var bad = FalloutPluginStack.Load(directory, ["Packages.esm", "Bad.esp"]))
                Reject(() => FalloutScriptPackage.Read(bad.GetEffective(Key(0x100))));
            Console.WriteLine("OPENNV_PLAYER_SCRIPT_PACKAGE_CONTRACT_PASS winning=true masterAdjusted=true idleCountWidths=true radius=true cell=true exact=true cold=true coldChange=true coldRemoval=true legacy=true invalidAtomic=true nativeClock=requires-owned-audit traversal=unbound parity=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static FalloutFormKey Key(uint id) => new("Packages.esm", id);
    private static byte[] Package(uint id, uint target, int radius, byte[]? count = null)
    {
        var data = new byte[12]; data[4] = 6;
        var location = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(location.AsSpan(4), target);
        BinaryPrimitives.WriteInt32LittleEndian(location.AsSpan(8), radius);
        return Record("PACK", id, Field("EDID", Text("Fixture" + id)), Field("PKDT", data), Field("PLDT", location),
            Field("IDLF", [1]), Field("IDLC", count ?? [1]), Field("IDLA", BitConverter.GetBytes(0x300u)));
    }
    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Text(master)), Field("DATA", new byte[8])));
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid or unsupported package state was accepted.");
    }
}
