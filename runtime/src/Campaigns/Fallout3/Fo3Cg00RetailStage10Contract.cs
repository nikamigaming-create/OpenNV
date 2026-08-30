using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg00RetailNiTransform(
    IReadOnlyList<float> RotationRowMajor,
    Vector3 TranslationGameUnits,
    float Scale);

internal sealed record Fo3Cg00RetailControllerSequence(
    string Name,
    uint CycleType,
    float Frequency,
    float BeginTimeSeconds,
    float EndTimeSeconds,
    float LastTimeSeconds,
    float LastScaledTimeSeconds,
    uint State);

internal sealed record Fo3Cg00RetailPackageIdleJoin(
    string Role,
    string PackageFormId,
    string IdleFormId,
    string SequenceName,
    int? ActivationStage);

internal sealed record Fo3Cg00RetailParticipant(
    string Role,
    string ReferenceFormId,
    bool Visible,
    bool AppCulled,
    Fo3Cg00RetailNiTransform RenderedWorldTransform,
    Fo3Cg00RetailControllerSequence Section01Sequence,
    Vector3 CameraLocalGameUnits,
    float RenderedRootDepthGameUnits,
    float RenderedRootNearPlaneSeparationGameUnits);

internal sealed record Fo3Cg00RetailCamera(
    Fo3Cg00RetailNiTransform WorldTransform,
    float Left,
    float Right,
    float Top,
    float Bottom,
    float NearGameUnits,
    float FarGameUnits,
    float HorizontalFovDegrees,
    float VerticalFovDegrees,
    IReadOnlyList<float> ViewportNormalized,
    IReadOnlyList<float> DerivedWorldToClipRowMajor);

internal sealed record Fo3Cg00RetailStage10Contract(
    string ContractPath,
    string ContractSha256,
    DateTimeOffset CapturedUtc,
    Fo3Cg00RetailCamera ActiveCamera,
    Fo3Cg00RetailNiTransform Camera1stWorldTransform,
    IReadOnlyDictionary<string, Fo3Cg00RetailPackageIdleJoin> PackageIdleJoins,
    IReadOnlyDictionary<string, Fo3Cg00RetailParticipant> Participants,
    string RawObservationPath,
    string RawObservationSha256)
{
    internal const string ExpectedSchema =
        "opennv.fo3-retail-cg00-stage10-camera-contract/v1";
    internal const string ExpectedProductionClassification =
        "private-exact-live-stage10-camera-and-participant-contract-not-pixel-parity";
    internal const string SyntheticFixtureClassification =
        "synthetic-parser-test-only-not-retail-evidence";
    internal const string ExpectedTargetVersion = "1.7.0.4";
    internal static string ExpectedTargetSha256 =>
        Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Prefix +
        Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Suffix;
    internal static string ExpectedObserverSha256 =>
        ExpectedObserverSha256Prefix + ExpectedObserverSha256Suffix;
    internal const string ExpectedQuestEditorId = "CG00";
    internal const int ExpectedStage = 10;
    internal const string ExpectedSourceUnits = "Gamebryo game units";
    internal const string ExpectedMatrixStorage = "row-major-3x3";
    internal const string ExpectedWorldToLocal =
        "transpose(worldRotation)*(worldPoint-worldTranslation)/worldScale";
    internal const string ExpectedCameraForwardAxis = "local-positive-X";

    private const string ExpectedObserverMode = "observe";
    private const string ExpectedObserverSha256Prefix =
        "10070829e620ae2e1e26d338a38bc4dc";
    private const string ExpectedObserverSha256Suffix =
        "b21d8c855f1fa3d846e03f71b812cc41";
    internal const string PlayerReferenceLocalFormId = "14";
    private const int ExpectedObserverToolSurface = 8;
    private const int TransformMatrixValues = 9;
    private const int ProjectionMatrixValues = 16;
    private const int ViewportValues = 4;
    private const float MinimumNonSingularScale = 1.0e-6f;
    private const float MinimumPerspectiveDegrees = 0.0f;
    private const float MaximumPerspectiveDegrees = 180.0f;
    private const float ContractFloatTolerance = 1.0e-4f;
    internal const float ProjectionAspectTolerance = 1.0e-3f;

    private static readonly IReadOnlyDictionary<string, ExpectedParticipant> ExpectedParticipants =
        new Dictionary<string, ExpectedParticipant>(StringComparer.Ordinal)
        {
            ["player"] = new(
                "SpecialIdle_CG00PlayerSection01",
                null),
            ["father"] = new(
                "SpecialIdle_CG00DadSection01",
                ExpectedStage),
            ["doctor"] = new(
                "SpecialIdle_CG00DrLiSection01",
                ExpectedStage),
            ["mother"] = new(
                "SpecialIdle_CG00MomSection01",
                ExpectedStage),
        };

    internal static Fo3Cg00RetailStage10Contract Load(string path) =>
        LoadCore(path, allowSyntheticFixture: false);

    internal static Fo3Cg00RetailStage10Contract LoadSyntheticFixture(string path) =>
        LoadCore(path, allowSyntheticFixture: true);

    private static Fo3Cg00RetailStage10Contract LoadCore(
        string path,
        bool allowSyntheticFixture)
    {
        var resolved = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(resolved);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        RequireExactProperties(
            root,
            "root",
            "schema", "classification", "captured_utc", "target", "observer",
            "stage_identity", "active_camera", "camera1st", "participants",
            "coordinate_contract", "unimplemented_boundary", "raw_observation");
        if (RequiredString(root, "schema") != ExpectedSchema)
            throw new InvalidOperationException("Fallout 3 stage-10 retail contract schema differs.");
        var classification = RequiredString(root, "classification");
        var synthetic = classification == SyntheticFixtureClassification;
        if ((!synthetic && classification != ExpectedProductionClassification) ||
            (synthetic && !allowSyntheticFixture))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 retail contract authority is not production evidence.");

        if (!DateTimeOffset.TryParse(
                RequiredString(root, "captured_utc"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var capturedUtc) ||
            capturedUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 retail capture timestamp is not UTC.");

        var target = RequiredObject(root, "target");
        RequireExactProperties(target, "target", "path", "version", "sha256");
        var targetPath = RequiredString(target, "path");
        var targetSha256 = RequiredSha256(target, "sha256");
        if (RequiredString(target, "version") != ExpectedTargetVersion ||
            targetSha256 != ExpectedTargetSha256)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 retail target executable identity differs.");

        var observer = RequiredObject(root, "observer");
        RequireExactProperties(
            observer,
            "observer",
            "path", "sha256", "required_tool_surface", "required_mode");
        var observerPath = RequiredString(observer, "path");
        var observerSha256 = RequiredSha256(observer, "sha256");
        if (observerSha256 != ExpectedObserverSha256 ||
            RequiredInteger(observer, "required_tool_surface") != ExpectedObserverToolSurface ||
            RequiredString(observer, "required_mode") != ExpectedObserverMode)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 observer identity differs.");

        var stage = RequiredObject(root, "stage_identity");
        RequireExactProperties(
            stage,
            "stage_identity",
            "quest", "stage", "proof", "owned_package_idle_joins");
        if (RequiredString(stage, "quest") != ExpectedQuestEditorId ||
            RequiredInteger(stage, "stage") != ExpectedStage ||
            string.IsNullOrWhiteSpace(RequiredString(stage, "proof")))
            throw new InvalidOperationException("Fallout 3 stage-10 identity differs.");
        var joins = ReadPackageIdleJoins(RequiredObject(stage, "owned_package_idle_joins"));

        var activeCamera = ReadCamera(RequiredObject(root, "active_camera"));
        var camera1st = ReadCamera1st(RequiredObject(root, "camera1st"));
        var participants = ReadParticipants(
            RequiredObject(root, "participants"),
            joins,
            activeCamera);

        var coordinates = RequiredObject(root, "coordinate_contract");
        RequireExactProperties(
            coordinates,
            "coordinate_contract",
            "source_units", "matrix_storage", "world_to_local", "camera_forward_axis",
            "evidence");
        if (RequiredString(coordinates, "source_units") != ExpectedSourceUnits ||
            RequiredString(coordinates, "matrix_storage") != ExpectedMatrixStorage ||
            RequiredString(coordinates, "world_to_local") != ExpectedWorldToLocal ||
            RequiredString(coordinates, "camera_forward_axis") != ExpectedCameraForwardAxis ||
            string.IsNullOrWhiteSpace(RequiredString(coordinates, "evidence")))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 coordinate contract differs.");
        if (string.IsNullOrWhiteSpace(RequiredString(root, "unimplemented_boundary")))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 contract omits its explicit unimplemented boundary.");

        var raw = RequiredObject(root, "raw_observation");
        RequireExactProperties(raw, "raw_observation", "path", "sha256");
        var rawPath = RequiredString(raw, "path");
        var rawSha256 = RequiredSha256(raw, "sha256");
        if (!synthetic)
        {
            VerifyFile(targetPath, targetSha256, "target executable");
            if (FileVersionInfo.GetVersionInfo(Path.GetFullPath(targetPath)).FileVersion !=
                ExpectedTargetVersion)
                throw new InvalidOperationException(
                    "Fallout 3 stage-10 target file version differs.");
            VerifyFile(observerPath, observerSha256, "observer");
            VerifyFile(rawPath, rawSha256, "raw observation");
            VerifyRawObservationEnvelope(rawPath);
        }

        return new Fo3Cg00RetailStage10Contract(
            resolved,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            capturedUtc,
            activeCamera,
            camera1st,
            joins,
            participants,
            rawPath,
            rawSha256);
    }

    private static IReadOnlyDictionary<string, Fo3Cg00RetailPackageIdleJoin>
        ReadPackageIdleJoins(JsonElement source)
    {
        RequireExactProperties(source, "owned_package_idle_joins", ExpectedParticipants.Keys);
        var result = new Dictionary<string, Fo3Cg00RetailPackageIdleJoin>(StringComparer.Ordinal);
        foreach (var expected in ExpectedParticipants)
        {
            var row = RequiredObject(source, expected.Key);
            RequireExactProperties(
                row,
                $"owned_package_idle_joins.{expected.Key}",
                "package_form_id", "idle_form_id", "sequence_name", "activation_stage");
            var activationStage = OptionalInteger(row, "activation_stage");
            var value = new Fo3Cg00RetailPackageIdleJoin(
                expected.Key,
                RequiredFormId(row, "package_form_id"),
                RequiredFormId(row, "idle_form_id"),
                RequiredString(row, "sequence_name"),
                activationStage);
            if (value.SequenceName != expected.Value.SequenceName ||
                value.ActivationStage != expected.Value.ActivationStage)
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {expected.Key} PACK/IDLE join differs.");
            result.Add(expected.Key, value);
        }
        return result;
    }

    private static Fo3Cg00RetailCamera ReadCamera(JsonElement source)
    {
        RequireExactProperties(
            source,
            "active_camera",
            "bs_scene_graph_address", "camera_address", "vtable", "world_transform",
            "frustum", "viewport_normalized", "derived_world_to_clip_row_major");
        RequiredPositiveAddress(source, "bs_scene_graph_address");
        RequiredPositiveAddress(source, "camera_address");
        RequiredPositiveAddress(source, "vtable");
        var world = ReadTransform(RequiredObject(source, "world_transform"), "active_camera");
        var frustum = RequiredObject(source, "frustum");
        RequireExactProperties(
            frustum,
            "active_camera.frustum",
            "left", "right", "top", "bottom", "near_game_units", "far_game_units",
            "orthographic", "horizontal_fov_degrees", "vertical_fov_degrees");
        var left = RequiredFiniteSingle(frustum, "left");
        var right = RequiredFiniteSingle(frustum, "right");
        var top = RequiredFiniteSingle(frustum, "top");
        var bottom = RequiredFiniteSingle(frustum, "bottom");
        var near = RequiredPositiveSingle(frustum, "near_game_units");
        var far = RequiredPositiveSingle(frustum, "far_game_units");
        if (RequiredBoolean(frustum, "orthographic") || far <= near ||
            left >= right || bottom >= top)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 camera is not a finite perspective frustum.");
        var horizontalFov = RequiredPerspectiveDegrees(frustum, "horizontal_fov_degrees");
        var verticalFov = RequiredPerspectiveDegrees(frustum, "vertical_fov_degrees");
        var derivedHorizontalFov = Mathf.RadToDeg(
            Mathf.Atan(right / near) - Mathf.Atan(left / near));
        var derivedVerticalFov = Mathf.RadToDeg(
            Mathf.Atan(top / near) - Mathf.Atan(bottom / near));
        if (!Approximately(horizontalFov, derivedHorizontalFov, ContractFloatTolerance) ||
            !Approximately(verticalFov, derivedVerticalFov, ContractFloatTolerance))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 camera FOV differs from its exact frustum.");
        var viewport = ReadFiniteArray(
            RequiredArray(source, "viewport_normalized"),
            ViewportValues,
            "active_camera.viewport_normalized");
        var projection = ReadFiniteArray(
            RequiredArray(source, "derived_world_to_clip_row_major"),
            ProjectionMatrixValues,
            "active_camera.derived_world_to_clip_row_major");
        if (viewport.Any(value => value < 0.0f || value > 1.0f) ||
            viewport[0] >= viewport[1] || viewport[3] >= viewport[2] ||
            projection.All(Mathf.IsZeroApprox))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 camera viewport or derived projection is invalid.");
        return new Fo3Cg00RetailCamera(
            world,
            left,
            right,
            top,
            bottom,
            near,
            far,
            horizontalFov,
            verticalFov,
            viewport,
            projection);
    }

    private static Fo3Cg00RetailNiTransform ReadCamera1st(JsonElement source)
    {
        RequireExactProperties(
            source,
            "camera1st",
            "address", "vtable", "name_address", "name", "parent", "controller", "flags",
            "app_culled", "visible", "local_transform", "world_transform");
        RequiredPositiveAddress(source, "address");
        RequiredPositiveAddress(source, "vtable");
        RequiredPositiveAddress(source, "name_address");
        RequiredPositiveAddress(source, "parent");
        RequiredUnsignedInteger(source, "controller");
        if (RequiredString(source, "name") != "Camera1st" ||
            RequiredBoolean(source, "app_culled") ||
            !RequiredBoolean(source, "visible"))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 Camera1st is absent or culled.");
        RequiredUnsignedInteger(source, "flags");
        ReadTransform(RequiredObject(source, "local_transform"), "camera1st.local");
        return ReadTransform(RequiredObject(source, "world_transform"), "camera1st.world");
    }

    private static IReadOnlyDictionary<string, Fo3Cg00RetailParticipant> ReadParticipants(
        JsonElement source,
        IReadOnlyDictionary<string, Fo3Cg00RetailPackageIdleJoin> joins,
        Fo3Cg00RetailCamera camera)
    {
        RequireExactProperties(source, "participants", ExpectedParticipants.Keys);
        var result = new Dictionary<string, Fo3Cg00RetailParticipant>(StringComparer.Ordinal);
        foreach (var expected in ExpectedParticipants)
        {
            var row = RequiredObject(source, expected.Key);
            RequireExactProperties(
                row,
                $"participants.{expected.Key}",
                "reference_form_id", "live_reference_address", "rendered_node_address",
                "visible", "app_culled", "rendered_world_transform", "section01_sequence",
                "camera_space");
            RequiredPositiveAddress(row, "live_reference_address");
            RequiredPositiveAddress(row, "rendered_node_address");
            var visible = RequiredBoolean(row, "visible");
            var culled = RequiredBoolean(row, "app_culled");
            if (!visible || culled)
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {expected.Key} is not visibly published.");
            var referenceFormId = RequiredFormId(row, "reference_form_id");
            var world = ReadTransform(
                RequiredObject(row, "rendered_world_transform"),
                $"participants.{expected.Key}.rendered_world_transform");
            var sequence = ReadControllerSequence(
                RequiredObject(row, "section01_sequence"),
                expected.Value.SequenceName,
                $"participants.{expected.Key}.section01_sequence");
            if (sequence.Name != joins[expected.Key].SequenceName)
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {expected.Key} controller/PACK join differs.");
            var cameraSpace = RequiredObject(row, "camera_space");
            RequireExactProperties(
                cameraSpace,
                $"participants.{expected.Key}.camera_space",
                "rendered_root_game_units", "camera_local_game_units", "forward_axis",
                "rendered_root_depth_game_units",
                "rendered_root_near_plane_separation_game_units", "limitation");
            var renderedRoot = ReadVector3(cameraSpace, "rendered_root_game_units");
            var local = ReadVector3(cameraSpace, "camera_local_game_units");
            var depth = RequiredFiniteSingle(cameraSpace, "rendered_root_depth_game_units");
            var separation = RequiredFiniteSingle(
                cameraSpace,
                "rendered_root_near_plane_separation_game_units");
            if (RequiredString(cameraSpace, "forward_axis") != ExpectedCameraForwardAxis ||
                string.IsNullOrWhiteSpace(RequiredString(cameraSpace, "limitation")) ||
                !renderedRoot.IsEqualApprox(world.TranslationGameUnits) ||
                !Approximately(depth, local.X, ContractFloatTolerance) ||
                !Approximately(
                    separation,
                    depth - camera.NearGameUnits,
                    ContractFloatTolerance) ||
                !Approximately(
                    CameraLocal(camera.WorldTransform, renderedRoot),
                    local,
                    ContractFloatTolerance))
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {expected.Key} camera-space root join differs.");
            result.Add(
                expected.Key,
                new Fo3Cg00RetailParticipant(
                    expected.Key,
                    referenceFormId,
                    visible,
                    culled,
                    world,
                    sequence,
                    local,
                    depth,
                    separation));
        }
        return result;
    }

    private static Fo3Cg00RetailControllerSequence ReadControllerSequence(
        JsonElement source,
        string expectedName,
        string label)
    {
        RequireExactProperties(
            source,
            label,
            "address", "name", "name_address", "cycle_type", "frequency",
            "begin_time_seconds", "end_time_seconds", "last_time_seconds",
            "last_scaled_time_seconds", "state", "accumulation_root",
            "actor_node_ancestry_join");
        RequiredPositiveAddress(source, "address");
        RequiredPositiveAddress(source, "name_address");
        RequiredPositiveAddress(source, "accumulation_root");
        if (source.GetProperty("actor_node_ancestry_join").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException(
                $"Fallout 3 stage-10 {label} contains an unexpected preselected actor join.");
        var name = RequiredString(source, "name");
        var cycle = RequiredUnsignedInteger(source, "cycle_type");
        var frequency = RequiredPositiveSingle(source, "frequency");
        var begin = RequiredFiniteSingle(source, "begin_time_seconds");
        var end = RequiredFiniteSingle(source, "end_time_seconds");
        var last = RequiredFiniteSingle(source, "last_time_seconds");
        var lastScaled = RequiredFiniteSingle(source, "last_scaled_time_seconds");
        var state = RequiredUnsignedInteger(source, "state");
        if (name != expectedName || cycle > 2 || end < begin || state == 0 ||
            lastScaled < begin - ContractFloatTolerance ||
            lastScaled > end + ContractFloatTolerance)
            throw new InvalidOperationException(
                $"Fallout 3 stage-10 {label} controller phase is unsupported.");
        return new Fo3Cg00RetailControllerSequence(
            name,
            cycle,
            frequency,
            begin,
            end,
            last,
            lastScaled,
            state);
    }

    private static Fo3Cg00RetailNiTransform ReadTransform(JsonElement source, string label)
    {
        RequireExactProperties(
            source,
            label,
            "rotation_row_major", "translation_game_units", "scale");
        return new Fo3Cg00RetailNiTransform(
            ReadFiniteArray(
                RequiredArray(source, "rotation_row_major"),
                TransformMatrixValues,
                $"{label}.rotation_row_major"),
            ReadVector3(source, "translation_game_units"),
            RequiredScale(source, "scale"));
    }

    private static Vector3 CameraLocal(
        Fo3Cg00RetailNiTransform camera,
        Vector3 worldPoint)
    {
        var delta = worldPoint - camera.TranslationGameUnits;
        var rotation = camera.RotationRowMajor;
        return new Vector3(
            (rotation[0] * delta.X +
                rotation[GamebryoCoordinate.SpatialDimensions] * delta.Y +
                rotation[GamebryoCoordinate.SpatialDimensions * 2] * delta.Z) /
                camera.Scale,
            (rotation[1] * delta.X +
                rotation[GamebryoCoordinate.SpatialDimensions + 1] * delta.Y +
                rotation[GamebryoCoordinate.SpatialDimensions * 2 + 1] * delta.Z) /
                camera.Scale,
            (rotation[2] * delta.X +
                rotation[GamebryoCoordinate.SpatialDimensions + 2] * delta.Y +
                rotation[GamebryoCoordinate.SpatialDimensions * 2 + 2] * delta.Z) /
                camera.Scale);
    }

    private static bool Approximately(float left, float right, float tolerance) =>
        MathF.Abs(left - right) <= tolerance;

    private static bool Approximately(Vector3 left, Vector3 right, float tolerance) =>
        (left - right).Length() <= tolerance;

    private static void VerifyFile(string path, string expectedSha256, string label)
    {
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
            throw new InvalidOperationException($"Fallout 3 stage-10 {label} is absent.");
        using var stream = File.OpenRead(resolved);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actual != expectedSha256)
            throw new InvalidOperationException($"Fallout 3 stage-10 {label} hash differs.");
    }

    private static void VerifyRawObservationEnvelope(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(path)));
        var root = document.RootElement;
        if (RequiredString(root, "schema") != "opennv.fo3-retail-raw-observation.v4" ||
            RequiredString(root, "classification") !=
                "private-raw-candidate-evidence-not-a-runtime-contract")
            throw new InvalidOperationException(
                "Fallout 3 stage-10 raw observation envelope differs.");
        var identity = RequiredObject(root, "identity");
        var game = RequiredObject(identity, "game");
        var observer = RequiredObject(identity, "observer");
        var stage10 = RequiredObject(root, "stage10_resolution");
        if (RequiredSha256(game, "sha256") != ExpectedTargetSha256 ||
            RequiredString(game, "version") != ExpectedTargetVersion ||
            RequiredSha256(observer, "sha256") != ExpectedObserverSha256 ||
            RequiredString(stage10, "classification") !=
                "exact-live-stage10-camera-participant-contract-ready" ||
            RequiredArray(stage10, "promotion_failures").GetArrayLength() != 0)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 raw observation promotion envelope differs.");
    }

    private static void RequireExactProperties(
        JsonElement source,
        string label,
        params string[] names) =>
        RequireExactProperties(source, label, (IEnumerable<string>)names);

    private static void RequireExactProperties(
        JsonElement source,
        string label,
        IEnumerable<string> names)
    {
        if (source.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 stage-10 {label} is not an object.");
        var expected = names.ToHashSet(StringComparer.Ordinal);
        var actual = source.EnumerateObject().Select(value => value.Name).ToArray();
        if (actual.Length != expected.Count || actual.Any(value => !expected.Contains(value)))
            throw new InvalidOperationException(
                $"Fallout 3 stage-10 {label} fields differ from the strict schema.");
    }

    private static JsonElement RequiredObject(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not an object.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not an array.");
        return value;
    }

    private static string RequiredString(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        return value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException(
                $"Fallout 3 stage-10 {name} is not a nonempty string.");
    }

    private static string RequiredSha256(JsonElement source, string name)
    {
        var value = RequiredString(source, name).ToLowerInvariant();
        if (value.Length != Fo3OpeningFlowNumericContracts.Sha256HexCharacters ||
            value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not SHA-256.");
        return value;
    }

    private static string RequiredFormId(JsonElement source, string name) =>
        FalloutFormId.Normalize(RequiredString(source, name));

    private static bool RequiredBoolean(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not Boolean.");
    }

    private static int RequiredInteger(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not an integer.");
    }

    private static int? OptionalInteger(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not nullable integer.");
    }

    private static ulong RequiredPositiveAddress(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out var result) ||
            result == 0)
            throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not an address.");
        return result;
    }

    private static uint RequiredUnsignedInteger(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var result)
            ? result
            : throw new InvalidOperationException(
                $"Fallout 3 stage-10 {name} is not an unsigned integer.");
    }

    private static float RequiredFiniteSingle(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var result) ||
            !float.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not finite.");
        return result;
    }

    private static float RequiredPositiveSingle(JsonElement source, string name)
    {
        var value = RequiredFiniteSingle(source, name);
        if (value <= 0.0f)
            throw new InvalidOperationException($"Fallout 3 stage-10 {name} is not positive.");
        return value;
    }

    private static float RequiredScale(JsonElement source, string name)
    {
        var value = RequiredFiniteSingle(source, name);
        if (value < MinimumNonSingularScale)
            throw new InvalidOperationException($"Fallout 3 stage-10 {name} is singular.");
        return value;
    }

    private static float RequiredPerspectiveDegrees(JsonElement source, string name)
    {
        var value = RequiredFiniteSingle(source, name);
        if (value <= MinimumPerspectiveDegrees || value >= MaximumPerspectiveDegrees)
            throw new InvalidOperationException(
                $"Fallout 3 stage-10 {name} is not perspective degrees.");
        return value;
    }

    private static IReadOnlyList<float> ReadFiniteArray(
        JsonElement source,
        int expectedCount,
        string label)
    {
        var values = source.EnumerateArray().Select(value =>
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var result) ||
                !float.IsFinite(result))
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {label} contains a non-finite value.");
            return result;
        }).ToArray();
        if (values.Length != expectedCount)
            throw new InvalidOperationException(
                $"Fallout 3 stage-10 {label} has the wrong cardinality.");
        return values;
    }

    private static Vector3 ReadVector3(JsonElement source, string name)
    {
        var values = ReadFiniteArray(RequiredArray(source, name), 3, name);
        return new Vector3(values[0], values[1], values[2]);
    }

    private sealed record ExpectedParticipant(string SequenceName, int? ActivationStage);
}

internal sealed record Fo3Cg00RetailParticipantTelemetry(
    string Role,
    string ReferenceFormId,
    string PackageFormId,
    string IdleFormId,
    string SequenceName,
    float ObservedControllerPhaseSeconds,
    double PublishedControllerPhaseSeconds,
    double ControllerPhaseErrorSeconds,
    int PosedMeshVertices,
    float CameraDepthMinimumMeters,
    float CameraDepthMaximumMeters,
    float NearPlaneMeters,
    float MinimumNearPlaneSeparationMeters,
    int VerticesAtOrBehindNearPlane,
    bool FullMeshClearsNearPlane);

internal sealed record Fo3Cg00RetailStage10JoinTelemetry(
    string ContractPath,
    string ContractSha256,
    string CameraAuthority,
    string ActorPlacementAuthority,
    string ControllerAuthority,
    float CameraNearMeters,
    float CameraFarMeters,
    float CameraVerticalFovDegrees,
    IReadOnlyDictionary<string, Fo3Cg00RetailParticipantTelemetry> Participants,
    bool FullNearPlaneSeparation);

internal static class Fo3Cg00RetailStage10Join
{
    internal const string CameraAuthority = "private-live-NiCamera-world-transform-and-frustum";
    internal const string ActorPlacementAuthority =
        "private-live-rendered-node-world-transform-no-authored-marker-substitution";
    internal const string ControllerAuthority =
        "private-live-Section01-NiControllerSequence-last-scaled-time";

    private const string ExpectedActorRootTranslationDisposition =
        "owned-world-root-authoritative-zero-local-translation";
    private const float RuntimePhaseToleranceSeconds = 1.0e-4f;

    internal static Fo3Cg00RetailStage10JoinTelemetry ApplyAndMeasure(
        Fo3Cg00RetailStage10Contract contract,
        Fo3Cg00EarlyBirthSequence sourceSequence,
        Fo3Vault101BirthSceneCoverage coverage)
    {
        var actors = new Dictionary<string, CellActorLoader.PlacedActor>(StringComparer.Ordinal)
        {
            ["doctor"] = coverage.DoctorActor,
            ["father"] = coverage.DadActor,
            ["mother"] = coverage.MomActor,
        };
        ValidatePlayerControllerJoin(contract, sourceSequence);
        PublishCamera(contract, coverage);
        var telemetry = new Dictionary<string, Fo3Cg00RetailParticipantTelemetry>(
            StringComparer.Ordinal);
        foreach (var entry in actors)
        {
            var observed = contract.Participants[entry.Key];
            var sourceJoin = contract.PackageIdleJoins[entry.Key];
            var package = sourceSequence.PackageSections[entry.Key].Single(value =>
                value.Section == 1);
            ValidateSourceJoin(sourceJoin, observed, package);
            var actor = entry.Value;
            var sourceParticipant = sourceSequence.SceneParticipants[entry.Key];
            if (!actor.ReferenceFormId.Equals(
                    observed.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) ||
                !sourceParticipant.ReferenceFormId.Equals(
                    observed.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {entry.Key} runtime actor identity differs.");
            var animation = actor.Actor.LoadedAnimations.Single(value =>
                value.SequenceName.Equals(observed.Section01Sequence.Name, StringComparison.Ordinal) &&
                ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                    ActorModelSlice.NormalizeAnimationPath(package.AnimationLogicalPath),
                    StringComparison.OrdinalIgnoreCase) &&
                value.SourceSha256.Equals(
                    package.AnimationSha256,
                    StringComparison.OrdinalIgnoreCase));
            if (animation.CycleType != observed.Section01Sequence.CycleType ||
                animation.AccumulationRootTranslationDisposition !=
                    ExpectedActorRootTranslationDisposition ||
                MathF.Abs(
                    animation.StartSeconds -
                    observed.Section01Sequence.BeginTimeSeconds) >
                    RuntimePhaseToleranceSeconds ||
                MathF.Abs(
                    animation.StopSeconds -
                    observed.Section01Sequence.EndTimeSeconds) >
                    RuntimePhaseToleranceSeconds)
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {entry.Key} owned animation interval differs from retail.");

            foreach (var player in actor.Actor.LoadedAnimations
                         .Select(value => value.Player)
                         .Distinct())
                player.Stop();
            animation.Player.Play(animation.RuntimeName);
            animation.Player.Seek(observed.Section01Sequence.LastScaledTimeSeconds, update: true);
            var publishedPhase = animation.Player.CurrentAnimationPosition;
            var phaseError = Math.Abs(
                publishedPhase - observed.Section01Sequence.LastScaledTimeSeconds);
            if (animation.Player.CurrentAnimation.ToString() != animation.RuntimeName ||
                phaseError > RuntimePhaseToleranceSeconds)
                throw new InvalidOperationException(
                    $"Fallout 3 stage-10 {entry.Key} controller phase did not publish exactly.");

            actor.Placement.Transform = new Transform3D(
                GamebryoCoordinate.ConvertBasis(
                    observed.RenderedWorldTransform.RotationRowMajor,
                    observed.RenderedWorldTransform.Scale,
                    $"FO3 CG00 stage10 {entry.Key} rendered actor"),
                GamebryoCoordinate.ConvertVector(
                    observed.RenderedWorldTransform.TranslationGameUnits -
                    coverage.Contract.EntryPositionGameUnits));
            actor.Placement.Visible = observed.Visible;
            actor.Placement.SetMeta(
                "opennv_fo3_retail_stage10_contract_sha256",
                contract.ContractSha256);
            actor.Placement.SetMeta("opennv_fo3_retail_stage10_role", entry.Key);
            actor.Placement.SetMeta(
                "opennv_fo3_retail_stage10_controller_phase_seconds",
                observed.Section01Sequence.LastScaledTimeSeconds);

            var vertices = ActorModelSlice.PosedWorldVertices(
                actor.Actor,
                includeWeapons: false);
            var measured = MeasureNearPlane(
                vertices,
                coverage.Camera.GlobalTransform,
                coverage.Camera.Near);
            telemetry.Add(
                entry.Key,
                new Fo3Cg00RetailParticipantTelemetry(
                    entry.Key,
                    actor.ReferenceFormId,
                    sourceJoin.PackageFormId,
                    sourceJoin.IdleFormId,
                    sourceJoin.SequenceName,
                    observed.Section01Sequence.LastScaledTimeSeconds,
                    publishedPhase,
                    phaseError,
                    vertices.Count,
                    measured.MinimumDepthMeters,
                    measured.MaximumDepthMeters,
                    coverage.Camera.Near,
                    measured.MinimumNearPlaneSeparationMeters,
                    measured.VerticesAtOrBehindNearPlane,
                    measured.FullMeshClearsNearPlane));
        }

        var fullSeparation = telemetry.Values.All(value => value.FullMeshClearsNearPlane);
        return new Fo3Cg00RetailStage10JoinTelemetry(
            contract.ContractPath,
            contract.ContractSha256,
            CameraAuthority,
            ActorPlacementAuthority,
            ControllerAuthority,
            coverage.Camera.Near,
            coverage.Camera.Far,
            coverage.Camera.Fov,
            telemetry,
            fullSeparation);
    }

    internal static NearPlaneMeasurement MeasureNearPlane(
        IReadOnlyList<Vector3> posedWorldVertices,
        Transform3D cameraGlobalTransform,
        float nearPlaneMeters)
    {
        if (posedWorldVertices.Count == 0 ||
            posedWorldVertices.Any(value => !value.IsFinite()) ||
            !cameraGlobalTransform.IsFinite() ||
            !float.IsFinite(nearPlaneMeters) || nearPlaneMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 posed-mesh near-plane inputs are invalid.");
        var cameraInverse = cameraGlobalTransform.AffineInverse();
        var depths = posedWorldVertices.Select(value => -(cameraInverse * value).Z).ToArray();
        if (depths.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 posed-mesh camera depths are invalid.");
        var minimum = depths.Min();
        var maximum = depths.Max();
        var atOrBehind = depths.Count(value => value <= nearPlaneMeters);
        return new NearPlaneMeasurement(
            minimum,
            maximum,
            minimum - nearPlaneMeters,
            atOrBehind,
            atOrBehind == 0);
    }

    private static void PublishCamera(
        Fo3Cg00RetailStage10Contract contract,
        Fo3Vault101BirthSceneCoverage coverage)
    {
        var observed = contract.ActiveCamera;
        if (!Mathf.IsEqualApprox(observed.WorldTransform.Scale, 1.0f) ||
            !Mathf.IsZeroApprox(observed.Left + observed.Right) ||
            !Mathf.IsZeroApprox(observed.Top + observed.Bottom) ||
            !Mathf.IsZeroApprox(observed.ViewportNormalized[0]) ||
            !Mathf.IsEqualApprox(observed.ViewportNormalized[1], 1.0f) ||
            !Mathf.IsEqualApprox(observed.ViewportNormalized[2], 1.0f) ||
            !Mathf.IsZeroApprox(observed.ViewportNormalized[3]))
            throw new InvalidOperationException(
                "Fallout 3 stage-10 retail projection is not exactly publishable by Camera3D.");
        var viewportSize = coverage.Camera.GetViewport().GetVisibleRect().Size;
        var sourceAspect = (observed.Right - observed.Left) /
            (observed.Top - observed.Bottom);
        var runtimeAspect = viewportSize.X / viewportSize.Y;
        if (!float.IsFinite(runtimeAspect) || viewportSize.Y <= 0.0f ||
            MathF.Abs(sourceAspect - runtimeAspect) >
                Fo3Cg00RetailStage10Contract.ProjectionAspectTolerance)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 runtime viewport aspect differs from retail frustum.");
        var local = new Transform3D(
            GamebryoCoordinate.ConvertCameraBasis(
                observed.WorldTransform.RotationRowMajor,
                "FO3 CG00 retail stage10 active NiCamera"),
            GamebryoCoordinate.ConvertVector(
                observed.WorldTransform.TranslationGameUnits -
                coverage.Contract.EntryPositionGameUnits));
        var scaledWorld = coverage.CellRoot.GlobalTransform * local;
        var rigidBasis = scaledWorld.Basis.Orthonormalized();
        if (!rigidBasis.IsFinite() || rigidBasis.Determinant() <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 retail camera basis is invalid after conversion.");
        coverage.Camera.GlobalTransform = new Transform3D(rigidBasis, scaledWorld.Origin);
        coverage.Camera.Fov = observed.VerticalFovDegrees;
        coverage.Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        coverage.Camera.Near = observed.NearGameUnits * coverage.Contract.UnitsToMeters;
        coverage.Camera.Far = observed.FarGameUnits * coverage.Contract.UnitsToMeters;
        coverage.Camera.Current = true;
        coverage.Camera.SetMeta(
            "opennv_fo3_retail_stage10_contract_sha256",
            contract.ContractSha256);
        coverage.Camera.SetMeta("opennv_fo3_retail_stage10_camera_authority", CameraAuthority);
    }

    private static void ValidatePlayerControllerJoin(
        Fo3Cg00RetailStage10Contract contract,
        Fo3Cg00EarlyBirthSequence sourceSequence)
    {
        var observed = contract.Participants["player"];
        var join = contract.PackageIdleJoins["player"];
        var package = sourceSequence.PackageSections["player"].Single(value => value.Section == 1);
        ValidateSourceJoin(join, observed, package);
        if (observed.ReferenceFormId != FalloutFormId.Normalize(
                Fo3Cg00RetailStage10Contract.PlayerReferenceLocalFormId) ||
            sourceSequence.PlayerCamera.SequenceName != observed.Section01Sequence.Name ||
            sourceSequence.PlayerCamera.PackageFormId != join.PackageFormId ||
            sourceSequence.PlayerCamera.IdleFormId != join.IdleFormId)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 player Camera1st controller join differs.");
    }

    private static void ValidateSourceJoin(
        Fo3Cg00RetailPackageIdleJoin join,
        Fo3Cg00RetailParticipant observed,
        Fo3Cg00PackageSection package)
    {
        if (package.PackageFormId != join.PackageFormId ||
            package.IdleFormId != join.IdleFormId ||
            package.AnimationSequenceName != join.SequenceName ||
            package.AnimationCycleType != observed.Section01Sequence.CycleType ||
            package.ActivationCondition?.Stage != join.ActivationStage)
            throw new InvalidOperationException(
                $"Fallout 3 stage-10 {join.Role} owned source/controller join differs.");
    }

    internal sealed record NearPlaneMeasurement(
        float MinimumDepthMeters,
        float MaximumDepthMeters,
        float MinimumNearPlaneSeparationMeters,
        int VerticesAtOrBehindNearPlane,
        bool FullMeshClearsNearPlane);
}
