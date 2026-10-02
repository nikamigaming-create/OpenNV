using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ActorAppearanceContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-actor-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var refs = Join(Reference(0x900, 0x100), Reference(0x902, 0x102), Reference(0x903, 0x103),
                Reference(0x904, 0x104), Reference(0x905, 0x105), Reference(0x906, 0x106), Reference(0x907, 0x107));
            File.WriteAllBytes(Path.Combine(directory, "Appearance.esm"), Join(Header(),
                Race(0x400, null, 0x401), Race(0x401, 0x400, 0x402), Race(0x402, 0x401, null),
                Race(0x410, null, 0x411), Race(0x411, 0x410, 0x412), Race(0x412, 0x411, null), Race(0x413),
                Race(0x420, 0x421), Race(0x421, 0x420), Race(0x430, null, 0x431), Race(0x431, null, 0x430),
                Race(0x440, 0x499), Record("MISC", 0x450), Race(0x441, 0x450),
                Npc(7, 0x401), Npc(0x100, 0x401, script: 0x500), Npc(0x102, 0x402), Npc(0x103, 0x400),
                Npc(0x104, 0x420), Npc(0x105, 0x440), Npc(0x106, 0x441), Npc(0x107, 0x402), Script(),
                Record("CELL", 0x800, Field("DATA", [1])), Group(0x800, refs),
                Record("CELL", 0x801, Field("DATA", [1])), Group(0x801, Reference(0x901, 0x100))));
            using var records = FalloutPluginStack.Load(directory, ["Appearance.esm"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
            var player = records.RuntimeFormKey(0x14);
            FalloutActorAppearanceState current = new(false, Key(0x410), null, null);
            world.BindPlayerAppearance(() => current);
            var scripts = new FalloutReferenceScripts(records, world, new FalloutQuestState(records),
                new((_, _) => false, _ => throw new InvalidDataException("MatchRace invented an immediate presentation effect.")));
            var result = scripts.Dispatch(Key(0x900), "GameMode");
            Require(result is { Error: null, Blocks: 1 } && world.Get(Key(0x900)).Read(1) == 1 &&
                world.Get(Key(0x900)).Read(2) == 1 && world.ActorRace(Key(0x900)) == Key(0x411),
                "Script command did not preserve the target's age tier and its statement order: " + result.Error);
            Require(!world.IsResident(Key(0x901)) && world.ActorRace(Key(0x901)) == Key(0x411) &&
                world.CaptureActorOverrides().Single().Target == Key(0x100), "NPC-base race mutation lost shared or unloaded scope.");
            var dialogue = new FalloutDialogueConditions(records, new FalloutQuestState(records), Key(0x900),
                new FalloutDialogueSpeaker(Key(0x100), Key(0x100), Key(0x400), "fixture", Key(0x401), false), actorRace: world.ActorRace);
            var raceCondition = new FalloutCondition(records.GetEffective(Key(0x100)), 0, 1, 69, 0x411, 0, 0, 0);
            Require(dialogue.Evaluate(raceCondition) == 1 && dialogue.Evaluate(raceCondition with { Argument1 = 0x401 }) == 0,
                "Dialogue selection ignored the authoritative changed race.");
            var matchedRevision = world.ActorAppearanceRevision(Key(0x900));
            Require(world.MatchRace(Key(0x900), player) && world.ActorRace(Key(0x900)) == Key(0x411) &&
                world.ActorAppearanceRevision(Key(0x900)) > matchedRevision,
                "Matching a different source tier lost its existing appearance refresh semantics.");
            current = current with { Race = Key(0x411) };
            var revision = world.ActorAppearanceRevision(Key(0x900));
            Require(!world.MatchRace(Key(0x900), player) && world.ActorAppearanceRevision(Key(0x900)) == revision,
                "Same-race matching reset presentation or changed state.");
            Require(world.MatchRace(Key(0x902), player) && world.ActorRace(Key(0x902)) == Key(0x412) &&
                world.MatchRace(Key(0x903), player) && world.ActorRace(Key(0x903)) == Key(0x410),
                "Older or younger actors did not retain their family tier.");
            var saved = JsonSerializer.Deserialize<FalloutActorOverrides[]>(JsonSerializer.Serialize(world.CaptureActorOverrides()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(world.Capture()); cold.RestoreActorOverrides(saved);
            Require(cold.ActorRace(Key(0x900)) == Key(0x411) && cold.ActorRace(Key(0x901)) == Key(0x411) &&
                cold.ActorAppearanceOverride(Key(0x902))?.Race == Key(0x412), "Cold state lost race or shared base scope.");
            Reject(() => cold.RestoreActorOverrides([saved[0] with { Race = saved[0].Race! with { Sha256 = new string('0', 64) } }]));
            Reject(() => cold.RestoreActorOverrides([saved[0], saved[0]]));
            Require(cold.ActorRace(Key(0x900)) == Key(0x411), "Rejected cold source drift partially replaced race state.");
            var coldRevision = cold.AppearanceRevision;
            cold.RestoreActorOverrides([]);
            Require(cold.ActorRace(Key(0x900)) == Key(0x401) && cold.AppearanceRevision > coldRevision,
                "Removing an override did not invalidate the resident or unloaded source body.");
            Require(world.MatchRace(Key(0x907), Key(0x900)) && world.ActorRace(Key(0x907)) == Key(0x412),
                "Matching another source NPC ignored its changed base race.");
            current = current with { Race = Key(0x401) };
            Require(world.MatchRace(player, Key(0x903)) && world.ActorRace(player) == Key(0x411),
                "Shared race matching rejected the player or changed its age tier.");
            world.RestoreActorOverrides(world.CaptureActorOverrides().Where(value => value.Target != Key(7)).ToArray());
            current = current with { Race = Key(0x401) };
            _ = world.MatchRace(Key(0x900), player);
            Require(dialogue.Evaluate(raceCondition) == 0 && dialogue.Evaluate(raceCondition with { Argument1 = 0x401 }) == 1,
                "An active dialogue retained the previous race after another source change.");
            current = current with { Race = Key(0x411) };
            _ = world.MatchRace(Key(0x900), player);
            current = current with { Race = Key(0x413) };
            Require(world.MatchRace(Key(0x902), player) && world.ActorRace(Key(0x902)) == Key(0x413),
                "A short source family invented a missing older race.");
            foreach (var reference in new[] { Key(0x904), Key(0x905), Key(0x906) }) Reject(() => world.MatchRace(reference, player));
            current = current with { Race = Key(0x430) };
            Reject(() => world.MatchRace(Key(0x907), player));
            Require(world.ActorRace(Key(0x900)) == Key(0x411), "An older-family cycle partially committed a race.");
            current = current with { Race = Key(0x499) };
            Reject(() => world.MatchRace(Key(0x900), player));
            Require(world.ActorRace(Key(0x900)) == Key(0x411), "A missing source race changed the target.");
            // A reached failure retains earlier source mutations but never runs
            // the suffix or replays the prefix on a latched instance.
            var before = world.Get(Key(0x900)).Read(1); var after = world.Get(Key(0x900)).Read(2);
            result = scripts.Dispatch(Key(0x900), "GameMode");
            Require(result.Error is not null && world.Get(Key(0x900)).Read(1) == before + 1 &&
                world.Get(Key(0x900)).Read(2) == after, "Race failure lost its executed prefix boundary.");
            _ = scripts.Dispatch(Key(0x900), "GameMode");
            Require(world.Get(Key(0x900)).Read(1) == before + 1, "Latched race failure replayed its prefix.");
            Console.WriteLine("OPENNV_ACTOR_APPEARANCE_CONTRACT_PASS source-order shared-base age-family unloaded cold drift cycles prefix");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static FalloutFormKey Key(uint id) => new("Appearance.esm", id);
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or KeyNotFoundException) { return; }
        throw new InvalidDataException("Malformed appearance graph was accepted.");
    }
    private static byte[] Header()
    {
        var data = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(data, 1.34f);
        return Record("TES4", 0, Field("HEDR", data));
    }
    private static byte[] Race(uint id, uint? younger = null, uint? older = null)
    {
        var data = new byte[36];
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(20), 1);
        return Record("RACE", id, Field("DATA", data),
            Join(younger is { } y ? Field("YNAM", BitConverter.GetBytes(y)) : [], older is { } o ? Field("ONAM", BitConverter.GetBytes(o)) : []));
    }
    private static byte[] Npc(uint id, uint race, uint script = 0) => Record("NPC_", id, Field("ACBS", new byte[24]),
        Field("RNAM", BitConverter.GetBytes(race)), script == 0 ? [] : Field("SCRI", BitConverter.GetBytes(script)));
    private static byte[] Reference(uint id, uint npc) => Record("ACHR", id, Field("EDID", Text($"Actor{id:x}")),
        Field("NAME", BitConverter.GetBytes(npc)), Field("DATA", new byte[24]));
    private static byte[] Script()
    {
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[16] = 1;
        byte[] Local(uint index, string name)
        {
            var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
            return Join(Field("SLSD", data), Field("SCVR", Text(name)));
        }
        return Record("SCPT", 0x500, Field("SCHR", header), Local(1, "before"), Local(2, "after"),
            Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text(
            "begin GameMode\nset before to before + 1\nMatchRace player\nset after to after + 1\nend")));
    }
    private static byte[] Group(uint cell, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), cell); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 6);
        data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + "\0");
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
}
