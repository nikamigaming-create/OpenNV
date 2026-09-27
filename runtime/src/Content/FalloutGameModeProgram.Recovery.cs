namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutGameModeProgram
{
    // Legacy saves have no instruction cursor. Retry only a missing read that
    // necessarily preceded every mutation, without invoking any query here.
    internal bool CanRetryMissingRead(string operand, Func<string, FalloutScriptFunction?> functions)
    {
        if (functions(operand) is null) return false;
        var firstMutation = _lines.Count;
        for (var index = 0; index < _lines.Count; ++index)
            if (_lines[index][0].ToLowerInvariant() is not ("if" or "elseif" or "else" or "endif"))
            { firstMutation = index; break; }
        var occurrences = Enumerable.Range(0, _lines.Count).Where(index =>
            _lines[index].Contains(operand, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (occurrences.Length == 0 || occurrences[0] > firstMutation) return false;
        var first = _lines[occurrences[0]];
        var assignment = first.Length >= 4 && first[0].Equals("set", StringComparison.OrdinalIgnoreCase) &&
            first[2].Equals("to", StringComparison.OrdinalIgnoreCase);
        if (occurrences[0] == firstMutation && !assignment) return false;
        // An unconditional first assignment stops at its first missing operand.
        // Otherwise later occurrences may follow already executed effects.
        var unconditional = occurrences[0] == 0 && assignment &&
            first.Skip(3).SkipWhile(token => token == "(").FirstOrDefault()?.Equals(operand, StringComparison.OrdinalIgnoreCase) == true;
        if (!unconditional && occurrences.Any(index => index > firstMutation)) return false;
        for (var index = 0; index <= occurrences[0]; ++index)
        {
            var tokens = _lines[index];
            var start = index == firstMutation ? 3 : 1;
            foreach (var token in tokens.Skip(start))
            {
                if (token.Equals("eval", StringComparison.OrdinalIgnoreCase) || token.Equals("call", StringComparison.OrdinalIgnoreCase) ||
                    FalloutNvseNumericExpression.IsAssignment(token) || token is "++" or "--") return false;
                if (!token.Equals(operand, StringComparison.OrdinalIgnoreCase) && functions(token) is { ReadOnly: false }) return false;
            }
        }
        return true;
    }
}
