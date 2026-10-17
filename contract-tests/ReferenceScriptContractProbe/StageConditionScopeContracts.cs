using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class StageConditionScopeContracts
{
    internal static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-stage-scope-");
        try
        {
            byte[] Condition(uint reference, byte flags = 0)
            {
                var data = new byte[28]; data[0] = flags;
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 47);
                U32(1).CopyTo(data, 12); U32(2).CopyTo(data, 20); U32(reference).CopyTo(data, 24);
                return Field("CTDA", data);
            }
            File.WriteAllBytes(Path.Combine(directory.FullName, "Scope.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])), Record("NPC_", 7),
                Record("MISC", 1, Field("EDID", Text("SourceItem")), Field("DATA", new byte[8])),
                Record("QUST", 0x20, Field("DATA", new byte[8]), Field("INDX", BitConverter.GetBytes((short)0)),
                    Field("QSDT", [0]), Condition(0x14), Field("SCTX", Text("missing-item")),
                    Field("QSDT", [0]), Field("SCTX", Text("always")),
                    Field("INDX", BitConverter.GetBytes((short)1)), Field("QSDT", [0]), Field("SCTX", Text("prefix")),
                    Field("QSDT", [0]), Condition(0x99), Field("SCTX", Text("unsupported-subject")),
                    Field("INDX", BitConverter.GetBytes((short)2)), Field("QSDT", [0]), Condition(0x14, 4), Field("SCTX", Text("global-flag")))));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Scope.esm"]);
            var inventory = new FalloutPlayerInventory(); inventory.Add(records, new("Scope.esm", 1), 1, 1, true);
            float Query(FalloutCondition condition) => FalloutInventoryConditions.Evaluate(records, inventory, _ => false, condition) ??
                throw new NotSupportedException("Unsupported source stage subject.");
            var executed = new List<string>();
            IEnumerable<bool> Execute(FalloutPluginRecord _, IReadOnlyList<FalloutPluginSubrecord> entry, string source)
            { executed.Add(source); yield return true; }
            var quest = new FalloutFormKey("Scope.esm", 0x20);
            var unowned = new FalloutQuestStages(records, new(records), Execute, Query);
            Reject(() => unowned.Enter(quest, 0));
            Require(executed.Count == 0, "Stage admitted a subject scope without its host contract.");
            var stages = new FalloutQuestStages(records, new(records), Execute, Query, evaluateRunOn: true);
            stages.Enter(quest, 0);
            Require(executed.SequenceEqual(new[] { "always" }), "Explicit player inventory condition used a quest or invented item.");
            Reject(() => stages.Enter(quest, 1));
            Reject(() => stages.Enter(quest, 1));
            Require(executed.SequenceEqual(new[] { "always", "prefix" }), "Failed subject replayed the consumed entry prefix.");
            Reject(() => stages.Enter(quest, 2));
            Require(executed.Count == 2, "Run-on admission bypassed an unsupported comparison-global flag.");
            Console.WriteLine("OPENNV_STAGE_CONDITION_SCOPE_PASS explicitPlayerInventory=true hostRequired=true " +
                "unsupportedSubjectRefused=true flagsStillRefused=true consumedPrefixRetained=true");
        }
        finally { directory.Delete(true); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new InvalidDataException("Unowned stage condition was admitted.");
    }
    private static byte[] U32(uint value) => BitConverter.GetBytes(value);
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
        U32((uint)data.Length).CopyTo(bytes, 4); U32(id).CopyTo(bytes, 12); data.CopyTo(bytes, 24); return bytes;
    }
}
