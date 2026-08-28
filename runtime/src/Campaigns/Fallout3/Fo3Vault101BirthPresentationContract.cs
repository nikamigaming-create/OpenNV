using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Vault101BirthAsset(
    string Id,
    string LogicalPath,
    string SourceSha256,
    string ModelPath,
    string SidecarPath,
    int Surfaces);

internal sealed record Fo3Vault101BirthReference(
    string FormId,
    string BaseFormId,
    string BaseRecordType,
    string BaseEditorId,
    string AssetId,
    Vector3 PositionGameUnits,
    Vector3 PositionGodotGameUnits,
    Vector3 RotationRadians,
    Quaternion RotationGodotQuaternion,
    float Scale);

internal sealed record Fo3Vault101BirthPresentationContract(
    string ManifestPath,
    string ManifestSha256,
    string RecipeId,
    string RecipeSha256,
    string CellFormId,
    string CellEditorId,
    float UnitsToMeters,
    string EntryReferenceFormId,
    Vector3 EntryPositionGameUnits,
    Vector3 EntryRotationRadians,
    Quaternion EntryRotationGodotQuaternion,
    float VerticalFovDegrees,
    Color ProofAmbientColor,
    float ProofAmbientEnergy,
    Color ProofBackgroundColor,
    IReadOnlyDictionary<string, Fo3Vault101BirthAsset> Assets,
    IReadOnlyList<Fo3Vault101BirthReference> References)
{
    internal const string ExpectedSchema = "opennv-fo3-vault101-birth-presentation/v1";
    private const string ExpectedStatus = "prepared-owned-geometry-not-yet-rendered";
    private const string ExpectedCellEditorId = "Vault101d";
    private const string ExpectedLightingAuthority =
        "recipe-proof-only-not-retail-CELL-lighting";
    private const string ExpectedMaterialAuthority =
        "owned-NIF-geometry-and-material-factors-without-textures";
    private const string RequiredUnsupportedActors = "actors and creatures";
    private const string RequiredUnsupportedCommands = "quest and package command execution";
    private const int Sha256HexCharacters = 64;
    private const int FormIdHexCharacters = 8;

    internal static Fo3Vault101BirthPresentationContract Load(
        Fo3BirthSliceContract birthSlice,
        string manifestPath)
    {
        var path = Path.GetFullPath(manifestPath);
        var bytes = File.ReadAllBytes(path);
        var manifestSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "status") != ExpectedStatus)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 birth-presentation identity is unsupported.");

        VerifySourceBinding(birthSlice, RequiredObject(root, "source"));
        var recipe = RequiredObject(root, "recipe");
        var recipeId = RequiredString(recipe, "id");
        var recipeSha256 = RequiredSha256(recipe, "sha256");
        VerifyFile(RequiredString(recipe, "path"), recipeSha256);

        var cell = RequiredObject(root, "cell");
        var cellFormId = RequiredFormId(cell, "formId");
        var cellEditorId = RequiredString(cell, "editorId");
        if (cellFormId != birthSlice.CellFormId ||
            cellEditorId != ExpectedCellEditorId ||
            !RequiredBoolean(cell, "interior"))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 birth-presentation CELL identity differs.");

        var coordinates = RequiredObject(root, "coordinates");
        if (RequiredString(coordinates, "source") !=
                "Gamebryo X-right/Y-forward/Z-up, radians" ||
            RequiredString(coordinates, "target") !=
                "Godot X-right/Y-up/-Z-forward")
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 coordinate contract is unsupported.");
        var unitsToMeters = RequiredPositiveSingle(coordinates, "unitsToMeters");
        var origin = ReadVector3(coordinates, "originGameUnits");

        var entry = RequiredObject(root, "entry");
        var entryReferenceFormId = RequiredFormId(entry, "referenceFormId");
        var entryPosition = ReadVector3(entry, "positionGameUnits");
        var entryLocalPosition = ReadVector3(entry, "positionGodotGameUnits");
        var entryRotation = ReadVector3(entry, "rotationRadians");
        var entryQuaternion = ReadQuaternion(entry, "rotationGodotQuaternion");
        if (RequiredString(entry, "source") != "owned-player-start-marker-transform" ||
            entryReferenceFormId != birthSlice.PlayerSpawnReferenceFormId ||
            !entryPosition.IsEqualApprox(ReadVector3(birthSlice.PlayerSpawnPositionGameUnits)) ||
            !entryRotation.IsEqualApprox(ReadVector3(birthSlice.PlayerSpawnRotationRadians)) ||
            !origin.IsEqualApprox(entryPosition) ||
            !entryLocalPosition.IsZeroApprox())
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 entry differs from the owned player marker.");

        var presentation = RequiredObject(root, "presentation");
        if (RequiredString(presentation, "lightingAuthority") != ExpectedLightingAuthority ||
            RequiredString(presentation, "materialAuthority") != ExpectedMaterialAuthority)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 proof-presentation authority is unsupported.");
        var verticalFovDegrees = RequiredPositiveSingle(presentation, "verticalFovDegrees");
        if (verticalFovDegrees >= 180.0f)
            throw new InvalidOperationException("Fallout 3 Vault 101 proof FOV is invalid.");
        var ambient = ReadColor(presentation, "proofAmbientColor");
        var ambientEnergy = RequiredPositiveSingle(presentation, "proofAmbientEnergy");
        var background = ReadColor(presentation, "proofBackgroundColor");

        var manifestDirectory = Path.GetDirectoryName(path)!;
        var assets = RequiredArray(root, "assets")
            .EnumerateArray()
            .Select(value => ReadAsset(value, manifestDirectory))
            .ToDictionary(value => value.Id, StringComparer.Ordinal);
        var references = RequiredArray(root, "references")
            .EnumerateArray()
            .Select(ReadReference)
            .ToArray();
        if (assets.Count == 0 || references.Length == 0 ||
            references.Select(value => value.FormId).Distinct(StringComparer.Ordinal).Count() !=
                references.Length ||
            references.Any(value => !assets.ContainsKey(value.AssetId)))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation reference closure is incomplete.");

        var coverage = RequiredObject(root, "coverage");
        if (RequiredInteger(coverage, "sourceCellReferences") != birthSlice.ReferenceCount ||
            RequiredInteger(coverage, "renderableAssets") != assets.Count ||
            RequiredInteger(coverage, "renderableReferences") != references.Length ||
            RequiredInteger(coverage, "selectedReferences") != references.Length ||
            RequiredInteger(coverage, "selectedUniqueModels") != assets.Count ||
            RequiredInteger(coverage, "nonPresentationAssets") != 0)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation coverage differs from its rows.");
        VerifyPromotion(RequiredObject(root, "promotion"));
        var unsupported = RequiredArray(root, "unsupported").EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet(StringComparer.Ordinal);
        if (!unsupported.Contains(RequiredUnsupportedActors) ||
            !unsupported.Contains(RequiredUnsupportedCommands))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 unsupported behavior boundary is incomplete.");

        return new Fo3Vault101BirthPresentationContract(
            path,
            manifestSha256,
            recipeId,
            recipeSha256,
            cellFormId,
            cellEditorId,
            unitsToMeters,
            entryReferenceFormId,
            entryPosition,
            entryRotation,
            entryQuaternion,
            verticalFovDegrees,
            ambient,
            ambientEnergy,
            background,
            assets,
            references);
    }

    private static Fo3Vault101BirthAsset ReadAsset(
        JsonElement source,
        string manifestDirectory)
    {
        var modelPath = Path.GetFullPath(RequiredString(source, "model"));
        var sidecarPath = Path.GetFullPath(RequiredString(source, "sidecar"));
        VerifyCacheLocalDerivative(manifestDirectory, modelPath);
        VerifyCacheLocalDerivative(manifestDirectory, sidecarPath);
        return new Fo3Vault101BirthAsset(
            RequiredString(source, "id"),
            RequiredString(source, "logicalPath"),
            RequiredSha256(source, "sourceSha256"),
            modelPath,
            sidecarPath,
            RequiredPositiveInteger(source, "surfaces"));
    }

    private static Fo3Vault101BirthReference ReadReference(JsonElement source)
    {
        if (RequiredBoolean(source, "initiallyDisabled"))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation admitted a disabled reference.");
        var recordType = RequiredString(source, "baseRecordType");
        if (recordType is "NPC_" or "CREA")
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 geometry presentation admitted an actor base.");
        return new Fo3Vault101BirthReference(
            RequiredFormId(source, "formId"),
            RequiredFormId(source, "baseFormId"),
            recordType,
            RequiredString(source, "baseEditorId"),
            RequiredString(source, "assetId"),
            ReadVector3(source, "positionGameUnits"),
            ReadVector3(source, "positionGodotGameUnits"),
            ReadVector3(source, "rotationRadians"),
            ReadQuaternion(source, "rotationGodotQuaternion"),
            RequiredPositiveSingle(source, "scale"));
    }

    private static void VerifySourceBinding(
        Fo3BirthSliceContract birthSlice,
        JsonElement source)
    {
        if (!Path.GetFullPath(RequiredString(source, "birthSlice"))
                .Equals(Path.GetFullPath(birthSlice.Path), StringComparison.OrdinalIgnoreCase) ||
            !RequiredSha256(source, "birthSliceSha256")
                .Equals(birthSlice.Sha256, StringComparison.OrdinalIgnoreCase) ||
            RequiredString(source, "birthSliceRecipeId") != birthSlice.RecipeId)
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 presentation differs from its owned birth slice.");
    }

    private static void VerifyPromotion(JsonElement promotion)
    {
        if (!RequiredBoolean(promotion, "transported") ||
            RequiredBoolean(promotion, "runtimeManifestValidated") ||
            RequiredBoolean(promotion, "runtimeSceneConstructed") ||
            RequiredBoolean(promotion, "rendered") ||
            RequiredBoolean(promotion, "interactive") ||
            RequiredBoolean(promotion, "actorsRendered") ||
            RequiredBoolean(promotion, "questCommandsExecuted") ||
            RequiredBoolean(promotion, "parityReviewed") ||
            RequiredBoolean(promotion, "headsetAccepted"))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 prepared-presentation promotion state is unsupported.");
    }

    private static void VerifyCacheLocalDerivative(string manifestDirectory, string path)
    {
        var relative = Path.GetRelativePath(manifestDirectory, path);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 derivative escapes its local cache: {path}");
    }

    private static void VerifyFile(string path, string expectedSha256)
    {
        using var stream = File.OpenRead(Path.GetFullPath(path));
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Fallout 3 file hash differs: {path}");
    }

    private static Vector3 ReadVector3(JsonElement parent, string name) =>
        ReadVector3(RequiredArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray());

    private static Vector3 ReadVector3(IReadOnlyList<float> values)
    {
        if (values.Count != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout 3 Vault 101 vector is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement parent, string name)
    {
        var values = RequiredArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 quaternion {name} is invalid.");
        var result = new Quaternion(values[0], values[1], values[2], values[3]);
        if (!Mathf.IsEqualApprox(result.LengthSquared(), 1.0f))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 quaternion {name} is not normalized.");
        return result;
    }

    private static Color ReadColor(JsonElement parent, string name)
    {
        var values = RequiredArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3 || values.Any(value => value is < 0.0f or > 1.0f))
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 color {name} is invalid.");
        return new Color(values[0], values[1], values[2]);
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 Vault 101 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 Vault 101 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 Vault 101 field {name} is absent.");
        return value.GetString()!;
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != FormIdHexCharacters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 Vault 101 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Sha256HexCharacters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 Vault 101 SHA-256 {name} is invalid.");
        return value;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) ||
            result < 0)
            throw new InvalidOperationException($"Fallout 3 Vault 101 integer {name} is invalid.");
        return result;
    }

    private static int RequiredPositiveInteger(JsonElement parent, string name)
    {
        var result = RequiredInteger(parent, name);
        if (result <= 0)
            throw new InvalidOperationException(
                $"Fallout 3 Vault 101 positive integer {name} is invalid.");
        return result;
    }

    private static float RequiredPositiveSingle(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetSingle(out var result) ||
            !float.IsFinite(result) || result <= 0.0f)
            throw new InvalidOperationException($"Fallout 3 Vault 101 number {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"Fallout 3 Vault 101 boolean {name} is invalid.");
        return value.GetBoolean();
    }
}
