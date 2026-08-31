namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed class ClassicProofOptions
{
    private readonly Dictionary<string, string> _values;
    private readonly string _proofName;

    private ClassicProofOptions(Dictionary<string, string> values, string proofName)
    {
        _values = values;
        _proofName = proofName;
    }

    internal static ClassicProofOptions Parse(string[] arguments, string proofName)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(proofName);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index++)
        {
            var key = arguments[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) || index + 1 >= arguments.Length)
                throw new InvalidOperationException(
                    $"Unexpected {proofName} argument: {key}");

            var optionName = key[2..];
            var optionValue = arguments[++index];
            if (string.IsNullOrWhiteSpace(optionName) ||
                string.IsNullOrWhiteSpace(optionValue) ||
                !values.TryAdd(optionName, optionValue))
                throw new InvalidOperationException(
                    $"Invalid or duplicate {proofName} option: {key}");
        }

        return new ClassicProofOptions(values, proofName);
    }

    internal string Required(string key) =>
        _values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"{_proofName} requires --{key}.");
}
