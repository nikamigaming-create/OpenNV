using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutGameModeProgram
{
    // A retained missing command is a stopped instruction, not permission to
    // replay the invocation. Its unique source site proves which enclosing
    // branches were entered. Their guards and every preceding effect stay
    // consumed. An enclosing loop or mutable/prepared argument needs a saved
    // execution frame and cannot be reconstructed from a legacy error string.
    internal FalloutGameModeProgram? MissingCommandContinuation(string error,
        Func<string, bool> fixedOperand, int? statement = null)
    {
        var failure = Regex.Match(error,
            @"^Reached (?:native script|object-script) command (?<command>\S+) \((?<arguments>\d+) arguments\) has no owner\.$",
            RegexOptions.CultureInvariant);
        if (!failure.Success) return null;
        var command = failure.Groups["command"].Value;
        var arguments = int.Parse(failure.Groups["arguments"].Value, CultureInfo.InvariantCulture);
        var sites = Enumerable.Range(0, _lines.Count).Where(index =>
            _lines[index][0].Equals(command, StringComparison.OrdinalIgnoreCase) &&
            _lines[index].Length == arguments + 1 && !IsExpressionStatement(_lines[index])).ToArray();
        if (statement is { } captured)
        {
            if (!sites.Contains(captured)) return null;
            sites = [captured];
        }
        if (sites.Length != 1) return null;
        var site = sites[0];
        var caller = command.Split('.');
        if (caller.Length > 2 || caller.Length == 2 && !fixedOperand(caller[0]) ||
            _lines[site].Skip(1).Any(token => !fixedOperand(token))) return null;
        var branches = new List<bool>();
        var loopDepth = 0;
        for (var index = 0; index < site; ++index)
            switch (_lines[index][0].ToLowerInvariant())
            {
                case "if": branches.Add(false); break;
                case "else": branches[^1] = true; break;
                case "endif": branches.RemoveAt(branches.Count - 1); break;
                case "while": ++loopDepth; break;
                case "loop": --loopDepth; break;
            }
        if (loopDepth != 0) return null;
        return new(_lines) { _startLine = site, _enteredBranches = branches.ToArray() };
    }

    internal int StartStatement => _startLine;
}
