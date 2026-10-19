using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicMapLocalsProbe
{
    internal static void Run(ClassicIntExpressionContext context)
    {
        var map = new byte[0xec + 20];
        void Word(int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(map.AsSpan(offset), value);
        Word(48, 2); Word(32, 3);
        Word(0xec, 91); Word(0xf0, 92);
        Word(0xf4, 17); Word(0xf8, -8); Word(0xfc, 36);
        var sourceBytes = map.ToArray();
        var allocated = ClassicMapScriptLocals.Read(map, 1, 2);
        if (allocated.Count != 2 || allocated[0] != -8 || allocated[1] != 36)
            throw new Exception("Allocated locals lost their offset after the map-variable array.");
        var lazy = ClassicMapScriptLocals.Read(map, -1, 3);
        if (lazy.Count != 0) throw new Exception("Unallocated locals read another variable block.");
        foreach (var (offset, count) in new[] { (-2, 3), (-1, -1), (2, 2), (4, 0), (int.MaxValue, 1), (0, int.MaxValue) })
            Reject(() => ClassicMapScriptLocals.Read(map, offset, count));
        Reject(() => ClassicMapScriptLocals.Read(map[..0xfc], 0, 1));
        Reject(() => ClassicMapScriptLocals.Read(map[..32], -1, 3));
        Word(32, -1); Reject(() => ClassicMapScriptLocals.Read(map, -1, 3)); Word(32, 3);
        Word(48, int.MaxValue); Reject(() => ClassicMapScriptLocals.Read(map, -1, 3)); Word(48, 2);
        var state = new ClassicIntProcedureState(new Dictionary<int, int> { [0] = -1 }, new Dictionary<int, int>(),
            lazy, new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        var program = Program();
        ClassicIntProcedureResult Execute(ClassicIntProcedureState input, int budget = 100) =>
            ClassicIntProcedureVm.Execute(program, "start", input, context, ClassicIntWorldObjectState.Empty, null, budget);
        var first = Execute(state);
        if (first.State.ProgramVariables[0] != 0 || first.State.ScriptLocalVariables[2] != 73 || lazy.Count != 0)
            throw new Exception("Lazy local read/write lost zero initialization or mutated its source instance.");
        var cold = JsonSerializer.Deserialize<ClassicIntProcedureState>(JsonSerializer.Serialize(first.State))!;
        if (Execute(cold).State.ProgramVariables[0] != 73 || Execute(state).State.ProgramVariables[0] != 0)
            throw new Exception("Local writes failed cold continuation or leaked into another script instance.");
        try { _ = Execute(state, 7); throw new Exception("Local transaction exceeded its instruction budget."); }
        catch (InvalidOperationException) { }
        if (lazy.Count != 0 || !map.SequenceEqual(sourceBytes)) throw new Exception("Local projection or failed execution mutated source data.");
        Console.WriteLine("OPENNV_CLASSIC_MAP_LOCALS_PASS lazyZero=true allocatedOffset=true invalidRangesRejected=true isolated=true cold=true rollback=true");
    }

    private static void Reject(Action action)
    {
        try { action(); throw new Exception("Invalid MAP local extent was accepted."); }
        catch (InvalidDataException) { }
    }

    private static ClassicIntProgram Program()
    {
        (ushort Opcode, int? Operand)[] source = [(0xc001, 2), (0x80c1, null), (0xc001, 0), (0x8013, null),
            (0xc001, 2), (0xc001, 73), (0x80c2, null), (0x801c, null)];
        var offset = 0;
        var code = source.Select(row =>
        {
            var instruction = new ClassicIntInstruction(offset, row.Opcode, row.Operand);
            offset += row.Operand is null ? 2 : 6; return instruction;
        }).ToArray();
        var procedure = new ClassicIntProcedure("start", 0, null, code);
        return new("synthetic-map-locals", [procedure], new Dictionary<string, ClassicIntProcedure> { ["start"] = procedure },
            code.ToDictionary(row => row.Offset), new Dictionary<int, string>(), new Dictionary<int, string>());
    }
}
