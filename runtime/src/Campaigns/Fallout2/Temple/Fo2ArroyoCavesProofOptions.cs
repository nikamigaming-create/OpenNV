namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal static class Fo2ArroyoCavesProofOptions
{
    internal static IReadOnlyDictionary<string, string> Parse(
        IReadOnlyList<string> arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) ||
                argument.Length == 2 || index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Fallout 2 proof option is invalid: {argument}");
            if (!options.TryAdd(argument[2..], arguments[++index]))
                throw new ArgumentException($"Fallout 2 proof option is duplicated: {argument}");
        }
        return options;
    }

    internal static string Require(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{name} is required.");
}
