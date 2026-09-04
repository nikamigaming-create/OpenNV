using System.Globalization;
using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal static class ParityRetailIngress
{
    internal const string Schema = "opennv-retail-parity-snapshot/v1";

    internal static ParityTelemetryFrame Parse(string json, ulong sequence)
    {
        if (sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Retail parity snapshot is empty.");

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        RequireObject(root, "Retail parity snapshot");
        RequireProperties(root, "Retail parity snapshot", "schema", "simulationTick",
            "monotonicNanoseconds", "eventOrdinal", "stateKey", "fields");
        if (RequiredString(root, "schema") != Schema)
            throw new InvalidDataException("Retail parity snapshot schema differs.");

        var fieldsElement = root.GetProperty("fields");
        if (fieldsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Retail parity snapshot fields must be an array.");
        var fields = new List<ParityTelemetryField>();
        foreach (var element in fieldsElement.EnumerateArray())
            fields.Add(ParseField(element));

        return new ParityTelemetryFrame(
            ParityEngine.Retail,
            sequence,
            ParseInt64(root, "simulationTick"),
            ParseInt64(root, "monotonicNanoseconds"),
            ParseUInt64(root, "eventOrdinal"),
            RequiredString(root, "stateKey"),
            fields);
    }

    private static ParityTelemetryField ParseField(JsonElement element)
    {
        RequireObject(element, "Retail parity field");
        RequireProperties(element, "Retail parity field", "category", "name", "kind", "value");
        var categoryText = RequiredString(element, "category");
        if (ulong.TryParse(categoryText, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
            !Enum.TryParse<ParityCategory>(categoryText, ignoreCase: false, out var category) ||
            !Enum.IsDefined(category))
            throw new InvalidDataException($"Retail parity category is invalid: {categoryText}");
        var stableId = ParityStableId.FromName(RequiredString(element, "name"));
        var kind = RequiredString(element, "kind");
        var value = RequiredString(element, "value", allowEmpty: true);
        return kind switch
        {
            nameof(ParityValueKind.Bytes) => ParityTelemetryField.Bytes(category, stableId, ParseBase64(value)),
            nameof(ParityValueKind.Int64) => ParityTelemetryField.Int64(
                category, stableId, ParseInt64(value, "Retail parity Int64 field")),
            nameof(ParityValueKind.UInt64) => ParityTelemetryField.UInt64(
                category, stableId, ParseUInt64(value, "Retail parity UInt64 field")),
            nameof(ParityValueKind.Float64) => ParityTelemetryField.Float64(category, stableId, ParseFloat64(value)),
            nameof(ParityValueKind.Float32) => new ParityTelemetryField(
                category, stableId, ParityValueKind.Float32, ParseFloat32Bytes(value)),
            nameof(ParityValueKind.Utf8) => ParityTelemetryField.Utf8(category, stableId, value),
            _ => throw new InvalidDataException($"Retail parity value kind is invalid: {kind}"),
        };
    }

    private static byte[] ParseFloat32Bytes(string value)
    {
        if (value.Length != sizeof(float) * 2 || !value.All(char.IsAsciiHexDigit))
            throw new InvalidDataException(
                "Retail parity Float32 requires eight hexadecimal digits containing the original little-endian bytes.");
        return Convert.FromHexString(value);
    }

    private static byte[] ParseBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Retail parity Bytes field is not canonical base64.", exception);
        }
    }

    private static double ParseFloat64(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
            throw new InvalidDataException("Retail parity Float64 field is invalid.");
        return parsed;
    }

    private static long ParseInt64(JsonElement element, string name) =>
        ParseInt64(RequiredString(element, name), $"Retail parity {name}");

    private static long ParseInt64(string value, string label)
    {
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidDataException($"{label} is invalid.");
        return parsed;
    }

    private static ulong ParseUInt64(JsonElement element, string name) =>
        ParseUInt64(RequiredString(element, name), $"Retail parity {name}");

    private static ulong ParseUInt64(string value, string label)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidDataException($"{label} is invalid.");
        return parsed;
    }

    private static string RequiredString(JsonElement element, string name, bool allowEmpty = false)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Retail parity property {name} must be a string.");
        var value = property.GetString()!;
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Retail parity property {name} is empty.");
        return value;
    }

    private static void RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{label} must be an object.");
    }

    private static void RequireProperties(JsonElement element, string label, params string[] names)
    {
        var expected = names.ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name) || !expected.Contains(property.Name))
                throw new InvalidDataException($"{label} has an unknown or duplicate property: {property.Name}");
        }
        if (!actual.SetEquals(expected))
            throw new InvalidDataException($"{label} is missing required properties.");
    }
}
