using System.Text.Json;
using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class DestructionContracts
{
    internal static void Run()
    {
        RunWorldQuery();
        var key = new FalloutFormKey("Synthetic.esm", 1);
        var source = new FalloutDestructible(key, new string('a', 64), 100, 1,
            [new(90, 0, 1, 0, 2, null, null, 0, null), new(60, 1, 2, 5, 1, null, null, 0, "meshes/wreck.nif"),
                new(0, 2, 0, 2, 0, null, null, 0, null)]);
        var state = FalloutDestructionState.Initial(source);
        var first = state.Damage(source, 80, key);
        Require(first.State is { Health: 60, Stage: 1, ModelStage: 2, Destroyed: true, Disabled: false } && first.Entered.Length == 2,
            "One shot must enter crossed stages in order and stop at the source cap.");
        Require(first.State.Damage(source, 20, null).State == first.State, "Destroyed objects must reject subsequent external hits.");
        var next = first.State.Damage(source, 10, null, self: true);
        Require(next.State.Health == 50 && next.Entered.Length == 0 && next.State.Attacker == key,
            "Self damage must continue after destruction without replaying stages or losing attribution.");
        var cold = JsonSerializer.Deserialize<FalloutDestructionState>(JsonSerializer.Serialize(next.State))!;
        cold.Validate(source);
        Require(cold == next.State, "Saved destruction fields did not survive serialization.");
        var last = cold.Damage(source, 500, null, self: true);
        Require(last.State is { Health: 0, Stage: 2, ModelStage: 2, Destroyed: true, Disabled: true } && last.Entered.Length == 1,
            "Terminal disable must retain the most recent nonzero model stage.");
        Reject(() => (cold with { SourceSha256 = new string('b', 64) }).Validate(source));
        Reject(() => (cold with { ModelStage = 7 }).Validate(source));
        Reject(() => (cold with { Destroyed = false }).Validate(source));
        Reject(() => cold.Damage(source, float.NaN, null));
        foreach (var (health, expected) in new (float, int)[]
        {
            (100, 0), (90, 1), (89.01f, 1), (60, 2), (59, 2), (1, 2),
        })
            Require((state with { Health = health }).QueryStage(source) == expected,
                "Destruction query used entered model stage or mishandled a threshold equality.");
        Require(first.State.QueryStage(source) == 2 && cold.QueryStage(source) == 2,
            "Live/cold destruction query did not recompute the source ordinal from retained health.");
    }

    private static void RunWorldQuery()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-destruction-query-");
        try
        {
            byte[] Stage(byte health, byte ordinal) { var data = new byte[20]; data[0] = health; data[1] = ordinal; return data; }
            byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
            var questHeader = new byte[20]; questHeader[16] = 1;
            var reference = Record("REFR", 0x90, Field("EDID", Text("QueryTarget")),
                Field("NAME", BitConverter.GetBytes(1u)), Field("DATA", new byte[24]));
            var group = new byte[24 + reference.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x80);
            BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); reference.CopyTo(group, 24);
            File.WriteAllBytes(Path.Combine(directory.FullName, "Synthetic.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])),
                Record("ACTI", 1, Field("SCRI", BitConverter.GetBytes(2u)),
                    Field("DEST", [100, 0, 0, 0, 2, 0, 0, 0]),
                    Field("DSTD", Stage(90, 0)), Field("DSTF", []), Field("DSTD", Stage(60, 1)), Field("DSTF", [])),
                Record("SCPT", 2, Field("SCHR", new byte[20]), Field("SCTX", Text(
                    "begin OnActivate\nif GetDestructionStage != -1\nUnownedBadCommand\nendif\nend"))),
                Record("QUST", 3, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(4u))),
                Record("SCPT", 4, Field("SCHR", questHeader), Field("SCRO", BitConverter.GetBytes(0x90u)), Field("SCTX", Text(
                    "begin GameMode\nif QueryTarget.GetDestructionStage != -1\nUnownedBadCommand\nendif\n" +
                    "if GetDestructionStage != -1\nUnownedBadCommand\nendif\n" +
                    "if 0 && Missing.GetDestructionStage\nUnownedBadCommand\nendif\nend"))),
                Record("CELL", 0x80, Field("DATA", [1])), group));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Synthetic.esm"]);
            using var world = new FalloutReferenceWorld(records);
            world.LoadCell(FalloutCellSceneReader.Read(records, new("Synthetic.esm", 0x80)));
            var key = new FalloutFormKey("Synthetic.esm", 0x90);
            var source = FalloutDestructible.Read(records, new("Synthetic.esm", 1))!;
            var state = world.Get(key);
            Require(world.DestructionStage(null) == -1, "A null reference acquired a quest/object receiver.");
            state.Destruction = FalloutDestructionState.Initial(source);
            Require(world.DestructionStage(key) == -1, "Model attachment invented retained object health.");
            var scripts = new FalloutReferenceScripts(records, world, new(records), new((_, _) => false, _ => { }));
            var activation = scripts.Activate(key, records.RuntimeFormKey(0x14));
            Require(activation.Error is null, $"Actual object destruction query failed: {activation.Error}");
            var quests = new FalloutQuestScripts(records, new(records), new HashSet<FalloutFormKey>(), new(),
                defaultProcessingDelay: 0, references: world);
            quests.Advance(.1);
            Require(quests.Capture().Instances.Single().Error is null, "Fallback destruction query failed.");
            state.ObjectHealthPresent = true;
            Require(world.DestructionStage(key) == 0, "Present full health was confused with missing health.");
            state.Destruction = state.Destruction.Damage(source, 20, null).State;
            Require(world.DestructionStage(key) == 1, "Damage query returned model index rather than source ordinal.");
            var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
            using var cold = new FalloutReferenceWorld(records);
            cold.Restore(saved);
            Require(cold.DestructionStage(key) == 1, "Cold query lost known object health.");
            using var legacy = new FalloutReferenceWorld(records);
            legacy.Restore(saved.Select(value => value with { ObjectHealthPresent = null }).ToArray());
            Reject(() => legacy.DestructionStage(key));
            Require(legacy.Capture().Single().ObjectHealthPresent is null, "Unknown history was invented on resave.");
            using var invalid = new FalloutReferenceWorld(records);
            Reject(() => invalid.Restore(saved.Select(value => value with { ObjectHealthPresent = false }).ToArray()));
            Require(invalid.Capture().Count == 0, "Invalid history partly replaced the owner.");
        }
        finally { directory.Delete(true); }
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException or NotSupportedException) { return; }
        throw new InvalidDataException("Invalid destruction state was admitted.");
    }
}
