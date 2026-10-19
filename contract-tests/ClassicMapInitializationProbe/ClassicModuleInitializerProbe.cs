using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Campaigns.Classic.Native;

internal static class ClassicModuleInitializerProbe
{
    internal static void Run(ClassicIntExpressionContext context)
    {
        var bytes = Source();
        var program = ClassicNativeIntReader.Read(bytes, "synthetic-module");
        var state = new ClassicIntProcedureState(new Dictionary<int, int>(), new Dictionary<int, int>(),
            new Dictionary<int, int>(), new Dictionary<int, int>(), new Dictionary<int, int>(), [], null);
        context = context with { ExternalVariables = new Dictionary<string, int>(), ExternalOwners = new Dictionary<string, string>() };
        var world = ClassicIntWorldObjectState.Empty;
        var before = JsonSerializer.Serialize(new { state, context.ExternalVariables, context.ExternalOwners });
        ClassicIntProcedureResult Run(ClassicIntProgram code, ClassicIntExpressionContext game, int budget = 100) =>
            ClassicIntProcedureVm.Execute(code, code.StartupProcedure!, state, game, world, null, budget, initializeModule: true);
        var result = Run(program, context);
        var capitalized = (byte[])bytes.Clone(); capitalized[74] = (byte)'S';
        var capitalizedProgram = ClassicNativeIntReader.Read(capitalized, program.Identity);
        if (capitalizedProgram.StartupProcedure != "Start" || Run(capitalizedProgram, context).State.ProgramVariables[0] != 12)
            throw new InvalidOperationException("Source startup name spelling was rejected or rewritten.");
        if (result.State.ProgramVariables.Count != 3 || result.State.ProgramVariables[0] != 12 ||
            result.State.ProgramVariables[1] != 9 || result.State.ProgramVariables[2] != 21 ||
            result.State.ValueStack.Count != 0 || result.ExternalVariables["Shared"] != 9 ||
            result.ExternalOwners["Shared"] != program.Identity || program.ProcedureOrder.Count != 1)
            throw new InvalidOperationException("Source module instructions, global frame, export or procedure indexes changed.");
        var redeclared = Run(program with { Identity = "SYNTHETIC-MODULE" }, context with
        {
            ExternalVariables = new Dictionary<string, int> { ["sHaReD"] = 77, ["InitializerOnly"] = 77 },
            ExternalOwners = new Dictionary<string, string> { ["sHaReD"] = program.Identity, ["InitializerOnly"] = program.Identity }
        });
        if (redeclared.ExternalVariables.Count != 2 || redeclared.ExternalVariables["sHaReD"] != 9 || redeclared.ExternalVariables["InitializerOnly"] != 0)
            throw new InvalidOperationException("Same-module export redeclaration or ASCII name comparison changed.");
        var save = new ClassicDoorWorldSave([], result.ExternalVariables, new Dictionary<int, int>(),
            new Dictionary<string, Dictionary<int, int>>()) { ExternalOwners = result.ExternalOwners };
        var cold = JsonSerializer.Deserialize<ClassicDoorWorldSave>(JsonSerializer.Serialize(save))!;
        _ = Run(program, context with { ExternalVariables = cold.Externals, ExternalOwners = cold.ExternalOwners });
        try
        {
            _ = Run(program with { Identity = "other-module" }, context with { ExternalVariables = cold.Externals, ExternalOwners = cold.ExternalOwners });
            throw new Exception("Cold export lost its owning module.");
        }
        catch (InvalidOperationException) { }
        foreach (var (code, game, budget) in new[]
        {
            (program, context with { ExternalVariables = result.ExternalVariables, ExternalOwners = new Dictionary<string, string>() }, 100),
            (program with { Identity = "other-module" }, context with { ExternalVariables = result.ExternalVariables, ExternalOwners = result.ExternalOwners }, 100),
            (program, context, 5),
            (program, context, result.ExecutedInstructions - 1)
        })
        {
            try { _ = Run(code, game, budget); throw new Exception("Invalid initializer was admitted."); }
            catch (InvalidOperationException) { }
        }
        var broken = (byte[])bytes.Clone();
        var fault = program.ModuleInitializer!.Instructions.Single(row => row.Opcode == 0x8003);
        BinaryPrimitives.WriteUInt16BigEndian(broken.AsSpan(fault.Offset), 0xffff);
        var unsupported = ClassicNativeIntReader.Read(broken, program.Identity);
        try { _ = Run(unsupported, context); throw new Exception("Unknown initializer opcode was skipped."); }
        catch (InvalidOperationException error) when (error.Message.Contains("unsupported-opcode-ffff", StringComparison.Ordinal)) { }
        if (JsonSerializer.Serialize(new { state, context.ExternalVariables, context.ExternalOwners }) != before)
            throw new InvalidOperationException("Failed module initialization mutated input state.");
        if (ClassicIntExportNames.Equal("À", "à") || !ClassicIntExportNames.Equal("Ascii", "aSCII"))
            throw new InvalidOperationException("Export comparison must fold ASCII only.");
        Console.WriteLine("OPENNV_CLASSIC_MODULE_INITIALIZER_PASS sourceInstructions=true globalFrame=true exports=true ownership=true rollback=true budget=true");
    }

    private static byte[] Source()
    {
        // Synthetic INT, including an export used only by the initializer and
        // non-contiguous globals. No owned script bytes enter this fixture.
        using var stream = new MemoryStream();
        void Word(int value)
        {
            Span<byte> buffer = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(buffer, value); stream.Write(buffer);
        }
        void Op(ushort opcode, int? operand = null)
        {
            Span<byte> buffer = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(buffer, opcode); stream.Write(buffer);
            if (operand is { } value) Word(value);
        }
        stream.Write(new byte[42]); Word(1);
        Word(4); Word(0); Word(0); Word(0); var bodyPointer = (int)stream.Position; Word(0); Word(0);
        var names = Encoding.ASCII.GetBytes("start\0Shared\0shared\0InitializerOnly\0");
        Word(names.Length); stream.Write(names); Word(-1); Word(-1);
        Op(0x802c); Op(0xc001, 5);
        Op(0x9001, 10); Op(0x8016);
        Op(0xc001, 9); Op(0x9001, 17); Op(0x8015);
        Op(0x9001, 24); Op(0x8016);
        Op(0xc001, 0); Op(0x8012); Op(0xc001, 7); Op(0x8039); Op(0xc001, 0); Op(0x8013);
        Op(0xc001, 0); Op(0xc001, 21); Op(0x8003);
        var jumpPointer = (int)stream.Position + 2; Op(0xc001, 0); Op(0x8004);
        var body = (int)stream.Position;
        Op(0x9001, 17); Op(0x8014); Op(0xc001, 1); Op(0x8013); Op(0x801c);
        var bytes = stream.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(bodyPointer), body);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(jumpPointer), body);
        return bytes;
    }
}
