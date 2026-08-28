using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Stage65ParentAppearance(
    string ReferenceFormId,
    string ReferenceEditorId,
    string BaseFormId,
    string RaceFormId,
    string SymmetricGeometrySha256,
    string AsymmetricGeometrySha256,
    string SymmetricTextureSha256);

internal sealed record Fo3Stage65AppearanceState(
    int Stage,
    int AppliedCommandCount,
    string PlayerSymmetricGeometrySha256,
    string PlayerAsymmetricGeometrySha256,
    string PlayerSymmetricTextureSha256,
    IReadOnlyList<Fo3Stage65ParentAppearance> Parents,
    string NextBoundary);

internal sealed record Fo3Stage65SelectionResult(
    string PlayerRaceFormId,
    string PlayerSex,
    string PlayerSymmetricGeometrySha256,
    string PlayerAsymmetricGeometrySha256,
    string PlayerSymmetricTextureSha256,
    IReadOnlyList<Fo3Stage65ParentAppearance> Parents);

internal sealed record Fo3Stage65AppearanceTransition(
    int SourceStage,
    int Stage,
    int AccountedCommandCount,
    IReadOnlyDictionary<string, Fo3Stage65SelectionResult> SelectionResults,
    string NextBoundary)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-stage-65-appearance/v1";
    private const string ExpectedStatus = "source-backed-command-application";

    internal static Fo3Stage65AppearanceTransition Load(
        JsonElement source,
        int packageStage,
        int nextStage,
        string expectedStageSourceSha256,
        string expectedSchema,
        IReadOnlyList<string> expectedRaceFormIds,
        IReadOnlyList<string> expectedCommandKinds)
    {
        if (RequiredString(source, "schema") != ExpectedSchema || ExpectedSchema != expectedSchema ||
            RequiredString(source, "status") != ExpectedStatus)
            throw new InvalidOperationException("Fallout 3 stage-65 appearance contract is unsupported.");
        var sourceStage = RequiredInteger(source, "sourceStage");
        var stage = RequiredInteger(source, "stage");
        if (sourceStage != packageStage || stage != nextStage)
            throw new InvalidOperationException(
                "Fallout 3 stage-65 appearance contract does not join the player package.");
        if (RequiredSha256(source, "stageSourceSha256") != expectedStageSourceSha256)
            throw new InvalidOperationException(
                "Fallout 3 stage-65 source differs from the package transition.");

        var commands = RequiredArray(source, "commands").EnumerateArray().ToArray();
        var accountedCommandCount = RequiredInteger(source, "accountedCommandCount");
        var commandKinds = commands.Select(value => RequiredString(value, "kind")).ToArray();
        if (commands.Length == 0 || commands.Length != accountedCommandCount ||
            !commandKinds.Order().SequenceEqual(expectedCommandKinds.Order()))
            throw new InvalidOperationException("Fallout 3 stage-65 command accounting differs.");
        var raceSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var faceSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var percentageEditorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            var kind = RequiredString(command, "kind");
            var subject = RequiredString(command, "subject");
            if (RequiredString(command, "target") != "player")
                throw new InvalidOperationException("Fallout 3 stage-65 command target differs.");
            if (kind == "matchRace")
            {
                if (!raceSubjects.Add(subject))
                    throw new InvalidOperationException("Fallout 3 MatchRace command is duplicated.");
            }
            else if (kind == "matchFaceGeometry")
            {
                if (!faceSubjects.Add(subject))
                    throw new InvalidOperationException(
                        "Fallout 3 MatchFaceGeometry command is ambiguous.");
                percentageEditorIds.Add(RequiredString(command, "template"));
            }
            else
            {
                throw new InvalidOperationException("Fallout 3 stage-65 command is unsupported.");
            }
        }
        if (!raceSubjects.SetEquals(faceSubjects) || percentageEditorIds.Count != 1)
            throw new InvalidOperationException("Fallout 3 stage-65 command pairs are incomplete.");

        var semantics = RequiredObject(source, "semantics");
        if (RequiredString(semantics, "matchRace") !=
                "target-race-equals-source-current-race-with-default-face-texture" ||
            RequiredString(semantics, "matchFaceGeometry") !=
                "linear-current-to-source-geometry-percent" ||
            RequiredString(semantics, "matchFaceTexture") !=
                "unchanged-by-match-face-geometry")
            throw new InvalidOperationException("Fallout 3 stage-65 appearance semantics differ.");

        var percentage = RequiredObject(source, "matchPercentage");
        _ = RequiredFormId(percentage, "formId");
        if (RequiredString(percentage, "editorId") != percentageEditorIds.Single())
            throw new InvalidOperationException("Fallout 3 MatchFaceGeometry global identity differs.");
        _ = RequiredSha256(percentage, "recordSha256");
        _ = RequiredString(percentage, "type");
        if (!percentage.TryGetProperty("value", out var percentageValue) ||
            !percentageValue.TryGetDouble(out var percent) ||
            !double.IsFinite(percent) || percent < 0.0 || percent > 100.0)
            throw new InvalidOperationException("Fallout 3 MatchFaceGeometry percentage is invalid.");

        var parentSources = RequiredArray(source, "parentSources").EnumerateArray().ToArray();
        var sourceReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceIdentities = new Dictionary<string, (string ReferenceFormId, string BaseFormId)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var parent in parentSources)
        {
            var referenceFormId = RequiredFormId(parent, "referenceFormId");
            var editorId = RequiredString(parent, "referenceEditorId");
            if (!sourceReferences.Add(editorId) || !raceSubjects.Contains(editorId))
                throw new InvalidOperationException("Fallout 3 stage-65 parent source differs.");
            _ = RequiredSha256(parent, "referenceRecordSha256");
            var baseFormId = RequiredFormId(parent, "baseFormId");
            sourceIdentities.Add(editorId, (referenceFormId, baseFormId));
            _ = RequiredString(parent, "baseEditorId");
            _ = RequiredSha256(parent, "baseRecordSha256");
            _ = RequiredFormId(parent, "originalRaceFormId");
            var face = RequiredObject(parent, "faceGenIdentity");
            _ = ValidateFloatContract(
                RequiredObject(face, "symmetricGeometry"),
                Fo3OpeningFlowNumericContracts.FaceGenSymmetricGeometryFloats);
            _ = ValidateFloatContract(
                RequiredObject(face, "asymmetricGeometry"),
                Fo3OpeningFlowNumericContracts.FaceGenAsymmetricGeometryFloats);
        }
        if (!sourceReferences.SetEquals(raceSubjects))
            throw new InvalidOperationException("Fallout 3 stage-65 parent inventory is incomplete.");

        var results = new Dictionary<string, Fo3Stage65SelectionResult>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var result in RequiredArray(source, "selectionResults").EnumerateArray())
        {
            var raceFormId = RequiredFormId(result, "playerRaceFormId");
            var playerSex = RequiredString(result, "playerSex");
            if (playerSex is not "male" and not "female")
                throw new InvalidOperationException("Fallout 3 stage-65 player sex is invalid.");
            var playerFace = RequiredObject(result, "playerFaceGen");
            var parentResults = RequiredArray(result, "parents").EnumerateArray()
                .Select(value => LoadParentResult(value, raceFormId))
                .ToArray();
            if (parentResults.Length != sourceReferences.Count ||
                !parentResults.Select(value => value.ReferenceEditorId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(sourceReferences))
                throw new InvalidOperationException(
                    "Fallout 3 stage-65 parent appearance result is incomplete.");
            if (parentResults.Any(value =>
            {
                var identity = sourceIdentities[value.ReferenceEditorId];
                return value.ReferenceFormId != identity.ReferenceFormId ||
                    value.BaseFormId != identity.BaseFormId;
            }))
                throw new InvalidOperationException(
                    "Fallout 3 stage-65 parent result identity differs from its source.");
            var row = new Fo3Stage65SelectionResult(
                raceFormId,
                playerSex,
                RequiredSha256(playerFace, "symmetricGeometrySha256"),
                RequiredSha256(playerFace, "asymmetricGeometrySha256"),
                RequiredSha256(playerFace, "symmetricTextureSha256"),
                parentResults);
            if (!results.TryAdd(SelectionKey(raceFormId, playerSex), row))
                throw new InvalidOperationException(
                    "Fallout 3 stage-65 selection result is duplicated.");
        }
        var races = results.Values.Select(value => value.PlayerRaceFormId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (results.Count != expectedRaceFormIds.Count * 2 ||
            races.Count == 0 ||
            !races.SetEquals(expectedRaceFormIds) ||
            races.Any(race =>
                !results.ContainsKey(SelectionKey(race, "male")) ||
                !results.ContainsKey(SelectionKey(race, "female"))))
            throw new InvalidOperationException("Fallout 3 stage-65 selection matrix is incomplete.");

        return new Fo3Stage65AppearanceTransition(
            sourceStage,
            stage,
            accountedCommandCount,
            results,
            RequiredString(source, "nextBoundary"));
    }

    internal Fo3Stage65AppearanceState Apply(
        string playerSex,
        string playerRaceFormId,
        Fo3FaceGenDefaults playerFace)
    {
        if (!SelectionResults.TryGetValue(
                SelectionKey(playerRaceFormId, playerSex),
                out var result))
            throw new InvalidOperationException(
                "Fallout 3 stage-65 selection is absent from the exact race/sex matrix.");
        if (result.PlayerSymmetricGeometrySha256 != playerFace.SymmetricGeometrySha256 ||
            result.PlayerAsymmetricGeometrySha256 != playerFace.AsymmetricGeometrySha256 ||
            result.PlayerSymmetricTextureSha256 != playerFace.SymmetricTextureSha256)
            throw new InvalidOperationException(
                "Fallout 3 stage-65 player appearance differs from the selected profile state.");
        return new Fo3Stage65AppearanceState(
            Stage,
            AccountedCommandCount,
            result.PlayerSymmetricGeometrySha256,
            result.PlayerAsymmetricGeometrySha256,
            result.PlayerSymmetricTextureSha256,
            result.Parents,
            NextBoundary);
    }

    internal void ValidateSavedState(JsonElement source, Fo3Stage65AppearanceState expected)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredInteger(source, "stage") != expected.Stage ||
            RequiredInteger(source, "appliedCommandCount") != expected.AppliedCommandCount ||
            RequiredString(source, "nextBoundary") != expected.NextBoundary)
            throw new InvalidOperationException("Saved Fallout 3 stage-65 state differs.");
        var player = RequiredObject(source, "playerFaceGen");
        if (RequiredSha256(player, "symmetricGeometrySha256") !=
                expected.PlayerSymmetricGeometrySha256 ||
            RequiredSha256(player, "asymmetricGeometrySha256") !=
                expected.PlayerAsymmetricGeometrySha256 ||
            RequiredSha256(player, "symmetricTextureSha256") !=
                expected.PlayerSymmetricTextureSha256)
            throw new InvalidOperationException("Saved Fallout 3 player FaceGen state differs.");
        var parents = RequiredArray(source, "parents").EnumerateArray().ToArray();
        if (parents.Length != expected.Parents.Count)
            throw new InvalidOperationException("Saved Fallout 3 parent appearance count differs.");
        foreach (var expectedParent in expected.Parents)
        {
            var parent = parents.Single(value =>
                RequiredString(value, "referenceEditorId") == expectedParent.ReferenceEditorId);
            if (RequiredFormId(parent, "referenceFormId") != expectedParent.ReferenceFormId ||
                RequiredFormId(parent, "baseFormId") != expectedParent.BaseFormId ||
                RequiredFormId(parent, "raceFormId") != expectedParent.RaceFormId ||
                RequiredSha256(parent, "symmetricGeometrySha256") !=
                    expectedParent.SymmetricGeometrySha256 ||
                RequiredSha256(parent, "asymmetricGeometrySha256") !=
                    expectedParent.AsymmetricGeometrySha256 ||
                RequiredSha256(parent, "symmetricTextureSha256") !=
                    expectedParent.SymmetricTextureSha256)
                throw new InvalidOperationException("Saved Fallout 3 parent appearance differs.");
        }
    }

    private static Fo3Stage65ParentAppearance LoadParentResult(
        JsonElement source,
        string playerRaceFormId)
    {
        if (RequiredFormId(source, "raceFormId") != playerRaceFormId)
            throw new InvalidOperationException("Fallout 3 matched parent race differs.");
        var face = RequiredObject(source, "faceGen");
        _ = ValidateFloatContract(
            RequiredObject(face, "preMatchSymmetricGeometry"),
            Fo3OpeningFlowNumericContracts.FaceGenSymmetricGeometryFloats);
        _ = ValidateFloatContract(
            RequiredObject(face, "preMatchAsymmetricGeometry"),
            Fo3OpeningFlowNumericContracts.FaceGenAsymmetricGeometryFloats);
        var symmetric = ValidateFloatContract(
            RequiredObject(face, "symmetricGeometry"),
            Fo3OpeningFlowNumericContracts.FaceGenSymmetricGeometryFloats);
        var asymmetric = ValidateFloatContract(
            RequiredObject(face, "asymmetricGeometry"),
            Fo3OpeningFlowNumericContracts.FaceGenAsymmetricGeometryFloats);
        var texture = ValidateFloatContract(
            RequiredObject(face, "symmetricTexture"),
            Fo3OpeningFlowNumericContracts.FaceGenSymmetricTextureFloats);
        if (RequiredString(face, "texturePolicy") !=
            "matched-race-default-not-face-geometry-morphed")
            throw new InvalidOperationException("Fallout 3 matched parent texture policy differs.");
        return new Fo3Stage65ParentAppearance(
            RequiredFormId(source, "referenceFormId"),
            RequiredString(source, "referenceEditorId"),
            RequiredFormId(source, "baseFormId"),
            playerRaceFormId,
            symmetric,
            asymmetric,
            texture);
    }

    private static string ValidateFloatContract(JsonElement source, int expectedCount)
    {
        if (RequiredInteger(source, "count") != expectedCount)
            throw new InvalidOperationException("Fallout 3 stage-65 FaceGen count differs.");
        var values = RequiredArray(source, "values").EnumerateArray()
            .Select(value => (float)value.GetDouble()).ToArray();
        if (values.Length != expectedCount || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout 3 stage-65 FaceGen values are invalid.");
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
            foreach (var value in values)
                writer.Write(value);
        var actual = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        if (actual != RequiredSha256(source, "sha256"))
            throw new InvalidOperationException("Fallout 3 stage-65 FaceGen hash differs.");
        return actual;
    }

    private static string SelectionKey(string raceFormId, string playerSex) =>
        $"{raceFormId}:{playerSex}";

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 stage-65 field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 stage-65 field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 stage-65 field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 stage-65 field {name} is invalid.");
        return result;
    }

    private static string RequiredFormId(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.FormIdHexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-65 FormID {name} is invalid.");
        return value;
    }

    private static string RequiredSha256(JsonElement parent, string name)
    {
        var value = RequiredString(parent, name);
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-65 hash {name} is invalid.");
        return value;
    }
}
