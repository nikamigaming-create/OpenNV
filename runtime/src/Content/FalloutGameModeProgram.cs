using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal enum FalloutScriptArgumentKind
{
    Number, Identifier, String, Value,
    OptionalNumber, OptionalIdentifier, OptionalString, OptionalValue,
}
internal readonly record struct FalloutScriptArgument(FalloutScriptValue Value, string? Identifier = null)
{
    internal FalloutScriptArgument(double number, string? identifier = null)
        : this((FalloutScriptValue)number, identifier) { }

    internal double Number => Value.Number;
    internal string Text => Value.Text;
}

internal sealed class FalloutScriptFunction
{
    private readonly Func<IReadOnlyList<FalloutScriptArgument>, double>? _invoke;
    private readonly Func<IReadOnlyList<FalloutScriptArgument>, FalloutScriptValue>? _invokeValue;

    internal IReadOnlyList<FalloutScriptArgumentKind> Arguments { get; }
    internal Func<IReadOnlyList<FalloutScriptArgument>, double> Invoke =>
        arguments => InvokeValue(arguments).Number;

    internal FalloutScriptFunction(IReadOnlyList<FalloutScriptArgumentKind> arguments,
        Func<IReadOnlyList<FalloutScriptArgument>, double> invoke)
    {
        Arguments = arguments;
        _invoke = invoke;
    }

    private FalloutScriptFunction(IReadOnlyList<FalloutScriptArgumentKind> arguments,
        Func<IReadOnlyList<FalloutScriptArgument>, FalloutScriptValue> invoke)
    {
        Arguments = arguments;
        _invokeValue = invoke;
    }

    internal static FalloutScriptFunction Typed(IReadOnlyList<FalloutScriptArgumentKind> arguments,
        Func<IReadOnlyList<FalloutScriptArgument>, FalloutScriptValue> invoke) => new(arguments, invoke);

    internal FalloutScriptValue InvokeValue(IReadOnlyList<FalloutScriptArgument> arguments) =>
        _invokeValue is not null ? _invokeValue(arguments) : _invoke!(arguments);
}
internal sealed record FalloutScriptEventProgram(string Event, string? Filter, FalloutGameModeProgram Program,
    IReadOnlyList<string>? Parameters = null);

internal sealed class FalloutScriptExecutionBudget(int maximum = 100_000)
{
    private int _remaining = maximum;
    internal void Spend()
    {
        if (--_remaining < 0) throw new NotSupportedException("Script execution exceeded the runtime instruction budget.");
    }
}

// A source-script owner, independent of menus, locations and quest identities.
// Unsupported expressions/commands stop the caller and retain its executed prefix.
internal sealed class FalloutGameModeProgram
{
    internal const int ParserVersion = 4;
    private readonly IReadOnlyList<string[]> _lines;
    private readonly Dictionary<int, int> _loopEnds = [];
    private FalloutGameModeProgram(IReadOnlyList<string[]> lines)
    {
        _lines = lines;
        var loops = new Stack<int>();
        for (var index = 0; index < lines.Count; ++index)
        {
            if (lines[index][0].Equals("while", StringComparison.OrdinalIgnoreCase)) loops.Push(index);
            if (lines[index][0].Equals("loop", StringComparison.OrdinalIgnoreCase)) _loopEnds.Add(loops.Pop(), index);
        }
    }

    internal IEnumerable<string> CommandNames => _lines
        .Where(tokens => tokens.Length < 2 || !FalloutNvseNumericExpression.IsAssignment(tokens[1]))
        .Select(tokens => tokens[0].ToLowerInvariant())
        .Where(command => command is not ("if" or "elseif" or "else" or "endif" or "while" or "loop" or "break" or "continue" or "set" or "let" or "eval" or "return"))
        .Select(command => command[(command.LastIndexOf('.') + 1)..]);

    internal static FalloutGameModeProgram Read(ReadOnlySpan<byte> source, string blockName = "GameMode", uint? argument = null)
    {
        if (source.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new NotSupportedException("Script source encoding is not ASCII.");
        var text = Encoding.ASCII.GetString(source).TrimEnd('\0');
        if (text.Contains('\0')) throw new InvalidDataException("Script source contains an embedded null.");
        return Read(text, blockName, argument);
    }

    internal static IReadOnlyList<FalloutScriptEventProgram> ReadEvents(string source)
    {
        var events = new List<FalloutScriptEventProgram>();
        List<string[]>? lines = null;
        string? eventName = null, filter = null;
        IReadOnlyList<string>? parameters = null;
        var nesting = new Stack<(string Kind, bool Else)>();
        var lineNumber = 0;
        foreach (var raw in source.Split('\n'))
        {
            ++lineNumber;
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            string[] tokens;
            try { tokens = Tokens(line); }
            catch (NotSupportedException error) { throw new NotSupportedException($"Script line {lineNumber}: {error.Message}", error); }
            if (tokens.Length == 0) throw new InvalidDataException($"Script line {lineNumber} has no statement.");
            var command = tokens[0].ToLowerInvariant();
            if (command == "begin")
            {
                if (lines is not null || tokens.Length < 2) throw new InvalidDataException("Invalid script block start.");
                parameters = null;
                if (tokens[1].Equals("Function", StringComparison.OrdinalIgnoreCase))
                {
                    if (tokens.Length < 4 || tokens[2] != "{" || tokens[^1] != "}")
                        throw new InvalidDataException("Function header needs a parameter list in braces.");
                    parameters = tokens[3..^1];
                    if (parameters.Count > 15 || parameters.Distinct(StringComparer.OrdinalIgnoreCase).Count() != parameters.Count ||
                        parameters.Any(name => !Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)))
                        throw new InvalidDataException("Function parameter list is invalid.");
                }
                else if (tokens.Length > 3) throw new NotSupportedException("Script event header arguments are unbound.");
                lines = [];
                eventName = tokens[1];
                filter = tokens.Length == 3 ? tokens[2] : null;
                continue;
            }
            if (command == "end")
            {
                if (lines is null || tokens.Length != 1 || nesting.Count != 0) throw new InvalidDataException("Invalid script block end.");
                events.Add(new(eventName!, filter, new(lines), parameters));
                lines = null;
                continue;
            }
            if (lines is null) continue;
            if (command is "if" or "while") nesting.Push((command, false));
            if (command is "endif" or "loop")
            {
                if (!nesting.TryPop(out var parent) || parent.Kind != (command == "endif" ? "if" : "while") || tokens.Length != 1)
                    throw new InvalidDataException("Unmatched script branch or loop end.");
            }
            if (command is "else" or "elseif")
            {
                if (!nesting.TryPop(out var parent) || parent.Kind != "if" || parent.Else || command == "else" && tokens.Length != 1)
                    throw new InvalidDataException("Unmatched or repeated script branch.");
                nesting.Push(("if", command == "else"));
            }
            if (command is "break" or "continue" && (tokens.Length != 1 || !nesting.Any(frame => frame.Kind == "while")))
                throw new InvalidDataException("Script loop control has no enclosing loop.");
            lines.Add(tokens);
        }
        if (lines is not null || nesting.Count != 0) throw new InvalidDataException("Unterminated source script block.");
        return events;
    }

    internal static FalloutGameModeProgram Read(string source, string blockName = "GameMode", uint? argument = null)
    {
        var matching = ReadEvents(source).Where(block => block.Event.Equals(blockName, StringComparison.OrdinalIgnoreCase));
        if (argument is { } expected)
            matching = matching.Where(block => uint.TryParse(block.Filter, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value == expected);
        var blocks = matching.ToArray();
        if (argument is null && blocks.Any(block => block.Filter is not null))
            throw new NotSupportedException($"{blockName} block arguments are unbound.");
        if (blocks.Length > 1) throw new NotSupportedException($"Multiple {blockName} blocks need independent scheduling.");
        return blocks.Length == 0 ? new([]) : blocks[0].Program;
    }
    internal void Execute(Func<string, double> variable, Action<string, double> assign,
        Action<string, IReadOnlyList<string>> call, Func<string, FalloutScriptFunction?>? function = null,
        Func<string, string, FalloutScriptFunction>? userFunction = null, FalloutScriptExecutionBudget? budget = null,
        FalloutScriptValueContext? values = null)
    {
        foreach (var _ in Steps(variable, assign, call, function, userFunction, budget, values)) { }
    }

    internal IEnumerable<bool> Steps(Func<string, double> variable, Action<string, double> assign,
        Action<string, IReadOnlyList<string>> call, Func<string, FalloutScriptFunction?>? function = null,
        Func<string, string, FalloutScriptFunction>? userFunction = null, FalloutScriptExecutionBudget? budget = null,
        FalloutScriptValueContext? values = null)
    {
        budget ??= new();
        var typed = values is not null;
        values ??= new(name => variable(name), (name, value) => assign(name, value.Number));
        FalloutScriptValue Expression(IReadOnlyList<string> tokens, bool nvseLogical = true) =>
            FalloutNvseNumericExpression.EvaluateValue(tokens, values, function, userFunction, nvseLogical);
        var branches = new Stack<(bool Parent, bool Taken, bool Else)>();
        var loops = new Stack<(int Start, int End, int Branches)>();
        var active = true;
        bool Condition(string[] tokens) => tokens.Length > 1 && tokens[1].Equals("eval", StringComparison.OrdinalIgnoreCase)
            ? Expression(tokens[2..]).Truth
            : typed ? Expression(tokens[1..], nvseLogical: false).Truth :
                Evaluate(tokens[1..], variable, function) != 0;
        for (var line = 0; line < _lines.Count; ++line)
        {
            budget.Spend();
            var tokens = _lines[line];
            switch (tokens[0].ToLowerInvariant())
            {
                case "if":
                    var result = active && Condition(tokens);
                    branches.Push((active, result, false));
                    active = result;
                    break;
                case "elseif":
                    var prior = branches.Pop();
                    if (prior.Else) throw new InvalidDataException("Elseif follows else.");
                    active = prior.Parent && !prior.Taken && Condition(tokens);
                    branches.Push((prior.Parent, prior.Taken || active, false));
                    break;
                case "else":
                    var other = branches.Pop();
                    if (other.Else || tokens.Length != 1) throw new InvalidDataException("Invalid script else.");
                    active = other.Parent && !other.Taken;
                    branches.Push((other.Parent, true, true));
                    break;
                case "endif": active = branches.Pop().Parent; break;
                case "while":
                    if (!active || !(typed ? Expression(tokens[1..]).Truth :
                        FalloutNvseNumericExpression.Evaluate(tokens[1..], variable, assign, function, userFunction) != 0))
                        line = _loopEnds[line];
                    else loops.Push((line, _loopEnds[line], branches.Count));
                    break;
                case "loop": line = loops.Pop().Start - 1; break;
                case "break" or "continue" when active:
                    var loop = loops.Pop();
                    while (branches.Count > loop.Branches) branches.Pop();
                    line = tokens[0].Equals("break", StringComparison.OrdinalIgnoreCase) ? loop.End : loop.Start - 1;
                    break;
                case "set" when active:
                    if (tokens.Length < 4 || !tokens[2].Equals("to", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Script assignment syntax is unbound.");
                    if (typed) values.Write(tokens[1], Expression(tokens[3..], nvseLogical: false));
                    else assign(tokens[1], Evaluate(tokens[3..], variable, function));
                    break;
                case "let" or "eval" when active:
                    _ = typed ? Expression(tokens[1..]) :
                        FalloutNvseNumericExpression.Evaluate(tokens[1..], variable, assign, function, userFunction);
                    break;
                case "return" when active: yield break;
                default:
                    if (!active) break;
                    if (tokens.Length > 1 && FalloutNvseNumericExpression.IsAssignment(tokens[1]))
                        _ = typed ? Expression(tokens) :
                            FalloutNvseNumericExpression.Evaluate(tokens, variable, assign, function, userFunction);
                    else call(tokens[0], tokens[1..]);
                    break;
            }
            yield return true;
        }
    }

    internal static double Evaluate(IReadOnlyList<string> tokens, Func<string, double> variable,
        Func<string, FalloutScriptFunction?>? function = null)
    {
        var at = 0;
        double Read(int precedence, bool execute)
        {
            if (at >= tokens.Count) throw new InvalidDataException("Missing script expression operand.");
            var token = tokens[at++];
            double left;
            if (token == "(")
            {
                left = Read(0, execute);
                if (at >= tokens.Count || tokens[at++] != ")") throw new InvalidDataException("Unclosed script expression.");
            }
            else if (token is "-" or "+" or "!")
            {
                left = Read(7, execute);
                left = token == "-" ? -left : token == "!" ? left == 0 ? 1 : 0 : left;
            }
            else if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out left))
            {
                if (function?.Invoke(token) is { } command)
                {
                    var arguments = new List<FalloutScriptArgument>();
                    foreach (var kind in command.Arguments)
                    {
                        if (kind == FalloutScriptArgumentKind.Number)
                            arguments.Add(new(Read(7, execute)));
                        else
                        {
                            if (at >= tokens.Count || !Regex.IsMatch(tokens[at], @"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant))
                                throw new InvalidDataException($"Script function {token} needs an identifier argument.");
                            arguments.Add(new(0, tokens[at++]));
                        }
                    }
                    left = execute ? command.Invoke(arguments) : 0;
                }
                else left = execute ? variable(token) : 0;
            }
            if (execute && !double.IsFinite(left)) throw new InvalidDataException("Script operand is non-finite.");
            while (at < tokens.Count && Priority(tokens[at]) is var priority && priority > precedence)
            {
                var op = tokens[at++];
                var right = Read(priority, execute && !(op == "&&" && left == 0 || op == "||" && left != 0));
                if (!execute) continue;
                left = op switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" when right != 0 => left / right,
                    "==" => left == right ? 1 : 0,
                    "!=" => left != right ? 1 : 0,
                    ">" => left > right ? 1 : 0,
                    "<" => left < right ? 1 : 0,
                    ">=" => left >= right ? 1 : 0,
                    "<=" => left <= right ? 1 : 0,
                    "&&" => left != 0 && right != 0 ? 1 : 0,
                    "||" => left != 0 || right != 0 ? 1 : 0,
                    _ => throw new NotSupportedException($"Script operator {op} is unbound or invalid."),
                };
            }
            return left;
        }
        var value = Read(0, true);
        if (at != tokens.Count || !double.IsFinite(value)) throw new NotSupportedException("Script expression is unbound or non-finite.");
        return value;
    }

    private static int Priority(string op) => op switch
    { "||" => 1, "&&" => 2, "==" or "!=" => 3, "<" or ">" or "<=" or ">=" => 4, "+" or "-" => 5, "*" or "/" => 6, _ => 0 };

    internal static string[] Tokens(string line)
    {
        var matches = Regex.Matches(line, "\"[^\"]*\"|(?:[0-9]+(?:\\.[0-9]*)?|\\.[0-9]+)(?:[eE][+-]?[0-9]+)?(?![A-Za-z0-9_.])|[A-Za-z_][A-Za-z0-9_.]*|[0-9]+[A-Za-z_][A-Za-z0-9_.]*|==|!=|>=|<=|&&|\\|\\||:=|[+*/-]=|[{}$=()+*/!<>-]", RegexOptions.CultureInvariant);
        var at = 0;
        foreach (Match match in matches)
        {
            if (!Separators(line.AsSpan(at, match.Index - at))) throw new NotSupportedException("Script contains an unbound token.");
            at = match.Index + match.Length;
        }
        if (!Separators(line.AsSpan(at))) throw new NotSupportedException("Script contains an unbound token.");
        return matches.Select(match => match.Value).ToArray();

        // Source command arguments permit optional commas. Quoted strings are
        // complete tokens above, so their commas remain part of the argument.
        static bool Separators(ReadOnlySpan<char> value)
        {
            foreach (var character in value)
                if (!char.IsWhiteSpace(character) && character != ',') return false;
            return true;
        }
    }

    internal static string StripComment(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length; ++index)
        {
            if (line[index] == '"') quoted = !quoted;
            if (line[index] == ';' && !quoted) return line[..index];
        }
        return line;
    }

    // Source commands have no separate argument AST. A parenthesized
    // expression is nevertheless one argument in the compiler/runtime
    // contract, so evaluate it before handing the command to its owner. The
    // returned spelling remains compatible with existing command owners that
    // accept quoted strings and invariant numeric tokens.
    internal static IReadOnlyList<string> ResolveCommandArguments(
        IReadOnlyList<string> tokens, FalloutScriptValueContext values,
        Func<string, FalloutScriptFunction?>? function = null,
        Func<string, string, FalloutScriptFunction>? userFunction = null)
    {
        var result = new List<string>();
        for (var index = 0; index < tokens.Count;)
        {
            if (tokens[index] == "(")
            {
                var depth = 1;
                var end = index + 1;
                for (; end < tokens.Count && depth != 0; ++end)
                {
                    if (tokens[end] == "(") ++depth;
                    else if (tokens[end] == ")") --depth;
                }
                if (depth != 0) throw new InvalidDataException("Script command argument has an unclosed expression.");
                var value = FalloutNvseNumericExpression.EvaluateValue(
                    tokens.Skip(index + 1).Take(end - index - 2).ToArray(), values, function, userFunction);
                result.Add(CommandValue(value, values.FormName));
                index = end;
                continue;
            }
            if (tokens[index] == "$" && index + 1 < tokens.Count)
            {
                var value = FalloutNvseNumericExpression.EvaluateValue(
                    tokens.Skip(index).Take(2).ToArray(), values, function, userFunction);
                result.Add(CommandValue(value, values.FormName));
                index += 2;
                continue;
            }
            result.Add(tokens[index++]);
        }
        return result;

        static string CommandValue(FalloutScriptValue value, Func<uint, string>? formName) => value.Kind switch
        {
            FalloutScriptValueKind.String => $"\"{value.Text.Replace("\"", "\\\"")}\"",
            FalloutScriptValueKind.Form => value.Stringize(formName),
            _ => value.Number.ToString("R", CultureInfo.InvariantCulture),
        };
    }

    // Current owners missing from an older snapshot are allowed only when that
    // parser rejected their unchanged source. Quotes/comments cannot grant a
    // migration, and current-version saves must contain every admitted owner.
    internal static bool WasRejectedByParser(string source, int version)
    {
        if (version == 0 && HasArgumentSeparator(source)) return true;
        if (version < 4 && source.Split('\n').Select(line => StripComment(line).Trim())
            .Where(line => line.Length != 0).Any(line => Tokens(line).Contains("$"))) return true;
        if (version < 3 && source.Split('\n').Select(line => StripComment(line).Trim())
            .Where(line => line.Length != 0).Any(line => Tokens(line).Any(token => token is "{" or "}"))) return true;
        if (version >= 2) return false;
        return source.Split('\n').Select(line => StripComment(line).Trim())
            .Where(line => line.Length != 0).Any(line => Tokens(line).Any(FalloutNvseNumericExpression.IsAssignment));
    }

    // Version-zero saves omitted every quest whose source contained an
    // argument comma, even in an inactive event. This identifies exactly that
    // legacy parse rejection without treating quoted/comment text as syntax.
    internal static bool HasArgumentSeparator(string source)
    {
        var quoted = false;
        var comment = false;
        foreach (var character in source)
        {
            if (character is '\r' or '\n') { quoted = false; comment = false; continue; }
            if (comment) continue;
            if (character == '"') quoted = !quoted;
            else if (!quoted && character == ';') comment = true;
            else if (!quoted && character == ',') return true;
        }
        return false;
    }
}
