using System.Buffers.Binary;
using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class VampireQueryContracts
{
    internal static void Run()
    {
        var code = Association();
        FalloutVampireQueryDeclaration Read(byte[] bytes) => FalloutExecutableStringTable.ReadVampireQueryAssociation(bytes, 0x300000, 0, 64);
        var declaration = Read(code);
        Check(declaration.Value == 0 && declaration.SourceSha256.Length == 64, "False query declaration lost source identity.");
        Reject(() => Read(code[..100]));
        foreach (var offset in new[] { 6, 64 + 7, 64 + 15, 64 + 53, 64 + 87, 224 + 8 })
        {
            var changed = code.ToArray(); changed[offset] ^= 1; Reject(() => Read(changed));
        }
        var external = code.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(external.AsSpan(16), int.MaxValue); Reject(() => Read(external));
        var directory = Path.Combine(Path.GetTempPath(), "opennv-vampire-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var header = new byte[20]; BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 2); header[16] = 1;
            const string source = "short done\nshort result\nbegin GameMode\nif done == 0\n" +
                "set result to GetVampire + player.GetVampire\nset done to 1\nendif\nend";
            File.WriteAllBytes(Path.Combine(directory, "Queries.esm"), Join(Record("TES4", 0),
                Record("NPC_", 7, Field("ACBS", new byte[24])),
                Record("INFO", 0x800, Field("CTDA", new byte[28])),
                Record("SCPT", 0x601, Field("SCHR", header), Local(1, "done"), Local(2, "result"),
                    Field("SCRO", BitConverter.GetBytes(0x14u)), Field("SCTX", Text(source))),
                Record("QUST", 0x600, Field("DATA", [1, 0]), Field("SCRI", BitConverter.GetBytes(0x601u)))));
            using var records = FalloutPluginStack.Load(directory, ["Queries.esm"]);
            FalloutFormKey Key(uint id) => new("Queries.esm", id);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            var scripts = new FalloutQuestScripts(records, quests, new HashSet<FalloutFormKey>(), new(), defaultProcessingDelay: 0, references: world);
            Check(ReferenceEquals(scripts.ActorQueries, world.ActorQueries), "Script query owners diverged.");
            var reads = 0;
            var binding = world.ActorQueries.BindVampire(() => { reads++; return declaration; });
            using (binding)
            {
                Check(reads == 0, "Binding eagerly read the owned executable.");
                scripts.Advance(0);
                Check(quests.Variable(Key(0x600), 1) == 1 && quests.Variable(Key(0x600), 2) == 0 && reads == 1,
                    "Bare/member fallback queries lost their result or source suffix.");
                var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false, _ => { }));
                void Execute(string body) => executor.ExecuteProgram(records.GetEffective(Key(0x600)), records.GetEffective(Key(0x601)),
                    FalloutGameModeProgram.Read("begin GameMode\n" + body + "\nend"), 0);
                Execute("set result to (player).GetVampire\nset done to 5");
                Check(quests.Variable(Key(0x600), 1) == 5 && reads == 1, "Typed reference query reread source or lost its suffix.");
                var identity = new FalloutDialogueSpeaker(Key(7), Key(7), Key(0x850), "Synthetic", null, false);
                var conditions = new FalloutDialogueConditions(records, quests, Key(0x900), identity, vampireQuery: world.ActorQueries.GetVampire);
                var condition = new FalloutCondition(records.GetEffective(Key(0x800)), 0, 0, 40, 0, 0, 0, 0);
                foreach (var scope in new uint[] { 0, 1, 2 })
                    Check(conditions.Evaluate(condition with { RunOn = scope }) == 0, "Source false query changed with subject scope.");
                Check(FalloutCondition.AllPass([condition], conditions.Evaluate, true) &&
                    !FalloutCondition.AllPass([condition with { Comparison = 1 }], conditions.Evaluate, true),
                    "Vampire query skipped authored comparison semantics.");
                Reject(() => conditions.Evaluate(condition with { RunOn = 3 }));
                Reject(() => Execute("set done to 6\nset result to player.GetVampire 1\nset done to 99"));
                Check(quests.Variable(Key(0x600), 1) == 6, "Invalid query ran its script suffix or discarded the prefix.");
                binding.Dispose();
                Reject(() => Execute("set done to 7\nset result to GetVampire\nset done to 99"));
                Check(quests.Variable(Key(0x600), 1) == 7, "Missing query declaration hid failure or ran its suffix.");
                Reject(() => conditions.Evaluate(condition));
            }
            Console.WriteLine("OPENNV_VAMPIRE_QUERY_CONTRACT_PASS sourceAssociation=true changedPredicateRefused=true shared=true lazy=true fallback=true typedReference=true sourceComparison=true prefixRetained=true absentDeclarationRefused=true");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Association()
    {
        var code = new byte[260];
        new byte[] { 0x55, 0x8b, 0xec, 0x8b, 0x45, 0x20, 0x50, 0x6a, 0, 0x6a, 0, 0x8b, 0x4d, 0x10, 0x51, 0xe8 }.CopyTo(code, 0);
        BinaryPrimitives.WriteInt32LittleEndian(code.AsSpan(16), 64 - 20);
        new byte[] { 0x83, 0xc4, 0x10, 0x5d, 0xc3 }.CopyTo(code, 20);
        var query = code.AsSpan(64);
        new byte[] { 0x55, 0x8b, 0xec, 0x8b, 0x45, 0x14, 0xd9, 0xee, 0xdd, 0x18, 0x83, 0x7d, 8, 0, 0x74, 45,
            0x8b, 0x4d, 8, 0x8b, 0x11, 0x8b, 0x4d, 8, 0x8b, 0x82 }.CopyTo(query);
        BinaryPrimitives.WriteUInt32LittleEndian(query[26..], 64);
        new byte[] { 0xff, 0xd0, 0x0f, 0xb6, 0xc8, 0x85, 0xc9, 0x74, 22, 0x8b, 0x4d, 8, 0xe8 }.CopyTo(query[30..]);
        BinaryPrimitives.WriteInt32LittleEndian(query[43..], 224 - (64 + 47));
        new byte[] { 0x0f, 0xb6, 0xd0, 0x85, 0xd2, 0x74, 7, 0x8b, 0x45, 0x14, 0xd9, 0xe8, 0xdd, 0x18, 0x8b, 0x0d }.CopyTo(query[47..]);
        new byte[] { 0x64, 0x8b, 0x15 }.CopyTo(query[67..]);
        new byte[] { 0x8b, 4, 0x8a, 0x0f, 0xb6, 0x88 }.CopyTo(query[74..]);
        new byte[] { 0x85, 0xc9, 0x74, 24, 0x8b, 0x55, 0x14, 0x83, 0xec, 8, 0xdd, 2, 0xdd, 0x1c, 0x24, 0x68 }.CopyTo(query[84..]);
        query[104] = 0xe8; BinaryPrimitives.WriteInt32LittleEndian(query[105..], 248 - (64 + 109));
        new byte[] { 0x83, 0xc4, 12, 0xb0, 1, 0x5d, 0xc3 }.CopyTo(query[109..]);
        new byte[] { 0x55, 0x8b, 0xec, 0x51, 0x89, 0x4d, 0xfc, 0x32, 0xc0, 0x8b, 0xe5, 0x5d, 0xc3 }.CopyTo(code, 224);
        code[248] = 0xc3;
        return code;
    }
    private static void Check(bool pass, string message) { if (!pass) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned vampire query was admitted.");
    }
    private static byte[] Text(string value) => Encoding.ASCII.GetBytes(value + '\0');
    private static byte[] Join(params byte[][] fields) => fields.SelectMany(field => field).ToArray();
    private static byte[] Local(uint index, string name)
    {
        var data = new byte[24]; BinaryPrimitives.WriteUInt32LittleEndian(data, index);
        return Join(Field("SLSD", data), Field("SCVR", Text(name)));
    }
    private static byte[] Field(string name, byte[] data)
    {
        var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(result, 6); return result;
    }
    private static byte[] Record(string name, uint id, params byte[][] fields)
    {
        var data = Join(fields); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), checked((uint)data.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), id); data.CopyTo(result, 24); return result;
    }
}
