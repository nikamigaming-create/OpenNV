using OpenNV.Runtime.Campaigns.Classic;

internal static class ClassicComparisonProbe
{
    internal static void Run(ClassicIntExpressionContext context)
    {
        var state = new ClassicIntProcedureState(new Dictionary<int, int>(), new Dictionary<int, int>(),
            new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        foreach (var (left, right, expected) in new[]
        {
            (int.MinValue, int.MaxValue, 1), (int.MaxValue, int.MinValue, 0),
            (-1, 0, 1), (0, -1, 0), (0, 0, 1), (int.MinValue, int.MinValue, 1),
        })
        {
            ClassicIntInstruction[] code = [new(0, 0xc001, left), new(6, 0xc001, right),
                new(12, 0x8035, null), new(14, 0x801c, null)];
            var procedure = new ClassicIntProcedure("compare", 0, null, code);
            var program = new ClassicIntProgram("signed-comparison", [procedure],
                new Dictionary<string, ClassicIntProcedure> { [procedure.Name] = procedure },
                code.ToDictionary(row => row.Offset), new Dictionary<int, string>(), new Dictionary<int, string>());
            var result = ClassicIntProcedureVm.Execute(program, procedure.Name, state, context,
                ClassicIntWorldObjectState.Empty, null, 4);
            if (!result.State.ValueStack.SequenceEqual([expected]) || state.ValueStack.Count != 0)
                throw new InvalidOperationException("Signed <= changed operand order, boundary comparison or input state.");
        }
        Console.WriteLine("OPENNV_CLASSIC_COMPARISON_PASS signedLessEqual=true boundaries=true operandOrder=true");
        var world = ClassicIntWorldObjectState.Empty with
        {
            Objects = new Dictionary<int, ClassicIntWorldObject> { [99] = new(99, 1, 200, 2, true) },
        };
        foreach (var (handle, expected) in new[] { (0, 0), (99, 2), (98, -1) })
        {
            ClassicIntInstruction[] code = [new(0, 0xc001, handle), new(6, 0x80ec, null), new(8, 0x801c, null)];
            var procedure = new ClassicIntProcedure("elevation", 0, null, code);
            var program = new ClassicIntProgram("object-elevation", [procedure],
                new Dictionary<string, ClassicIntProcedure> { [procedure.Name] = procedure },
                code.ToDictionary(row => row.Offset), new Dictionary<int, string>(), new Dictionary<int, string>());
            try
            {
                var result = ClassicIntProcedureVm.Execute(program, procedure.Name, state, context, world, null, 3);
                if (expected < 0 || !result.State.ValueStack.SequenceEqual([expected]))
                    throw new Exception("Object elevation did not use the authoritative object handle.");
            }
            catch (InvalidOperationException error) when (expected < 0 && error.Message.Contains("missing-world-object", StringComparison.Ordinal)) { }
        }
        Console.WriteLine("OPENNV_CLASSIC_OBJECT_ELEVATION_PASS authoritative=true null=true unknownRejected=true");
    }
}
