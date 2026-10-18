using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicNestedUseProbe
{
    internal static void Run(ClassicIntExpressionContext context)
    {
        var world = ClassicIntWorldObjectState.Empty with
        {
            Objects = new Dictionary<int, ClassicIntWorldObject> { [200] = new(200, 1, 100, 0, true) },
            Doors = new Dictionary<int, ClassicIntDoorObjectState> { [200] = new(false, false) }
        };
        var state = new ClassicIntProcedureState(new Dictionary<int, int> { [0] = 1 },
            new Dictionary<int, int> { [0] = 7 }, new Dictionary<int, int> { [0] = 2 },
            new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        var child = Program((0x80bc, null), (0x8131, null), (0xc001, 11), (0xc001, 0), (0x8013, null),
            (0xc001, 0), (0xc001, 12), (0x80c2, null), (0xc001, 99), (0xc001, 0), (0x8031, null),
            (0x80b9, null), (0x801c, null));
        var parent = Program((0xc001, 77), (0x80bc, null), (0x80db, null), (0x80bc, null), (0x8130, null),
            (0xc001, 0), (0x8032, null), (0xc001, 0), (0x8012, null), (0xc001, 0), (0x80c1, null), (0x801c, null));
        var dispatcher = new Dispatcher(child, context);
        var before = JsonSerializer.Serialize(new { state, world });
        var result = ClassicIntProcedureVm.Execute(parent, "probe", state, context with { ObjectUse = dispatcher }, world, null, 100);
        if (!result.State.ValueStack.SequenceEqual(new[] { 77, 1, 7, 11, 12 }) || result.WorldObjects.ScriptOverrides ||
            !result.WorldObjects.Doors[200].Open || result.ExecutedInstructions != parent.Instructions.Count + child.Instructions.Count)
            throw new InvalidOperationException("Nested use lost synchronous effects, caller frame, shared variables or instruction accounting.");
        var invalidParent = Program((0x80bc, null), (0x80db, null), (0xffff, null));
        foreach (var (program, owner, budget) in new (ClassicIntProgram, IClassicIntObjectUseDispatcher?, int)[]
        { (parent, null, 100), (parent, dispatcher, 5), (invalidParent, dispatcher, 100) })
        {
            try
            {
                _ = ClassicIntProcedureVm.Execute(program, "probe", state, context with { ObjectUse = owner }, world, null, budget);
                throw new Exception("Invalid nested use was accepted.");
            }
            catch (InvalidOperationException) { }
            if (JsonSerializer.Serialize(new { state, world }) != before)
                throw new InvalidOperationException("Failed nested use mutated its source state.");
        }
        Console.WriteLine("OPENNV_CLASSIC_NESTED_USE_PASS synchronous=true callerFrame=true sharedState=true overrideScoped=true budget=true rollback=true");
    }

    private sealed class Dispatcher(ClassicIntProgram child, ClassicIntExpressionContext context) : IClassicIntObjectUseDispatcher
    {
        public ClassicIntProcedureResult Use(int sourceObject, int targetObject, ClassicIntProcedureResult current, int remainingInstructionBudget)
        {
            if (sourceObject != 200 || targetObject != 200) throw new InvalidOperationException("Nested source identity changed.");
            var childState = current.State with { LocalVariables = new Dictionary<int, int>(), ValueStack = [] };
            var result = ClassicIntProcedureVm.Execute(child, "probe", childState,
                context with { SelfObject = targetObject, SourceObject = sourceObject },
                current.WorldObjects with { ScriptOverrides = false }, null, remainingInstructionBudget);
            return result with { ExecutedInstructions = current.ExecutedInstructions + result.ExecutedInstructions };
        }
    }

    private static ClassicIntProgram Program(params (ushort Opcode, int? Operand)[] source)
    {
        var offset = 0;
        var instructions = source.Select(row =>
        {
            var result = new ClassicIntInstruction(offset, row.Opcode, row.Operand);
            offset += row.Operand is null ? 2 : 6;
            return result;
        }).ToArray();
        var procedure = new ClassicIntProcedure("probe", 0, null, instructions);
        return new("synthetic-nested-use", [procedure], new Dictionary<string, ClassicIntProcedure> { ["probe"] = procedure },
            instructions.ToDictionary(row => row.Offset), new Dictionary<int, string>(), new Dictionary<int, string>());
    }
}
