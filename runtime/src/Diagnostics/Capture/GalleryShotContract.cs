using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Capture;

internal static class GalleryShotContract
{
    private const string ExpectedSchema = "opennv-gallery-shot/v5";
    private const string ExpectedStatus = "owned-authored-placement";

    internal static Contract Load(string path, RuntimeConfiguration configuration)
    {
        var resolved = Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(resolved));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ExpectedSchema ||
            root.GetProperty("status").GetString() != ExpectedStatus)
            throw new InvalidOperationException($"Unexpected OpenNV gallery shot: {resolved}");
        var locationClass = RequireText(root, "locationClass");
        if (locationClass is not ("interior" or "exterior"))
            throw new InvalidOperationException(
                $"Gallery shot has invalid location class: {locationClass}");
        var outputFile = RequireText(root, "outputFile");
        if (!outputFile.EndsWith(
                configuration.Capture.Gallery.StillImageExtension,
                StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(outputFile) != outputFile)
            throw new InvalidOperationException(
                $"Gallery shot output must be one configured still-image file name: {outputFile}");
        var enableStateMode = RequireText(root.GetProperty("enableState"), "mode");
        if (enableStateMode is not ("authored" or "proof-enable-initially-disabled"))
            throw new InvalidOperationException(
                $"Gallery shot has invalid enable-state mode: {enableStateMode}");
        var id = RequireText(root, "id");
        var ordinal = root.GetProperty("ordinal").GetInt32();
        var label = RequireText(root, "label");
        var location = RequireText(root, "location");
        var locationId = RequireText(root, "locationId");
        var referenceFormId = RequireText(root, "referenceFormId");
        var baseFormId = RequireText(root, "baseFormId");
        var recordType = RequireText(root, "recordType");
        var actorCellFormId = RequireText(root.GetProperty("actor"), "cellFormId");
        var scene = root.GetProperty("scene");
        var sceneCellFormId = RequireText(scene, "cellFormId");
        var sceneInterior = scene.GetProperty("interior").GetBoolean();
        var sceneWorldspaceElement = scene.GetProperty("worldspaceFormId");
        var sceneWorldspaceFormId = sceneWorldspaceElement.ValueKind == JsonValueKind.Null
            ? null
            : sceneWorldspaceElement.GetString();
        if (sceneInterior != (locationClass == "interior") ||
            (sceneInterior && sceneWorldspaceFormId is not null) ||
            (!sceneInterior && string.IsNullOrWhiteSpace(sceneWorldspaceFormId)))
            throw new InvalidOperationException(
                "Gallery shot rendered-scene CELL/WRLD identity is inconsistent.");
        var sceneIdentity = new SceneIdentity(
            sceneCellFormId,
            sceneWorldspaceFormId,
            sceneInterior);
        var retailEvidence = GalleryRetailEvidence.Load(
            root.GetProperty("retailEvidence"),
            id,
            ordinal,
            label,
            location,
            locationId,
            locationClass,
            referenceFormId,
            baseFormId,
            actorCellFormId,
            sceneIdentity,
            recordType,
            enableStateMode,
            outputFile,
            configuration);
        return new Contract(
            resolved,
            FileSha256(resolved),
            id,
            ordinal,
            label,
            location,
            locationId,
            locationClass,
            referenceFormId,
            baseFormId,
            actorCellFormId,
            sceneIdentity,
            recordType,
            enableStateMode,
            outputFile,
            retailEvidence);
    }

    private static string RequireText(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Gallery shot {property} is empty.");
        return value;
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal sealed record Contract(
        string Path,
        string Sha256,
        string Id,
        int Ordinal,
        string Label,
        string Location,
        string LocationId,
        string LocationClass,
        string ReferenceFormId,
        string BaseFormId,
        string CellFormId,
        SceneIdentity Scene,
        string RecordType,
        string EnableStateMode,
        string OutputFile,
        GalleryRetailEvidence.Contract RetailEvidence);

    internal sealed record SceneIdentity(
        string CellFormId,
        string? WorldspaceFormId,
        bool Interior);
}
