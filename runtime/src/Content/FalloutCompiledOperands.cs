using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutCompiledVariable(ushort? OwnerReference, ushort Slot, byte Storage);
internal sealed record FalloutCompiledArgument(FalloutScriptValue Value, byte SourceParameterType);
internal sealed record FalloutCompiledCommand(ushort Opcode, ushort? Receiver,
    IReadOnlyList<FalloutCompiledArgument> Arguments, FalloutCompiledMessageArguments? Message = null);

// The decoder reports bound operands. It never recovers commands or values
// from SCTX, EDID spellings, host output, or a decompiled expression.
internal sealed record FalloutCompiledOperandContext(
    Func<ushort, FalloutScriptValue> Reference,
    Func<FalloutCompiledVariable, FalloutScriptValue> Variable,
    Func<ushort, FalloutScriptValue> Global,
    Func<FalloutCompiledCommand, FalloutScriptValue> Query,
    FalloutScriptExecutionBudget Budget,
    Func<ushort, FalloutCompiledCommandDeclaration>? Declaration = null,
    Func<ushort, ushort?, ReadOnlyMemory<byte>, FalloutScriptValue?>? NativeQuery = null);

internal sealed class FalloutCompiledOperandCursor(ReadOnlyMemory<byte> data)
{
    internal int Offset { get; private set; }
    internal bool AtEnd => Offset == data.Length;
    internal byte Peek => !AtEnd ? data.Span[Offset] : throw new InvalidDataException("Compiled operand is truncated.");
    internal ReadOnlyMemory<byte> Bytes(int count)
    {
        if (count < 0 || count > data.Length - Offset)
            throw new InvalidDataException("Compiled operand exceeds its instruction extent.");
        var result = data.Slice(Offset, count); Offset += count; return result;
    }
    internal byte Byte() => Bytes(1).Span[0];
    internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2).Span);
    internal int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4).Span);
    internal double Double() => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Bytes(8).Span));
    internal void RequireEnd()
    { if (!AtEnd) throw new InvalidDataException("Compiled operand has unconsumed bytes."); }
    internal FalloutCompiledVariable Variable(bool allowLong = false)
    {
        ushort? reference = null;
        var type = Byte();
        if (type == 'r') { reference = UInt16(); type = Byte(); }
        if (type is not ((byte)'f' or (byte)'s') && !(allowLong && type == 'l'))
            throw new NotSupportedException("Compiled variable storage tag is unowned.");
        var slot = UInt16();
        if (slot == 0) throw new InvalidDataException("Compiled variable slot zero is invalid.");
        return new(reference, slot, type);
    }
    internal string String()
    {
        var value = Bytes(UInt16());
        if (value.Length > 0x200 || value.Span.Contains((byte)0))
            throw new NotSupportedException("Compiled vanilla string exceeds its owned extent or contains null.");
        return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(value.Span);
    }
    internal bool Match(string value)
    {
        if (value.Length > data.Length - Offset) return false;
        for (var index = 0; index < value.Length; ++index)
            if (data.Span[Offset + index] != value[index]) return false;
        Offset += value.Length; return true;
    }
    internal double AsciiNumber()
    {
        var start = Offset;
        if (Peek == '.') Byte();
        while (!AtEnd && Peek is >= (byte)'0' and <= (byte)'9') Byte();
        if (!AtEnd && Peek == '.')
        {
            Byte(); while (!AtEnd && Peek is >= (byte)'0' and <= (byte)'9') Byte();
        }
        if (!AtEnd && Peek is (byte)'e' or (byte)'E')
        {
            Byte(); if (!AtEnd && Peek is (byte)'+' or (byte)'-') Byte();
            var digits = Offset;
            while (!AtEnd && Peek is >= (byte)'0' and <= (byte)'9') Byte();
            if (Offset == digits) throw new InvalidDataException("Compiled exponent has no digits.");
        }
        var text = Encoding.ASCII.GetString(data.Span[start..Offset]);
        if (text.Length == 0 || text.Length > 0x200 || !double.TryParse(text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new InvalidDataException("Compiled numeric literal is invalid.");
        return number;
    }
}

internal static partial class FalloutCompiledOperands
{
    // Original vanilla parameter categories. Unowned category8 and extension
    // expression overrides remain explicit refusals; no token guessing occurs.
    private static readonly byte[] Extraction =
    [
        0, 1, 4, 6, 6, 2, 6, 6, 3, 6, 2, 6, 6, 6, 6, 6, 6, 6, 2, 6, 6, 6, 8, 1,
        6, 6, 6, 6, 2, 6, 6, 6, 3, 6, 6, 6, 6, 6, 6, 6, 6, 2, 6, 6, 5, 7, 8, 6,
        6, 6, 6, 2, 2, 6, 6, 2, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6
    ];

    internal static FalloutCompiledCommand Command(ushort opcode, ushort? receiver,
        ReadOnlyMemory<byte> payload, FalloutCompiledOperandContext context)
    {
        var declaration = context.Declaration?.Invoke(opcode) ?? FalloutCompiledCommandDeclarations.Get(opcode);
        if (FalloutCompiledSemanticBinding.Read(declaration).Kind == FalloutCompiledSemanticKind.Message)
        {
            if (declaration.VanillaArguments)
                throw new NotSupportedException("Compiled ShowMessage declares a different selected argument parser.");
            return Message(opcode, receiver, payload, context);
        }
        if (!declaration.VanillaArguments)
            throw new NotSupportedException("Compiled command uses an unowned special argument parser.");
        var cursor = new FalloutCompiledOperandCursor(payload);
        var count = cursor.AtEnd ? 0 : cursor.UInt16();
        if (count > 0x7fff)
            throw new NotSupportedException("NVSE compiler-override argument stream is unowned.");
        if (count < declaration.Required || count > declaration.Parameters.Count)
            throw new InvalidDataException("Compiled command argument count differs from its declaration.");
        var arguments = new List<FalloutCompiledArgument>(count);
        for (var index = 0; index < count; ++index)
        {
            context.Budget.Spend();
            var type = declaration.Parameters[index];
            if (type >= Extraction.Length)
                throw new NotSupportedException("Compiled parameter has an unknown vanilla extraction category.");
            if (cursor.Peek == 0xff)
                throw new NotSupportedException("Inline NVSE argument expression is unowned.");
            var value = Extraction[type] switch
            {
                0 => FalloutScriptValue.String(cursor.String()),
                1 or 4 or 5 => Number(cursor, context),
                2 => (FalloutScriptValue)cursor.UInt16(),
                3 => (FalloutScriptValue)cursor.Byte(),
                6 => Form(cursor, context),
                _ => throw new NotSupportedException("Compiled parameter extraction behavior is unowned.")
            };
            if (Extraction[type] is 1 or 4 or 5 && value.Kind != FalloutScriptValueKind.Number)
                throw new NotSupportedException("Compiled numeric parameter has a nonnumeric storage class.");
            if (Extraction[type] == 1 && (value.Number < int.MinValue || value.Number > int.MaxValue ||
                value.Number != Math.Truncate(value.Number)))
                throw new NotSupportedException("Compiled integer conversion outside exact Int32 is unowned.");
            arguments.Add(new(value, type));
        }
        cursor.RequireEnd();
        return new(opcode, receiver, arguments);
    }

    private static FalloutScriptValue Number(FalloutCompiledOperandCursor cursor, FalloutCompiledOperandContext context)
    {
        return cursor.Peek switch
        {
            (byte)'n' => ReadInteger(cursor),
            (byte)'z' => ReadDouble(cursor),
            (byte)'G' => ReadGlobal(cursor, context),
            _ => context.Variable(cursor.Variable())
        };
    }
    private static FalloutScriptValue ReadInteger(FalloutCompiledOperandCursor cursor)
    { _ = cursor.Byte(); return cursor.Int32(); }
    private static FalloutScriptValue ReadDouble(FalloutCompiledOperandCursor cursor)
    { _ = cursor.Byte(); return cursor.Double(); }
    private static FalloutScriptValue ReadGlobal(FalloutCompiledOperandCursor cursor, FalloutCompiledOperandContext context)
    { _ = cursor.Byte(); return context.Global(cursor.UInt16()); }
    private static FalloutScriptValue Form(FalloutCompiledOperandCursor cursor, FalloutCompiledOperandContext context)
    {
        if (cursor.Peek == 'r') { _ = cursor.Byte(); return context.Reference(cursor.UInt16()); }
        var value = context.Variable(cursor.Variable());
        if (value.Kind != FalloutScriptValueKind.Form)
            throw new InvalidDataException("Compiled form parameter has no typed reference local.");
        return value;
    }

    internal static FalloutScriptValue Expression(ReadOnlyMemory<byte> expression, FalloutCompiledOperandContext context)
    {
        var cursor = new FalloutCompiledOperandCursor(expression);
        var stack = new Stack<FalloutScriptValue>();
        ushort? reference = null;
        while (!cursor.AtEnd)
        {
            context.Budget.Spend();
            if (cursor.Peek <= 0x20) { _ = cursor.Byte(); continue; }
            switch (cursor.Peek)
            {
                case (byte)'r':
                    if (reference is not null) throw new InvalidDataException("Compiled expression reference prefix is duplicated.");
                    _ = cursor.Byte(); reference = cursor.UInt16(); continue;
                case (byte)'s':
                case (byte)'l':
                case (byte)'f':
                    var local = cursor.Variable(allowLong: true);
                    stack.Push(context.Variable(local with { OwnerReference = reference })); reference = null; break;
                case (byte)'G':
                    if (reference is not null) throw new InvalidDataException("Global operand has an unrelated reference prefix.");
                    stack.Push(ReadGlobal(cursor, context)); break;
                case (byte)'Z':
                    if (reference is not null) throw new InvalidDataException("Form operand has an unrelated reference prefix.");
                    _ = cursor.Byte(); stack.Push(context.Reference(cursor.UInt16())); break;
                case (byte)'X':
                    _ = cursor.Byte(); var opcode = cursor.UInt16();
                    var payload = cursor.Bytes(cursor.UInt16());
                    if (context.NativeQuery?.Invoke(opcode, reference, payload) is { } native)
                        stack.Push(native);
                    else stack.Push(context.Query(Command(opcode, reference, payload, context)));
                    reference = null; break;
                case (byte)'"':
                    if (reference is not null) throw new InvalidDataException("String operand has an unrelated reference prefix.");
                    _ = cursor.Byte(); stack.Push(FalloutScriptValue.String(cursor.String())); break;
                case (byte)'n':
                case (byte)'z':
                    throw new NotSupportedException("Vanilla postfix n/z operand semantics are unowned.");
                default:
                    if (reference is not null) throw new InvalidDataException("Compiled reference prefix has no variable or command.");
                    var operation = new[] { "&&", "||", "<=", ">=", "==", "!=", "+", "-", "*", "/", "%", "<", ">", "~" }
                        .FirstOrDefault(cursor.Match);
                    if (operation is null)
                    {
                        if (!(cursor.Peek is >= (byte)'0' and <= (byte)'9' || cursor.Peek == '.'))
                            throw new NotSupportedException("Compiled postfix operand/operator is unowned.");
                        stack.Push(cursor.AsciiNumber()); break;
                    }
                    if (stack.Count < (operation == "~" ? 1 : 2))
                        throw new InvalidDataException("Compiled postfix stack underflow.");
                    var right = stack.Pop();
                    if (right.Kind is not (FalloutScriptValueKind.Number or FalloutScriptValueKind.Form))
                        throw new NotSupportedException("Compiled postfix string/array operators are unowned.");
                    if (operation == "~")
                    {
                        if (right.Kind != FalloutScriptValueKind.Number)
                            throw new NotSupportedException("Compiled unary form conversion is unowned.");
                        stack.Push(-right.Number); break;
                    }
                    var left = stack.Pop();
                    if (left.Kind is not (FalloutScriptValueKind.Number or FalloutScriptValueKind.Form))
                        throw new NotSupportedException("Compiled postfix string/array operators are unowned.");
                    if ((left.Kind == FalloutScriptValueKind.Form || right.Kind == FalloutScriptValueKind.Form) &&
                        (operation is not ("==" or "!=") || left.Kind != right.Kind &&
                            !(left.Kind == FalloutScriptValueKind.Number && left.Number == 0 ||
                            right.Kind == FalloutScriptValueKind.Number && right.Number == 0)))
                        throw new NotSupportedException("Compiled form arithmetic or numeric conversion is unowned.");
                    stack.Push(operation switch
                    {
                        "+" => left.Number + right.Number,
                        "-" => left.Number - right.Number,
                        "*" => left.Number * right.Number,
                        "/" => left.Number / right.Number,
                        "%" => left.Number % right.Number,
                        "<" => left.Number < right.Number ? 1 : 0,
                        ">" => left.Number > right.Number ? 1 : 0,
                        "<=" => left.Number <= right.Number ? 1 : 0,
                        ">=" => left.Number >= right.Number ? 1 : 0,
                        "==" => left.Number == right.Number ? 1 : 0,
                        "!=" => left.Number != right.Number ? 1 : 0,
                        "&&" => left.Number != 0 && right.Number != 0 ? 1 : 0,
                        "||" => left.Number != 0 || right.Number != 0 ? 1 : 0,
                        _ => throw new NotSupportedException("Compiled postfix operator is unowned.")
                    });
                    break;
            }
            if (stack.Count > 256) throw new NotSupportedException("Compiled postfix stack exceeds its finite bound.");
        }
        if (reference is not null || stack.Count != 1)
            throw new InvalidDataException("Compiled postfix expression has no unique final value.");
        return stack.Pop();
    }
}
