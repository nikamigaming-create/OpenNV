using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class QuestObjectiveProbe
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-objectives-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Header().Concat(Quest("Original", 17, 91)).ToArray());
            File.WriteAllBytes(Path.Combine(directory, "Override.esp"), Header("Base.esm").Concat(Quest("Winning", 17, 91)).ToArray());
            using var stack = FalloutPluginStack.Load(directory, ["Base.esm", "Override.esp"]);
            var state = new FalloutQuestState(stack);
            var events = new List<FalloutQuestObjectiveChange>();
            state.ObjectiveChanged += events.Add;
            var commands = FalloutQuestState.ReadObjectiveCommands(
                "; SetObjectiveDisplayed Ignored 999 1\nSetObjectiveDisplayed ProbeQuest 17 1\n" +
                "SetObjectiveCompleted ProbeQuest 17 1\nSetObjectiveDisplayed ProbeQuest 17 0");
            foreach (var command in commands) state.ApplyObjective(command);
            Require(events.Count == 3 && events[0].Quest == new FalloutFormKey("Base.esm", 0x100) &&
                events[0].Text == "Winning17" && events[0].After.Displayed &&
                events[1].After.Completed && !events[2].After.Displayed && events[2].After.Completed,
                "Objective source identity, ordering or independent flags differ.");
            var revision = state.Revision;
            state.ApplyObjective(commands[^1]);
            Require(state.Revision == revision && events.Count == 3, "An unchanged objective emitted a second mutation.");
            state.ApplyObjective(new("ProbeQuest", 17, false, false));
            Require(!events[^1].After.Completed, "Objective completion could not be cleared.");
            state.ApplyObjective(new("ProbeQuest", 91, true, true));
            var saved = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(state.Capture()))!;
            var restored = new FalloutQuestState(stack);
            restored.Restore(saved);
            Require(JsonSerializer.Serialize(restored.Capture()) == JsonSerializer.Serialize(saved),
                "Objective flags changed across JSON persistence.");
            var restoredEvents = new List<FalloutQuestObjectiveChange>();
            restored.ObjectiveChanged += restoredEvents.Add;
            restored.ApplyObjective(new("ProbeQuest", 91, true, true));
            Require(restoredEvents.Count == 0, "Restore replayed a previously displayed objective.");
            Expect<NotSupportedException>(() => state.ApplyObjective(new("ProbeQuest", 999, true, true)));
            Expect<NotSupportedException>(() => FalloutQuestState.ReadObjectiveCommands("SetObjectiveDisplayed ProbeQuest 17 2"));
            Expect<NotSupportedException>(() => FalloutQuestState.ReadObjectiveCommands("if SomeCondition\nSetObjectiveCompleted ProbeQuest 17 1\nendif"));
            Expect<InvalidDataException>(() => new FalloutQuestState(stack).Restore([saved[0] with { Objectives = null }]));
            Expect<InvalidDataException>(() => new FalloutQuestState(stack).Restore([saved[0] with
            {
                Objectives = [new(17, false, false), new(17, false, false)],
            }]));
            File.WriteAllBytes(Path.Combine(directory, "Duplicate.esm"), Header().Concat(Quest("Duplicate", 17, 17)).ToArray());
            using var duplicate = FalloutPluginStack.Load(directory, ["Duplicate.esm"]);
            Expect<InvalidDataException>(() => new FalloutQuestState(duplicate).ApplyObjective(new("ProbeQuest", 17, true, true)));
        }
        finally
        {
            foreach (var name in new[] { "Base.esm", "Override.esp", "Duplicate.esm" }) File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_QUEST_OBJECTIVES_PASS winningSource=true independentFlags=true eventOrder=true persistence=true unknownVisible=true");
    }

    private static byte[] Quest(string text, params uint[] indices) => Record("QUST", 0x100,
        Field("EDID", Encoding.ASCII.GetBytes("ProbeQuest\0")).Concat(indices.SelectMany(index =>
            Field("QOBJ", BitConverter.GetBytes(index)).Concat(Field("NNAM", Encoding.ASCII.GetBytes(text + index + '\0'))))).ToArray());

    private static byte[] Header(string? master = null)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var fields = Field("HEDR", header);
        if (master is not null) fields = fields.Concat(Field("MAST", Encoding.ASCII.GetBytes(master + '\0')))
            .Concat(Field("DATA", new byte[8])).ToArray();
        return Record("TES4", 0, fields);
    }

    private static byte[] Field(string signature, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length));
        data.CopyTo(result, 6); return result;
    }

    private static byte[] Record(string signature, uint id, byte[] data)
    {
        var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Invalid objective state was admitted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
