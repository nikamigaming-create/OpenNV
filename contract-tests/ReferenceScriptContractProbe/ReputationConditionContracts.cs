using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class ReputationConditionContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-reputation-conditions-");
        try
        {
            File.WriteAllBytes(Path.Combine(directory.FullName, "Repu.esm"), Join(Header(),
                Record("REPU", 1, Field("DATA", BitConverter.GetBytes(100f))),
                Record("FACT", 2)));
            File.WriteAllBytes(Path.Combine(directory.FullName, "Other.esm"), Header());
            File.WriteAllBytes(Path.Combine(directory.FullName, "Query.esp"), Join(Header("Other.esm", "Repu.esm"),
                Record("INFO", 0x02000010, Field("CTDA", Condition(1, 0))),
                Record("MESG", 0x02000011, Field("DNAM", BitConverter.GetBytes(1u)),
                    Field("INAM", new byte[4]), Field("DESC", Text("Question")),
                    Field("ITXT", Text("Positive")), Field("CTDA", Condition(1, 3, 5)),
                    Field("ITXT", Text("Negative")), Field("CTDA", Condition(0, 3, 8)))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Repu.esm", "Other.esm", "Query.esp"]);
            using var world = new FalloutReferenceWorld(records);
            var form = new FalloutFormKey("Repu.esm", 1);
            var owner = records.GetEffective(new("Query.esp", 0x10));
            var condition = FalloutCondition.Read(owner).Single();
            Check(condition.Function == 573 && condition.FormArgument1 == form, "CTDA lost master-adjusted REPU identity.");
            float Query(FalloutCondition value) => world.EvaluateReputationCondition(value) ??
                throw new NotSupportedException("Unowned condition.");
            var identity = new FalloutDialogueSpeaker(new("Repu.esm", 0x20), new("Repu.esm", 0x20),
                new("Repu.esm", 0x21), "fixture", null, false);
            var dialogue = new FalloutDialogueConditions(records, new(records), new("Repu.esm", 0x30), identity,
                Query, listener: new("Repu.esm", 0x31), listenerIdentity: identity);
            Check(dialogue.Evaluate(condition) == 0, "Fresh condition did not use initialized zero.");
            world.SetReputation(form, 1, 5.25); world.SetReputation(form, 0, 8.5);
            foreach (var selector in new uint[] { 0, 1, 2, uint.MaxValue })
                foreach (var runOn in new uint[] { 0, 1, 2 })
                    Check(dialogue.Evaluate(condition with { Argument2 = selector, RunOn = runOn, Reference = 0x01000030 }) ==
                        (selector == 1 ? 5.25f : 8.5f), "Reputation depended on subject or validated the raw selector as a script command.");
            foreach (var (operation, comparison, expected) in new (byte, float, bool)[]
                { (0,5.25f,true),(1,5.25f,false),(2,5,true),(3,5.25f,true),(4,6,true),(5,5.25f,true) })
                Check(FalloutCondition.AllPass([condition with { Flags = (byte)(operation << 5), Comparison = comparison }], dialogue.Evaluate) == expected,
                    "Reputation changed shared CTDA comparison semantics.");
            var message = FalloutSourceMessage.Read(records.GetEffective(new("Query.esp", 0x11)));
            Check(message.ResolveButtons(records, Query).ButtonIndices!.SequenceEqual(new[] { 0, 1 }),
                "Actual conditional message buttons did not consume shared reputation.");
            world.SetReputation(form, 1, -2);
            Check(dialogue.Evaluate(condition) == -2 && message.ResolveButtons(records, Query).ButtonIndices!.SequenceEqual(new[] { 1 }),
                "Condition retained stale reputation between evaluations.");
            var before = JsonSerializer.Serialize(world.CaptureReputations());
            using var cold = new FalloutReferenceWorld(records);
            cold.RestoreReputations(JsonSerializer.Deserialize<FalloutReputationsSnapshot>(before));
            Check(cold.EvaluateReputationCondition(condition) == -2 && cold.EvaluateReputationCondition(condition with { Argument2 = 0 }) == 8.5f,
                "Cold condition lost known axes.");
            Reject(() => dialogue.Evaluate(condition with { Argument1 = 0x01000002 }));
            Reject(() => dialogue.Evaluate(condition with { RunOn = 3 }));
            Reject(() => new FalloutDialogueConditions(records, new(records), new("Repu.esm", 0x30), identity).Evaluate(condition));
            Reject(() => FalloutCondition.AllPass([condition with { Flags = 4 }], Query));
            Check(world.EvaluateReputationCondition(condition with { Function = 574 }) is null &&
                JsonSerializer.Serialize(world.CaptureReputations()) == before, "Refusal/query mutated state or admitted another reputation query.");
            cold.RestoreReputations(null);
            Reject(() => cold.EvaluateReputationCondition(condition));
            cold.SetReputation(form, 1, 3);
            Check(cold.EvaluateReputationCondition(condition) == 3, "Explicit legacy axis was not available to CTDA.");
            Reject(() => cold.EvaluateReputationCondition(condition with { Argument2 = 0 }));
            Console.WriteLine("OPENNV_REPUTATION_CONDITION_PASS sourceMasters=true rawSelector=true sharedDialogueAndMessage=true comparisons=true live=true cold=true legacyUnknown=true refusals=true noMutation=true ordinaryAndRetail=unverified");
        }
        finally { directory.Delete(true); }
    }
    private static byte[] Condition(uint selector, byte comparison, float value = 0)
    {
        var bytes = new byte[28]; bytes[0] = (byte)(comparison << 5);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), value);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 573);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0x01000001);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), selector); return bytes;
    }
    private static byte[] Header(params string[] masters) => Record("TES4", 0,
        Join(new[] { Field("HEDR", new byte[12]) }.Concat(masters.Select(name => Join(Field("MAST", Text(name)), Field("DATA", new byte[8])))).ToArray()));
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
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
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidOperationException("Unqualified reputation condition was admitted.");
    }
}
