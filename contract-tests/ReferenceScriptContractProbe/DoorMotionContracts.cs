using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class DoorMotionContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-door-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var local = new byte[24]; UInt(local, 0, 1);
            var placed = Record("REFR", 0x90, Field("EDID", Text("DoorRef")), Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24]));
            var group = new byte[24 + placed.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x80); UInt(group, 12, 6); placed.CopyTo(group, 24);
            File.WriteAllBytes(Path.Combine(directory, "Doors.esm"), Join(Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("DOOR", 1), Record("CELL", 0x80, Field("DATA", [1])), group,
                Record("SCPT", 0x50, Field("SLSD", local), Field("SCVR", Text("sample")),
                    Field("SCRO", BitConverter.GetBytes(0x90u)), Field("SCTX", Text("short sample\nbegin GameMode\nend"))),
                Record("QUST", 0x60, Field("SCRI", BitConverter.GetBytes(0x50u)))));
            using var records = FalloutPluginStack.Load(directory, ["Doors.esm"]);
            var cell = FalloutCellSceneReader.Read(records, Key(0x80));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var instance = world.Get(Key(0x90));
            var hash = new string('a', 64);
            instance.DoorMotion = new(3, hash, false, false);
            var quests = new FalloutQuestState(records);
            var requests = 0;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                Require(effect.Kind == FalloutReferenceEffectKind.DoorOpenState && effect.Target == Key(0x90),
                    "Script door command dispatched activation or changed another target.");
                var requested = instance.DoorMotion!.Request(effect.Enable);
                if (requested == instance.DoorMotion) return;
                instance.DoorOpen = effect.Enable; instance.DoorMotion = requested; requests++;
            }));
            void Run(string source) => scripts.ExecuteProgram(records.GetEffective(Key(0x60)), records.GetEffective(Key(0x50)),
                FalloutGameModeProgram.Read("begin GameMode\n" + source + "\nend"), 0);
            Run("set sample to DoorRef.GetOpenState"); Require(quests.Variable(Key(0x60), 1) == 3, "Closed state is not 3.");
            Run("DoorRef.SetOpenState 1\nset sample to DoorRef.GetOpenState");
            Require(requests == 1 && instance.DoorOpen && quests.Variable(Key(0x60), 1) == 2, "Opening target/query lost shared state.");
            Run("DoorRef.SetOpenState 1"); Require(requests == 1, "Duplicate target restarted an unfinished door.");
            Reject(() => Run("set sample to 7\nDoorRef.SetOpenState 0\nset sample to 99"));
            Require(requests == 1 && quests.Variable(Key(0x60), 1) == 7 && instance.DoorOpen,
                "Unbound moving reversal discarded its prefix or executed the suffix.");
            instance.DoorMotion = instance.DoorMotion! with { Moving = false };
            Run("set sample to DoorRef.GetOpenState"); Require(quests.Variable(Key(0x60), 1) == 1, "Open state is not 1.");
            foreach (var argument in new[] { "2", "-1", "0.5", "0 1" }) Reject(() => Run("DoorRef.SetOpenState " + argument));
            Require(requests == 1 && instance.DoorOpen, "Invalid Boolean/arity changed door state.");
            Run("DoorRef.SetOpenState 0\nset sample to DoorRef.GetOpenState");
            Require(requests == 2 && !instance.DoorOpen && quests.Variable(Key(0x60), 1) == 4, "Closing state is not 4.");
            instance.ObjectAnimations = [new(3, hash, "Close", .2, false)];
            var snapshots = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshots); cold.LoadCell(cell);
            Require(cold.Get(Key(0x90)).DoorMotion == instance.DoorMotion &&
                cold.Get(Key(0x90)).ObjectAnimations!.Single().ElapsedSeconds == .2, "Cold door target/phase did not retain.");
            foreach (var invalid in new[]
            {
                snapshots.Single() with { DoorOpen = true }, snapshots.Single() with { ObjectAnimations = null },
                snapshots.Single() with { DoorMotion = instance.DoorMotion! with { Controller = 9 } },
                snapshots.Single() with { ObjectAnimations = [new(3, hash, "Open", .2, false)] },
            })
            {
                using var rejected = new FalloutReferenceWorld(records);
                Reject(() => rejected.Restore([invalid])); Require(rejected.InstanceCount == 0, "Corrupt door restoration partially committed.");
            }
            Console.WriteLine("OPENNV_DOOR_MOTION_STATE_PASS typedTarget=true states=1,2,3,4 duplicateNoRestart=true noActivation=true prefix=true coldPhase=true corruptAtomic=true reversal=unbound sourcePose=unverified");
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private static FalloutFormKey Key(uint id) => new("Doors.esm", id);
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        UInt(bytes, 4, (uint)data.Length); UInt(bytes, 12, id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(value => value).ToArray();
    private static void UInt(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unbound or invalid door state was accepted.");
    }
}
