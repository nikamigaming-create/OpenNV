using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunBoundQueries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-bound-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            BoundQueriesThroughReader(directory);
            BoundQueryMissingOwnerPrefix(directory);
            BoundQueryAliasIdentity();
            BoundQueryAliasesThroughReader(directory);
            BoundQueryArgumentRefusals(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
        Console.WriteLine("OPENNV_COMPILED_BOUND_QUERY_PASS sourceDeclaration=true actualSharedFunctions=true winningTags=true coldTags=true unknownPrefix=true aliasIdentity=true");
    }

    private static byte[] BoundQueryExpression(ushort opcode, params byte[][] values)
    {
        var payload = Join(U16(checked((ushort)values.Length)), Join(values));
        return Join([(byte)'X'], U16(opcode), U16(checked((ushort)payload.Length)), payload);
    }
    private static byte[] BoundQueryDouble(double value) => Join([(byte)'z'], BitConverter.GetBytes(value));
    private static byte[] BoundQuerySet(ushort slot, byte[] expression) =>
        Instruction(0x15, Join([(byte)'f'], U16(slot), U16(checked((ushort)expression.Length)), expression));

    private static void BoundQueryFixture(string directory, byte[] body, Action<FalloutPluginStack> run)
    {
        var selected = Path.Combine(directory, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(selected);
        string[] names = ["Barter", "EnergyWeapons", "Explosives", "Lockpick", "Medicine", "MeleeWeapons",
            "Repair", "Science", "SmallGuns", "Sneak", "Speech", "Throwing", "Unarmed"];
        var original = Path.Combine(selected, "Bytecode.esm"); var patch = Path.Combine(selected, "Patch.esp");
        File.WriteAllBytes(original, Join(RecurrenceFixture("Diagnostic text cannot choose a getter or tag state.",
            StructuralStandalone(Block(0, body))), Join(names.Select((name, index) => Record("AVIF", (uint)(0x100 + index),
                Field("EDID", Text("AV" + name)), Field("FULL", Text("Authored " + name)))).ToArray())));
        File.WriteAllBytes(patch, Join(Tes4("Bytecode.esm"), Record("AVIF", 0x107,
            Field("EDID", Text("RenamedScience")), Field("FULL", Text("Winning science")))));
        var hashes = new[] { original, patch }.Select(path => SHA256.HashData(File.ReadAllBytes(path))).ToArray();
        using (var records = FalloutPluginStack.Load(selected, ["Bytecode.esm", "Patch.esp"])) run(records);
        Require(hashes[0].SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))) &&
            hashes[1].SequenceEqual(SHA256.HashData(File.ReadAllBytes(patch))), "Bound query altered an original fixture input.");
    }

    private static FalloutReferenceScripts BoundQueryExecutor(FalloutPluginStack records, FalloutReferenceWorld world,
        FalloutQuestState quests, FalloutPlayerTagSkills? tags) => new(records, world, quests,
            new((_, _) => throw new InvalidDataException("Bound query requested unrelated furniture state."),
                _ => throw new InvalidDataException("Bound query fabricated a presentation effect."), TagSkills: tags));

    private static void BoundQueriesThroughReader(string directory)
    {
        var abs = BoundQueryExpression(0x1221, BoundQueryDouble(-4.5));
        var science = BoundQueryExpression(0x11ce, U16(40)); var guns = BoundQueryExpression(0x11ce, U16(41));
        var body = Join(Call(0x1221, Join(U16(1), BoundQueryDouble(-8))), BoundQuerySet(1, abs),
            BoundQuerySet(2, science), BoundQuerySet(3, guns));
        BoundQueryFixture(directory, body, records =>
        {
            var tags = new FalloutPlayerTagSkills(records, FalloutNativeTagSkillResolver.ResolveSkills(records));
            tags.Set("Science", 3); tags.Set("SmallGuns", 1);
            Require(tags.Capture().Slots[3]?.EditorId == "RenamedScience", "Tag state replaced the winning AVIF with its old name.");
            var quests = new FalloutQuestState(records); using var world = new FalloutReferenceWorld(records);
            var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
            var flow = FalloutCompiledControlFlow.Read(program.EventInstructions(program.Events[0]));
            var cursor = new FalloutCompiledExecutionCursor(flow.InitialCursor);
            foreach (var _ in BoundQueryExecutor(records, world, quests, tags).CompiledSteps(Key(0x20), program,
                program.Events[0], cursor: cursor)) { }
            Require(quests.Variable(Key(0x20), 1) == 4.5 && quests.Variable(Key(0x20), 2) == 1 &&
                quests.Variable(Key(0x20), 3) == 1 && cursor.State is { Completed: true, CommittedInstructions: 4 },
                "Full-reader queries failed to call real math/tag owners or query-as-statement did not retire.");
            var tagSnapshot = JsonSerializer.Deserialize<FalloutPlayerTagSkillsSnapshot>(JsonSerializer.Serialize(tags.Capture()))!;
            using var coldWorld = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(Copy(quests.Capture()));
            var coldTags = new FalloutPlayerTagSkills(records, FalloutNativeTagSkillResolver.ResolveSkills(records), snapshot: tagSnapshot);
            coldTags.Set("Guns", 3);
            foreach (var _ in BoundQueryExecutor(records, coldWorld, coldQuests, coldTags).CompiledSteps(Key(0x20), program, program.Events[0])) { }
            Require(coldQuests.Variable(Key(0x20), 1) == 4.5 && coldQuests.Variable(Key(0x20), 2) == 0 &&
                coldQuests.Variable(Key(0x20), 3) == 1 && tags.IsTagged("Science"),
                "Cold/current query used another owner's tags, stale membership, or an invented readiness result.");
        });
    }

    private static void BoundQueryMissingOwnerPrefix(string directory)
    {
        var body = Join(BoundQuerySet(1, BoundQueryExpression(0x1221, BoundQueryDouble(-7))),
            BoundQuerySet(2, BoundQueryExpression(0x11ce, U16(40))), Set('f', 3, " 999"));
        BoundQueryFixture(directory, body, records =>
        {
            var quests = new FalloutQuestState(records); using var world = new FalloutReferenceWorld(records);
            var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
            var flow = FalloutCompiledControlFlow.Read(program.EventInstructions(program.Events[0]));
            var cursor = new FalloutCompiledExecutionCursor(flow.InitialCursor);
            Reject(() => { foreach (var _ in BoundQueryExecutor(records, world, quests, null).CompiledSteps(Key(0x20), program,
                program.Events[0], cursor: cursor)) { } });
            Require(quests.Variable(Key(0x20), 1) == 7 && quests.Variable(Key(0x20), 2) == 0 &&
                quests.Variable(Key(0x20), 3) == 0 && cursor.State.CommittedInstructions == 1 && !cursor.State.Completed,
                "Missing real tag owner manufactured false/success or lost its committed prefix.");
        });
    }

    private static void BoundQueryAliasIdentity()
    {
        var entered = 0; Func<IReadOnlyList<FalloutScriptArgument>, double> invoke = _ => ++entered;
        var primary = new FalloutScriptFunction([], invoke); var alias = new FalloutScriptFunction([], invoke);
        var declaration = new FalloutCompiledCommandDeclaration("SourceGetter", false, 0, [], true, "SourceAlias");
        var bound = FalloutCompiledSemanticBinding.ResolveQuery(declaration, name => name == "SourceGetter" ? primary : alias)!;
        Require(entered == 0 && ReferenceEquals(bound.Function, primary) && bound.Semantic.SourceName == "SourceGetter" &&
            bound.Semantic.SourceAlias == "SourceAlias", "Alias resolution executed a getter or discarded source provenance.");
        _ = bound.Function.InvokeValue([]); Require(entered == 1, "Actual bound getter was replaced by a synthetic result.");
        var other = new FalloutScriptFunction([], _ => { entered += 100; return entered; });
        Reject(() => FalloutCompiledSemanticBinding.ResolveQuery(declaration, name => name == "SourceGetter" ? primary : other));
        foreach (var mismatch in new[]
        {
            new FalloutScriptFunction([FalloutScriptArgumentKind.OptionalNumber], invoke),
            new FalloutScriptFunction([], invoke) { ReadOnly = true },
            new FalloutScriptFunction([], invoke) { Variadic = FalloutScriptArgumentKind.Number },
        })
            Reject(() => FalloutCompiledSemanticBinding.ResolveQuery(declaration,
                name => name == "SourceGetter" ? primary : mismatch));
        Require(entered == 1, "Conflicting alias entered an effect before refusal.");
        var firstOwner = new BoundQueryAliasOwner(); var secondOwner = new BoundQueryAliasOwner();
        var first = new FalloutScriptFunction([], firstOwner.Read);
        var sameOwner = new FalloutScriptFunction([], firstOwner.Read);
        var differentOwner = new FalloutScriptFunction([], secondOwner.Read);
        Require(ReferenceEquals(FalloutCompiledSemanticBinding.ResolveQuery(declaration,
            name => name == "SourceGetter" ? first : sameOwner)!.Function, first),
            "Aliases bound to the same actual receiver were rejected.");
        Reject(() => FalloutCompiledSemanticBinding.ResolveQuery(declaration,
            name => name == "SourceGetter" ? first : differentOwner));
        Require(firstOwner.Calls == 0 && secondOwner.Calls == 0,
            "Alias identity comparison executed a receiver or accepted a different receiver's same method.");
        var aliasOnly = FalloutCompiledSemanticBinding.ResolveQuery(declaration, name => name == "SourceAlias" ? alias : null)!;
        Require(aliasOnly.Semantic.ExecutionName == "SourceAlias" && ReferenceEquals(aliasOnly.Function, alias),
            "Exact selected alias failed to retain its living owner.");
        Require(FalloutCompiledSemanticBinding.ResolveQuery(declaration, _ => null) is null, "Unknown getter became admitted.");
        Reject(() => FalloutCompiledSemanticBinding.ResolveQuery(declaration with { Name = "bad name" }, _ => primary));
        Reject(() => FalloutCompiledSemanticBinding.ResolveQuery(declaration with { Name = "GetStage", Alias = "MenuMode" }, _ => primary));
        Require(FalloutCompiledSemanticBinding.ResolveQuery(FalloutCompiledCommandDeclarations.Get(0x1039),
            _ => throw new InvalidDataException("Effect was probed as a getter.")) is null, "Effect became a query through a live resolver.");
    }

    private sealed class BoundQueryAliasOwner
    {
        internal int Calls { get; private set; }
        internal double Read(IReadOnlyList<FalloutScriptArgument> _) => ++Calls;
    }

    private static void BoundQueryAliasesThroughReader(string directory)
    {
        Require(FalloutCompiledCommandDeclarations.Get(0x10ce).Alias == "this" &&
            FalloutCompiledCommandDeclarations.Get(0x10cd).Alias == "getAR",
            "Authored full-reader alias fixture omitted the original command's selected alias.");
        byte[] Compare(ushort opcode, ushort receiver, ushort expected) => Join(
            Query(opcode, U16(0), receiver), Ascii(" "), [(byte)'Z'], U16(expected), Ascii(" =="));
        var body = Join(BoundQuerySet(4, Join([(byte)'Z'], U16(1))),
            BoundQuerySet(1, Compare(0x10ce, 3, 1)),
            BoundQuerySet(4, Join([(byte)'Z'], U16(2))),
            BoundQuerySet(2, Compare(0x10ce, 3, 2)), BoundQuerySet(3, Compare(0x10cd, 3, 2)));
        var code = StructuralStandalone(Block(0, body));
        var selected = Path.Combine(directory, "registered-aliases"); Directory.CreateDirectory(selected);
        var input = Path.Combine(selected, "Bytecode.esm");
        File.WriteAllBytes(input, Join(Tes4(), Record("NPC_", 2),
            Record("QUST", 0x20, Field("DATA", new byte[8]), Field("SCRI", U32(0x30))),
            Record("SCPT", 0x30, Field("SCHR", Header(code, 3, 4, 1)), Field("SCDA", code),
                Local(1, "self"), Local(2, "other"), Local(3, "action"), Local(4, "target"),
                Field("SCRO", U32(0x901)), Field("SCRO", U32(0x902)), Field("SCRV", U32(4))),
            Record("CELL", 0x800, Field("DATA", [1])), NestedActorGroup()));
        var hash = SHA256.HashData(File.ReadAllBytes(input));
        using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records); var scripts = BoundQueryExecutor(records, world, quests, null);
        var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
        foreach (var _ in scripts.CompiledSteps(Key(0x20), program, program.Events[0], action: Key(0x902))) { }
        Require(quests.Variable(Key(0x20), 1) == 1 && quests.Variable(Key(0x20), 2) == 1 &&
            quests.Variable(Key(0x20), 3) == 1,
            "Real declared aliases failed to share callable identity or merged different receiver bindings.");
        foreach (var _ in scripts.CompiledSteps(Key(0x20), program, program.Events[0], action: Key(0x901))) { }
        Require(quests.Variable(Key(0x20), 1) == 1 && quests.Variable(Key(0x20), 2) == 1 &&
            quests.Variable(Key(0x20), 3) == 0,
            "Alias callable retained another invocation's action reference or cached a query result.");
        Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))),
            "Alias execution changed its authored full-reader input.");
    }

    private static void BoundQueryArgumentRefusals(string directory)
    {
        foreach (var expression in new[] { BoundQueryExpression(0x1221), BoundQueryExpression(0x1221, U16(40)),
            BoundQueryExpression(0x11ce, Join(U16(40), [1])) })
            BoundQueryFixture(directory, BoundQuerySet(1, expression), records =>
            {
                var quests = new FalloutQuestState(records); using var world = new FalloutReferenceWorld(records);
                var program = FalloutQuestScriptAuthority.ReadCompiled(records.GetEffective(Key(0x30)))!;
                Reject(() => { foreach (var _ in BoundQueryExecutor(records, world, quests, null).CompiledSteps(Key(0x20), program,
                    program.Events[0])) { } });
                Require(quests.Variable(Key(0x20), 1) == 0, "Malformed query operand mutated its target.");
            });
    }
}
