using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class EditorTravelContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-editor-travel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Join(Header(), Record("NPC_", 0x700),
                Record("CELL", 0x800, Field("DATA", [1])), Actors([1, 2, 3, .1f, .2f, .3f]),
                Package(0x400, 6, 3, 0x1006), Package(0x401, 6, 2, 0x1006),
                Package(0x402, 8, 3, 0x1006), Package(0x403, 6, 3, 0x100e),
                Package(0x404, 6, 3, 0x1006, radius: -1), Package(0x405, 6, 3, 0x1006, search: true)));
            File.WriteAllBytes(Path.Combine(directory, "Patch.esp"), Join(Header("Base.esm"), Actors([7, 8, 9, .4f, .5f, .6f])));
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Patch.esp"]);
            FalloutFormKey Key(uint id) => new("Base.esm", id);
            using var world = new FalloutReferenceWorld(records);
            var actor = Key(0x900); var source = records.GetEffective(Key(0x400));
            var package = FalloutEditorTravelPackage.Read(source);
            Require(package.Radius == 16 && package.MustComplete && !package.Running && !package.WeaponDrawn,
                "Editor travel lost source procedure or movement declarations.");
            foreach (var id in new uint[] { 0x401, 0x402, 0x403, 0x404, 0x405 })
                Reject(() => FalloutEditorTravelPackage.Read(records.GetEffective(Key(id))));
            var progress = package.Start(world, actor);
            Require(progress.Cell == Key(0x800) && progress.Location.SequenceEqual([7f, 8f, 9f]) && !progress.Complete,
                "Editor destination lost its winning source pose or granted arrival.");
            world.SetPlacement(actor, new(Key(0x800), [70, 80, 90], [1, 2, 3]));
            Require(package.Start(world, actor).Location.SequenceEqual([7f, 8f, 9f]) &&
                world.Placement(actor).Position.SequenceEqual([70f, 80f, 90f]),
                "Editor travel substituted the current moved pose for its source destination.");
            var copy = world.EditorPlacement(actor); copy.Position[0] = 1000;
            package.Validate(world, actor, progress);
            Reject(() => package.Validate(world, actor, progress with { Location = [70, 80, 90] }));
            Reject(() => (progress with { Location = [float.NaN, 8, 9] }).Validate());
            var motion = new FalloutActorPackageMotion(package.Form, Convert.ToHexString(SHA256.HashData(source.ReadData())),
                "meshes/actor/mtforward.kf", new string('a', 64), 3, false, [70, 80, 90], [0, 0, 0, 1], EditorTravel: progress);
            motion.Validate();
            Reject(() => (motion with { Escort = new(false, false) }).Validate());
            world.Get(actor).PackageMotion = motion;
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            var restored = cold.Get(actor).PackageMotion!.EditorTravel!;
            package.Validate(cold, actor, restored);
            using (var rejected = new FalloutReferenceWorld(records))
            {
                Reject(() => rejected.Restore(saved.Select(snapshot => snapshot with
                { PackageMotion = motion with { EditorTravel = progress with { Location = [70, 80, 90] } } }).ToArray()));
                Require(rejected.Capture().Count == 0, "Rejected editor destination partially restored world state.");
            }
            Require(!restored.Complete && cold.Placement(actor).Position.SequenceEqual([70f, 80f, 90f]),
                "Cold source destination lost distinct current pose or granted arrival.");
            var events = new List<string>(); var lifecycle = new FalloutPackageEvents((_, kind) => events.Add(kind));
            lifecycle.Restore(FalloutScriptPackage.Read(source), false); lifecycle.Complete(); lifecycle.Complete();
            var completed = restored with { Complete = true };
            var completedMotion = motion with { EditorTravel = completed };
            var coldCompleted = JsonSerializer.Deserialize<FalloutActorPackageMotion>(JsonSerializer.Serialize(completedMotion))!;
            coldCompleted.Validate();
            var restoredLifecycle = new FalloutPackageEvents((_, kind) => events.Add(kind));
            restoredLifecycle.Restore(FalloutScriptPackage.Read(source), coldCompleted.EditorTravel!.Complete);
            restoredLifecycle.Complete();
            Require(events.SequenceEqual(["POEA"]), "Travel completion or restoration replayed a source lifecycle event.");
            Console.WriteLine("OPENNV_EDITOR_TRAVEL_CONTRACT_PASS winningPose=true movedPoseDistinct=true copied=true invalidSourceRejected=true cold=true conflictingProgressRejected=true eventOnce=true");
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Base.esm")); File.Delete(Path.Combine(directory, "Patch.esp"));
            Directory.Delete(directory);
        }
    }

    private static byte[] Header(string? master = null) => Record("TES4", 0, Field("HEDR", new byte[12]),
        master is null ? [] : Join(Field("MAST", Encoding.ASCII.GetBytes(master + '\0')), Field("DATA", new byte[8])));
    private static byte[] Actors(float[] pose)
    {
        var reference = Record("ACHR", 0x900, Field("NAME", BitConverter.GetBytes(0x700u)),
            Field("DATA", pose.SelectMany(BitConverter.GetBytes).ToArray()));
        var group = new byte[24 + reference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        reference.CopyTo(group, 24); return group;
    }
    private static byte[] Package(uint id, byte procedure, int locationType, uint flags, int radius = 16, bool search = false)
    {
        var data = new byte[12]; BinaryPrimitives.WriteUInt32LittleEndian(data, flags); data[4] = procedure; data[5] = 0xcd;
        data[10] = data[11] = 0xcd;
        var location = new byte[12]; BinaryPrimitives.WriteInt32LittleEndian(location, locationType);
        BinaryPrimitives.WriteInt32LittleEndian(location.AsSpan(8), radius);
        return Record("PACK", id, Field("EDID", "EditorTravel\0"u8.ToArray()), Field("PKDT", data), Field("PLDT", location),
            search ? Field("PLD2", location) : []);
    }
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
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Invalid editor travel state was accepted.");
    }
}
