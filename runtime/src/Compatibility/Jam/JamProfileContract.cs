using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace OpenNV.Runtime.Compatibility.Jam;

/// <summary>
/// Shared identity and fail-closed boundary for one local JAM profile.
/// Individual semantic transports consume capabilities from this verified source.
/// </summary>
internal sealed record JamProfileContract(
    string ManifestPath,
    string ManifestSha256,
    string ProfileId,
    string SourcePluginSha256,
    int MissingDependencyCount,
    JsonElement Root)
{
    private const string ExpectedSchema = "opennv-jam-profile/v1";
    private const string ExpectedCapabilityStatus =
        "transported-bounded-runtime-capability";
    private const string TrustedRequirementsSchema =
        "opennv-jam-trusted-requirements/v1";
    private const string TrustedRequirementsResource =
        "res://config/jam-trusted-requirements-v1.json";
    private const int Sha256HexCharacters = 64;

    internal static JamProfileContract Load(string manifestPath)
    {
        var resolved = Path.GetFullPath(manifestPath);
        var bytes = File.ReadAllBytes(resolved);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "kind") != "jam")
            throw new InvalidDataException("The selected file is not a JAM profile.");

        var compatibility = root.GetProperty("runtimeCompatibility");
        if (compatibility.GetProperty("nativeDllLoading").GetBoolean() ||
            compatibility.GetProperty("ready").GetBoolean())
            throw new InvalidDataException(
                "A bounded JAM transport cannot admit DLLs or certify complete compatibility.");

        var portableCapabilities = root.GetProperty("portableCapabilities");
        var portableCanonical = RequiredString(root, "portableCapabilitiesCanonical");
        var portableSha256 = RequiredSha256(root, "portableCapabilitiesSha256");
        if (Sha256(Encoding.UTF8.GetBytes(portableCanonical)) != portableSha256)
            throw new InvalidDataException("The JAM portable capability contract hash differs.");
        var canonicalCapabilities = JsonNode.Parse(portableCanonical);
        var emittedCapabilities = JsonNode.Parse(portableCapabilities.GetRawText());
        if (!JsonNode.DeepEquals(canonicalCapabilities, emittedCapabilities))
            throw new InvalidDataException(
                "The JAM portable capability contract and emitted capabilities differ.");

        var requirements = root.GetProperty("requirements");
        var requirementsSha256 = RequiredSha256(requirements, "sha256");
        var trustedRequirements = LoadTrustedRequirements();
        if (RequiredString(requirements, "id") != trustedRequirements.Id ||
            requirementsSha256 != trustedRequirements.Sha256)
            throw new InvalidDataException(
                "The JAM profile was not compiled by the shipped requirements contract.");
        var profileId = RequiredString(root, "profileId");
        if (profileId != ExpectedProfileId(root, requirementsSha256, portableSha256) ||
            RequiredString(root, "saveCompatibilityId") !=
            $"fallout-new-vegas+jam:{profileId}")
            throw new InvalidDataException("The JAM profile identity binding differs.");

        var jamPlugin = root.GetProperty("jamPlugin");
        var pluginSha256 = RequiredSha256(jamPlugin, "sha256");
        if (!trustedRequirements.PluginContracts.TryGetValue(
                pluginSha256,
                out var trustedCapabilitiesSha256) ||
            trustedCapabilitiesSha256 != portableSha256)
            throw new InvalidDataException(
                "The installed JAM plugin has no shipped portable capability contract.");
        var plugin = root.GetProperty("files").GetProperty("effectiveData")
            .EnumerateArray()
            .SingleOrDefault(value =>
                value.TryGetProperty("component", out var component) &&
                component.GetString() == "jam");
        if (plugin.ValueKind == JsonValueKind.Undefined ||
            RequiredSha256(plugin, "sha256") != pluginSha256)
            throw new InvalidDataException("The JAM plugin identity is inconsistent.");
        var source = Path.GetFullPath(RequiredString(plugin, "source"));
        if (!File.Exists(source) ||
            new FileInfo(source).Length != plugin.GetProperty("bytes").GetInt64() ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant() !=
            pluginSha256)
            throw new InvalidDataException(
                "The hash-bound JustAssortedMods.esp source changed; register it again.");

        var missingCount = root.TryGetProperty("missingDependencies", out var missing)
            ? missing.GetArrayLength()
            : 0;
        return new JamProfileContract(
            resolved,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            profileId,
            pluginSha256,
            missingCount,
            root.Clone());
    }

    internal JsonElement Capability(string id, string module)
    {
        var capability = Root.GetProperty("portableCapabilities")
            .EnumerateArray()
            .SingleOrDefault(value =>
                value.TryGetProperty("id", out var capabilityId) &&
                capabilityId.GetString() == id);
        if (capability.ValueKind == JsonValueKind.Undefined ||
            RequiredString(capability, "status") != ExpectedCapabilityStatus ||
            RequiredString(capability, "module") != module)
            throw new InvalidDataException(
                $"The JAM profile has no transported {id} capability.");
        return capability;
    }

    internal static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"JAM property is empty: {property}")
            : value;
    }

    internal static void RequireExactNumber(
        JsonElement source,
        string property,
        float expected)
    {
        if (!Mathf.IsEqualApprox(source.GetProperty(property).GetSingle(), expected))
            throw new InvalidDataException(
                $"The installed JAM setting is outside this bounded transport: {property}");
    }

    internal static void RequireExactStrings(
        JsonElement source,
        string property,
        params string[] expected)
    {
        var values = source.GetProperty(property)
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        if (!values.SequenceEqual(expected))
            throw new InvalidDataException(
                $"The bounded JAM semantic declaration changed: {property}");
    }

    internal static void RequireExactSourceScripts(
        JsonElement capability,
        params string[] expectedEditorIds)
    {
        var module = RequiredString(capability, "module");
        var scripts = capability.GetProperty("sourceScripts").EnumerateArray().ToArray();
        if (!scripts.Select(value => RequiredString(value, "editorId"))
            .SequenceEqual(expectedEditorIds))
            throw new InvalidDataException("The bounded JAM source-script inventory changed.");
        foreach (var script in scripts)
        {
            var formId = RequiredString(script, "formId");
            if (formId.Length != 8 || formId.Any(character => !Uri.IsHexDigit(character)) ||
                script.GetProperty("sourceBytes").GetInt32() < 1 ||
                RequiredSha256(script, "sourceSha256").Length != Sha256HexCharacters ||
                RequiredString(script, "module") != module)
                throw new InvalidDataException("The bounded JAM source-script identity differs.");
        }
    }

    internal static void RequireExactCommandContracts(
        JsonElement capability,
        string[] xnvse,
        string[] jip,
        string[] events)
    {
        var contracts = capability.GetProperty("commandContracts");
        RequireExactStrings(contracts, "xnvse", xnvse);
        RequireExactStrings(contracts, "jip-ln", jip);
        RequireExactStrings(contracts, "dispatchedEvents", events);
    }

    private static string RequiredSha256(JsonElement source, string property)
    {
        var value = RequiredString(source, property);
        return value.Length == Sha256HexCharacters && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : throw new InvalidDataException($"JAM SHA-256 is invalid: {property}");
    }

    private static string ExpectedProfileId(
        JsonElement root,
        string requirementsSha256,
        string portableCapabilitiesSha256)
    {
        var files = root.GetProperty("files");
        var present = files.GetProperty("gameRoot").EnumerateArray()
            .Concat(files.GetProperty("effectiveData").EnumerateArray())
            .Select(row => new[]
            {
                RequiredString(row, "component"),
                RequiredString(row, "logicalPath"),
                RequiredSha256(row, "sha256"),
            })
            .ToArray();
        var identity = JsonSerializer.SerializeToElement(
            new Dictionary<string, object?>
            {
                ["present"] = present,
                ["missing"] = root.GetProperty("missingDependencies").Clone(),
                ["missingMasters"] = root.GetProperty("missingPluginMasters").Clone(),
                ["requirementsSha256"] = requirementsSha256,
                ["portableCapabilitiesSha256"] = portableCapabilitiesSha256,
            });
        var encoded = Encoding.UTF8.GetBytes(ExpectedSchema + "\0" + CanonicalJson(identity));
        return Sha256(encoded)[..20];
    }

    private static string CanonicalJson(JsonElement source)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(writer, source);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement source)
    {
        switch (source.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in source.EnumerateObject()
                    .OrderBy(value => value.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var value in source.EnumerateArray())
                    WriteCanonicalJson(writer, value);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(source.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(source.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("The JAM profile identity contains invalid JSON.");
        }
    }

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static (
        string Id,
        string Sha256,
        IReadOnlyDictionary<string, string> PluginContracts) LoadTrustedRequirements()
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(TrustedRequirementsResource);
        if (bytes.Length == 0)
            throw new InvalidDataException("The trusted JAM requirements identity is missing.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != TrustedRequirementsSchema)
            throw new InvalidDataException("The trusted JAM requirements schema differs.");
        var pluginContracts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var contract in root.GetProperty("supportedPluginContracts").EnumerateArray())
        {
            var pluginSha256 = RequiredSha256(contract, "jamPluginSha256");
            var capabilitiesSha256 = RequiredSha256(
                contract,
                "portableCapabilitiesSha256");
            if (!pluginContracts.TryAdd(pluginSha256, capabilitiesSha256))
                throw new InvalidDataException(
                    "The trusted JAM plugin contract is duplicated.");
        }
        if (pluginContracts.Count == 0)
            throw new InvalidDataException("No trusted JAM plugin contract is shipped.");
        return (
            RequiredString(root, "requirementsId"),
            RequiredSha256(root, "requirementsSha256"),
            pluginContracts);
    }
}
