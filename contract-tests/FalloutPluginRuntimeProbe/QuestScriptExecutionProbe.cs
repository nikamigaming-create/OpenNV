using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class QuestScriptExecutionProbe
{
    private const string Body = "if active == 1\nif timer > 0\nset timer to timer - GetSecondsPassed\nelse\n" +
        "set result to Abs (Player.GetActorValue Courage - 4) * 2\nset active to 0\nSetStage ProbeQuest 42\nendif\nendif";
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-script-execution-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "Base.esm"), Header().Concat(Quest()).Concat(Script(Body, [1, 2, 3])).ToArray());
            File.WriteAllBytes(Path.Combine(directory, "Override.esp"), Header("Base.esm").Concat(Script(Body, [11, 19, 27])).ToArray());
            using var records = FalloutPluginStack.Load(directory, ["Base.esm", "Override.esp"]);
            var quest = FalloutDialogueTopic.Find(records, "QUST", "ProbeQuest");
            var graph = FalloutOpeningPlayerControlResolver.Resolve(records, ["ProbeQuest"]);
            var state = new FalloutQuestState(records);
            var writes = FalloutStageQuestVariableProgram.Read(records, graph.Stage("ProbeQuest", 0)).Prepare(state, null);
            Require(writes.Select(write => write.Index).SequenceEqual([19u, 11u, 27u]),
                "Stage assignments ignored winning compiled variable slots or source order.");
            foreach (var write in writes) state.SetVariable(write.Owner, write.Index, write.Value);
            state.EnterStage(quest.FormKey, 0);
            var script = records.GetEffective(FalloutDialogueTopic.RequiredForm(quest, "SCRI"));
            var bindings = new FalloutScriptBindings(records, quest, script, script.ReadSubrecords());
            Reject(() => bindings.Form("UnboundQuest"));
            Reject(() => state.Variable(quest.FormKey, 1));
            var claims = new HashSet<FalloutFormKey> { quest.FormKey };
            FalloutQuestScripts Scripts(FalloutQuestState owner) => new(records, owner, claims, new FalloutPlayerInventory(), defaultProcessingDelay: 1);
            FalloutQuestScriptHost Host(FalloutQuestState owner) => new((target, stage) =>
            {
                Require(target == quest.FormKey && stage == 42, "Source SetStage selected a different target.");
                return () =>
                {
                    Require(owner.Variable(target, 27) == 9 && owner.Variable(target, 11) == 0,
                        "SetStage published before its preceding source calculations.");
                    owner.EnterStage(target, stage);
                };
            }, name => name == "Courage" ? 8.5 : throw new InvalidDataException("Unexpected actor value argument."));
            var scripts = Scripts(state);
            scripts.Advance(10); // The presentation owner, not this scheduler lane, invokes claimed scripts.
            Require(scripts.Capture().Instances.Single().Executions == 0, "Claimed scripts acquired a second executor.");
            scripts.AdvanceClaimed(quest.FormKey, 0.025, Host(state));
            var savedQuests = JsonSerializer.Deserialize<FalloutQuestSnapshot[]>(JsonSerializer.Serialize(state.Capture()))!;
            var savedScripts = JsonSerializer.Deserialize<FalloutQuestScriptsSnapshot>(JsonSerializer.Serialize(scripts.Capture()))!;
            var restoredState = new FalloutQuestState(records); restoredState.Restore(savedQuests);
            var restoredScripts = Scripts(restoredState); restoredScripts.Restore(savedScripts);
            for (var frame = 0; frame < 30; frame++)
            {
                scripts.AdvanceClaimed(quest.FormKey, 0.025, Host(state));
                restoredScripts.AdvanceClaimed(quest.FormKey, 0.025, Host(restoredState));
                Require(JsonSerializer.Serialize(state.Capture()) == JsonSerializer.Serialize(restoredState.Capture()) &&
                    JsonSerializer.Serialize(scripts.Capture()) == JsonSerializer.Serialize(restoredScripts.Capture()),
                    "Cold script recurrence, effects or variable storage diverged.");
            }
            Require(state.Stage(quest.FormKey) == 42 && state.Variable(quest.FormKey, 27) == 9,
                "The complete source calculation did not drive progression.");
            File.WriteAllBytes(Path.Combine(directory, "Bad.esm"), Header().Concat(Quest())
                .Concat(Script("set result to 99\nUnknownReachedCommand", [1, 2, 3])).ToArray());
            using var bad = FalloutPluginStack.Load(directory, ["Bad.esm"]);
            var badQuest = new FalloutFormKey("Bad.esm", 0x100);
            var badState = new FalloutQuestState(bad);
            var badScripts = new FalloutQuestScripts(bad, badState, new HashSet<FalloutFormKey> { badQuest },
                new FalloutPlayerInventory(), defaultProcessingDelay: 1);
            Reject(() => badScripts.AdvanceClaimed(badQuest, 0.1, new((_, _) => () => { }, _ => 0)));
            Require(badState.Variable(badQuest, 3) == 0 && badScripts.Capture().Instances.Single().Error is not null,
                "An unsupported reached command published partial variable writes or hid its failure.");
        }
        finally
        {
            foreach (var file in new[] { "Base.esm", "Override.esp", "Bad.esm" }) File.Delete(Path.Combine(directory, file));
            Directory.Delete(directory);
        }
        Console.WriteLine("OPENNV_QUEST_SCRIPT_EXECUTION_PASS sourceSlots=true stageEffects=true calculations=true coldRestore=true failureAtomic=true");
    }

    private static byte[] Quest()
    {
        var data = new byte[8]; BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 0.05f);
        var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1);
        return Record("QUST", 0x100, Field("EDID", Text("ProbeQuest")).Concat(Field("DATA", data))
            .Concat(Field("SCRI", BitConverter.GetBytes(0x200u))).Concat(Field("INDX", BitConverter.GetBytes((short)0)))
            .Concat(Field("SCHR", header)).Concat(Field("SCTX", Text("set ProbeQuest.timer to 0.15\nset ProbeQuest.active to 1\nset ProbeQuest.result to 0")))
            .Concat(Field("SCRO", BitConverter.GetBytes(0x100u))).Concat(Field("INDX", BitConverter.GetBytes((short)42)))
            .Concat(Field("SCTX", Text(""))).ToArray());
    }
    private static byte[] Script(string body, uint[] indices)
    {
        var header = new byte[20]; header[16] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 3);
        var fields = Field("SCHR", header).Concat(Field("SCTX", Text("begin GameMode\n" + body + "\nend")))
            .Concat(Field("SCRO", BitConverter.GetBytes(0x100u))).Concat(Field("SCRO", BitConverter.GetBytes(0x14u)));
        foreach (var (name, index) in new[] { "active", "timer", "result" }.Zip(indices))
        {
            var declaration = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(declaration, index);
            fields = fields.Concat(Field("SLSD", declaration)).Concat(Field("SCVR", Text(name)));
        }
        return Record("SCPT", 0x200, fields.ToArray());
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
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string signature, uint form, byte[] data)
    {
        var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), form); data.CopyTo(result, 24); return result;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("An unbound script operation was admitted.");
    }
}
