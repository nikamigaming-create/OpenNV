using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

// A source-script owner, independent of menus, locations and quest identities.
// Unsupported expressions/commands stop the caller before its staged effects commit.
internal sealed class FalloutGameModeProgram
{
    private readonly IReadOnlyList<string> _lines;
    private FalloutGameModeProgram(IReadOnlyList<string> lines) => _lines = lines;

    internal static FalloutGameModeProgram Read(ReadOnlySpan<byte> source)
    {
        if (source.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new NotSupportedException("Script source encoding is not ASCII.");
        var text = Encoding.ASCII.GetString(source).TrimEnd('\0');
        if (text.Contains('\0')) throw new InvalidDataException("Script source contains an embedded null.");
        return Read(text);
    }

    internal static FalloutGameModeProgram Read(string source)
    {
        var lines = new List<string>();
        var block = false;
        var gameMode = false;
        var foundGameMode = false;
        var depth = 0;
        foreach (var raw in source.Split('\n'))
        {
            var line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            var tokens = Tokens(line);
            var command = tokens[0].ToLowerInvariant();
            if (command == "begin")
            {
                if (block || tokens.Length < 2) throw new InvalidDataException("Invalid script block start.");
                block = true;
                gameMode = tokens[1].Equals("GameMode", StringComparison.OrdinalIgnoreCase);
                if (gameMode && tokens.Length != 2) throw new NotSupportedException("GameMode block arguments are unbound.");
                if (gameMode && foundGameMode) throw new NotSupportedException("Multiple GameMode blocks need independent scheduling.");
                foundGameMode |= gameMode;
                continue;
            }
            if (command == "end")
            {
                if (!block || tokens.Length != 1 || depth != 0) throw new InvalidDataException("Invalid script block end.");
                block = gameMode = false;
                continue;
            }
            if (!gameMode) continue;
            if (command == "if") ++depth;
            if (command == "endif" && --depth < 0) throw new InvalidDataException("Unmatched script endif.");
            if (command is "else" or "elseif" && depth == 0) throw new InvalidDataException("Unmatched script branch.");
            lines.Add(line);
        }
        if (block || depth != 0) throw new InvalidDataException("Unterminated source script block.");
        return new(lines);
    }

    internal void Execute(Func<string, double> variable, Action<string, double> assign,
        Action<string, IReadOnlyList<string>> call)
    {
        var branches = new Stack<(bool Parent, bool Taken, bool Else)>();
        var active = true;
        foreach (var line in _lines)
        {
            var tokens = Tokens(line);
            switch (tokens[0].ToLowerInvariant())
            {
                case "if":
                    var result = active && Evaluate(tokens[1..], variable) != 0;
                    branches.Push((active, result, false));
                    active = result;
                    break;
                case "elseif":
                    var prior = branches.Pop();
                    if (prior.Else) throw new InvalidDataException("Elseif follows else.");
                    active = prior.Parent && !prior.Taken && Evaluate(tokens[1..], variable) != 0;
                    branches.Push((prior.Parent, prior.Taken || active, false));
                    break;
                case "else":
                    var other = branches.Pop();
                    if (other.Else || tokens.Length != 1) throw new InvalidDataException("Invalid script else.");
                    active = other.Parent && !other.Taken;
                    branches.Push((other.Parent, true, true));
                    break;
                case "endif": active = branches.Pop().Parent; break;
                case "set" when active:
                    if (tokens.Length < 4 || !tokens[2].Equals("to", StringComparison.OrdinalIgnoreCase))
                        throw new NotSupportedException("Script assignment syntax is unbound.");
                    assign(tokens[1], Evaluate(tokens[3..], variable));
                    break;
                case "return" when active: return;
                default:
                    if (active) call(tokens[0], tokens[1..]);
                    break;
            }
        }
    }

    internal static double Evaluate(IReadOnlyList<string> tokens, Func<string, double> variable)
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
                left = execute ? variable(token) : 0;
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
        var matches = Regex.Matches(line, "\"[^\"]*\"|[A-Za-z_][A-Za-z0-9_.]*|[0-9]+(?:\\.[0-9]*)?(?:[eE][+-]?[0-9]+)?|==|!=|>=|<=|&&|\\|\\||[()+*/!<>-]", RegexOptions.CultureInvariant);
        var at = 0;
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(line[at..match.Index])) throw new NotSupportedException("Script contains an unbound token.");
            at = match.Index + match.Length;
        }
        if (!string.IsNullOrWhiteSpace(line[at..])) throw new NotSupportedException("Script contains an unbound token.");
        return matches.Select(match => match.Value).ToArray();
    }

    private static string StripComment(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length; ++index)
        {
            if (line[index] == '"') quoted = !quoted;
            if (line[index] == ';' && !quoted) return line[..index];
        }
        return line;
    }
}
