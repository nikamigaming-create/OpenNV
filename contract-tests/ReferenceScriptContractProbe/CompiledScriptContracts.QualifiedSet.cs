using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    private static void QualifiedSet(string directory)
    {
        var selected = Path.Combine(directory, "qualified-set"); Directory.CreateDirectory(selected);
        byte[] Prefix(ushort receiver, byte[] instruction) => Join(U16(0x1c), U16(receiver), instruction);
        // Each compiled prefix supplies a calling reference. Explicit operand
        // owners retain precedence; the following instruction returns to its
        // original result scope. No diagnostic source is available.
        var code = Join(Prefix(1, Set('f', 1, " f\u0001\u0000 2 +")),
            Prefix(1, Set('f', 2, " f\u0001\u0000 4 +", reference: 2)),
            Set('f', 1, " 17", reference: 2));
        var references = Join(Record("REFR", 0x900, Field("NAME", U32(1)), Field("DATA", new byte[24])),
            Record("REFR", 0x901, Field("NAME", U32(2)), Field("DATA", new byte[24])));
        var group = new byte[24 + references.Length]; System.Text.Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
        UInt(group, 4, (uint)group.Length); UInt(group, 8, 0x800); UInt(group, 12, 6); references.CopyTo(group, 24);
        File.WriteAllBytes(Path.Combine(selected, "Bytecode.esm"), Join(Tes4(),
            Record("ACTI", 1, Field("SCRI", U32(0x50))), Record("ACTI", 2, Field("SCRI", U32(0x51))),
            Script(0x50, Instruction(0x1d), []), Script(0x51, Instruction(0x1d), []),
            Script(0x30, Instruction(0x1d), [], quest: true),
            Record("QUST", 0x20, Field("DATA", new byte[8]), Field("SCRI", U32(0x30)), Field("INDX", U16(10)), Field("QSDT", [0]),
                Field("SCHR", Header(code, 2)), Field("SCDA", code), Field("SCRO", U32(0x901)), Field("SCRO", U32(0x900))),
            Record("CELL", 0x800, Field("DATA", [1])), group));
        using var records = FalloutPluginStack.Load(selected, ["Bytecode.esm"]);
        using var world = new FalloutReferenceWorld(records); world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x800)));
        var quests = new FalloutQuestState(records);
        var source = records.GetEffective(Key(0x20));
        var program = FalloutCompiledScriptProgram.Read(source, FalloutScriptScope.QuestEntry(source, 2, 3), standalone: false);
        var scripts = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Qualified assignment dispatched an unrelated native effect.")));
        foreach (var _ in scripts.CompiledSteps(Key(0x20), program)) { }
        Require(program.Instructions.Count == 3 && program.Instructions[0].Receiver == 1 &&
            world.ReadVariable(quests, Key(0x901), 1) == 2 && world.ReadVariable(quests, Key(0x900), 2) == 6 &&
            world.ReadVariable(quests, Key(0x900), 1) == 17 && quests.Variable(Key(0x20), 1) == 0,
            "Qualified Set ignored its calling reference, lost explicit operand precedence or leaked context into the next instruction.");
        var saved = Copy(world.Capture());
        using var cold = new FalloutReferenceWorld(records); cold.Restore(saved);
        Require(cold.ReadVariable(quests, Key(0x901), 1) == 2 && cold.ReadVariable(quests, Key(0x900), 2) == 6,
            "Qualified assignment's shared reference locals did not survive serialized cold state.");
        Console.WriteLine("OPENNV_COMPILED_QUALIFIED_SET_PASS actualScda=true targetLocals=true explicitOwner=true contextReset=true serializedCold=true SCTXAbsent=true");
    }
}
