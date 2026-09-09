using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class StageAndInventoryContracts
{
    internal static void Run()
    {
        var number = 0d;
        var commandSeen = false;
        FalloutGameModeProgram.Read("begin GameMode\nset value to 1e-3 + .5\nPlayMusic 1Track\nend")
            .Execute(_ => throw new InvalidDataException("Unexpected variable."), (_, value) => number = value,
                (command, arguments) => commandSeen = command == "PlayMusic" && arguments.Single() == "1Track");
        Require(Math.Abs(number - .501) < 1e-12 && commandSeen, "Numeric-leading identifiers broke scientific or fractional numbers.");
        Require(FalloutMenuXml.Parse(Text("<menu><string>&-sExample </string></menu>")[..^1]).Descendants("string").Single().Value.Trim() == "entity_-sExample",
            "Owned menu entity terminator grammar was rejected.");
        var directory = Path.Combine(Path.GetTempPath(), "opennv-stage-inventory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Base.esm");
        try
        {
            var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
            File.WriteAllBytes(path, Record("TES4", 0, Field("HEDR", header))
                .Concat(Record("MISC", 1)).Concat(Record("MISC", 2))
                .Concat(List(10, 0, 0, Entry(1, 1, 2), Entry(5, 2, 3)))
                .Concat(List(11, 5, 0, Entry(1, 1, 2), Entry(5, 2, 3)))
                .Concat(List(12, 2, 50, Entry(1, 1, 1)))
                .Concat(List(13, 0, 100, Entry(1, 1, 1)))
                .Concat(List(14, 0, 0, Entry(1, 10, 3)))
                .Concat(List(15, 0, 0, Entry(1, 15, 1)))
                .Concat(Quest(0x20, 30, "First", 0, "set First.value to 1\nHold\nset First.value to 2\nSetStage Second 0", 0x21))
                .Concat(Quest(0x21, 31, "Second", 1, "set Second.value to 9\nUnknownReachedCommand", 0x20))
                .Concat(Script(30)).Concat(Script(31)).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["Base.esm"]);
            var conditionOwner = records.GetEffective(Key(0x20));
            foreach (var (function, expected) in new (ushort, float)[] { (309, 0), (523, 0), (524, 1) })
                Require(FalloutPlatformConditions.Evaluate(new(conditionOwner, 0, expected, function, 0, 0, 0, 0)) == expected,
                    "PC platform predicates used process width or selected a console platform.");
            Require(FalloutPlatformConditions.Evaluate(new(conditionOwner, 0, 0, 58, 0, 0, 0, 0)) is null,
                "Platform evaluation consumed a quest condition.");
            var low = FalloutLeveledItems.Resolve(records, Key(10), 2, 4, _ => 0).Single();
            Require(low.Form == Key(1) && low.Variant.Count == 4, "Leveled list ignored level or multiplied quantity.");
            var high = FalloutLeveledItems.Resolve(records, Key(10), 1, 5, _ => 0).Single();
            Require(high.Form == Key(2) && high.Variant.Count == 3, "Highest eligible level was not selected.");
            Require(FalloutLeveledItems.Resolve(records, Key(11), 1, 5, _ => 0).Sum(value => value.Variant.Count) == 5, "Use-all lost an eligible entry.");
            var rolls = 0;
            var selected = FalloutLeveledItems.Resolve(records, Key(12), 4, 1, bound => bound == 1 ? 0 : ++rolls % 2 == 0 ? 80u : 20u);
            Require(rolls == 4 && selected.Sum(value => value.Variant.Count) == 2, "Per-item chance was rolled once for the whole stack.");
            Require(FalloutLeveledItems.Resolve(records, Key(13), 1, 1, _ => 99).Count == 0, "Certain none chance produced an item.");
            Require(FalloutLeveledItems.Resolve(records, Key(14), 2, 1, _ => 0).Single().Variant.Count == 12, "Nested quantities were lost.");
            Reject(() => FalloutLeveledItems.Resolve(records, Key(15), 1, 1, _ => 0));
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var paused = false;
            FalloutQuestStages? stages = null;
            var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, effect =>
            {
                if (effect.Kind != FalloutReferenceEffectKind.SetStage) throw new InvalidDataException("Unexpected effect.");
                stages!.Enter(effect.Target!.Value, effect.Stage);
            }, Command: (_, _, command, _) =>
            {
                if (command != "Hold") throw new NotSupportedException(command);
                paused = true;
            }));
            stages = new(records, quests, scripts.StageSteps, _ => throw new InvalidDataException("Unexpected condition."), () => !paused);
            stages.Enter(Key(0x20), 0);
            Require(quests.IsRunning(Key(0x20)) && quests.Variable(Key(0x20), 1) == 1 && !quests.StageDone(Key(0x21), 0), "Blocking command did not retain its exact continuation.");
            stages.Continue();
            Require(quests.Variable(Key(0x20), 1) == 1, "Paused script advanced.");
            paused = false;
            Reject(stages.Continue);
            Require(quests.Variable(Key(0x20), 1) == 2 && quests.Variable(Key(0x21), 1) == 9 && quests.IsCompleted(Key(0x21)), "Nested stage failure rolled back its reached prefix.");
            Reject(() => stages.Enter(Key(0x21), 0));
            var cold = new FalloutQuestState(records);
            cold.Restore(JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(quests.Capture()))!);
            Require(JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(quests.Capture()), "Cold stage flags or variables diverged.");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
        Console.WriteLine("OPENNV_STAGE_INVENTORY_CONTRACT_PASS levelSelection=true nestedCounts=true perItemChance=true cyclesRejected=true suspension=true sourceOrder=true nestedFailure=true coldQuestState=true");
    }

    private static FalloutFormKey Key(uint id) => new("Base.esm", id);
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException) { return; }
        throw new InvalidDataException("Invalid input or reached failure was accepted.");
    }
    private static byte[] Quest(uint id, uint script, string name, byte flags, string body, uint other) => Record("QUST", id,
        Field("EDID", Text(name)), Field("SCRI", BitConverter.GetBytes(script)), Field("DATA", new byte[8]),
        Field("INDX", BitConverter.GetBytes((short)0)), Field("QSDT", [flags]), Field("SCTX", Text(body)),
        Field("SCRO", BitConverter.GetBytes(id)), Field("SCRO", BitConverter.GetBytes(other)));
    private static byte[] Script(uint id)
    {
        var local = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(local, 1);
        return Record("SCPT", id, Field("SLSD", local), Field("SCVR", Text("value")));
    }
    private static byte[] List(uint id, byte flags, byte chance, params byte[][] entries) =>
        Record("LVLI", id, [Field("LVLF", [flags]), Field("LVLD", [chance]), .. entries]);
    private static byte[] Entry(ushort level, uint form, ushort count)
    {
        var bytes = new byte[12]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, level);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), form); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), count);
        return Field("LVLO", bytes);
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, params byte[][] fields)
    {
        var data = fields.SelectMany(bytes => bytes).ToArray();
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
}
