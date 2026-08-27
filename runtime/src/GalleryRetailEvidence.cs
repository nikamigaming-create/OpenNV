using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class GalleryRetailEvidence
{
    private const string ExpectedSchema = "opennv-gallery-retail-evidence/v2";
    private const string ExpectedStatus = "retail-authored-reference-observed";
    private const string ExpectedPlacementMode = "owned-authored-reference-preserved";
    private const int FrustumComponentCount = 7;
    private const int ViewportComponentCount = 4;
    private const int MatrixComponentCount = 16;
    private const int FrustumLeftIndex = 0;
    private const int FrustumRightIndex = 1;
    private const int FrustumTopIndex = 2;
    private const int FrustumBottomIndex = 3;
    private const int FrustumNearIndex = 4;
    private const int FrustumFarIndex = 5;
    private const int FrustumOrthographicIndex = 6;

    internal static Contract Load(
        JsonElement descriptor,
        string expectedId,
        int expectedOrdinal,
        string expectedLabel,
        string expectedLocation,
        string expectedLocationId,
        string expectedLocationClass,
        string expectedReferenceFormId,
        string expectedBaseFormId,
        string expectedActorCellFormId,
        GalleryShotContract.SceneIdentity expectedScene,
        string expectedRecordType,
        string expectedEnableStateMode,
        string expectedOutputFile,
        RuntimeConfiguration configuration)
    {
        var evidence = VerifyDescriptor(descriptor, "gallery retail evidence");
        using var document = JsonDocument.Parse(File.ReadAllText(evidence.Path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ExpectedSchema ||
            root.GetProperty("status").GetString() != ExpectedStatus)
            throw new InvalidOperationException(
                $"Unexpected gallery retail evidence: {evidence.Path}");
        var shot = root.GetProperty("shot");
        if (RequireText(shot, "id") != expectedId ||
            shot.GetProperty("ordinal").GetInt32() != expectedOrdinal ||
            RequireText(shot, "label") != expectedLabel ||
            RequireText(shot, "location") != expectedLocation ||
            RequireText(shot, "locationId") != expectedLocationId ||
            RequireText(shot, "locationClass") != expectedLocationClass ||
            Normalize(shot, "referenceFormId") != Normalize(expectedReferenceFormId) ||
            Normalize(shot, "baseFormId") != Normalize(expectedBaseFormId) ||
            Normalize(shot.GetProperty("actor"), "cellFormId") !=
                Normalize(expectedActorCellFormId) ||
            RequireText(shot, "recordType") != expectedRecordType ||
            RequireText(shot.GetProperty("enableState"), "mode") !=
                expectedEnableStateMode ||
            RequireText(shot, "outputFile") != expectedOutputFile)
            throw new InvalidOperationException(
                "Gallery retail evidence identifies another authored shot.");
        VerifySceneIdentity(shot.GetProperty("scene"), expectedScene, "shot");

        VerifyDescriptor(root.GetProperty("gallery"), "gallery recipe");
        var runtimeConfiguration = VerifyDescriptor(
            root.GetProperty("runtimeConfiguration"),
            "gallery runtime configuration");
        if (!runtimeConfiguration.Sha256.Equals(
                configuration.Sha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Gallery retail evidence was compiled with another runtime configuration.");
        var retail = root.GetProperty("retail");
        var report = VerifyDescriptor(retail.GetProperty("report"), "retail report");
        var oracle = VerifyDescriptor(retail.GetProperty("oracleJsonl"), "retail oracle JSONL");
        if (RequireText(retail, "placementMode") != ExpectedPlacementMode ||
            retail.GetProperty("actorTransformMutated").GetBoolean())
            throw new InvalidOperationException(
                "Gallery retail evidence did not preserve the authored actor transform.");
        VerifySceneIdentity(
            retail.GetProperty("sceneObserver"),
            expectedScene,
            "retail observer");
        var sourceFrames = retail.GetProperty("sourceFrames").EnumerateArray()
            .Select((source, index) => VerifyDescriptor(
                source,
                $"retail source frame {index}"))
            .ToArray();
        if (sourceFrames.Length == 0 ||
            sourceFrames.Select(frame => frame.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourceFrames.Length)
            throw new InvalidOperationException(
                "Gallery retail evidence has no unique native source-frame set.");
        var presentation = ParsePresentation(
            retail.GetProperty("presentation"),
            sourceFrames,
            expectedReferenceFormId,
            expectedBaseFormId,
            configuration);

        var policy = root.GetProperty("evidencePolicy");
        if (!policy.GetProperty("retailIsReferenceOnly").GetBoolean() ||
            !policy.GetProperty("ownedActorTransformPreserved").GetBoolean() ||
            policy.GetProperty("windowsAppControlUsed").GetBoolean() ||
            policy.GetProperty("foregroundActivationUsed").GetBoolean() ||
            policy.GetProperty("foregroundInputInjected").GetBoolean())
            throw new InvalidOperationException(
                "Gallery retail evidence violates unattended capture policy.");

        ActorReviewContract.EnvironmentState? environment = null;
        uint effectiveWeatherForm = 0u;
        if (expectedLocationClass == "exterior")
        {
            var environmentSource = retail.GetProperty("environment");
            environment = ActorReviewContract.ParseEnvironment(
                environmentSource,
                configuration);
            effectiveWeatherForm = environment.Value.WeatherForm == 0u
                ? environment.Value.DefaultWeatherForm
                : environment.Value.WeatherForm;
            if (environmentSource.GetProperty("effectiveWeatherForm").GetUInt32() !=
                effectiveWeatherForm)
                throw new InvalidOperationException(
                    "Gallery retail evidence effective WTHR identity changed.");
        }
        return new Contract(
            evidence,
            report,
            oracle,
            sourceFrames,
            RequireText(retail, "runtimePluginStackEventSha256"),
            presentation,
            environment,
            effectiveWeatherForm);
    }

    private static PresentationReference ParsePresentation(
        JsonElement source,
        IReadOnlyList<PrivateFile> sourceFrames,
        string expectedReferenceFormId,
        string expectedBaseFormId,
        RuntimeConfiguration configuration)
    {
        var shotKind = RequireText(source, "shotKind");
        var selectionProof = ParseSelectionProof(
            source.GetProperty("selection"), shotKind, configuration);
        var frame = source.GetProperty("frame").GetInt32();
        if (frame <= 0)
            throw new InvalidOperationException(
                "Gallery retail presentation frame must be positive.");
        var sourceFrame = VerifyDescriptor(
            source.GetProperty("sourceFrame"),
            "gallery retail presentation source frame");
        if (!sourceFrames.Any(candidate =>
                candidate.Path.Equals(sourceFrame.Path, StringComparison.OrdinalIgnoreCase) &&
                candidate.Bytes == sourceFrame.Bytes &&
                candidate.Sha256.Equals(sourceFrame.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Gallery retail presentation frame is outside the source-frame ledger.");

        var camera = source.GetProperty("camera");
        var cameraWorld = camera.GetProperty("world");
        var cameraRotation = ReadNumbers(
            cameraWorld.GetProperty("rotation"),
            GamebryoCoordinate.SpatialDimensions * GamebryoCoordinate.SpatialDimensions,
            "presentation camera rotation");
        var cameraTranslation = ReadVector(
            cameraWorld.GetProperty("translation"),
            "presentation camera translation");
        var cameraScale = cameraWorld.GetProperty("scale").GetSingle();
        var offset = ReadVector(
            camera.GetProperty("offsetFromActorRootGameUnits"),
            "presentation camera offset");
        var fov = camera.GetProperty("fovYRadians").GetSingle();
        var frustum = ReadNumbers(
            camera.GetProperty("frustum"),
            FrustumComponentCount,
            "presentation camera frustum");
        var viewport = ReadNumbers(
            camera.GetProperty("viewport"),
            ViewportComponentCount,
            "presentation camera viewport");
        var viewMatrix = ReadNumbers(
            camera.GetProperty("viewMatrix"),
            MatrixComponentCount,
            "presentation camera view matrix");
        var projectionMatrix = ReadNumbers(
            camera.GetProperty("projectionMatrix"),
            MatrixComponentCount,
            "presentation camera projection matrix");
        if (!float.IsFinite(cameraScale) || cameraScale <= 0.0f ||
            !float.IsFinite(fov) || fov <= 0.0f || fov >= MathF.PI ||
            frustum[FrustumLeftIndex] >= frustum[FrustumRightIndex] ||
            frustum[FrustumBottomIndex] >= frustum[FrustumTopIndex] ||
            frustum[FrustumNearIndex] <= 0.0f ||
            frustum[FrustumFarIndex] <= frustum[FrustumNearIndex] ||
            frustum[FrustumOrthographicIndex] != 0.0f)
            throw new InvalidOperationException(
                "Gallery retail presentation camera is invalid.");

        var actor = source.GetProperty("actor");
        var actorRoot = actor.GetProperty("rootWorld");
        var actorRotation = ReadNumbers(
            actorRoot.GetProperty("rotation"),
            GamebryoCoordinate.SpatialDimensions * GamebryoCoordinate.SpatialDimensions,
            "presentation actor rotation");
        var actorTranslation = ReadVector(
            actorRoot.GetProperty("translation"),
            "presentation actor translation");
        var actorScale = actorRoot.GetProperty("scale").GetSingle();
        if (!float.IsFinite(actorScale) || actorScale <= 0.0f ||
            cameraTranslation.DistanceTo(actorTranslation + offset) >
                configuration.ActorParity.CameraPositionToleranceGameUnits)
            throw new InvalidOperationException(
                "Gallery retail presentation actor/camera join is invalid.");
        var sequences = actor.GetProperty("animationDataSequences")
            .EnumerateArray()
            .Select(sequence => new AnimationSequence(
                RequireText(sequence, "file"),
                sequence.GetProperty("state").GetInt32(),
                sequence.GetProperty("cycle").GetInt32(),
                sequence.GetProperty("weight").GetSingle(),
                sequence.GetProperty("frequency").GetSingle(),
                sequence.GetProperty("lastScaledSeconds").GetSingle(),
                sequence.GetProperty("group").GetInt32()))
            .ToArray();
        if (sequences.Length < 1 || sequences.Any(sequence =>
                !float.IsFinite(sequence.Weight) ||
                !float.IsFinite(sequence.Frequency) ||
                !float.IsFinite(sequence.LastScaledSeconds)))
            throw new InvalidOperationException(
                "Gallery retail presentation animation state is incomplete.");
        return new PresentationReference(
            shotKind,
            frame,
            sourceFrame,
            RequireSha256(source, "cameraEventSha256"),
            RequireSha256(source, "sourceFrameCameraContractEventSha256"),
            RequireSha256(source, "actorSnapshotEventSha256"),
            RequireSha256(source, "actorPoseEventSha256"),
            cameraTranslation,
            offset,
            GamebryoCoordinate.ConvertCameraBasis(
                cameraRotation,
                $"gallery frame {frame} camera basis"),
            cameraScale,
            fov,
            new ActorReviewContract.FrustumState(
                frustum[FrustumLeftIndex], frustum[FrustumRightIndex],
                frustum[FrustumTopIndex], frustum[FrustumBottomIndex],
                frustum[FrustumNearIndex], frustum[FrustumFarIndex]),
            viewport,
            viewMatrix,
            projectionMatrix,
            new ActorReference(
                actorTranslation,
                GamebryoCoordinate.ConvertBasis(
                    actorRotation,
                    actorScale,
                    $"gallery frame {frame} actor basis"),
                actorScale,
                actor.GetProperty("weaponOut").GetBoolean(),
                actor.GetProperty("weaponForm").GetUInt32(),
                sequences),
            selectionProof,
            RequireText(source, "derivation"));
    }

    private static PresentationSelectionProof ParseSelectionProof(
        JsonElement source,
        string shotKind,
        RuntimeConfiguration configuration)
    {
        var policy = configuration.Capture.Gallery.RetailPresentationSelection;
        var candidateShotKinds = source.GetProperty("candidateShotKinds")
            .EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        var focusKind = RequireText(source, "focusKind");
        var focusRuleOrdinalElement = source.GetProperty("focusRuleOrdinal");
        int? focusRuleOrdinal = focusRuleOrdinalElement.ValueKind == JsonValueKind.Null
            ? null
            : focusRuleOrdinalElement.GetInt32();
        var facingDot = source.GetProperty(
            "cameraDirectionDotFocusForward").GetDouble();
        var rule = policy.SemanticFocusFacingRules.SingleOrDefault(candidate =>
            candidate.FocusKind == focusKind);
        if (RequireText(source, "policySchema") != policy.Schema ||
            RequireText(source, "tieBreak") != policy.TieBreak ||
            !candidateShotKinds.SequenceEqual(policy.CandidateShotKinds) ||
            !policy.CandidateShotKinds.Contains(shotKind, StringComparer.Ordinal) ||
            rule is null ||
            !rule.AllowedShotKinds.Contains(shotKind, StringComparer.Ordinal) ||
            focusRuleOrdinal is < 0 ||
            !double.IsFinite(facingDot) ||
            facingDot < rule.MinimumCameraDirectionDotFocusForward ||
            facingDot > rule.MaximumCameraDirectionDotFocusForward ||
            RequireText(source, "surfaceStatus") != policy.RequiredSurfaceStatus ||
            source.GetProperty("semanticFocusSurface").GetBoolean() !=
                policy.RequireSemanticFocusSurface ||
            source.GetProperty("cameraOutsideActorWorldBound").GetBoolean() !=
                policy.RequireCameraOutsideActorWorldBound ||
            source.GetProperty("cameraCorridorPassed").GetBoolean() !=
                policy.RequireClearCameraCorridor ||
            source.GetProperty("cameraTranslationToleranceGameUnits").GetSingle() !=
                policy.CameraTranslationToleranceGameUnits)
            throw new InvalidOperationException(
                "Gallery retail presentation selection differs from runtime policy.");
        return new PresentationSelectionProof(
            focusKind,
            focusRuleOrdinal,
            facingDot,
            RequireText(source, "surfaceStatus"),
            source.GetProperty("semanticFocusSurface").GetBoolean(),
            source.GetProperty("cameraOutsideActorWorldBound").GetBoolean(),
            source.GetProperty("cameraCorridorPassed").GetBoolean());
    }

    private static void VerifySceneIdentity(
        JsonElement source,
        GalleryShotContract.SceneIdentity expected,
        string label)
    {
        var worldspace = source.GetProperty("worldspaceFormId");
        var actualWorldspace = worldspace.ValueKind == JsonValueKind.Null
            ? null
            : worldspace.GetString();
        if (Normalize(source, "cellFormId") != Normalize(expected.CellFormId) ||
            source.GetProperty("interior").GetBoolean() != expected.Interior ||
            (actualWorldspace is null) != (expected.WorldspaceFormId is null) ||
            (actualWorldspace is not null &&
                Normalize(actualWorldspace) != Normalize(expected.WorldspaceFormId!)))
            throw new InvalidOperationException(
                $"Gallery retail evidence {label} CELL/WRLD identity changed.");
    }

    private static float[] ReadNumbers(JsonElement source, int count, string label)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != count || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Gallery retail {label} must contain {count} finite values.");
        return values;
    }

    private static Vector3 ReadVector(JsonElement source, string label)
    {
        var values = ReadNumbers(source, GamebryoCoordinate.SpatialDimensions, label);
        return new Vector3(values[0], values[1], values[2]);
    }

    private static string RequireSha256(JsonElement source, string property)
    {
        var value = RequireText(source, property);
        try
        {
            if (Convert.FromHexString(value).Length == SHA256.HashSizeInBytes)
                return value.ToLowerInvariant();
        }
        catch (FormatException)
        {
        }
        throw new InvalidOperationException(
            $"Gallery retail evidence {property} is not SHA-256.");
    }

    private static PrivateFile VerifyDescriptor(JsonElement source, string label)
    {
        var path = VerifiedGltfLoader.ResolvePath(RequireText(source, "path"));
        var expectedBytes = source.GetProperty("bytes").GetInt64();
        var expectedSha256 = RequireText(source, "sha256");
        var information = new FileInfo(path);
        if (!information.Exists || information.Length != expectedBytes)
            throw new InvalidOperationException(
                $"{label} byte contract changed: {path}");
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{label} hash changed: {path}");
        return new PrivateFile(path, expectedBytes, actualSha256);
    }

    private static string RequireText(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Gallery retail evidence {property} is empty.");
        return value;
    }

    private static string Normalize(JsonElement source, string property) =>
        Normalize(RequireText(source, property));

    private static string Normalize(string value) => FalloutFormId.Normalize(value);

    internal sealed record Contract(
        PrivateFile Evidence,
        PrivateFile Report,
        PrivateFile OracleJsonl,
        IReadOnlyList<PrivateFile> SourceFrames,
        string RuntimePluginStackEventSha256,
        PresentationReference Presentation,
        ActorReviewContract.EnvironmentState? Environment,
        uint EffectiveWeatherForm);

    internal sealed record PresentationReference(
        string ShotKind,
        int Frame,
        PrivateFile SourceFrame,
        string CameraEventSha256,
        string SourceFrameCameraContractEventSha256,
        string ActorSnapshotEventSha256,
        string ActorPoseEventSha256,
        Vector3 CameraWorldTranslationGameUnits,
        Vector3 CameraOffsetGameUnits,
        Basis CameraBasis,
        float CameraScale,
        float FovYRadians,
        ActorReviewContract.FrustumState Frustum,
        IReadOnlyList<float> Viewport,
        IReadOnlyList<float> ViewMatrix,
        IReadOnlyList<float> ProjectionMatrix,
        ActorReference Actor,
        PresentationSelectionProof Selection,
        string Derivation);

    internal sealed record PresentationSelectionProof(
        string FocusKind,
        int? FocusRuleOrdinal,
        double CameraDirectionDotFocusForward,
        string SurfaceStatus,
        bool SemanticFocusSurface,
        bool CameraOutsideActorWorldBound,
        bool CameraCorridorPassed);

    internal sealed record ActorReference(
        Vector3 WorldTranslationGameUnits,
        Basis WorldBasis,
        float WorldScale,
        bool WeaponOut,
        uint WeaponForm,
        IReadOnlyList<AnimationSequence> AnimationSequences);

    internal sealed record AnimationSequence(
        string File,
        int State,
        int Cycle,
        float Weight,
        float Frequency,
        float LastScaledSeconds,
        int Group);

    internal readonly record struct PrivateFile(
        string Path,
        long Bytes,
        string Sha256);
}
