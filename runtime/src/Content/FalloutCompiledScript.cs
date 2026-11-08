using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace OpenNV.Runtime.Content;

// Structural SCDA records, independent of editor text. Payloads are retained as
// source offsets: framing a command does not implement its operands or effects.
internal readonly record struct FalloutCompiledInstruction(int Offset, int PayloadOffset,
    int PayloadLength, ushort Opcode, ushort? ReferenceIndex)
{
    internal int End => PayloadOffset + PayloadLength;
}

internal enum FalloutCompiledStatementKind : byte
{
    ScriptName = 0,
    Begin = 1,
    End = 2,
    LocalDeclaration = 3,
    Set = 4,
    If = 5,
    Else = 6,
    ElseIf = 7,
    EndIf = 8,
    While = 9,
    EndWhile = 10,
    Return = 11,
    Command = 12,
}

internal enum FalloutCompiledArgumentKind : byte
{
    IntLiteral = 0,
    DoubleLiteral = 1,
    StringLiteral = 2,
    Reference = 3,
    Global = 4,
    LocalVariable = 5,
    ForeignVariable = 6,
    VariableSlot = 7,
    Axis = 8,
    UInt16Enum = 9,
    AnimationGroup = 10,
}

internal readonly record struct FalloutCompiledVariableOperand(
    ushort? ReferenceIndex,
    byte StorageKind,
    ushort VariableIndex)
{
    internal bool IsInteger => StorageKind is (byte)'s' or (byte)'l';
}

internal readonly record struct FalloutCompiledArgument(
    FalloutCompiledArgumentKind Kind,
    int IntValue = 0,
    double DoubleValue = 0,
    string? TextValue = null,
    ushort ReferenceIndex = 0,
    byte VariableStorageKind = 0,
    ushort VariableIndex = 0);

internal enum FalloutCompiledExpressionTokenKind : byte
{
    Operator = 0,
    NumberLiteral = 1,
    Global = 2,
    ReferencedForm = 3,
    LocalVariable = 4,
    ForeignVariable = 5,
    FunctionCall = 6,
}

internal sealed record FalloutCompiledFunctionCall(
    ushort Opcode,
    string Name,
    string ShortName,
    ushort? ReferenceIndex,
    IReadOnlyList<FalloutCompiledArgument> Arguments,
    int Duration = 0);

internal sealed record FalloutCompiledExpressionToken(
    FalloutCompiledExpressionTokenKind Kind,
    string? Operator = null,
    double Number = 0,
    ushort ReferenceIndex = 0,
    byte VariableStorageKind = 0,
    ushort VariableIndex = 0,
    FalloutCompiledFunctionCall? Call = null,
    bool IsIntegerLiteral = false);

internal sealed record FalloutCompiledStatement(
    FalloutCompiledInstruction Framing,
    FalloutCompiledStatementKind Kind,
    ushort BlockId = 0,
    string? BlockName = null,
    int EndOffset = 0,
    FalloutCompiledArgument? BlockFilter = null,
    ushort SkipCount = 0,
    ushort LocalVariableIndex = 0,
    FalloutCompiledVariableOperand? SetTarget = null,
    IReadOnlyList<FalloutCompiledExpressionToken>? Expression = null,
    FalloutCompiledFunctionCall? Command = null);

internal static class FalloutCompiledScript
{
    internal static IReadOnlyList<FalloutCompiledInstruction> Read(ReadOnlySpan<byte> data)
    {
        var instructions = new List<FalloutCompiledInstruction>();
        var cursor = 0;
        while (cursor < data.Length)
        {
            var start = cursor;
            var opcode = Word(data, ref cursor, start);
            ushort? reference = null;
            if (opcode == 0x1c)
            {
                reference = Word(data, ref cursor, start);
                opcode = Word(data, ref cursor, start);
                if (opcode == 0x1c)
                    throw new InvalidDataException($"SCDA instruction at {start} repeats its reference prefix.");
            }
            var length = unchecked((short)Word(data, ref cursor, start));
            if (length < 0 || length > data.Length - cursor)
                throw new InvalidDataException($"SCDA instruction at {start} has an invalid payload extent {length}.");
            instructions.Add(new(start, cursor, length, opcode, reference));
            cursor += length;
        }
        return instructions;
    }

    internal static IReadOnlyList<FalloutCompiledStatement> Decode(ReadOnlySpan<byte> data)
    {
        var instructions = Read(data);
        var statements = new List<FalloutCompiledStatement>(instructions.Count);
        foreach (var instruction in instructions)
            statements.Add(DecodeInstruction(data, instruction));
        return statements;
    }

    internal static FalloutCompiledStatement DecodeInstruction(ReadOnlySpan<byte> data, FalloutCompiledInstruction instruction)
    {
        var payload = data.Slice(instruction.PayloadOffset, instruction.PayloadLength);
        var start = instruction.Offset;
        if (instruction.ReferenceIndex is not null && instruction.Opcode < 0x0100 && instruction.Opcode != 0x0015)
            throw new InvalidDataException($"SCDA flow instruction 0x{instruction.Opcode:X4} at {start} cannot have a reference prefix.");

        switch (instruction.Opcode)
        {
            case 0x001D:
                RequireEmptyPayload(payload, start, "SCRIPTNAME");
                return new(instruction, FalloutCompiledStatementKind.ScriptName);
            case 0x0010:
                return DecodeBegin(instruction, payload);
            case 0x0011:
                RequireEmptyPayload(payload, start, "END");
                return new(instruction, FalloutCompiledStatementKind.End);
            case 0x0012 or 0x0013 or 0x0014 or 0x001F:
                if (payload.Length != 2)
                    throw new InvalidDataException($"SCDA local declaration 0x{instruction.Opcode:X4} at {start} has invalid payload length {payload.Length}.");
                return new(instruction, FalloutCompiledStatementKind.LocalDeclaration,
                    LocalVariableIndex: BinaryPrimitives.ReadUInt16LittleEndian(payload));
            case 0x0015:
                return DecodeSet(instruction, payload);
            case 0x0016 or 0x0018:
                return DecodeIfOrElseIf(instruction, payload,
                    instruction.Opcode == 0x0016 ? FalloutCompiledStatementKind.If : FalloutCompiledStatementKind.ElseIf);
            case 0x0017:
                if (payload.Length != 2)
                    throw new InvalidDataException($"SCDA ELSE at {start} has invalid payload length {payload.Length}.");
                return new(instruction, FalloutCompiledStatementKind.Else,
                    SkipCount: BinaryPrimitives.ReadUInt16LittleEndian(payload));
            case 0x0019:
                RequireEmptyPayload(payload, start, "ENDIF");
                return new(instruction, FalloutCompiledStatementKind.EndIf);
            case 0x001A:
                return DecodeWhile(instruction, payload);
            case 0x001B:
                RequireEmptyPayload(payload, start, "ENDWHILE");
                return new(instruction, FalloutCompiledStatementKind.EndWhile);
            case 0x001E:
                RequireEmptyPayload(payload, start, "RETURN");
                return new(instruction, FalloutCompiledStatementKind.Return);
            default:
                var call = DecodeCommandCall(instruction.Opcode, instruction.ReferenceIndex, payload, start);
                return new(instruction, FalloutCompiledStatementKind.Command, Command: call);
        }
    }

    private static FalloutCompiledStatement DecodeBegin(FalloutCompiledInstruction instruction, ReadOnlySpan<byte> payload)
    {
        var start = instruction.Offset;
        if (payload.Length < 6)
            throw new InvalidDataException($"SCDA BEGIN at {start} has truncated payload length {payload.Length}.");
        var blockId = BinaryPrimitives.ReadUInt16LittleEndian(payload);
        var endOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[2..6]);
        if (!FalloutCompiledScriptCommands.TryGetBlock(blockId, out var block))
            throw new InvalidDataException($"SCDA BEGIN at {start} references unknown block ID {blockId}.");
        if (endOffset < 0)
            throw new InvalidDataException($"SCDA BEGIN at {start} has negative block end offset {endOffset}.");
        var cursor = 6;
        FalloutCompiledArgument? filter = null;
        if (cursor < payload.Length)
        {
            var args = DecodeStandardArguments(payload, ref cursor, block.ParameterSignature, start, block.Name);
            if (args.Count > 1)
                throw new InvalidDataException($"SCDA BEGIN {block.Name} at {start} has {args.Count} filter arguments.");
            if (args.Count == 1) filter = args[0];
        }
        if (cursor != payload.Length)
            throw new InvalidDataException($"SCDA BEGIN {block.Name} at {start} has {payload.Length - cursor} trailing payload bytes.");
        return new(instruction, FalloutCompiledStatementKind.Begin, BlockId: blockId, BlockName: block.Name,
            EndOffset: endOffset, BlockFilter: filter);
    }

    private static FalloutCompiledStatement DecodeSet(FalloutCompiledInstruction instruction, ReadOnlySpan<byte> payload)
    {
        var start = instruction.Offset;
        var cursor = 0;
        var target = ReadVariableTarget(payload, ref cursor, start);
        var exprLen = ReadWord(payload, ref cursor, start, "SET expression length");
        if (exprLen > payload.Length - cursor || cursor + exprLen != payload.Length)
            throw new InvalidDataException($"SCDA SET at {start} has invalid expression extent {exprLen}.");
        var expression = DecodeExpression(payload.Slice(cursor, exprLen), start);
        return new(instruction, FalloutCompiledStatementKind.Set, SetTarget: target, Expression: expression);
    }

    private static FalloutCompiledStatement DecodeIfOrElseIf(FalloutCompiledInstruction instruction,
        ReadOnlySpan<byte> payload, FalloutCompiledStatementKind kind)
    {
        var start = instruction.Offset;
        var cursor = 0;
        var skipCount = ReadWord(payload, ref cursor, start, "IF skip count");
        var exprLen = ReadWord(payload, ref cursor, start, "IF expression length");
        if (exprLen > payload.Length - cursor || cursor + exprLen != payload.Length)
            throw new InvalidDataException($"SCDA {kind} at {start} has invalid expression extent {exprLen}.");
        var expression = DecodeExpression(payload.Slice(cursor, exprLen), start);
        return new(instruction, kind, SkipCount: skipCount, Expression: expression);
    }

    private static FalloutCompiledStatement DecodeWhile(FalloutCompiledInstruction instruction, ReadOnlySpan<byte> payload)
    {
        var start = instruction.Offset;
        if (payload.Length < 6)
            throw new InvalidDataException($"SCDA WHILE at {start} has truncated payload length {payload.Length}.");
        var endOffset = BinaryPrimitives.ReadInt32LittleEndian(payload[..4]);
        var cursor = 4;
        var exprLen = ReadWord(payload, ref cursor, start, "WHILE expression length");
        if (exprLen > payload.Length - cursor || cursor + exprLen != payload.Length)
            throw new InvalidDataException($"SCDA WHILE at {start} has invalid expression extent {exprLen}.");
        var expression = DecodeExpression(payload.Slice(cursor, exprLen), start);
        return new(instruction, FalloutCompiledStatementKind.While, EndOffset: endOffset, Expression: expression);
    }

    private static FalloutCompiledVariableOperand ReadVariableTarget(ReadOnlySpan<byte> payload, ref int cursor, int start)
    {
        if (cursor >= payload.Length)
            throw new InvalidDataException($"SCDA variable target at {start} is empty.");
        var marker = payload[cursor++];
        if (marker == (byte)'G')
        {
            var refIndex = ReadWord(payload, ref cursor, start, "global variable index");
            if (refIndex == 0) throw new InvalidDataException($"SCDA global variable target at {start} has zero index.");
            return new(null, (byte)'G', refIndex);
        }
        ushort? ownerRef = null;
        if (marker == (byte)'r')
        {
            var refIndex = ReadWord(payload, ref cursor, start, "foreign variable reference index");
            if (refIndex == 0) throw new InvalidDataException($"SCDA foreign variable target at {start} has zero reference index.");
            ownerRef = refIndex;
            if (cursor >= payload.Length)
                throw new InvalidDataException($"SCDA foreign variable target at {start} is missing its storage marker.");
            marker = payload[cursor++];
        }
        if (marker is not ((byte)'s' or (byte)'l' or (byte)'f'))
            throw new InvalidDataException($"SCDA variable target at {start} has invalid storage marker 0x{marker:X2}.");
        var varIndex = ReadWord(payload, ref cursor, start, "variable slot index");
        if (varIndex == 0) throw new InvalidDataException($"SCDA variable target at {start} has zero variable index.");
        return new(ownerRef, marker, varIndex);
    }

    internal static IReadOnlyList<FalloutCompiledExpressionToken> DecodeExpression(ReadOnlySpan<byte> expr, int start)
    {
        var tokens = new List<FalloutCompiledExpressionToken>();
        var cursor = 0;
        while (cursor < expr.Length && expr[cursor] == (byte)' ') cursor++;
        while (cursor < expr.Length)
        {
            var marker = expr[cursor];
            if (marker == (byte)'r' || marker == (byte)'X')
            {
                ushort? refIndex = null;
                if (marker == (byte)'r')
                {
                    cursor++;
                    refIndex = ReadWord(expr, ref cursor, start, "expression reference prefix");
                    if (refIndex == 0)
                        throw new InvalidDataException($"SCDA expression at {start} has zero reference prefix.");
                    if (cursor >= expr.Length)
                        throw new InvalidDataException($"SCDA expression at {start} ends after reference prefix.");
                    marker = expr[cursor];
                }
                if (marker == (byte)'X')
                {
                    cursor++;
                    var opcode = ReadWord(expr, ref cursor, start, "embedded function opcode");
                    var callLen = ReadWord(expr, ref cursor, start, "embedded function payload length");
                    if (callLen > expr.Length - cursor)
                        throw new InvalidDataException($"SCDA embedded function 0x{opcode:X4} at {start} exceeds expression extent.");
                    var callPayload = expr.Slice(cursor, callLen);
                    cursor += callLen;
                    var call = DecodeCommandCall(opcode, refIndex, callPayload, start);
                    tokens.Add(new(FalloutCompiledExpressionTokenKind.FunctionCall, Call: call));
                }
                else if (refIndex is not null && marker is (byte)'s' or (byte)'l' or (byte)'f')
                {
                    cursor++;
                    var varIndex = ReadWord(expr, ref cursor, start, "foreign variable index");
                    if (varIndex == 0)
                        throw new InvalidDataException($"SCDA foreign variable at {start} has zero variable index.");
                    tokens.Add(new(FalloutCompiledExpressionTokenKind.ForeignVariable,
                        ReferenceIndex: refIndex.Value, VariableStorageKind: marker, VariableIndex: varIndex));
                }
                else
                {
                    throw new InvalidDataException($"SCDA expression at {start} has invalid token 0x{marker:X2} after reference prefix.");
                }
            }
            else if (marker == (byte)'G')
            {
                cursor++;
                var refIndex = ReadWord(expr, ref cursor, start, "expression global index");
                if (refIndex == 0)
                    throw new InvalidDataException($"SCDA expression global at {start} has zero reference index.");
                tokens.Add(new(FalloutCompiledExpressionTokenKind.Global, ReferenceIndex: refIndex));
            }
            else if (marker == (byte)'Z')
            {
                cursor++;
                var refIndex = ReadWord(expr, ref cursor, start, "expression form index");
                if (refIndex == 0)
                    throw new InvalidDataException($"SCDA expression form at {start} has zero reference index.");
                tokens.Add(new(FalloutCompiledExpressionTokenKind.ReferencedForm, ReferenceIndex: refIndex));
            }
            else if (marker is (byte)'s' or (byte)'l' or (byte)'f')
            {
                cursor++;
                var varIndex = ReadWord(expr, ref cursor, start, "expression local variable index");
                if (varIndex == 0)
                    throw new InvalidDataException($"SCDA expression local at {start} has zero variable index.");
                tokens.Add(new(FalloutCompiledExpressionTokenKind.LocalVariable,
                    VariableStorageKind: marker, VariableIndex: varIndex));
            }
            else
            {
                var tokenStart = cursor;
                while (cursor < expr.Length && expr[cursor] != (byte)' ' && expr[cursor] != 0)
                    cursor++;
                var text = Encoding.ASCII.GetString(expr.Slice(tokenStart, cursor - tokenStart));
                if (IsOperator(text))
                {
                    tokens.Add(new(FalloutCompiledExpressionTokenKind.Operator, Operator: text));
                }
                else if (!text.Contains('.') && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intNumber))
                {
                    tokens.Add(new(FalloutCompiledExpressionTokenKind.NumberLiteral, Number: intNumber, IsIntegerLiteral: true));
                }
                else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                {
                    tokens.Add(new(FalloutCompiledExpressionTokenKind.NumberLiteral, Number: number));
                }
                else
                {
                    throw new InvalidDataException($"SCDA expression at {start} contains invalid token '{text}'.");
                }
            }
            while (cursor < expr.Length && (expr[cursor] == (byte)' ' || expr[cursor] == 0))
                cursor++;
        }
        if (tokens.Count == 0)
            throw new InvalidDataException($"SCDA expression at {start} has no tokens.");
        return tokens;
    }

    private static bool IsOperator(string token) => token is
        "&&" or "||" or "<=" or "<" or ">=" or ">" or "==" or "!=" or "-" or "+" or "*" or "/" or "%" or "~";

    internal static void ValidatePostfixStack(IReadOnlyList<FalloutCompiledExpressionToken> tokens, int start)
    {
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.Kind == FalloutCompiledExpressionTokenKind.Operator)
            {
                var needed = token.Operator == "~" ? 1 : 2;
                if (depth < needed)
                    throw new InvalidDataException($"SCDA postfix expression at {start} underflows operator '{token.Operator}'.");
                depth = depth - needed + 1;
            }
            else
            {
                depth++;
            }
        }
        if (depth != 1)
            throw new InvalidDataException($"SCDA postfix expression at {start} leaves stack depth {depth} instead of 1.");
    }

    private static FalloutCompiledFunctionCall DecodeCommandCall(ushort opcode, ushort? referenceIndex,
        ReadOnlySpan<byte> payload, int start)
    {
        if (!FalloutCompiledScriptCommands.TryGetCommand(opcode, out var metadata))
            throw new InvalidDataException($"SCDA instruction at {start} has unknown command opcode 0x{opcode:X4}.");
        if (referenceIndex == 0)
            throw new InvalidDataException($"SCDA command {metadata.Name} at {start} has zero reference prefix.");
        if (metadata.IsNonDefaultMessage)
            return DecodeNonDefaultMessageCall(metadata, referenceIndex, payload, start);
        var cursor = 0;
        var args = payload.Length == 0
            ? []
            : DecodeStandardArguments(payload, ref cursor, metadata.ParameterSignature, start, metadata.Name);
        if (cursor != payload.Length)
            throw new InvalidDataException($"SCDA command {metadata.Name} at {start} has {payload.Length - cursor} trailing payload bytes.");
        return new(opcode, metadata.Name, metadata.ShortName, referenceIndex, args);
    }

    private static FalloutCompiledFunctionCall DecodeNonDefaultMessageCall(
        FalloutCompiledCommandMetadata metadata, ushort? referenceIndex, ReadOnlySpan<byte> payload, int start)
    {
        var cursor = 0;
        var version = ReadWord(payload, ref cursor, start, $"{metadata.Name} format version");
        if (version != 1)
            throw new InvalidDataException($"SCDA {metadata.Name} at {start} has unsupported message version {version}.");
        var args = new List<FalloutCompiledArgument>();
        if (metadata.IsLiteralMessage)
        {
            var textLen = ReadWord(payload, ref cursor, start, $"{metadata.Name} text length");
            if (textLen > payload.Length - cursor)
                throw new InvalidDataException($"SCDA {metadata.Name} at {start} has truncated message text.");
            var text = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(payload.Slice(cursor, textLen));
            cursor += textLen;
            args.Add(new(FalloutCompiledArgumentKind.StringLiteral, TextValue: text));
        }
        else
        {
            if (cursor >= payload.Length || payload[cursor++] != (byte)'r')
                throw new InvalidDataException($"SCDA {metadata.Name} at {start} is missing its message reference marker.");
            var msgRef = ReadWord(payload, ref cursor, start, $"{metadata.Name} message reference");
            if (msgRef == 0)
                throw new InvalidDataException($"SCDA {metadata.Name} at {start} has zero message reference.");
            args.Add(new(FalloutCompiledArgumentKind.Reference, ReferenceIndex: msgRef));
        }
        var varCount = ReadWord(payload, ref cursor, start, $"{metadata.Name} variable count");
        for (var index = 0; index < varCount; index++)
            args.Add(DecodeNumericOrVariableArgument(payload, ref cursor, start, metadata.Name));
        var duration = 0;
        if (!metadata.IsLiteralMessage)
        {
            if (payload.Length - cursor < 4)
                throw new InvalidDataException($"SCDA {metadata.Name} at {start} is missing its display duration.");
            duration = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(cursor, 4));
            cursor += 4;
        }
        if (cursor != payload.Length)
            throw new InvalidDataException($"SCDA {metadata.Name} at {start} has {payload.Length - cursor} trailing payload bytes.");
        return new(metadata.Opcode, metadata.Name, metadata.ShortName, referenceIndex, args, duration);
    }

    private static IReadOnlyList<FalloutCompiledArgument> DecodeStandardArguments(
        ReadOnlySpan<byte> payload, ref int cursor, string signature, int start, string name)
    {
        var count = ReadWord(payload, ref cursor, start, $"{name} parameter count");
        if (count > signature.Length)
            throw new InvalidDataException($"SCDA {name} at {start} supplies {count} parameters but signature allows {signature.Length}.");
        var args = new List<FalloutCompiledArgument>(count);
        for (var index = 0; index < count; index++)
        {
            var kind = (FalloutCompiledParameterWireKind)signature[index];
            switch (kind)
            {
                case FalloutCompiledParameterWireKind.Reference:
                    if (cursor >= payload.Length || payload[cursor++] != (byte)'r')
                        throw new InvalidDataException($"SCDA {name} parameter {index} at {start} is missing its 'r' marker.");
                    var refIndex = ReadWord(payload, ref cursor, start, $"{name} reference parameter");
                    if (refIndex == 0)
                        throw new InvalidDataException($"SCDA {name} parameter {index} at {start} has zero reference index.");
                    args.Add(new(FalloutCompiledArgumentKind.Reference, ReferenceIndex: refIndex));
                    break;
                case FalloutCompiledParameterWireKind.String:
                    var strLen = ReadWord(payload, ref cursor, start, $"{name} string length");
                    if (strLen > payload.Length - cursor)
                        throw new InvalidDataException($"SCDA {name} string parameter {index} at {start} is truncated.");
                    var str = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(payload.Slice(cursor, strLen));
                    cursor += strLen;
                    args.Add(new(FalloutCompiledArgumentKind.StringLiteral, TextValue: str));
                    break;
                case FalloutCompiledParameterWireKind.Integer:
                case FalloutCompiledParameterWireKind.Float:
                    args.Add(DecodeNumericOrVariableArgument(payload, ref cursor, start, name));
                    break;
                case FalloutCompiledParameterWireKind.Axis:
                    if (cursor >= payload.Length)
                        throw new InvalidDataException($"SCDA {name} axis parameter at {start} is truncated.");
                    var axis = payload[cursor++];
                    if (axis is not ((byte)'X' or (byte)'Y' or (byte)'Z'))
                        throw new InvalidDataException($"SCDA {name} axis parameter at {start} has invalid byte 0x{axis:X2}.");
                    args.Add(new(FalloutCompiledArgumentKind.Axis, IntValue: axis, TextValue: ((char)axis).ToString()));
                    break;
                case FalloutCompiledParameterWireKind.UInt16Enum:
                    var enumVal = ReadWord(payload, ref cursor, start, $"{name} enum parameter");
                    args.Add(new(FalloutCompiledArgumentKind.UInt16Enum, IntValue: enumVal));
                    break;
                case FalloutCompiledParameterWireKind.AnimationGroup:
                    var groupIdx = ReadWord(payload, ref cursor, start, $"{name} animation group");
                    if (!FalloutCompiledScriptCommands.TryGetAnimationGroupName(groupIdx, out var groupName))
                        throw new InvalidDataException($"SCDA {name} at {start} has unknown animation group {groupIdx}.");
                    args.Add(new(FalloutCompiledArgumentKind.AnimationGroup, IntValue: groupIdx, TextValue: groupName));
                    break;
                case FalloutCompiledParameterWireKind.VariableName:
                    if (cursor >= payload.Length)
                        throw new InvalidDataException($"SCDA {name} variable-name parameter at {start} is truncated.");
                    var storage = payload[cursor++];
                    if (storage is not ((byte)'s' or (byte)'l' or (byte)'f'))
                        throw new InvalidDataException($"SCDA {name} variable-name parameter at {start} has invalid storage marker 0x{storage:X2}.");
                    var slotIdx = ReadWord(payload, ref cursor, start, $"{name} variable slot");
                    if (slotIdx == 0)
                        throw new InvalidDataException($"SCDA {name} variable-name parameter at {start} has zero slot index.");
                    args.Add(new(FalloutCompiledArgumentKind.VariableSlot,
                        VariableStorageKind: storage, VariableIndex: slotIdx));
                    break;
                default:
                    throw new InvalidDataException($"SCDA {name} at {start} has unsupported parameter wire kind '{signature[index]}'.");
            }
        }
        return args;
    }

    private static FalloutCompiledArgument DecodeNumericOrVariableArgument(
        ReadOnlySpan<byte> payload, ref int cursor, int start, string name)
    {
        if (cursor >= payload.Length)
            throw new InvalidDataException($"SCDA {name} numeric parameter at {start} is truncated.");
        var marker = payload[cursor++];
        switch (marker)
        {
            case (byte)'n':
                if (payload.Length - cursor < 4)
                    throw new InvalidDataException($"SCDA {name} integer literal at {start} is truncated.");
                var intVal = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(cursor, 4));
                cursor += 4;
                return new(FalloutCompiledArgumentKind.IntLiteral, IntValue: intVal, DoubleValue: intVal);
            case (byte)'z':
                if (payload.Length - cursor < 8)
                    throw new InvalidDataException($"SCDA {name} float literal at {start} is truncated.");
                var dblVal = BinaryPrimitives.ReadDoubleLittleEndian(payload.Slice(cursor, 8));
                cursor += 8;
                if (!double.IsFinite(dblVal))
                    throw new InvalidDataException($"SCDA {name} float literal at {start} is non-finite.");
                return new(FalloutCompiledArgumentKind.DoubleLiteral, DoubleValue: dblVal);
            case (byte)'G':
                var globalIdx = ReadWord(payload, ref cursor, start, $"{name} global argument");
                if (globalIdx == 0)
                    throw new InvalidDataException($"SCDA {name} global argument at {start} has zero index.");
                return new(FalloutCompiledArgumentKind.Global, ReferenceIndex: globalIdx);
            case (byte)'s' or (byte)'l' or (byte)'f':
                var localIdx = ReadWord(payload, ref cursor, start, $"{name} local argument");
                if (localIdx == 0)
                    throw new InvalidDataException($"SCDA {name} local argument at {start} has zero index.");
                return new(FalloutCompiledArgumentKind.LocalVariable,
                    VariableStorageKind: marker, VariableIndex: localIdx);
            case (byte)'r':
                var refIdx = ReadWord(payload, ref cursor, start, $"{name} foreign variable reference");
                if (refIdx == 0)
                    throw new InvalidDataException($"SCDA {name} foreign variable argument at {start} has zero reference index.");
                if (cursor >= payload.Length)
                    throw new InvalidDataException($"SCDA {name} foreign variable argument at {start} is missing its storage marker.");
                var foreignStorage = payload[cursor++];
                if (foreignStorage is not ((byte)'s' or (byte)'l' or (byte)'f'))
                    throw new InvalidDataException($"SCDA {name} foreign variable argument at {start} has invalid storage marker 0x{foreignStorage:X2}.");
                var foreignVarIdx = ReadWord(payload, ref cursor, start, $"{name} foreign variable slot");
                if (foreignVarIdx == 0)
                    throw new InvalidDataException($"SCDA {name} foreign variable argument at {start} has zero variable index.");
                return new(FalloutCompiledArgumentKind.ForeignVariable,
                    ReferenceIndex: refIdx, VariableStorageKind: foreignStorage, VariableIndex: foreignVarIdx);
            default:
                throw new InvalidDataException($"SCDA {name} numeric parameter at {start} has invalid type marker 0x{marker:X2}.");
        }
    }

    private static void RequireEmptyPayload(ReadOnlySpan<byte> payload, int start, string name)
    {
        if (payload.Length != 0)
            throw new InvalidDataException($"SCDA {name} at {start} must have an empty payload, found {payload.Length} bytes.");
    }

    private static ushort ReadWord(ReadOnlySpan<byte> data, ref int cursor, int start, string description)
    {
        if (data.Length - cursor < 2)
            throw new InvalidDataException($"SCDA instruction at {start} has a truncated {description}.");
        var result = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        cursor += 2;
        return result;
    }

    private static ushort Word(ReadOnlySpan<byte> data, ref int cursor, int start)
    {
        if (data.Length - cursor < 2)
            throw new InvalidDataException($"SCDA instruction at {start} has a truncated header.");
        var result = BinaryPrimitives.ReadUInt16LittleEndian(data[cursor..]);
        cursor += 2;
        return result;
    }
}
