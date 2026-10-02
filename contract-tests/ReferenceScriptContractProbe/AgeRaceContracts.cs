using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class AgeRaceContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-age-race-");
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Age.esm"), Join(Record("TES4", 0, Field("HEDR", header)),
                Race(0x400, .6f, .55f, older: 0x401), Race(0x401, 1, .95f, 0x400, 0x402), Race(0x402, 1.1f, 1.05f, 0x401, 0x403),
                Race(0x403, .9f, .85f, 0x402), Race(0x410, 1, 1, younger: 0x499),
                Race(0x411, 1, 1, younger: 0x700), Record("MISC", 0x700),
                Race(0x420, .7f, .7f, older: 0x421), Race(0x421, .8f, .8f, older: 0x420),
                Record("HAIR", 0x300, Field("DATA", [0])), Record("HAIR", 0x301, Field("DATA", [0])),
                Record("QUST", 0x600, Field("SCRO", BitConverter.GetBytes(0x14u))),
                Npc(7, 0x401), Npc(0x100, 0x401, 1.1f), Npc(0x101, 0x401, female: true), Npc(0x102, 0x410), Npc(0x103, 0x411), Npc(0x104, 0x420),
                Record("CELL", 0x800, Field("DATA", [1])), Group(0x800, Join(Reference(0x900, 0x100), Reference(0x901, 0x101),
                    Reference(0x902, 0x102), Reference(0x903, 0x103), Reference(0x904, 0x100), Reference(0x905, 0x104)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Age.esm"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var player = records.RuntimeFormKey(0x14);
            var creation = new FalloutActorAppearanceState(false, Key(0x401), Key(0x300), null);
            world.BindPlayerAppearance(() => creation);
            Require(world.ActorHeight(player) == 1 && world.ActorHeight(Key(0x901)) == .95f && world.ActorHeight(Key(0x900)) == 1.1f,
                "Zero height did not inherit sex-specific race height, or custom NAM6 was ignored.");
            var executor = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false,
                _ => throw new InvalidDataException("AgeRace invented a presentation side effect.")));
            var revision = world.AppearanceRevision;
            Require(!world.AgeRace(player, 0) && world.AppearanceRevision == revision && world.CaptureActorOverrides().Count == 0,
                "Zero age steps mutated state.");
            var quest = records.GetEffective(Key(0x600));
            executor.ExecuteStage(quest, quest.ReadSubrecords().ToArray(), "player.AgeRace -1");
            Require(world.ActorRace(player) == Key(0x400) && world.ActorHeight(player) == .6f &&
                creation.Race == Key(0x401) && creation.Hair == Key(0x300) && world.ActorAppearanceOverride(player)?.Hair == Key(0x301),
                "Player aging rewrote creation identity or missed the younger nonplayable race/default hair.");
            revision = world.AppearanceRevision;
            Require(!world.AgeRace(player, int.MinValue) && world.AppearanceRevision == revision,
                "Younger endpoint did not saturate without a revision.");
            Require(world.AgeRace(player, int.MaxValue) && world.ActorRace(player) == Key(0x403) && world.ActorHeight(player) == .9f &&
                world.ActorAppearanceOverride(player)?.Hair == Key(0x300),
                "Positive steps did not follow the complete older chain.");
            Require(world.AgeRace(Key(0x900), 1) && world.ActorHeight(Key(0x900)) == 1.1f && world.ActorRace(Key(0x904)) == Key(0x402),
                "Custom height or shared base scope was lost.");
            var saved = JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(world.CaptureActorOverrides()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.BindPlayerAppearance(() => creation); cold.Restore(world.Capture()); cold.RestoreActorOverrides(saved);
            Require(cold.ActorRace(player) == Key(0x403) && cold.ActorHeight(player) == .9f &&
                cold.AgeRace(Key(0x904), 1) && cold.ActorHeight(Key(0x900)) == .9f &&
                cold.ActorAppearanceOverride(Key(0x900))?.Hair == Key(0x301),
                "Cold state lost height equality before the next race transition.");
            Require(world.AgeRace(Key(0x901), -1) && world.ActorHeight(Key(0x901)) == .55f,
                "Female age transition used male height.");
            foreach (var target in new[] { Key(0x902), Key(0x903) }) Reject(() => world.AgeRace(target, -1));
            Require(world.AgeRace(Key(0x905), int.MaxValue) && world.ActorRace(Key(0x905)) == Key(0x421) &&
                !world.AgeRace(Key(0x905), 2), "Finite steps through a cyclic age family were rejected or counted incorrectly.");
            var before = JsonSerializer.Serialize(cold.CaptureActorOverrides());
            Reject(() => cold.RestoreActorOverrides([saved[0] with { Height = 0 }]));
            Reject(() => cold.RestoreActorOverrides([saved[0] with { Height = float.NaN }]));
            Reject(() => cold.RestoreActorOverrides([saved[0] with { Race = saved[0].Race! with { Sha256 = new string('0', 64) } }]));
            Require(JsonSerializer.Serialize(cold.CaptureActorOverrides()) == before, "Invalid saved height/race partially restored state.");
            Require(FalloutReferenceWorld.RaceAgeSteps(-1.9) == -1, "Script integer conversion changed.");
            Reject(() => FalloutReferenceWorld.RaceAgeSteps(double.PositiveInfinity));
            Reject(() => FalloutReferenceWorld.RaceAgeSteps(2147483648d));
            revision = world.ActorAppearanceRevision(Key(0x900));
            world.InvalidateActorAppearance(Key(0x900));
            Require(world.ActorAppearanceRevision(Key(0x900)) > revision && world.ActorAppearanceRevision(Key(0x904)) == revision,
                "Equipment invalidation changed another reference's presentation revision.");
            Console.WriteLine("OPENNV_AGE_RACE_CONTRACT_PASS signed-links endpoints player-identity shared-base source-height cold-height invalid-source atomic-restore");
        }
        finally { directory.Delete(true); }
    }

    private static FalloutFormKey Key(uint id) => new("Age.esm", id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Invalid race-age input was accepted.");
    }
    private static byte[] Race(uint id, float male, float female, uint? younger = null, uint? older = null)
    {
        var data = new byte[36]; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(16), male); BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(20), female);
        return Record("RACE", id, Field("DATA", data), Field("HNAM", id is 0x400 or 0x402 ? BitConverter.GetBytes(0x301u) :
                Join(BitConverter.GetBytes(0x300u), BitConverter.GetBytes(0x301u))), Field("DNAM", Join(BitConverter.GetBytes(0x301u), BitConverter.GetBytes(0x301u))),
            younger is { } y ? Field("YNAM", BitConverter.GetBytes(y)) : [], older is { } o ? Field("ONAM", BitConverter.GetBytes(o)) : []);
    }
    private static byte[] Npc(uint id, uint race, float height = 0, bool female = false)
    {
        var acbs = new byte[24]; acbs[0] = female ? (byte)1 : (byte)0;
        return Record("NPC_", id, Field("ACBS", acbs), Field("RNAM", BitConverter.GetBytes(race)), Field("NAM6", BitConverter.GetBytes(height)),
            Field("HNAM", BitConverter.GetBytes(0x300u)));
    }
    private static byte[] Reference(uint id, uint npc) => Record("ACHR", id, Field("NAME", BitConverter.GetBytes(npc)), Field("DATA", new byte[24]));
    private static byte[] Group(uint cell, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 6); data.CopyTo(bytes, 24); return bytes;
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
