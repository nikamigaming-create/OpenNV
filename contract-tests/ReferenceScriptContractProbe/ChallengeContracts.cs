using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ChallengeContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-challenge-");
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Challenge.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Challenge(0x100, 13, 2, 1), Challenge(0x101, 13, 2, 2), Challenge(0x102, 11, 2, 2, 27),
                Challenge(0x103, 0, 1, 0), Challenge(0x104, 13, 1, 0, script: 0x500),
                Record("SCPT", 0x500), Record("QUST", 0x600, Field("SCRO", BitConverter.GetBytes(0x100u)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Challenge.esm"]);
            var queue = new FalloutHudNotifications(); var owner = new FalloutChallenges(records, queue);
            owner.IncrementScripted(Key(0x100));
            Require(owner.Locked(Key(0x100)) && owner.State(Key(0x100)).Progress == 0 && queue.Capture().Pending.Count == 0, "Locked challenge progressed.");
            using var world = new FalloutReferenceWorld(records);
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("Challenge used an unrelated host effect."), Challenges: owner));
            var quest = records.GetEffective(Key(0x600));
            executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "UnlockChallenge Test100\nIncrementScriptedChallenge Test100");
            Require(!owner.Locked(Key(0x100)) && owner.State(Key(0x100)).Progress == 1 && owner.ChallengesCompleted == 0,
                "Bound source command missed unlock or partial progress.");
            var coldQueue = new FalloutHudNotifications(); coldQueue.Restore(queue.Capture());
            var cold = new FalloutChallenges(records, coldQueue);
            cold.Restore(JsonSerializer.Deserialize<FalloutChallengesSnapshot>(JsonSerializer.Serialize(owner.Capture()))!);
            cold.IncrementScripted(Key(0x100)); cold.IncrementScripted(Key(0x100)); cold.Unlock(Key(0x100)); cold.IncrementScripted(Key(0x100));
            Require(cold.State(Key(0x100)) is { Completed: true, EverCompleted: true, Progress: 2 } && cold.ChallengesCompleted == 1 &&
                cold.State(Key(0x102)).Progress == 1, "Cold completion replayed or dropped its statistic challenge.");
            cold.IncrementScripted(Key(0x101)); cold.IncrementScripted(Key(0x101));
            Require(cold.ChallengesCompleted == 2 && cold.State(Key(0x101)) is { Completed: false, EverCompleted: true, Progress: 0 } &&
                cold.State(Key(0x102)) is { Completed: false, EverCompleted: true, Progress: 0 }, "Recurring challenge did not reset, or statistic recursively counted itself.");
            var notices = coldQueue.Capture().Pending.Select(notice => notice.Event).ToArray();
            Require(notices.Count(value => value.Kind == FalloutHudEventKind.ChallengeCompleted) == 3 &&
                notices[^2].Source == Key(0x102) && notices[^1].Source == Key(0x101), "Challenge completion notices lost source order.");
            cold.IncrementScripted(Key(0x103)); Require(cold.State(Key(0x103)).Progress == 0, "Scripted command incremented a non-scripted challenge.");
            Reject(() => cold.IncrementScripted(Key(0x104)));
            var failed = cold.Capture(); var restore = new FalloutChallenges(records, new()); restore.Restore(failed);
            Reject(() => restore.IncrementScripted(Key(0x104)));
            Require(restore.State(Key(0x104)).Progress == 1 && restore.State(Key(0x104)).Error is not null,
                "Unsupported completion script lost its prefix or replayed cold.");
            var invalid = failed with { Entries = failed.Entries.Select(value => value.Form == Key(0x100) ? value with { SourceSha256 = new string('0', 64) } : value).ToArray() };
            var fresh = new FalloutChallenges(records, new()); Reject(() => fresh.Restore(invalid));
            Require(fresh.Capture().Entries.Count == 0 && fresh.ChallengesCompleted == 0, "Invalid restore partially mutated challenge state.");
            var bytes = new byte[64]; bytes[0] = 0x68; BitConverter.GetBytes(1u).CopyTo(bytes, 1); bytes[5] = 0x68;
            BitConverter.GetBytes(512u).CopyTo(bytes, 6); bytes[24] = 0xd9; bytes[25] = 5; BitConverter.GetBytes(2u).CopyTo(bytes, 26);
            Require(FalloutExecutableStringTable.ReadChallengeHudDeclaration(bytes, id => id == 1 ? "%s %d/%d %s" : null,
                id => id == 2 ? 4.5f : throw new InvalidDataException()) == new FalloutChallengeHudDeclaration("%s %d/%d %s", 4.5f),
                "Challenge HUD declaration lost owned literals or timing.");
            Reject(() => FalloutExecutableStringTable.ReadChallengeHudDeclaration(bytes, _ => "%s %f", _ => 4.5f));
            Console.WriteLine("OPENNV_CHALLENGE_CONTRACT_PASS locked source-dispatch cold-progress once-only recurring statistic-cascade hud-order retained-fault source-validation");
        }
        finally { directory.Delete(true); }
    }
    private static FalloutFormKey Key(uint id) => new("Challenge.esm", id);
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid challenge state was accepted.");
    }
    private static byte[] Challenge(uint id, uint type, int threshold, uint flags, ushort value1 = 0, uint? script = null)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, type); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), threshold);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), flags); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), value1);
        return Record("CHAL", id, Field("EDID", Encoding.UTF8.GetBytes($"Test{id:x}\0")), Field("FULL", Encoding.UTF8.GetBytes("Test\0")),
            Field("DESC", Encoding.UTF8.GetBytes("Progress\0")), Field("DATA", data), script is { } key ? Field("SCRI", BitConverter.GetBytes(key)) : []);
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string name, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)data.Length); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
}
