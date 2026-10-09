using OpenNV.Runtime.Content;

internal static partial class CompiledScriptContracts
{
    private static void CommandAliasContracts()
    {
        var source = new FalloutNativeScriptCommandDeclaration("MoveToMarker", "MoveTo", true, 1, [4, 2, 2, 2], true, 256);
        var declaration = FalloutCompiledCommandDeclarations.FromSource(source);
        var bound = FalloutCompiledSemanticBinding.Read(declaration);
        Require(declaration.Name == source.Name && declaration.Alias == source.Alias && declaration.RequiresReference &&
            declaration.Required == 1 && declaration.Parameters.SequenceEqual(source.Parameters) && declaration.VanillaArguments &&
            declaration.Flags == source.Flags &&
            bound is { SourceName: "MoveToMarker", SourceAlias: "MoveTo", ExecutionName: "MoveTo", Kind: FalloutCompiledSemanticKind.Effect },
            "Selected CommandInfo alias was discarded, renamed or separated from its original argument declaration.");
        Require(FalloutCompiledSemanticBinding.Read(declaration with { Name = "OtherSourceName" }) is
            { SourceName: "OtherSourceName", ExecutionName: "MoveTo", Kind: FalloutCompiledSemanticKind.Effect },
            "Semantic dispatch special-cased a command's canonical spelling instead of the selected declaration relationship.");
        Require(FalloutCompiledSemanticBinding.Read(declaration with { Alias = "" }).Kind == FalloutCompiledSemanticKind.Unowned &&
            FalloutCompiledSemanticBinding.Read(declaration with { Alias = "UnownedAlias" }).Kind == FalloutCompiledSemanticKind.Unowned,
            "A missing/changed source alias was guessed from an opcode or former name.");
        Reject(() => FalloutCompiledSemanticBinding.Read(declaration with { Name = "AddItem" }));
        Reject(() => FalloutCompiledSemanticBinding.Read(declaration with { Name = "GetDistance" }));
        Reject(() => FalloutCompiledSemanticBinding.Read(declaration with { Alias = "MoveTo " }));
        var query = FalloutCompiledSemanticBinding.Read(declaration with { Name = "GetDistance", Alias = "" });
        var message = FalloutCompiledSemanticBinding.Read(declaration with { Name = "UnownedMessageName", Alias = "ShowMessage" });
        Require(query is { ExecutionName: "GetDistance", Kind: FalloutCompiledSemanticKind.Query } &&
            message is { SourceName: "UnownedMessageName", ExecutionName: "ShowMessage", Kind: FalloutCompiledSemanticKind.Message },
            "Alias resolution crossed independent query/effect/special-parser owners.");
        var budget = new FalloutScriptExecutionBudget(100);
        var context = new FalloutCompiledOperandContext(index => index == 1 ? FalloutScriptValue.Form(0x14) :
            throw new InvalidDataException("Unexpected authored reference operand."),
            _ => throw new InvalidDataException("Unexpected authored local operand."),
            _ => throw new InvalidDataException("Unexpected authored global operand."),
            _ => throw new InvalidDataException("Unexpected authored embedded command."), budget,
            _ => declaration);
        var command = FalloutCompiledOperands.Command(0x109e, 2, Join(U16(4), Form(1),
            [(byte)'z'], BitConverter.GetBytes(2d), [(byte)'z'], BitConverter.GetBytes(-3d), [(byte)'z'], BitConverter.GetBytes(4d)), context);
        Require(command.Receiver == 2 && command.Arguments.Select(argument => argument.SourceParameterType).SequenceEqual(new byte[] { 4, 2, 2, 2 }) &&
            command.Arguments[0].Value is { Kind: FalloutScriptValueKind.Form, Number: 0x14 } &&
            command.Arguments.Skip(1).Select(argument => argument.Value.Number).SequenceEqual(new[] { 2d, -3d, 4d }),
            "Source-bound alias changed the original static receiver/reference or optional offset values.");
        Require(FalloutCompiledOperands.Command(0x109e, null, Join(U16(1), Form(1)), context).Arguments.Count == 1,
            "Alias decoding invented omitted optional operands instead of preserving the actual compiled argument stream.");
        Reject(() => FalloutCompiledOperands.Command(0x109e, 2, U16(0), context));
        Reject(() => FalloutCompiledOperands.Command(0x109e, 2, U16(5), context));
        var specialContext = context with { Declaration = _ => declaration with
            { Name = "UnownedMessageName", Alias = "ShowMessage", RequiresReference = false,
                Required = 1, Parameters = new byte[] { 49 }, VanillaArguments = false } };
        var special = FalloutCompiledOperands.Command(0x7ffe, null,
            Join(U16(1), Form(1), U16(1), Integer(7), BitConverter.GetBytes(0)), specialContext);
        Require(special.Message?.Substitutions.SequenceEqual(new[] { 7d }) == true &&
            special.Arguments is [{ SourceParameterType: 49 }],
            "Source alias bypassed the actual special message parser or changed its declared argument category.");
        Reject(() => FalloutCompiledOperands.Command(0x7ffe, null,
            Join(U16(1), Form(1), U16(0), BitConverter.GetBytes(0)), specialContext with
            { Declaration = _ => declaration with { Name = "UnownedMessageName", Alias = "ShowMessage", VanillaArguments = true } }));
        Console.WriteLine("OPENNV_COMPILED_COMMAND_ALIAS_PASS authoredOnly=true sameSourceDeclaration=true " +
            "rawNameRetained=true canonicalAndAlias=true realOperands=true conflictingOwnersRefused=true nativeGameplay=UNEXECUTED");
    }
}
