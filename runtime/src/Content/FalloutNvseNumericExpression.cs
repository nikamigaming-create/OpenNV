using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

// NVSE expressions have different assignment/logical semantics from vanilla
// set/if. Typed values still belong to the caller's real script state owner.
internal static class FalloutNvseNumericExpression
{
    private sealed record Operand(Func<FalloutScriptValue> Value, string? Target = null);

    internal static bool IsAssignment(string token) => token is "=" or ":=" or "+=" or "-=" or "*=" or "/=";

    internal static double Evaluate(IReadOnlyList<string> tokens, Func<string, double> variable,
        Action<string, double> assign, Func<string, FalloutScriptFunction?>? function = null,
        Func<string, string, FalloutScriptFunction>? userFunction = null) =>
        EvaluateValue(tokens, new(name => variable(name), (name, value) => assign(name, value.Number)),
            function, userFunction).Number;

    internal static FalloutScriptValue EvaluateValue(IReadOnlyList<string> tokens,
        FalloutScriptValueContext values, Func<string, FalloutScriptFunction?>? function = null,
        Func<string, string, FalloutScriptFunction>? userFunction = null,
        bool nvseLogical = true)
    {
        var at = 0;
        FalloutScriptFunction? Resolve(string token)
        {
            if (!token.Split('.')[^1].Equals("call", StringComparison.OrdinalIgnoreCase))
                return function?.Invoke(token);
            if (at >= tokens.Count || !Identifier(tokens[at]))
                throw new NotSupportedException("Call needs a bound function identity.");
            return (userFunction ?? throw new NotSupportedException("Call has no user-function owner."))(
                token, tokens[at++]);
        }

        Operand Read(int precedence)
        {
            if (at >= tokens.Count) throw new InvalidDataException("Missing NVSE expression operand.");
            var token = tokens[at++];
            Operand left;
            if (token == "(")
            {
                left = Read(-1);
                if (at >= tokens.Count || tokens[at++] != ")")
                    throw new InvalidDataException("Unclosed NVSE expression.");
            }
            else if (token is "-" or "+" or "$" or "!" ||
                token.Equals("ToString", StringComparison.OrdinalIgnoreCase))
            {
                var operand = Read(token == "!" ? 13 :
                    token.Equals("ToString", StringComparison.OrdinalIgnoreCase) ? 14 : 12);
                left = new(() =>
                {
                    var value = operand.Value();
                    return token.ToLowerInvariant() switch
                    {
                        "-" => Finite(-value.Number),
                        "+" => value.Number,
                        "!" => value.Truth ? 0 : 1,
                        _ => value.Stringize(values.FormName),
                    };
                });
            }
            else if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
            {
                var literal = FalloutScriptValue.String(token[1..^1]);
                left = new(() => literal);
            }
            else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                var literal = (FalloutScriptValue)Finite(number);
                left = new(() => literal);
            }
            else if (!Identifier(token))
            {
                throw new NotSupportedException($"NVSE operand {token} is not a supported value.");
            }
            else if (Resolve(token) is { } command)
            {
                var arguments = new List<Func<FalloutScriptArgument>>();
                foreach (var kind in command.Arguments)
                {
                    if (kind == FalloutScriptArgumentKind.Identifier)
                    {
                        if (at >= tokens.Count || !Identifier(tokens[at]))
                            throw new InvalidDataException($"Script function {token} needs an identifier argument.");
                        var name = tokens[at++];
                        arguments.Add(() => new(0, name));
                    }
                    else
                    {
                        var argument = Read(14);
                        arguments.Add(() => kind == FalloutScriptArgumentKind.Number
                            ? new(argument.Value().Number)
                            : new(argument.Value(), null));
                    }
                }
                left = new(() => command.InvokeValue(arguments.Select(argument => argument()).ToArray()));
            }
            else
            {
                left = new(() => values.Read(token), token);
            }

            while (at < tokens.Count && Priority(tokens[at]) is var priority && priority > precedence)
            {
                var op = tokens[at++];
                var before = left;
                // Plain assignment is right-associative; other admitted
                // operators follow the documented NVSE precedence table.
                var right = Read(op is "=" or ":=" ? priority - 1 : priority);
                if (IsAssignment(op))
                {
                    var target = before.Target ?? throw new InvalidDataException(
                        "NVSE assignment needs a variable target.");
                    left = new(() =>
                    {
                        var value = op is "=" or ":="
                            ? right.Value()
                            : Apply(op[..1], before.Value(), right.Value());
                        values.Write(target, value);
                        return value;
                    });
                }
                else
                {
                    left = new(() =>
                    {
                        var value = before.Value();
                        if (op == "||")
                            return nvseLogical
                                ? value.Truth ? value.Logical : right.Value().Logical
                                : value.Truth || right.Value().Truth ? 1 : 0;
                        if (op == "&&")
                            return nvseLogical
                                ? value.Truth ? right.Value().Logical : 0
                                : value.Truth && right.Value().Truth ? 1 : 0;
                        return Apply(op, value, right.Value());
                    });
                }
            }
            return left;
        }

        var expression = Read(-1);
        if (at != tokens.Count)
            throw new NotSupportedException("NVSE expression has an unbound operation.");
        // Parse the complete expression before invoking any stateful command
        // or assignment. Short-circuited branches never read or write state.
        return expression.Value();
    }

    private static bool Identifier(string token) => Regex.IsMatch(token,
        @"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant);

    private static int Priority(string op) => op switch
    {
        "=" or ":=" => 0,
        "||" => 1,
        "&&" or "+=" or "-=" or "*=" or "/=" => 2,
        "==" or "!=" => 4,
        "<" or ">" or "<=" or ">=" => 5,
        "+" or "-" => 9,
        "*" or "/" => 10,
        _ => -1,
    };

    private static FalloutScriptValue Apply(string op, FalloutScriptValue left, FalloutScriptValue right)
    {
        if (op == "+" && left.Kind == FalloutScriptValueKind.String &&
            right.Kind == FalloutScriptValueKind.String)
            return FalloutScriptValue.String(left.Text + right.Text);
        if (op is "==" or "!=" or "<" or ">" or "<=" or ">=")
        {
            var comparison = left.Kind == FalloutScriptValueKind.String &&
                right.Kind == FalloutScriptValueKind.String
                ? StringComparer.OrdinalIgnoreCase.Compare(left.Text, right.Text)
                : left.Kind == FalloutScriptValueKind.String || right.Kind == FalloutScriptValueKind.String
                    ? throw new InvalidDataException("Script comparison mixes strings and non-string values.")
                    : left.Number.CompareTo(right.Number);
            return (op switch
            {
                "==" => comparison == 0,
                "!=" => comparison != 0,
                "<" => comparison < 0,
                ">" => comparison > 0,
                "<=" => comparison <= 0,
                _ => comparison >= 0,
            }) ? 1 : 0;
        }
        var first = left.Number;
        var second = right.Number;
        return op switch
        {
            "+" => Finite(first + second),
            "-" => Finite(first - second),
            "*" => Finite(first * second),
            "/" when second != 0 => Finite(first / second),
            _ => throw new NotSupportedException($"NVSE operator {op} is invalid or unbound."),
        };
    }

    private static double Finite(double value) => double.IsFinite(value) ? value :
        throw new InvalidDataException("NVSE numeric expression is non-finite.");
}
