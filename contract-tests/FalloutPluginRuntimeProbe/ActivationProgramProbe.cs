using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;

internal static class ActivationProgramProbe
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-activation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        const string body = "if GetStage EntryQuest == 24 && GetObjectiveDisplayed EntryQuest 7 == 1\n" +
            "ShowLoveTesterMenuParams 44\nSetStage EntryQuest 29\nendif";
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Header().Concat(Quest())
                .Concat(Script(0x200, body)).Concat(Script(0x201, "if GetStage MissingQuest == 24\nUnowned\nendif"))
                .Concat(Script(0x202, "FirstEffect\nif GetStage EntryQuest == 24\nSecondEffect\nendif")).ToArray());
            File.WriteAllBytes(Path.Combine(directory, "Override.esp"), Header("Base.esm")
                .Concat(Script(0x200, body.Replace("24", "31").Replace("44", "45"))).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Override.esp"]);
            var program = new FalloutActivationProgram(records, records.GetEffective(new("Base.esm", 0x200)));
            var quest = new FalloutFormKey("Base.esm", 0x100);
            var state = new FalloutQuestState(records);
            state.EnterStage(quest, 24);
            state.ApplyObjective(new("EntryQuest", 7, true, true));
            Require(program.Prepare(state).Count == 0, "Activation ignored the winning source predicate.");
            state.EnterStage(quest, 31);
            state.ApplyObjective(new("EntryQuest", 7, true, false));
            Require(program.Prepare(state).Count == 0, "Activation opened before the authored objective prerequisite.");
            state.ApplyObjective(new("EntryQuest", 7, true, true));
            var calls = program.Prepare(state);
            Require(calls.Count == 2 && calls[0].Command == "ShowLoveTesterMenuParams" && calls[0].Arguments.Single() == "45" &&
                calls[1].Command == "SetStage" && calls[1].Arguments.SequenceEqual(new[] { "EntryQuest", "29" }),
                "Activation lost winning arguments or menu-before-stage source order.");
            Require(state.Stage(quest) == 31 && program.Form("EntryQuest").FormKey == quest,
                "Preparing activation mutated state or lost master-adjusted references.");
            Reject(() => new FalloutActivationProgram(records, records.GetEffective(new("Base.esm", 0x201))).Prepare(state));
            Reject(() => new FalloutActivationProgram(records, records.GetEffective(new("Base.esm", 0x202))).Prepare(state));
        }
        finally
        {
            File.Delete(Path.Combine(directory, "Base.esm"));
            File.Delete(Path.Combine(directory, "Override.esp"));
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_ACTIVATION_PROGRAM_PASS winningPredicate=true objectiveGuard=true orderedEffects=true compiledReferences=true unknownVisible=true");
    }

    private static byte[] Quest() => Record("QUST", 0x100, Field("EDID", Text("EntryQuest"))
        .Concat(Field("QOBJ", BitConverter.GetBytes(7u))).Concat(Field("NNAM", Text("Use the instrument."))).ToArray());
    private static byte[] Script(uint id, string body)
    {
        var header = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        return Record("SCPT", id, Field("SCHR", header).Concat(Field("SCTX", Text("begin OnActivate\n" + body + "\nend")))
            .Concat(Field("SCRO", BitConverter.GetBytes(0x100u))).ToArray());
    }
    private static byte[] Header(string? master = null)
    {
        var header = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(header, 1.34f);
        var fields = Field("HEDR", header);
        if (master is not null) fields = fields.Concat(Field("MAST", Text(master))).Concat(Field("DATA", new byte[8])).ToArray();
        return Record("TES4", 0, fields);
    }
    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text + '\0');
    private static byte[] Field(string signature, byte[] data)
    {
        var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes;
    }
    private static byte[] Record(string signature, uint id, byte[] data)
    {
        var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new InvalidDataException("Unowned activation execution was admitted.");
    }
}
