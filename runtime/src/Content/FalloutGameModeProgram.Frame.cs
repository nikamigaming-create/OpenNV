namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutGameModeProgram
{
    internal int StatementCount => _lines.Count;
    internal IEnumerable<(int Statement, string Command, IReadOnlyList<string> Arguments)> CommandSites(string operation) =>
        _lines.Select((tokens, index) => (Tokens: tokens, Index: index)).Where(value => !IsExpressionStatement(value.Tokens) &&
            value.Tokens[0].Split('.')[^1].Equals(operation, StringComparison.OrdinalIgnoreCase))
        .Select(value => (value.Index, value.Tokens[0], (IReadOnlyList<string>)value.Tokens.Skip(1).ToArray()));
}
