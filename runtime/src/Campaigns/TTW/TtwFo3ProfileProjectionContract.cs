using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.TTW;

internal sealed record TtwFo3ProfileProjectionContract(
    string Path,
    string Sha256,
    string CacheCompatibilityId,
    string SourceProfilePath,
    string SourceProfileSha256,
    string SourceNamespacePath,
    string SourceNamespaceSha256,
    string PluginStackId,
    string SaveCompatibilityId,
    string OpeningCommandCacheCompatibilityId,
    JsonElement OpeningCommandContract)
{
    private const string ExpectedSchema =
        "opennv-ttw-fo3-cg00-profile-projection/v1";
    private const string ExpectedStatus =
        "validated-runtime-consumable-identity-projection-assets-pending";
    private const string ExpectedIdentitySchema =
        "opennv-ttw-fo3-cg00-projection-identity/v1";
    private const string ExpectedEffectiveSourceSchema =
        "opennv-ttw-effective-profile-compiler-source/v1";
    private const string ExpectedRecordResolutionPolicy =
        "stable-origin-formkey-last-active-plugin-wins";
    private const string ExpectedCacheKind =
        "dedicated-ttw-cg00-profile-projection";
    private const string ExpectedLoader = "TtwFo3OpeningContract.Load";
    private const string CachePrefix =
        "opennv-ttw-fo3-cg00-projection-cache-v1\0";
    private const string CacheNamespace = "ttw-fo3-opening";
    private const string EffectiveSourceCachePrefix = "ttw-effective-source:";
    private const string RequiredAssetBlocker =
        "archive-members-are-identity-only-not-materialized-runtime-source-paths";
    private const int ExpectedRecordCount = 76;
    private const int ExpectedMemberCount = 57;
    private const int FormIdHexCharacters = 8;
    private const int Sha256HexCharacters = 64;

    internal static TtwFo3ProfileProjectionContract Load(string path)
    {
        var resolved = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(resolved);
        var sha256 = ComputeSha256(bytes);
        using var document = JsonDocument.Parse(bytes);
        return TryLoad(resolved, sha256, document.RootElement) ??
            throw new InvalidOperationException(
                "TTW CG00 profile projection schema is absent.");
    }

    internal static TtwFo3ProfileProjectionContract? TryLoad(
        string path,
        string sha256,
        JsonElement root)
    {
        if (!root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.String ||
            schema.GetString() != ExpectedSchema)
            return null;
        if (TtwJson.String(root, "status") != ExpectedStatus ||
            TtwJson.String(root, "campaign") != "Fallout3" ||
            TtwJson.String(root, "edition") != "TTW" ||
            TtwJson.Boolean(root, "ownedPayloadsEmitted") ||
            !TtwJson.Boolean(root, "archiveMembersIndexed") ||
            !TtwJson.Boolean(root, "profileEmissionReady") ||
            TtwJson.Boolean(root, "runtimeReady"))
            throw new InvalidOperationException("TTW CG00 projection status differs.");

        var compatibility = TtwJson.Object(root, "runtimeLoaderCompatibility");
        var blockers = TtwJson.Array(compatibility, "blockers")
            .EnumerateArray()
            .Select(value => TtwJson.ValueString(value, "projection blocker"))
            .ToHashSet(StringComparer.Ordinal);
        if (TtwJson.String(compatibility, "loader") != ExpectedLoader ||
            TtwJson.Boolean(compatibility, "schemaAmbiguous") ||
            !TtwJson.Boolean(compatibility, "identityEnvelopeValidated") ||
            !TtwJson.Boolean(compatibility, "commandStateExecutorReady") ||
            !blockers.Contains(RequiredAssetBlocker))
            throw new InvalidOperationException(
                "TTW CG00 projection loader boundary differs.");

        var records = TtwJson.Object(root, "effectiveRecordClosure");
        ValidateRecordClosure(records);
        var members = TtwJson.Object(root, "effectiveMemberClosure");
        ValidateMemberClosure(members);

        var envelope = TtwJson.Object(root, "identityEnvelope");
        if (TtwJson.String(envelope, "schema") != ExpectedIdentitySchema ||
            TtwJson.Hex(envelope, "recordClosureSha256", Sha256HexCharacters) !=
                CanonicalSha256(records) ||
            TtwJson.Hex(envelope, "memberClosureSha256", Sha256HexCharacters) !=
                CanonicalSha256(members))
            throw new InvalidOperationException("TTW CG00 projection closure hash differs.");
        _ = TtwJson.Hex(envelope, "compilerSemanticSha256", Sha256HexCharacters);

        var sourceProfile = TtwJson.Object(envelope, "sourceProfile");
        var sourceProfilePath = System.IO.Path.GetFullPath(
            TtwJson.String(sourceProfile, "file"));
        var sourceProfileSha256 = TtwJson.Hex(
            sourceProfile,
            "sha256",
            Sha256HexCharacters);
        var pluginStackId = TtwJson.Hex(
            sourceProfile,
            "pluginStackId",
            Sha256HexCharacters);
        var saveCompatibilityId = TtwJson.String(
            sourceProfile,
            "saveCompatibilityId");
        if (saveCompatibilityId != $"ttw:{pluginStackId}")
            throw new InvalidOperationException("TTW CG00 projection save identity differs.");

        var sourceNamespace = TtwJson.Object(envelope, "sourceNamespace");
        var sourceNamespacePath = System.IO.Path.GetFullPath(
            TtwJson.String(sourceNamespace, "file"));
        var sourceNamespaceSha256 = TtwJson.Hex(
            sourceNamespace,
            "sha256",
            Sha256HexCharacters);
        var effectiveSource = TtwJson.Object(envelope, "effectiveSource");
        if (TtwJson.String(effectiveSource, "schema") !=
                ExpectedEffectiveSourceSchema ||
            TtwJson.Hex(effectiveSource, "pluginStackId", Sha256HexCharacters) !=
                pluginStackId ||
            TtwJson.String(effectiveSource, "saveCompatibilityId") !=
                saveCompatibilityId ||
            TtwJson.Hex(effectiveSource, "sourceProfileSha256", Sha256HexCharacters) !=
                sourceProfileSha256 ||
            TtwJson.Hex(effectiveSource, "sourceNamespaceSha256", Sha256HexCharacters) !=
                sourceNamespaceSha256 ||
            TtwJson.String(effectiveSource, "recordResolutionPolicy") !=
                ExpectedRecordResolutionPolicy ||
            !TtwJson.String(effectiveSource, "cacheCompatibilityId").StartsWith(
                EffectiveSourceCachePrefix,
                StringComparison.Ordinal) ||
            TtwJson.Boolean(effectiveSource, "standaloneFallout3ProfileAccepted") ||
            TtwJson.Boolean(effectiveSource, "standaloneFallout3CacheReused") ||
            TtwJson.Boolean(effectiveSource, "standaloneNewVegasProfileAccepted") ||
            TtwJson.Boolean(effectiveSource, "standaloneNewVegasCacheReused") ||
            TtwJson.Boolean(effectiveSource, "runtimeReady"))
            throw new InvalidOperationException(
                "TTW CG00 projection effective-source identity differs.");

        ValidateRecipeIdentity(TtwJson.Object(envelope, "projectionRecipe"));
        ValidateStandaloneShapeSource(
            TtwJson.Object(envelope, "standaloneContractShapeSource"));

        var opening = TtwJson.Object(root, "openingCommandContract").Clone();
        if (TtwJson.Hex(
                envelope,
                "openingCommandContractSha256",
                Sha256HexCharacters) != CanonicalSha256(opening))
            throw new InvalidOperationException(
                "TTW CG00 projection opening-command hash differs.");
        var openingCache = TtwJson.String(
            TtwJson.Object(opening, "cacheBoundary"),
            "compatibilityId");

        var cache = TtwJson.Object(root, "cacheBoundary");
        if (TtwJson.String(cache, "kind") != ExpectedCacheKind ||
            TtwJson.Boolean(cache, "standaloneFallout3ProfileAccepted") ||
            TtwJson.Boolean(cache, "standaloneFallout3CacheReused") ||
            TtwJson.Boolean(cache, "standaloneNewVegasProfileAccepted") ||
            TtwJson.Boolean(cache, "standaloneNewVegasCacheReused"))
            throw new InvalidOperationException("TTW CG00 projection cache isolation differs.");
        var cacheCompatibilityId = TtwJson.String(cache, "compatibilityId");
        var expectedCacheCompatibilityId = ComputeCacheCompatibilityId(envelope);
        if (cacheCompatibilityId != expectedCacheCompatibilityId)
            throw new InvalidOperationException(
                "TTW CG00 projection cache compatibility ID differs.");

        return new TtwFo3ProfileProjectionContract(
            path,
            sha256,
            cacheCompatibilityId,
            sourceProfilePath,
            sourceProfileSha256,
            sourceNamespacePath,
            sourceNamespaceSha256,
            pluginStackId,
            saveCompatibilityId,
            openingCache,
            opening);
    }

    internal void ValidateSourceBinding(
        string sourceProfilePath,
        string sourceProfileSha256,
        string sourceNamespacePath,
        string sourceNamespaceSha256,
        string pluginStackId,
        string saveCompatibilityId,
        string openingCommandCacheCompatibilityId)
    {
        if (!SourceProfilePath.Equals(
                sourceProfilePath,
                StringComparison.OrdinalIgnoreCase) ||
            SourceProfileSha256 != sourceProfileSha256 ||
            !SourceNamespacePath.Equals(
                sourceNamespacePath,
                StringComparison.OrdinalIgnoreCase) ||
            SourceNamespaceSha256 != sourceNamespaceSha256 ||
            PluginStackId != pluginStackId ||
            SaveCompatibilityId != saveCompatibilityId ||
            OpeningCommandCacheCompatibilityId != openingCommandCacheCompatibilityId)
            throw new InvalidOperationException(
                "TTW CG00 projection/opening source binding differs.");
    }

    private static void ValidateRecordClosure(JsonElement source)
    {
        var records = TtwJson.Array(source, "records").EnumerateArray().ToArray();
        if (TtwJson.Integer(source, "recordCount") != ExpectedRecordCount ||
            records.Length != ExpectedRecordCount)
            throw new InvalidOperationException("TTW CG00 projection record count differs.");
        var formKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localFormIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var formKey = TtwJson.String(record, "formKey");
            var stableLocalFormId = TtwJson.Hex(
                record,
                "stableLocalFormId",
                FormIdHexCharacters);
            _ = TtwJson.Hex(record, "runtimeFormId", FormIdHexCharacters);
            _ = TtwJson.String(record, "recordType");
            if (!formKey.Contains(':', StringComparison.Ordinal) ||
                !formKeys.Add(formKey) ||
                !localFormIds.Add(stableLocalFormId))
                throw new InvalidOperationException(
                    "TTW CG00 projection record identity is ambiguous.");
            var winner = TtwJson.Object(record, "winner");
            _ = TtwJson.String(winner, "plugin");
            _ = TtwJson.Hex(winner, "pluginSha256", Sha256HexCharacters);
            _ = TtwJson.Hex(winner, "recordSha256", Sha256HexCharacters);
            if (TtwJson.Integer(winner, "loadOrderIndex") < 0 ||
                TtwJson.Integer(winner, "sourceRootIndex") < 0)
                throw new InvalidOperationException(
                    "TTW CG00 projection record winner index differs.");
        }
    }

    private static void ValidateMemberClosure(JsonElement source)
    {
        var members = TtwJson.Array(source, "members").EnumerateArray().ToArray();
        if (TtwJson.Integer(source, "memberCount") != ExpectedMemberCount ||
            members.Length != ExpectedMemberCount)
            throw new InvalidOperationException("TTW CG00 projection member count differs.");
        var logicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            var logicalPath = TtwJson.String(member, "logicalPath");
            var parts = logicalPath.Replace('/', '\\').Split(
                '\\',
                StringSplitOptions.RemoveEmptyEntries);
            var bytes = TtwJson.Long(member, "bytes");
            var sha256 = TtwJson.Hex(member, "sha256", Sha256HexCharacters);
            if (System.IO.Path.IsPathRooted(logicalPath) ||
                parts.Length == 0 ||
                parts.Any(part => part == "..") ||
                bytes <= 0 ||
                !logicalPaths.Add(logicalPath))
                throw new InvalidOperationException(
                    "TTW CG00 projection member identity is ambiguous.");
            var winner = TtwJson.Object(member, "winner");
            var kind = TtwJson.String(winner, "kind");
            if (kind == "bsa")
            {
                if (TtwJson.Long(winner, "memberBytes") != bytes ||
                    TtwJson.Hex(winner, "memberSha256", Sha256HexCharacters) != sha256 ||
                    TtwJson.Integer(winner, "archiveOrderIndex") < 0 ||
                    TtwJson.Integer(winner, "sourceRootIndex") < 0)
                    throw new InvalidOperationException(
                        "TTW CG00 projection archive-member winner differs.");
                _ = TtwJson.String(winner, "archive");
                _ = TtwJson.Hex(winner, "archiveSha256", Sha256HexCharacters);
            }
            else if (kind == "loose")
            {
                if (TtwJson.Long(winner, "bytes") != bytes ||
                    TtwJson.Hex(winner, "sha256", Sha256HexCharacters) != sha256 ||
                    TtwJson.Integer(winner, "sourceRootIndex") < 0)
                    throw new InvalidOperationException(
                        "TTW CG00 projection loose-member winner differs.");
                _ = System.IO.Path.GetFullPath(TtwJson.String(winner, "source"));
            }
            else
            {
                throw new InvalidOperationException(
                    "TTW CG00 projection member winner kind differs.");
            }
        }
    }

    private static void ValidateRecipeIdentity(JsonElement source)
    {
        _ = TtwJson.String(source, "file");
        _ = TtwJson.Hex(source, "sha256", Sha256HexCharacters);
    }

    private static void ValidateStandaloneShapeSource(JsonElement source)
    {
        _ = System.IO.Path.GetFullPath(TtwJson.String(source, "file"));
        if (TtwJson.Long(source, "bytes") <= 0)
            throw new InvalidOperationException(
                "TTW CG00 projection standalone shape source is empty.");
        _ = TtwJson.Hex(source, "sha256", Sha256HexCharacters);
    }

    private static string ComputeCacheCompatibilityId(JsonElement envelope)
    {
        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("identityEnvelope");
            WriteCanonical(writer, envelope);
            writer.WriteString("schema", ExpectedSchema);
            writer.WriteEndObject();
        }
        var prefix = Encoding.UTF8.GetBytes(CachePrefix);
        var input = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(input, 0);
        payload.ToArray().CopyTo(input, prefix.Length);
        return $"{CacheNamespace}:{ComputeSha256(input)}";
    }

    private static string CanonicalSha256(JsonElement source)
    {
        using var payload = new MemoryStream();
        using (var writer = new Utf8JsonWriter(payload))
            WriteCanonical(writer, source);
        return ComputeSha256(payload.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement source)
    {
        switch (source.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in source.EnumerateObject().OrderBy(
                    property => property.Name,
                    StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in source.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(source.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(source.GetRawText(), skipInputValidation: false);
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
                throw new InvalidOperationException(
                    "TTW CG00 projection canonical value is unsupported.");
        }
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
