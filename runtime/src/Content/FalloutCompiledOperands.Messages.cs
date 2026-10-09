namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledMessageArguments(IReadOnlyList<double> Substitutions);

internal static partial class FalloutCompiledOperands
{
    private static FalloutCompiledCommand Message(ushort opcode, ushort? receiver,
        ReadOnlyMemory<byte> payload, FalloutCompiledOperandContext context)
    {
        var cursor = new FalloutCompiledOperandCursor(payload);
        var declared = cursor.UInt16();
        if (declared > 0x7fff)
            throw new NotSupportedException("Compiled message compiler-override arguments are unowned.");
        if (declared != 1)
            throw new InvalidDataException("Compiled message requires its one declared MESG operand.");
        context.Budget.Spend();
        var form = Form(cursor, context);
        if (form.Kind != FalloutScriptValueKind.Form || form.Number == 0)
            throw new InvalidDataException("Compiled message operand is not a nonnull typed form.");
        var count = cursor.UInt16();
        if (count > 9)
            throw new InvalidDataException("Compiled message exceeds its nine numeric substitutions.");
        var substitutions = new double[count];
        for (var index = 0; index < substitutions.Length; ++index)
        {
            context.Budget.Spend();
            if (cursor.Peek == 0xff)
                throw new NotSupportedException("Compiled message inline extension arguments are unowned.");
            var value = Number(cursor, context);
            if (value.Kind != FalloutScriptValueKind.Number || !double.IsFinite(value.Number))
                throw new InvalidDataException("Compiled message substitution is not a finite number.");
            substitutions[index] = value.Number;
        }
        // Original ordinary message streams declare this exact zero footer.
        // Its nonzero behavior is not inferred from diagnostic source or from
        // another parser leaving bytes unread.
        if (cursor.Int32() != 0)
            throw new NotSupportedException("Compiled message nonzero footer behavior is unowned.");
        cursor.RequireEnd();
        return new(opcode, receiver, [new(form, 49)], new(substitutions));
    }
}
