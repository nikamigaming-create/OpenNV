using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Capture;

internal static class ActorReviewScene
{
    private const string ActorReviewSchema = "opennv-actor-review-scene/v1";
    private const string HumanoidRecordType = "NPC_";
    private const string CreatureRecordType = "CREA";
    private const string CompiledPendingStatus =
        "compiled-retail-observed-pending-godot-capture";

    internal static Scene Load(string path, RuntimeConfiguration configuration)
    {
        var resolved = Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(resolved));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ActorReviewSchema ||
            root.GetProperty("status").GetString() != CompiledPendingStatus)
            throw new InvalidOperationException($"Unexpected actor review scene: {resolved}");
        var recordType = RequireText(root, "recordType", "record type");
        if (recordType is not (HumanoidRecordType or CreatureRecordType))
            throw new InvalidOperationException(
                $"Actor review scene has unsupported record type: {recordType}");
        configuration.VerifyCompiledConfiguration(root);

        var directory = Path.GetDirectoryName(resolved)!;
        var outputs = root.GetProperty("outputs");
        var model = Verify(
            Path.Combine(directory, RequireText(outputs, "gltf", "actor glTF")),
            RequireText(outputs, "gltfSha256", "actor glTF hash"));
        var sidecar = Verify(
            Path.Combine(directory, RequireText(outputs, "sidecar", "actor sidecar")),
            RequireText(outputs, "sidecarSha256", "actor sidecar hash"));
        var retail = root.GetProperty("retailContract");
        var contract = Verify(
            RequireText(retail, "path", "retail contract"),
            RequireText(retail, "sha256", "retail contract hash"));
        return new Scene(
            resolved,
            FileSha256(resolved),
            RequireText(root, "reviewKey", "review key"),
            recordType,
            model,
            sidecar,
            contract);
    }

    private static string Verify(string path, string expectedSha256)
    {
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved) ||
            !FileSha256(resolved).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Actor review artifact changed: {resolved}");
        return resolved;
    }

    private static string RequireText(JsonElement source, string property, string label)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Actor review scene {label} is empty.");
        return value;
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal readonly record struct Scene(
        string Path,
        string Sha256,
        string ReviewKey,
        string RecordType,
        string ModelPath,
        string SidecarPath,
        string RetailContractPath);
}
