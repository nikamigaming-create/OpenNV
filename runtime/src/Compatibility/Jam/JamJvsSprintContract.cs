using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Compatibility.Jam;

/// <summary>
/// One source-bound translation of JAM 4.6's JVS forward sprint speed setting.
/// This is intentionally not an xNVSE interpreter or a complete JVS runtime.
/// </summary>
internal sealed record JamJvsSprintContract(
    string ManifestPath,
    string ManifestSha256,
    string ProfileId,
    string SourcePluginSha256,
    string DesktopPhysicalKey,
    float SpeedBonusPercent,
    float SpeedMultiplier,
    int MissingDependencyCount)
{
    internal const string CapabilityId = "jvs-forward-sprint-speed-v1";
    internal const string InputAction = "jam_jvs_sprint";
    private const string ExpectedSchema = "opennv-jam-profile/v1";
    private const string ExpectedCapabilityStatus =
        "transported-bounded-runtime-capability";
    private const int Sha256HexCharacters = 64;
    private const float PercentScale = 100.0f;
    private const float InstalledKeyboardScanCode = 42.0f;
    private const int InstalledControllerButton = 64;

    internal static JamJvsSprintContract Load(string manifestPath)
    {
        var resolved = Path.GetFullPath(manifestPath);
        var bytes = File.ReadAllBytes(resolved);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "kind") != "jam")
            throw new InvalidDataException("The JVS sprint contract is not a JAM profile.");

        var compatibility = root.GetProperty("runtimeCompatibility");
        if (compatibility.GetProperty("nativeDllLoading").GetBoolean())
            throw new InvalidDataException("JVS sprint cannot admit native DLL loading.");
        if (compatibility.GetProperty("ready").GetBoolean())
            throw new InvalidDataException(
                "The bounded JVS sprint transport cannot certify complete JAM compatibility.");

        var capability = root.GetProperty("portableCapabilities")
            .EnumerateArray()
            .SingleOrDefault(value =>
                value.TryGetProperty("id", out var id) &&
                id.GetString() == CapabilityId);
        if (capability.ValueKind == JsonValueKind.Undefined ||
            RequiredString(capability, "status") != ExpectedCapabilityStatus ||
            RequiredString(capability, "module") != "jvs")
            throw new InvalidDataException(
                $"The JAM profile has no transported {CapabilityId} capability.");

        RequireExactStrings(
            capability,
            "supportedSemantics",
            "hold-to-sprint",
            "forward-movement-only",
            "authored-speed-percent");
        var runtime = capability.GetProperty("runtime");
        if (!runtime.GetProperty("enabled").GetBoolean() ||
            runtime.GetProperty("toggle").GetBoolean())
            throw new InvalidDataException(
                "The bounded JVS sprint transport requires the authored enabled hold mode.");
        var physicalKey = RequiredString(runtime, "desktopPhysicalKey");
        if (physicalKey != "Shift" ||
            runtime.GetProperty("controllerButton").GetInt32() != InstalledControllerButton)
            throw new InvalidDataException(
                "The bounded JVS sprint transport only maps the installed keyboard setting.");

        var globals = capability.GetProperty("sourceGlobals");
        RequireExactNumber(globals, "JVSEnabled", 1.0f);
        RequireExactNumber(globals, "JVSKey", InstalledKeyboardScanCode);
        RequireExactNumber(globals, "JVSButton", InstalledControllerButton);
        RequireExactNumber(globals, "JVSToggle", 0.0f);
        var speedBonus = globals.GetProperty("JVSSpeedMult").GetSingle();
        var speedMultiplier = runtime.GetProperty("speedMultiplier").GetSingle();
        if (!float.IsFinite(speedBonus) || speedBonus < 0.0f ||
            !float.IsFinite(speedMultiplier) ||
            !Mathf.IsEqualApprox(speedMultiplier, 1.0f + speedBonus / PercentScale) ||
            !Mathf.IsEqualApprox(
                runtime.GetProperty("speedBonusPercent").GetSingle(),
                speedBonus))
            throw new InvalidDataException("The JVS authored sprint speed is inconsistent.");

        var jamPlugin = root.GetProperty("jamPlugin");
        var pluginSha256 = RequiredSha256(jamPlugin, "sha256");
        var plugin = root.GetProperty("files").GetProperty("effectiveData")
            .EnumerateArray()
            .SingleOrDefault(value =>
                value.TryGetProperty("component", out var component) &&
                component.GetString() == "jam");
        if (plugin.ValueKind == JsonValueKind.Undefined ||
            RequiredSha256(plugin, "sha256") != pluginSha256)
            throw new InvalidDataException("The JAM plugin identity is inconsistent.");
        var source = Path.GetFullPath(RequiredString(plugin, "source"));
        if (!File.Exists(source) || new FileInfo(source).Length != plugin.GetProperty("bytes").GetInt64() ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant() !=
            pluginSha256)
            throw new InvalidDataException(
                "The hash-bound JustAssortedMods.esp source changed; register it again.");

        var missingCount = root.TryGetProperty("missingDependencies", out var missing)
            ? missing.GetArrayLength()
            : 0;
        return new JamJvsSprintContract(
            resolved,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            RequiredString(root, "profileId"),
            pluginSha256,
            physicalKey,
            speedBonus,
            speedMultiplier,
            missingCount);
    }

    internal float MovementSpeed(float baseSpeed, Vector2 movement, bool sprintHeld)
    {
        if (!float.IsFinite(baseSpeed) || baseSpeed < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(baseSpeed));
        return sprintHeld && movement.Y > 0.0f
            ? baseSpeed * SpeedMultiplier
            : baseSpeed;
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"JVS sprint property is empty: {property}")
            : value;
    }

    private static string RequiredSha256(JsonElement source, string property)
    {
        var value = RequiredString(source, property);
        return value.Length == Sha256HexCharacters && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f')
            ? value
            : throw new InvalidDataException($"JVS sprint SHA-256 is invalid: {property}");
    }

    private static void RequireExactNumber(
        JsonElement source,
        string property,
        float expected)
    {
        if (!Mathf.IsEqualApprox(source.GetProperty(property).GetSingle(), expected))
            throw new InvalidDataException(
                $"The installed JVS setting is outside this bounded transport: {property}");
    }

    private static void RequireExactStrings(
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
                $"The bounded JVS semantic declaration changed: {property}");
    }
}
