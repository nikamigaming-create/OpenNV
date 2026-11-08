using System.Globalization;
using System.Security.Cryptography;
using OpenNV.Runtime.Content.Definitions;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutCompiledExecutionEnvironment(
    FalloutPluginStack Records,
    FalloutScriptBindings Bindings,
    Func<FalloutFormKey, double> ReadGlobal,
    Action<FalloutFormKey, float> WriteGlobal,
    Func<FalloutFormKey, uint, double> ReadVariable,
    Action<FalloutFormKey, uint, double> WriteVariable,
    Action<string, IReadOnlyList<string>> Call,
    Func<string, FalloutScriptFunction?> Function,
    FalloutScriptValueContext Values,
    Func<uint, double> ReadOwnerLocal);

internal sealed class FalloutCompiledScriptVm
{
    private readonly IReadOnlyList<FalloutCompiledStatement> _statements;
    private readonly string? _blockName;
    private readonly string? _blockFilter;
    internal int LastStatement { get; private set; } = -1;
    internal int StatementCount => _statements.Count;
    internal string ProgramSha256 { get; }

    internal FalloutCompiledScriptVm(ReadOnlySpan<byte> data, string? blockName = null, string? blockFilter = null)
        : this(FalloutCompiledScript.Decode(data), Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), blockName, blockFilter)
    {
    }

    internal FalloutCompiledScriptVm(IReadOnlyList<FalloutCompiledStatement> statements, string programSha256,
        string? blockName = null, string? blockFilter = null)
    {
        _statements = statements ?? throw new ArgumentNullException(nameof(statements));
        ProgramSha256 = programSha256;
        _blockName = blockName;
        _blockFilter = blockFilter;
    }

    internal static IReadOnlyList<(string Event, string? Filter, FalloutCompiledScriptVm Program)> ReadEvents(
        ReadOnlySpan<byte> data, FalloutScriptBindings bindings, Func<uint, double> readOwnerLocal)
    {
        var statements = FalloutCompiledScript.Decode(data);
        var sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var events = new List<(string Event, string? Filter, FalloutCompiledScriptVm Program)>();
        for (var index = 0; index < statements.Count; index++)
        {
            var statement = statements[index];
            if (statement.Kind != FalloutCompiledStatementKind.Begin) continue;
            var endIndex = index + 1;
            while (endIndex < statements.Count && statements[endIndex].Kind != FalloutCompiledStatementKind.End)
                endIndex++;
            if (endIndex >= statements.Count)
                throw new InvalidDataException($"SCDA BEGIN {statement.BlockName} at {statement.Framing.Offset} has no matching END.");
            string? filter = null;
            if (statement.BlockFilter is { } arg)
                filter = FormatArgument(arg, 0x0010, 0, default!, bindings, readOwnerLocal);
            var slice = statements.Skip(index + 1).Take(endIndex - index - 1).ToArray();
            events.Add((statement.BlockName!, filter, new(slice, sha256, statement.BlockName, filter)));
            index = endIndex;
        }
        return events;
    }

    internal void Execute(FalloutCompiledExecutionEnvironment env, FalloutScriptExecutionBudget? budget = null)
    {
        foreach (var _ in Steps(env, budget)) { }
    }

    internal IEnumerable<bool> Steps(FalloutCompiledExecutionEnvironment env, FalloutScriptExecutionBudget? budget = null)
    {
        budget ??= new();
        using var execution = env.Values.Arrays?.BeginExecution();
        var hasBlocks = _statements.Any(statement => statement.Kind == FalloutCompiledStatementKind.Begin);
        var inActiveBlock = !hasBlocks;
        var ifStack = new Stack<(bool Active, bool ConditionPassed)>();
        var whileStack = new Stack<int>();

        for (var index = 0; index < _statements.Count; index++)
        {
            budget.Spend();
            var statement = _statements[index];
            if (hasBlocks)
            {
                if (statement.Kind == FalloutCompiledStatementKind.Begin)
                {
                    inActiveBlock = MatchesBlock(statement, env);
                    if (!inActiveBlock)
                    {
                        while (index + 1 < _statements.Count && _statements[index + 1].Kind != FalloutCompiledStatementKind.End)
                            index++;
                    }
                    continue;
                }
                if (statement.Kind == FalloutCompiledStatementKind.End)
                {
                    inActiveBlock = false;
                    ifStack.Clear();
                    whileStack.Clear();
                    continue;
                }
                if (!inActiveBlock) continue;
            }

            if (statement.Kind is FalloutCompiledStatementKind.ScriptName or FalloutCompiledStatementKind.LocalDeclaration)
                continue;

            LastStatement = index;
            switch (statement.Kind)
            {
                case FalloutCompiledStatementKind.Return:
                    yield break;

                case FalloutCompiledStatementKind.If:
                    {
                        var condition = EvaluateExpression(statement.Expression!, statement.Framing.Offset, env).Value != 0;
                        if (condition)
                        {
                            ifStack.Push((true, true));
                        }
                        else
                        {
                            ifStack.Push((false, false));
                            index = SkipCompiledInstructions(index, statement.SkipCount);
                        }
                        break;
                    }

                case FalloutCompiledStatementKind.ElseIf:
                    {
                        if (ifStack.Count == 0)
                            throw new InvalidDataException($"SCDA ELSEIF at {statement.Framing.Offset} has no enclosing IF.");
                        var top = ifStack.Pop();
                        if (top.ConditionPassed)
                        {
                            ifStack.Push((false, true));
                            index = SkipCompiledInstructions(index, statement.SkipCount);
                        }
                        else
                        {
                            var condition = EvaluateExpression(statement.Expression!, statement.Framing.Offset, env).Value != 0;
                            if (condition)
                            {
                                ifStack.Push((true, true));
                            }
                            else
                            {
                                ifStack.Push((false, false));
                                index = SkipCompiledInstructions(index, statement.SkipCount);
                            }
                        }
                        break;
                    }

                case FalloutCompiledStatementKind.Else:
                    {
                        if (ifStack.Count == 0)
                            throw new InvalidDataException($"SCDA ELSE at {statement.Framing.Offset} has no enclosing IF.");
                        var top = ifStack.Pop();
                        if (top.ConditionPassed)
                        {
                            ifStack.Push((false, true));
                            index = SkipCompiledInstructions(index, statement.SkipCount);
                        }
                        else
                        {
                            ifStack.Push((true, true));
                        }
                        break;
                    }

                case FalloutCompiledStatementKind.EndIf:
                    {
                        if (ifStack.Count > 0) ifStack.Pop();
                        break;
                    }

                case FalloutCompiledStatementKind.While:
                    {
                        var condition = EvaluateExpression(statement.Expression!, statement.Framing.Offset, env).Value != 0;
                        if (condition)
                        {
                            whileStack.Push(index);
                        }
                        else
                        {
                            var depth = 1;
                            while (depth > 0 && index + 1 < _statements.Count)
                            {
                                index++;
                                if (_statements[index].Kind == FalloutCompiledStatementKind.While) depth++;
                                else if (_statements[index].Kind == FalloutCompiledStatementKind.EndWhile) depth--;
                            }
                        }
                        break;
                    }

                case FalloutCompiledStatementKind.EndWhile:
                    {
                        if (whileStack.Count == 0)
                            throw new InvalidDataException($"SCDA ENDWHILE at {statement.Framing.Offset} has no enclosing WHILE.");
                        index = whileStack.Pop() - 1;
                        break;
                    }

                case FalloutCompiledStatementKind.Set:
                    {
                        var target = statement.SetTarget!.Value;
                        var value = EvaluateExpression(statement.Expression!, statement.Framing.Offset, env).Value;
                        AssignVariable(target, statement.Framing.ReferenceIndex, value, env);
                        break;
                    }

                case FalloutCompiledStatementKind.Command:
                    {
                        ExecuteCommand(statement.Command!, env);
                        break;
                    }

                default:
                    throw new InvalidDataException($"SCDA statement kind {statement.Kind} at {statement.Framing.Offset} is unsupported.");
            }
            yield return true;
        }
    }

    private int SkipCompiledInstructions(int currentIndex, ushort skipCount)
    {
        var remaining = (int)skipCount;
        var cursor = currentIndex;
        while (remaining > 0 && cursor + 1 < _statements.Count)
        {
            cursor++;
            if (_statements[cursor].Kind != FalloutCompiledStatementKind.LocalDeclaration)
                remaining--;
        }
        return cursor;
    }

    private bool MatchesBlock(FalloutCompiledStatement statement, FalloutCompiledExecutionEnvironment env)
    {
        if (_blockName is null) return true;
        if (!string.Equals(statement.BlockName, _blockName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_blockFilter is null) return statement.BlockFilter is null;
        if (statement.BlockFilter is not { } filterArg) return false;
        var filterText = FormatArgument(filterArg, 0x0010, 0, env, env.Bindings, env.ReadOwnerLocal);
        return string.Equals(filterText, _blockFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssignVariable(FalloutCompiledVariableOperand target, ushort? instructionRefIndex,
        double value, FalloutCompiledExecutionEnvironment env)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException("Compiled script assignment produced a non-finite value.");
        if (target.StorageKind == (byte)'G')
        {
            var globalKey = env.Bindings.ReferenceFormKey(target.VariableIndex, env.ReadOwnerLocal);
            var stored = (float)value;
            if (!float.IsFinite(stored))
                throw new InvalidDataException("Script global exceeds Float32 storage.");
            env.WriteGlobal(globalKey, stored);
            return;
        }
        var refIndex = target.ReferenceIndex ?? instructionRefIndex;
        var slot = env.Bindings.VariableSlot(refIndex, target.VariableIndex, target.StorageKind, env.ReadOwnerLocal);
        var storedValue = slot.IsInteger ? (int)Math.Truncate(value) : value;
        env.WriteVariable(slot.Owner, slot.Index, storedValue);
    }

    private static (double Value, bool IsInteger) EvaluateExpression(
        IReadOnlyList<FalloutCompiledExpressionToken> tokens, int offset, FalloutCompiledExecutionEnvironment env)
    {
        FalloutCompiledScript.ValidatePostfixStack(tokens, offset);
        var stack = new Stack<(double Value, bool IsInteger)>(tokens.Count);
        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case FalloutCompiledExpressionTokenKind.NumberLiteral:
                    stack.Push((token.Number, token.IsIntegerLiteral));
                    break;

                case FalloutCompiledExpressionTokenKind.Global:
                    {
                        var globalKey = env.Bindings.ReferenceFormKey(token.ReferenceIndex, env.ReadOwnerLocal);
                        stack.Push((env.ReadGlobal(globalKey), false));
                        break;
                    }

                case FalloutCompiledExpressionTokenKind.ReferencedForm:
                    {
                        var formKey = env.Bindings.ReferenceFormKey(token.ReferenceIndex, env.ReadOwnerLocal);
                        stack.Push((env.Records.RuntimeFormId(formKey), false));
                        break;
                    }

                case FalloutCompiledExpressionTokenKind.LocalVariable:
                    {
                        var slot = env.Bindings.VariableSlot(null, token.VariableIndex, token.VariableStorageKind, env.ReadOwnerLocal);
                        stack.Push((env.ReadVariable(slot.Owner, slot.Index), false));
                        break;
                    }

                case FalloutCompiledExpressionTokenKind.ForeignVariable:
                    {
                        var slot = env.Bindings.VariableSlot(token.ReferenceIndex, token.VariableIndex, token.VariableStorageKind, env.ReadOwnerLocal);
                        stack.Push((env.ReadVariable(slot.Owner, slot.Index), false));
                        break;
                    }

                case FalloutCompiledExpressionTokenKind.FunctionCall:
                    {
                        var result = EvaluateFunctionCall(token.Call!, env);
                        stack.Push((result, false));
                        break;
                    }

                case FalloutCompiledExpressionTokenKind.Operator:
                    {
                        ApplyOperator(token.Operator!, stack, offset);
                        break;
                    }

                default:
                    throw new InvalidDataException($"SCDA expression token kind {token.Kind} at {offset} is unsupported.");
            }
        }
        if (stack.Count != 1)
            throw new InvalidDataException($"SCDA postfix expression at {offset} leaves stack depth {stack.Count} instead of 1.");
        return stack.Pop();
    }

    private static void ApplyOperator(string op, Stack<(double Value, bool IsInteger)> stack, int offset)
    {
        if (op == "~")
        {
            var top = stack.Pop();
            if (top.IsInteger)
            {
                var negated = unchecked(-(int)Math.Truncate(top.Value));
                stack.Push((negated, true));
            }
            else
            {
                stack.Push((-top.Value, false));
            }
            return;
        }

        var rhs = stack.Pop();
        var lhs = stack.Pop();
        var bothInt = lhs.IsInteger && rhs.IsInteger;
        switch (op)
        {
            case "&&":
                {
                    var l = lhs.IsInteger ? (int)Math.Truncate(lhs.Value) != 0 : lhs.Value != 0;
                    var r = rhs.IsInteger ? (int)Math.Truncate(rhs.Value) != 0 : rhs.Value != 0;
                    stack.Push((l && r ? 1 : 0, true));
                    break;
                }
            case "||":
                {
                    var l = lhs.IsInteger ? (int)Math.Truncate(lhs.Value) != 0 : lhs.Value != 0;
                    var r = rhs.IsInteger ? (int)Math.Truncate(rhs.Value) != 0 : rhs.Value != 0;
                    stack.Push((l || r ? 1 : 0, true));
                    break;
                }
            case "==":
                stack.Push(((bothInt ? (int)Math.Truncate(lhs.Value) == (int)Math.Truncate(rhs.Value) : lhs.Value == rhs.Value) ? 1 : 0, true));
                break;
            case "!=":
                stack.Push(((bothInt ? (int)Math.Truncate(lhs.Value) != (int)Math.Truncate(rhs.Value) : lhs.Value != rhs.Value) ? 1 : 0, true));
                break;
            case "<":
                stack.Push(((bothInt ? (int)Math.Truncate(lhs.Value) < (int)Math.Truncate(rhs.Value) : lhs.Value < rhs.Value) ? 1 : 0, true));
                break;
            case "<=":
                stack.Push(((bothInt ? (int)Math.Truncate(lhs.Value) <= (int)Math.Truncate(rhs.Value) : lhs.Value <= rhs.Value) ? 1 : 0, true));
                break;
            case ">":
                stack.Push(((bothInt ? (int)Math.Truncate(lhs.Value) > (int)Math.Truncate(rhs.Value) : lhs.Value > rhs.Value) ? 1 : 0, true));
                break;
            case ">=":
                stack.Push(((bothInt ? (int)Math.Truncate(lhs.Value) >= (int)Math.Truncate(rhs.Value) : lhs.Value >= rhs.Value) ? 1 : 0, true));
                break;
            case "+":
                if (bothInt) stack.Push((unchecked((int)Math.Truncate(lhs.Value) + (int)Math.Truncate(rhs.Value)), true));
                else stack.Push((lhs.Value + rhs.Value, false));
                break;
            case "-":
                if (bothInt) stack.Push((unchecked((int)Math.Truncate(lhs.Value) - (int)Math.Truncate(rhs.Value)), true));
                else stack.Push((lhs.Value - rhs.Value, false));
                break;
            case "*":
                if (bothInt) stack.Push((unchecked((int)Math.Truncate(lhs.Value) * (int)Math.Truncate(rhs.Value)), true));
                else stack.Push((lhs.Value * rhs.Value, false));
                break;
            case "/":
                if (rhs.Value == 0)
                    throw new InvalidDataException($"SCDA expression at {offset} divides by zero.");
                if (bothInt) stack.Push(((int)Math.Truncate(lhs.Value) / (int)Math.Truncate(rhs.Value), true));
                else stack.Push((lhs.Value / rhs.Value, false));
                break;
            case "%":
                {
                    var rInt = (int)Math.Truncate(rhs.Value);
                    if (rInt == 0)
                        throw new InvalidDataException($"SCDA expression at {offset} takes modulo by zero.");
                    var lInt = (int)Math.Truncate(lhs.Value);
                    stack.Push((lInt % rInt, true));
                    break;
                }
            default:
                throw new InvalidDataException($"SCDA operator '{op}' at {offset} is unsupported.");
        }
    }

    private static double EvaluateFunctionCall(FalloutCompiledFunctionCall call, FalloutCompiledExecutionEnvironment env)
    {
        var commandName = FormatCommandName(call, env);
        var args = FormatArguments(call, env);
        return FalloutNvseNumericExpression.EvaluateValue([commandName, .. args], env.Values, env.Function).Number;
    }

    private static void ExecuteCommand(FalloutCompiledFunctionCall call, FalloutCompiledExecutionEnvironment env)
    {
        var commandName = FormatCommandName(call, env);
        var args = FormatArguments(call, env);
        env.Call(commandName, args);
    }

    private static string FormatCommandName(FalloutCompiledFunctionCall call, FalloutCompiledExecutionEnvironment env)
    {
        if (call.ReferenceIndex is not { } refIdx) return call.Name;
        var prefix = env.Bindings.ReferenceToken(refIdx, env.ReadOwnerLocal);
        return $"{prefix}.{call.Name}";
    }

    private static IReadOnlyList<string> FormatArguments(FalloutCompiledFunctionCall call, FalloutCompiledExecutionEnvironment env)
    {
        if (call.Arguments.Count == 0) return [];
        var result = new string[call.Arguments.Count];
        for (var index = 0; index < call.Arguments.Count; index++)
            result[index] = FormatArgument(call.Arguments[index], call.Opcode, index, env, env.Bindings, env.ReadOwnerLocal);
        return result;
    }

    private static string FormatArgument(FalloutCompiledArgument arg, ushort opcode, int argIndex,
        FalloutCompiledExecutionEnvironment env, FalloutScriptBindings bindings, Func<uint, double> readOwnerLocal)
    {
        switch (arg.Kind)
        {
            case FalloutCompiledArgumentKind.IntLiteral:
                return arg.IntValue.ToString(CultureInfo.InvariantCulture);

            case FalloutCompiledArgumentKind.DoubleLiteral:
                return arg.DoubleValue.ToString("R", CultureInfo.InvariantCulture);

            case FalloutCompiledArgumentKind.StringLiteral:
                return $"\"{(arg.TextValue ?? string.Empty).Replace("\"", "\\\"")}\"";

            case FalloutCompiledArgumentKind.Reference:
                return bindings.ReferenceToken(arg.ReferenceIndex, readOwnerLocal);

            case FalloutCompiledArgumentKind.Global:
                {
                    var globalKey = bindings.ReferenceFormKey(arg.ReferenceIndex, readOwnerLocal);
                    return env.ReadGlobal(globalKey).ToString("R", CultureInfo.InvariantCulture);
                }

            case FalloutCompiledArgumentKind.LocalVariable:
                {
                    var slot = bindings.VariableSlot(null, arg.VariableIndex, arg.VariableStorageKind, readOwnerLocal);
                    return env.ReadVariable(slot.Owner, slot.Index).ToString("R", CultureInfo.InvariantCulture);
                }

            case FalloutCompiledArgumentKind.ForeignVariable:
                {
                    var slot = bindings.VariableSlot(arg.ReferenceIndex, arg.VariableIndex, arg.VariableStorageKind, readOwnerLocal);
                    return env.ReadVariable(slot.Owner, slot.Index).ToString("R", CultureInfo.InvariantCulture);
                }

            case FalloutCompiledArgumentKind.VariableSlot:
                {
                    var slot = bindings.VariableSlot(null, arg.VariableIndex, arg.VariableStorageKind, readOwnerLocal);
                    return slot.Index.ToString(CultureInfo.InvariantCulture);
                }

            case FalloutCompiledArgumentKind.Axis:
                return arg.TextValue ?? ((char)arg.IntValue).ToString();

            case FalloutCompiledArgumentKind.AnimationGroup:
                return arg.TextValue ?? throw new InvalidDataException($"Animation group {arg.IntValue} has no name.");

            case FalloutCompiledArgumentKind.UInt16Enum:
                return FormatEnumArgument(opcode, argIndex, arg.IntValue);

            default:
                throw new InvalidDataException($"Compiled script argument kind {arg.Kind} is unsupported.");
        }
    }

    private static string FormatEnumArgument(ushort opcode, int argIndex, int value)
    {
        if (argIndex == 0 && opcode is 0x100E or 0x100F or 0x1011 or 0x1103 or 0x1104 or 0x1105 or 0x1115 or 0x115D or 0x1206 or 0x1207 or 0x1219)
            return FalloutActorValueNames.Name((FalloutActorValueId)value);
        if (argIndex == 0 && opcode is 0x1046 or 0x1083)
            return value == 0 ? "Male" : "Female";
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
