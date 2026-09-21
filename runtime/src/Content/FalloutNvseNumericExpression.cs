using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

// Numeric NVSE expressions have different assignment/logical semantics from
// vanilla set/if. Values still belong to the caller's real script state owner.
internal static class FalloutNvseNumericExpression
{
    private sealed record Operand(Func<double> Value, string? Target = null);

    internal static bool IsAssignment(string token) => token is "=" or ":=" or "+=" or "-=" or "*=" or "/=";

    internal static double Evaluate(IReadOnlyList<string> tokens, Func<string, double> variable,
        Action<string, double> assign, Func<string, FalloutScriptFunction?>? function = null)
    {
        var at = 0;
        Operand Read(int precedence)
        {
            if (at >= tokens.Count) throw new InvalidDataException("Missing NVSE numeric operand.");
            var token = tokens[at++];
            Operand left;
            if (token == "(")
            {
                left = Read(-1);
                if (at >= tokens.Count || tokens[at++] != ")") throw new InvalidDataException("Unclosed NVSE expression.");
            }
            else if (token is "-" or "+" or "!")
            {
                var operand = Read(token == "!" ? 13 : 12);
                left = new(() =>
                {
                    var value = operand.Value();
                    return token == "-" ? -value : token == "!" ? value == 0 ? 1 : 0 : value;
                });
            }
            else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                Finite(number);
                left = new(() => number);
            }
            else if (!Identifier(token))
                throw new NotSupportedException($"NVSE operand {token} is not a supported numeric value.");
            else if (function?.Invoke(token) is { } command)
            {
                var arguments = new List<Func<FalloutScriptArgument>>();
                foreach (var kind in command.Arguments)
                {
                    if (kind == FalloutScriptArgumentKind.Number)
                    {
                        var argument = Read(14);
                        arguments.Add(() => new(argument.Value()));
                    }
                    else
                    {
                        if (at >= tokens.Count || !Identifier(tokens[at]))
                            throw new InvalidDataException($"Script function {token} needs an identifier argument.");
                        var name = tokens[at++];
                        arguments.Add(() => new(0, name));
                    }
                }
                left = new(() => Finite(command.Invoke(arguments.Select(argument => argument()).ToArray())));
            }
            else left = new(() => Finite(variable(token)), token);

            while (at < tokens.Count && Priority(tokens[at]) is var priority && priority > precedence)
            {
                var op = tokens[at++];
                var before = left;
                // Plain assignment is right-associative; other admitted
                // operators follow the documented NVSE precedence table.
                var right = Read(op is "=" or ":=" ? priority - 1 : priority);
                if (IsAssignment(op))
                {
                    var target = before.Target ?? throw new InvalidDataException("NVSE assignment needs a variable target.");
                    left = new(() =>
                    {
                        var value = op is "=" or ":=" ? right.Value() : Apply(op[..1], before.Value(), right.Value());
                        assign(target, Finite(value));
                        return value;
                    });
                }
                else left = new(() =>
                {
                    var value = before.Value();
                    if (op == "||") return value != 0 ? value : right.Value();
                    if (op == "&&") return value == 0 ? 0 : right.Value();
                    return Apply(op, value, right.Value());
                });
            }
            return left;
        }

        var expression = Read(-1);
        if (at != tokens.Count) throw new NotSupportedException("NVSE numeric expression has an unbound operation.");
        // Parse the complete expression before invoking any stateful command
        // or assignment. Short-circuited branches never read or write state.
        return Finite(expression.Value());
    }

    private static bool Identifier(string token) => Regex.IsMatch(token, @"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant);
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

    private static double Apply(string op, double left, double right) => Finite(op switch
    {
        "+" => left + right,
        "-" => left - right,
        "*" => left * right,
        "/" when right != 0 => left / right,
        "==" => left == right ? 1 : 0,
        "!=" => left != right ? 1 : 0,
        "<" => left < right ? 1 : 0,
        ">" => left > right ? 1 : 0,
        "<=" => left <= right ? 1 : 0,
        ">=" => left >= right ? 1 : 0,
        _ => throw new NotSupportedException($"NVSE numeric operator {op} is invalid or unbound."),
    });

    private static double Finite(double value) => double.IsFinite(value) ? value :
        throw new InvalidDataException("NVSE numeric expression is non-finite.");
}
