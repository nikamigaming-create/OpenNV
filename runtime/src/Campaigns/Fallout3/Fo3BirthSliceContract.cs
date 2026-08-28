using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3BirthSliceContract(
    string Path,
    string Sha256,
    string RecipeId,
    string CellFormId,
    string PlayerSpawnReferenceFormId,
    string DoctorActorReferenceFormId,
    int ReferenceCount,
    int ResolvedBaseRecordCount,
    int CellModelResourceCount)
{
    internal const string ExpectedSchema = "opennv-fo3-opening-slice/v1";
    private const string ExpectedStatus = "transported";
    private const string ExpectedRecipeId = "fo3-vault101-cg00-birth-v1";
    private const string ExpectedRecipeSha256 =
        "523d579556260f8405416eaa161ebd1b10310ed14f2b9e8e5e7c3a37b6d0799d";
    private const string ExpectedSceneBlocker = "fo3-vault101-godot-scene-not-compiled";
    private const string ExpectedInterpreterBlocker = "fo3-opening-command-interpreter-not-implemented";
    private const string ExpectedCellFormId = "00028138";
    private const string ExpectedPlayerSpawnReferenceFormId = "00039562";
    private const string ExpectedDoctorActorReferenceFormId = "000290a5";
    private const int ExpectedReferenceCount = 1610;
    private const int ExpectedResolvedBaseRecordCount = 401;
    private const int ExpectedCellModelResourceCount = 299;
    private const int FormIdHexCharacters = 8;
    private const int Sha256HexCharacters = 64;

    internal static Fo3BirthSliceContract Load(JsonElement profileSource, JsonElement install)
    {
        if (RequiredString(profileSource, "schema") != ExpectedSchema)
            throw new InvalidOperationException("Fallout 3 birth-slice profile schema is unsupported.");

        var path = System.IO.Path.GetFullPath(RequiredString(profileSource, "output"));
        var expectedSha256 = RequiredSha256(profileSource, "sha256");
        var bytes = File.ReadAllBytes(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 birth-slice manifest hash differs.");

        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "status") != ExpectedStatus)
            throw new InvalidOperationException("Fallout 3 birth-slice manifest identity is unsupported.");

        var recipe = RequiredObject(root, "recipe");
        var recipeId = RequiredString(recipe, "id");
        if (recipeId != ExpectedRecipeId ||
            RequiredSha256(recipe, "sha256") != ExpectedRecipeSha256)
            throw new InvalidOperationException("Fallout 3 birth-slice recipe identity is unsupported.");

        VerifySourceBindings(RequiredObject(root, "source"), install);
        VerifyPromotionBoundary(RequiredObject(root, "promotion"));
        VerifyBlockers(RequiredArray(root, "blockers"));

        var cellFormId = RequiredFormId(RequiredObject(root, "cell"), "formId");
        var playerSpawnReferenceFormId = RequiredFormId(
            RequiredObject(RequiredObject(root, "startGraph"), "playerSpawn"),
            "formId");
        var doctorActorReferenceFormId = RequiredFormId(
            RequiredObject(RequiredObject(root, "doctorActor"), "reference"),
            "formId");
        if (cellFormId != RequiredFormId(profileSource, "cellFormId") ||
            playerSpawnReferenceFormId != RequiredFormId(profileSource, "playerSpawnReferenceFormId") ||
            doctorActorReferenceFormId != RequiredFormId(profileSource, "doctorActorReferenceFormId") ||
            cellFormId != ExpectedCellFormId ||
            playerSpawnReferenceFormId != ExpectedPlayerSpawnReferenceFormId ||
            doctorActorReferenceFormId != ExpectedDoctorActorReferenceFormId)
            throw new InvalidOperationException("Fallout 3 birth-slice profile identities differ from its manifest.");

        var graph = RequiredObject(root, "cellGraph");
        var references = RequiredArray(graph, "references").EnumerateArray().ToArray();
        var bases = RequiredArray(graph, "bases").GetArrayLength();
        var modelResources = RequiredArray(graph, "modelResources").GetArrayLength();
        var coverage = RequiredObject(root, "coverage");
        if (references.Length != RequiredInteger(coverage, "references") ||
            bases != RequiredInteger(coverage, "resolvedBaseRecords") ||
            modelResources != RequiredInteger(coverage, "cellModelResources") ||
            references.Length != ExpectedReferenceCount ||
            bases != ExpectedResolvedBaseRecordCount ||
            modelResources != ExpectedCellModelResourceCount)
            throw new InvalidOperationException("Fallout 3 birth-slice coverage differs from its graph.");
        var uniqueBaseFormIds = references
            .Select(value => RequiredFormId(value, "baseFormId"))
            .ToHashSet(StringComparer.Ordinal);
        if (uniqueBaseFormIds.Count != RequiredInteger(coverage, "uniqueBaseFormIds"))
            throw new InvalidOperationException("Fallout 3 birth-slice base coverage differs from its graph.");

        return new Fo3BirthSliceContract(
            path,
            actualSha256,
            recipeId,
            cellFormId,
            playerSpawnReferenceFormId,
            doctorActorReferenceFormId,
            references.Length,
            bases,
            modelResources);
    }

    private static void VerifySourceBindings(JsonElement source, JsonElement install)
    {
        VerifySourceBinding(
            RequiredObject(source, "master"),
            RequiredObject(install, "master"));
        var archives = RequiredArray(install, "archives").EnumerateArray().ToArray();
        VerifySourceBinding(
            RequiredObject(source, "meshesArchive"),
            RequiredArchive(archives, "meshes"));
        VerifySourceBinding(
            RequiredObject(source, "texturesArchive"),
            RequiredArchive(archives, "textures"));
    }

    private static JsonElement RequiredArchive(IReadOnlyList<JsonElement> archives, string role)
    {
        var matches = archives
            .Where(value => RequiredString(value, "role") == role)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Fallout 3 profile archive role {role} is ambiguous.");
        return matches[0];
    }

    private static void VerifySourceBinding(JsonElement manifestSource, JsonElement profileSource)
    {
        if (!string.Equals(
                RequiredString(manifestSource, "file"),
                RequiredString(profileSource, "file"),
                StringComparison.OrdinalIgnoreCase) ||
            RequiredLong(manifestSource, "bytes") != RequiredLong(profileSource, "bytes") ||
            !string.Equals(
                RequiredSha256(manifestSource, "sha256"),
                RequiredSha256(profileSource, "sha256"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 birth-slice source binding differs from its profile.");
    }

    private static void VerifyPromotionBoundary(JsonElement promotion)
    {
        if (!RequiredBoolean(promotion, "transported") ||
            RequiredBoolean(promotion, "rendered") ||
            RequiredBoolean(promotion, "interactive") ||
            RequiredBoolean(promotion, "parityReviewed") ||
            RequiredBoolean(promotion, "headsetAccepted"))
            throw new InvalidOperationException("Fallout 3 birth-slice promotion boundary is unsupported.");
    }

    private static void VerifyBlockers(JsonElement blockers)
    {
        var values = blockers.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null)
            .ToHashSet(StringComparer.Ordinal);
        if (values.Contains(null) ||
            !values.SetEquals(new[] { ExpectedSceneBlocker, ExpectedInterpreterBlocker }))
            throw new InvalidOperationException("Fallout 3 birth-slice blockers are unsupported.");
    }

    private static string RequiredFormId(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (!ValidHex(value, FormIdHexCharacters))
            throw new InvalidOperationException($"Fallout 3 birth-slice FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (!ValidHex(value, Sha256HexCharacters))
            throw new InvalidOperationException($"Fallout 3 birth-slice hash {name} is invalid.");
        return value;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 birth-slice field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 birth-slice field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 birth-slice field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) || result < 0)
            throw new InvalidOperationException($"Fallout 3 birth-slice field {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result) || result <= 0)
            throw new InvalidOperationException($"Fallout 3 birth-slice field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"Fallout 3 birth-slice field {name} is invalid.");
        return value.GetBoolean();
    }

    private static bool ValidHex(string value, int characters) =>
        value.Length == characters && value.All(Uri.IsHexDigit);
}
