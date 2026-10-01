using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class PlayerMoveContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-player-moves-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[20]; header[16] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2);
            var source = "ref target\nshort prefix\nbegin GameMode\nset target to ArrivalREF\n" +
                "Player.MoveTo target (2) (-3) (4)\nset prefix to prefix + 1\nend";
            File.WriteAllBytes(Path.Combine(directory, "Moves.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("ACTI", 0x700),
                Record("QUST", 0x600, Field("EDID", Text("MoveQuest")), Field("DATA", [1, 0, 0, 0, 0, 0, 0, 0]),
                    Field("SCRI", BitConverter.GetBytes(0x500u))),
                Record("SCPT", 0x500, Field("SCHR", header), Local(1, "target"), Local(2, "prefix"),
                    Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCRO", BitConverter.GetBytes(0x901u)),
                    Field("SCRO", BitConverter.GetBytes(0x900u)),
                    Field("SCTX", Text(source))), Cell(0x800, 0x900, "OriginREF", [1, 2, 3, 0, 0, 0]),
                Cell(0x801, 0x901, "ArrivalREF", [10, 20, 30, .1f, .2f, .3f])));
            using var records = FalloutPluginStack.Load(directory, ["Moves.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var executor = new FalloutReferenceScripts(records, world, quests,
                new((_, _) => false, _ => throw new InvalidOperationException("Player movement invented a presentation effect.")));
            var quest = records.GetEffective(Key(0x600)); var script = records.GetEffective(Key(0x500));
            void Run(string body) => executor.ExecuteProgram(quest, script,
                FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
            executor.ExecuteProgram(quest, script, FalloutGameModeProgram.Read(source), .1);
            var move = world.PlayerMoves.Next!;
            Require(move is { Source.ObjectId: 0x600, Destination.ObjectId: 0x901, X: 2, Y: -3, Z: 4 } &&
                quests.Variable(Key(0x600), 2) == 1 && world.Placement(Key(0x900)).Cell == Key(0x800),
                "Player MoveTo lost its compiled variable, source prefix, offsets or deferred disposition.");
            var placement = world.ResolvePlayerMove(move, new(Key(0x800), [1, 2, 3], [0, 0, 0]), 1);
            Require(placement.Cell == Key(0x801) && placement.Position.SequenceEqual([12f, 17f, 34f]) &&
                placement.RotationRadians.SequenceEqual([.1f, .2f, .3f]), "Player MoveTo did not resolve source placement.");
            Reject(() => world.Capture());
            Reject(() => Run("Player.MoveTo ArrivalREF 1e40\nset prefix to 99"));
            Require(ReferenceEquals(move, world.PlayerMoves.Next) && quests.Variable(Key(0x600), 2) == 1,
                "Rejected movement changed the pending request or executed its suffix.");
            world.PlayerMoves.Complete(move);

            var fallback = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
                defaultProcessingDelay: .01f, references: world)
            { Host = new((_, _) => throw new InvalidOperationException("Unexpected stage."), _ => 0, executor.ExecuteProgram) };
            fallback.Advance(1);
            Require(fallback.Capture().Instances.Single().Error is null && quests.Variable(Key(0x600), 2) == 2 &&
                world.PlayerMoves.Next?.Destination == Key(0x901), "Quest execution did not use the shared player movement owner.");
            world.PlayerMoves.Complete(world.PlayerMoves.Next!);
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
            Require(!cold.PlayerMoves.Pending && cold.PlayerMoves.Error is null, "Cold world inherited a retired movement request.");

            Run("Player.MoveTo ArrivalREF 8\nPlayer.MoveTo OriginREF");
            var first = world.PlayerMoves.Next!;
            world.SetPlacement(Key(0x901), new(Key(0x801), [100, 200, 300], [0, 0, .5f]));
            Require(world.ResolvePlayerMove(first, placement, 1).Position.SequenceEqual([108f, 200f, 300f]),
                "Queued movement used stale target placement.");
            world.PlayerMoves.Complete(first);
            var second = world.PlayerMoves.Next!;
            Require(second.Destination == Key(0x900), "Independent movement requests lost their order.");
            world.PlayerMoves.Fail(second, new InvalidDataException("Unavailable destination presentation."));
            Require(world.PlayerMoves.Next is null && world.PlayerMoves.Pending && world.PlayerMoves.Error is not null,
                "Failed movement lost its request or became eligible for automatic retry.");
            Reject(() => world.Capture());
            Reject(() => Run("Player.MoveTo ArrivalREF"));
            world.Dispose();
            Require(!world.PlayerMoves.Pending && world.PlayerMoves.Error is null, "World retirement retained movement state.");
            Console.WriteLine("OPENNV_PLAYER_MOVE_CONTRACT_PASS source=true typedTarget=true offsets=true queued=true suffix=true sharedQuest=true invalidAtomic=true failureRetained=true cold=true retirement=true parity=unverified");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static FalloutFormKey Key(uint id) => new("Moves.esm", id);
    private static byte[] Cell(uint id, uint reference, string name, float[] transform)
    {
        var body = Record("REFR", reference, Field("EDID", Text(name)), Field("NAME", BitConverter.GetBytes(0x700u)),
            Field("DATA", transform.SelectMany(BitConverter.GetBytes).ToArray()));
        var group = new byte[24 + body.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), id); BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6);
        body.CopyTo(group, 24);
        return Join(Record("CELL", id, Field("DATA", [1])), group);
    }
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
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
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unsupported player movement was accepted.");
    }
}
