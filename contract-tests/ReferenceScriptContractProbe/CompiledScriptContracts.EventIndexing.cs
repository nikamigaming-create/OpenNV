using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    internal static void RunEventIndexing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-compiled-event-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Bytecode.esm");
            var code = Join(Instruction(0x1d), Block(0, Set('f', 1, " 11")), Block(21, []),
                Block(0, Join(StructuralAdd('f', 1, 7), Instruction(0x1e), Set('f', 1, " 999"))),
                Block(2, Set('f', 2, " 37")), Block(0, StructuralAdd('f', 2, 2)));
            var reference = Record("REFR", 0x900, Field("NAME", U32(0x100)), Field("DATA", new byte[24]));
            var group = new byte[24 + reference.Length];
            System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6);
            reference.CopyTo(group, 24);
            File.WriteAllBytes(path, Join(Tes4(), Script(0x50, code, []), Script(0x51, code, []),
                Script(0x52, Instruction(0x1d), []), Record("STAT", 0x100, Field("SCRI", U32(0x50))),
                Record("CELL", 0x800, Field("DATA", [1])), group));
            var sourceHash = SHA256.HashData(File.ReadAllBytes(path));
            using var records = FalloutPluginStack.Load(directory, ["Bytecode.esm"]);
            FalloutCompiledScriptProgram Read(uint id)
            {
                var source = records.GetEffective(Key(id));
                return FalloutCompiledScriptProgram.Read(source, FalloutScriptScope.Standalone(source), standalone: true);
            }
            var program = Read(0x50);
            var reparsed = Read(0x50);
            var foreign = Read(0x51);
            var empty = Read(0x52);
            Require(program.ProgramSha256 == reparsed.ProgramSha256 && program.Events.Count == 5 && empty.Events.Count == 0,
                "Event indexing changed repeated source identity or empty-script framing.");
            ushort[][] expectedOpcodes = [[0x15], [], [0x15, 0x1e, 0x15], [0x15], [0x15]];
            var indexed = new List<FalloutCompiledInstruction>();
            for (var ordinal = 0; ordinal < program.Events.Count; ++ordinal)
            {
                var block = program.Events[ordinal];
                var body = program.EventInstructions(block);
                Require(program.OwnsEvent(block) && ReferenceEquals(body, program.EventInstructions(block)) &&
                    body.Select(instruction => instruction.Opcode).SequenceEqual(expectedOpcodes[ordinal]),
                    "Event body lost its retained instruction slice, empty boundary or original order.");
                indexed.AddRange(body);
                var clone = block with { };
                Require(clone == block && !program.OwnsEvent(clone) && !program.OwnsEvent(reparsed.Events[ordinal]) &&
                    !program.OwnsEvent(foreign.Events[ordinal]) && !empty.OwnsEvent(block),
                    "Event membership accepted structural equality or another decoding's event identity.");
                foreach (var unowned in new[] { clone, reparsed.Events[ordinal], foreign.Events[ordinal] })
                {
                    Reject(() => program.EventInstructions(unowned).ToArray());
                    Reject(() => FalloutCompiledScriptEvents.ActorCallbackFilter(program, unowned));
                }
                Reject(() => empty.EventInstructions(block).ToArray());
            }
            var originalBodies = program.Instructions.Where(instruction => instruction.Opcode is not (0x1d or 0x10 or 0x11)).ToArray();
            Require(indexed.Count == originalBodies.Length && indexed.Zip(originalBodies)
                .All(pair => ReferenceEquals(pair.First, pair.Second)),
                "Event slices copied instructions, crossed a header/END boundary or omitted the final body.");

            var cell = FalloutCellSceneReader.Read(records, Key(0x800));
            using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
            var quests = new FalloutQuestState(records);
            FalloutReferenceScripts Scripts(FalloutReferenceWorld state) => new(records, state, quests,
                new((_, _) => false, _ => throw new InvalidDataException("Unexpected indexed-event effect.")));
            var scripts = Scripts(world);
            var results = scripts.DispatchFrame(Key(0x900), [new("OnLoad"), new("GameMode")], .125);
            Require(results[0] is { Error: null, Blocks: 1 } && results[1] is { Error: null, Blocks: 3 } &&
                world.Get(Key(0x900)).Read(1) == 18 && world.Get(Key(0x900)).Read(2) == 2,
                "Indexed callbacks changed source block order, empty blocks or Return's event boundary.");
            var saved = Copy(world.Capture().ToArray());
            using var cold = new FalloutReferenceWorld(records); cold.Restore(saved); cold.LoadCell(cell);
            var coldScripts = Scripts(cold);
            Require(coldScripts.Dispatch(Key(0x900), "OnActivate", records.RuntimeFormKey(FalloutScriptBindings.PlayerReferenceId))
                    is { Error: null, Blocks: 1 } &&
                coldScripts.Dispatch(Key(0x900), "GameMode") is { Error: null, Blocks: 3 } &&
                cold.Get(Key(0x900)).Read(1) == 18 && cold.Get(Key(0x900)).Read(2) == 39,
                "Cold event reconstruction leaked another block or replayed a completed invocation.");
            Require(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))),
                "Event indexing or execution changed original source bytes.");
            Console.WriteLine("OPENNV_COMPILED_EVENT_INDEX_PASS exactEventIdentity=true retainedSlices=true " +
                "emptyAndFinalBoundaries=true sourceOrder=true returnBoundary=true cold=true sourceUnchanged=true renderer=NONE gameplay=UNEXECUTED");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
