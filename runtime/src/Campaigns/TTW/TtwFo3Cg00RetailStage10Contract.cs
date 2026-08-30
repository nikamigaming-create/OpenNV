using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Campaigns.TTW;

internal sealed record TtwFo3Cg00Stage10Transform(
    IReadOnlyList<double> RotationRowMajor,
    IReadOnlyList<double> TranslationGameUnits,
    double Scale);

internal sealed record TtwFo3Cg00Stage10Camera(
    TtwFo3Cg00Stage10Transform WorldTransform,
    double LeftGameUnits,
    double RightGameUnits,
    double TopGameUnits,
    double BottomGameUnits,
    double NearGameUnits,
    double FarGameUnits,
    double HorizontalFovDegrees,
    double VerticalFovDegrees,
    IReadOnlyList<double> ViewportNormalized);

internal sealed record TtwFo3Cg00Stage10Participant(
    string Role,
    string FormKey,
    string RuntimeFormId,
    TtwFo3Cg00Stage10Transform RenderedWorldTransform,
    string PackageFormKey,
    string PackageRuntimeFormId,
    string IdleFormKey,
    string IdleRuntimeFormId,
    string SequenceName,
    double SequenceFrequency,
    double SequenceBeginTimeSeconds,
    double SequenceEndTimeSeconds,
    int SequenceCycleType,
    double LastScaledTimeSeconds,
    IReadOnlyList<double> CameraLocalGameUnits,
    double NearPlaneSeparationGameUnits);

internal sealed record TtwFo3Cg00RetailStage10Contract(
    string ContractPath,
    string ContractSha256,
    DateTimeOffset CapturedUtc,
    string TargetExecutableSha256,
    string SourceRoot,
    string SourceProfilePath,
    string SourceProfileSha256,
    string SourceNamespacePath,
    string SourceNamespaceSha256,
    string OpeningProfilePath,
    string OpeningProfileSha256,
    string PluginStackId,
    string SaveCompatibilityId,
    TtwFo3Cg00Stage10Camera ActiveCamera,
    TtwFo3Cg00Stage10Transform Camera1stWorldTransform,
    IReadOnlyDictionary<string, TtwFo3Cg00Stage10Participant> Participants,
    string RawObservationPath,
    string RawObservationSha256)
{
    internal const string ExpectedSchema =
        "opennv.ttw-fo3-retail-cg00-stage10-camera-contract/v1";
    internal const string ExpectedProductionClassification =
        "private-exact-live-ttw-stage10-camera-and-participant-contract-not-pixel-parity";
    internal const string SyntheticFixtureClassification =
        "synthetic-ttw-parser-test-only-not-retail-evidence";
    internal const string ExpectedTargetVersion = "1.4.0.525";
    internal const string ExpectedSourceProfileSchema = "opennv-ttw-profile/v1";
    internal const string ExpectedSourceProfileStatus =
        "validated-generated-plugin-profile";
    internal const string ExpectedSourceNamespaceSchema =
        "opennv-ttw-effective-source-namespace/v1";
    internal const string ExpectedSourceNamespaceStatus =
        "validated-neutral-effective-source-namespace";
    internal const string ExpectedOpeningProfileSchema =
        "opennv-ttw-fo3-opening-profile/v1";
    internal const string ExpectedOpeningProfileStatus =
        "transported-bounded-ttw-fo3-opening-command-contract";
    internal const string ExpectedRawObservationSchema =
        "opennv.ttw-fo3-retail-cg00-stage10-observation/v1";

    private const string ExpectedTargetSha256Prefix =
        "518c87f58a6c4d9826e9ef8fbb7f4213";
    private const string ExpectedTargetSha256Suffix =
        "882fa70822675610d45aea2464502a57";
    private const string ExpectedObserverSha256Prefix =
        "10070829e620ae2e1e26d338a38bc4dc";
    private const string ExpectedObserverSha256Suffix =
        "b21d8c855f1fa3d846e03f71b812cc41";
    private const string ExpectedObserverMode = "observe";
    private const string ExpectedQuestEditorId = "CG00";
    private const string ExpectedSourceUnits = "Gamebryo game units";
    private const string ExpectedMatrixStorage = "row-major-3x3";
    private const string ExpectedWorldToLocal =
        "transpose(worldRotation)*(worldPoint-worldTranslation)/worldScale";
    private const string ExpectedCameraForwardAxis = "local-positive-X";
    private const string ExpectedResolutionPolicy =
        "stable-origin-formkey-last-active-plugin-wins";
    private const int ExpectedStage = 10;
    private const int ExpectedObserverToolSurface = 1 << 3;
    private const int RotationValueCount = 9;
    private const int TranslationValueCount = 3;
    private const int ProjectionValueCount = 16;
    private const int ViewportValueCount = 4;
    private const int Sha256Characters = 64;
    private const int FormIdCharacters = sizeof(uint) * 2;
    private const double NumericTolerance = 1.0e-4;
    private const double MinimumScale = 1.0e-6;
    private const int RotationRowTwoColumnZeroIndex = RotationValueCount - 3;
    private const int RotationRowTwoColumnOneIndex = RotationValueCount - 2;
    private const int RotationRowTwoColumnTwoIndex = RotationValueCount - 1;

    private static string ExpectedTargetSha256 =>
        ExpectedTargetSha256Prefix + ExpectedTargetSha256Suffix;
    private static string ExpectedObserverSha256 =>
        ExpectedObserverSha256Prefix + ExpectedObserverSha256Suffix;

    private static readonly IReadOnlyDictionary<string, ExpectedParticipant> ExpectedParticipants =
        new Dictionary<string, ExpectedParticipant>(StringComparer.Ordinal)
        {
            ["player"] = new(
                null,
                "SpecialIdle_CG00PlayerSection01",
                null),
            ["father"] = new(
                "Fallout3.esm:0290a7",
                "SpecialIdle_CG00DadSection01",
                ExpectedStage),
            ["doctor"] = new(
                "Fallout3.esm:0290a5",
                "SpecialIdle_CG00DrLiSection01",
                ExpectedStage),
            ["mother"] = new(
                "Fallout3.esm:05ede0",
                "SpecialIdle_CG00MomSection01",
                ExpectedStage),
        };

    internal static TtwFo3Cg00RetailStage10Contract Load(string path) =>
        LoadCore(path, allowSyntheticFixture: false);

    internal static TtwFo3Cg00RetailStage10Contract LoadSyntheticFixture(string path) =>
        LoadCore(path, allowSyntheticFixture: true);

    private static TtwFo3Cg00RetailStage10Contract LoadCore(
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
            "ttw_identity", "stage_identity", "active_camera", "camera1st",
            "participants", "coordinate_contract", "unimplemented_boundary",
            "raw_observation");
        if (RequiredString(root, "schema") != ExpectedSchema)
            throw new InvalidOperationException("TTW CG00 stage-10 contract schema differs.");
        var classification = RequiredString(root, "classification");
        var synthetic = classification == SyntheticFixtureClassification;
        if ((!synthetic && classification != ExpectedProductionClassification) ||
            (synthetic && !allowSyntheticFixture))
            throw new InvalidOperationException(
                "TTW CG00 stage-10 evidence is not production authority.");
        if (!DateTimeOffset.TryParse(
                RequiredString(root, "captured_utc"),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var capturedUtc) || capturedUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("TTW CG00 stage-10 timestamp is not UTC.");

        var target = RequiredObject(root, "target");
        RequireExactProperties(target, "target", "path", "version", "sha256", "edition");
        var targetPath = RequiredString(target, "path");
        var targetSha256 = RequiredSha256(target, "sha256");
        if (RequiredString(target, "version") != ExpectedTargetVersion ||
            targetSha256 != ExpectedTargetSha256 ||
            RequiredString(target, "edition") != "TTW")
            throw new InvalidOperationException(
                "TTW CG00 stage-10 target must be the exact FalloutNV.exe build.");

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
            throw new InvalidOperationException("TTW CG00 stage-10 observer identity differs.");

        var identity = ReadIdentity(RequiredObject(root, "ttw_identity"));
        var packageJoins = ReadPackageIdleJoins(
            RequiredObject(root, "stage_identity"));
        var camera = ReadCamera(RequiredObject(root, "active_camera"));
        var camera1st = ReadCamera1st(RequiredObject(root, "camera1st"));
        var participants = ReadParticipants(
            RequiredObject(root, "participants"),
            packageJoins,
            camera);
        ValidateCoordinateContract(RequiredObject(root, "coordinate_contract"));
        if (string.IsNullOrWhiteSpace(RequiredString(root, "unimplemented_boundary")))
            throw new InvalidOperationException(
                "TTW CG00 stage-10 contract omits its unimplemented boundary.");
        var raw = RequiredObject(root, "raw_observation");
        RequireExactProperties(raw, "raw_observation", "path", "sha256");
        var rawPath = RequiredString(raw, "path");
        var rawSha256 = RequiredSha256(raw, "sha256");

        if (!synthetic)
        {
            VerifyFile(targetPath, targetSha256, "FalloutNV.exe");
            if (FileVersionInfo.GetVersionInfo(Path.GetFullPath(targetPath)).FileVersion !=
                ExpectedTargetVersion)
                throw new InvalidOperationException("TTW target executable version differs.");
            VerifyFile(observerPath, observerSha256, "observer");
            VerifyIdentityFiles(identity);
            VerifyFile(rawPath, rawSha256, "raw TTW observation");
            using var rawDocument = JsonDocument.Parse(File.ReadAllBytes(rawPath));
            if (RequiredString(rawDocument.RootElement, "schema") !=
                ExpectedRawObservationSchema)
                throw new InvalidOperationException("TTW raw observation schema differs.");
        }

        return new TtwFo3Cg00RetailStage10Contract(
            resolved,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            capturedUtc,
            targetSha256,
            identity.SourceRoot,
            identity.SourceProfilePath,
            identity.SourceProfileSha256,
            identity.SourceNamespacePath,
            identity.SourceNamespaceSha256,
            identity.OpeningProfilePath,
            identity.OpeningProfileSha256,
            identity.PluginStackId,
            identity.SaveCompatibilityId,
            camera,
            camera1st,
            participants,
            rawPath,
            rawSha256);
    }

    private static Identity ReadIdentity(JsonElement source)
    {
        RequireExactProperties(
            source,
            "ttw_identity",
            "source_root", "source_profile", "effective_source_namespace",
            "opening_profile", "plugin_stack_id", "save_compatibility_id",
            "record_resolution_policy");
        var sourceRoot = Path.GetFullPath(RequiredString(source, "source_root"));
        var pluginStackId = RequiredSha256(source, "plugin_stack_id");
        var saveCompatibilityId = RequiredString(source, "save_compatibility_id");
        if (saveCompatibilityId != $"ttw:{pluginStackId}" ||
            RequiredString(source, "record_resolution_policy") != ExpectedResolutionPolicy)
            throw new InvalidOperationException("TTW stage-10 source namespace identity differs.");
        var profile = ReadFileIdentity(
            RequiredObject(source, "source_profile"),
            "source_profile",
            ExpectedSourceProfileSchema,
            ExpectedSourceProfileStatus);
        var sourceNamespace = ReadFileIdentity(
            RequiredObject(source, "effective_source_namespace"),
            "effective_source_namespace",
            ExpectedSourceNamespaceSchema,
            ExpectedSourceNamespaceStatus);
        var opening = ReadFileIdentity(
            RequiredObject(source, "opening_profile"),
            "opening_profile",
            ExpectedOpeningProfileSchema,
            ExpectedOpeningProfileStatus);
        return new Identity(
            sourceRoot,
            profile.Path,
            profile.Sha256,
            sourceNamespace.Path,
            sourceNamespace.Sha256,
            opening.Path,
            opening.Sha256,
            pluginStackId,
            saveCompatibilityId);
    }

    private static FileIdentity ReadFileIdentity(
        JsonElement source,
        string label,
        string schema,
        string status)
    {
        RequireExactProperties(source, label, "path", "sha256", "schema", "status");
        if (RequiredString(source, "schema") != schema ||
            RequiredString(source, "status") != status)
            throw new InvalidOperationException($"TTW {label} schema/status differs.");
        return new FileIdentity(
            Path.GetFullPath(RequiredString(source, "path")),
            RequiredSha256(source, "sha256"));
    }

    private static IReadOnlyDictionary<string, PackageIdleJoin> ReadPackageIdleJoins(
        JsonElement stage)
    {
        RequireExactProperties(
            stage,
            "stage_identity",
            "quest", "stage", "proof", "owned_package_idle_joins");
        if (RequiredString(stage, "quest") != ExpectedQuestEditorId ||
            RequiredInteger(stage, "stage") != ExpectedStage ||
            string.IsNullOrWhiteSpace(RequiredString(stage, "proof")))
            throw new InvalidOperationException("TTW CG00 stage identity differs.");
        var source = RequiredObject(stage, "owned_package_idle_joins");
        RequireExactProperties(source, "owned_package_idle_joins", ExpectedParticipants.Keys);
        var result = new Dictionary<string, PackageIdleJoin>(StringComparer.Ordinal);
        foreach (var pair in ExpectedParticipants)
        {
            var row = RequiredObject(source, pair.Key);
            RequireExactProperties(
                row,
                $"owned_package_idle_joins.{pair.Key}",
                "package_form_key", "package_stable_local_form_id",
                "package_runtime_form_id", "idle_form_key", "idle_stable_local_form_id",
                "idle_runtime_form_id", "sequence_name", "activation_stage");
            var value = new PackageIdleJoin(
                RequiredFormKey(row, "package_form_key"),
                RequiredFormId(row, "package_stable_local_form_id"),
                RequiredFormId(row, "package_runtime_form_id"),
                RequiredFormKey(row, "idle_form_key"),
                RequiredFormId(row, "idle_stable_local_form_id"),
                RequiredFormId(row, "idle_runtime_form_id"),
                RequiredString(row, "sequence_name"),
                OptionalInteger(row, "activation_stage"));
            var expected = pair.Value;
            if (!FormKeyMatchesStableLocal(value.PackageFormKey, value.PackageStableLocalFormId) ||
                !FormKeyMatchesStableLocal(value.IdleFormKey, value.IdleStableLocalFormId) ||
                value.SequenceName != expected.SequenceName ||
                value.ActivationStage != expected.ActivationStage)
                throw new InvalidOperationException(
                    $"TTW CG00 stage-10 {pair.Key} PACK/IDLE effective identity differs.");
            result.Add(pair.Key, value);
        }
        return result;
    }

    private static TtwFo3Cg00Stage10Camera ReadCamera(JsonElement source)
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
        var left = RequiredFinite(frustum, "left");
        var right = RequiredFinite(frustum, "right");
        var top = RequiredFinite(frustum, "top");
        var bottom = RequiredFinite(frustum, "bottom");
        var near = RequiredPositive(frustum, "near_game_units");
        var far = RequiredPositive(frustum, "far_game_units");
        if (RequiredBoolean(frustum, "orthographic") || far <= near ||
            left >= right || bottom >= top)
            throw new InvalidOperationException("TTW stage-10 camera frustum is invalid.");
        var horizontalFov = RequiredPerspective(frustum, "horizontal_fov_degrees");
        var verticalFov = RequiredPerspective(frustum, "vertical_fov_degrees");
        var derivedHorizontal = RadiansToDegrees(Math.Atan(right / near) - Math.Atan(left / near));
        var derivedVertical = RadiansToDegrees(Math.Atan(top / near) - Math.Atan(bottom / near));
        if (!Approximately(horizontalFov, derivedHorizontal) ||
            !Approximately(verticalFov, derivedVertical))
            throw new InvalidOperationException("TTW stage-10 FOV differs from the frustum.");
        var viewport = ReadFiniteArray(
            RequiredArray(source, "viewport_normalized"),
            ViewportValueCount,
            "viewport");
        var projection = ReadFiniteArray(
            RequiredArray(source, "derived_world_to_clip_row_major"),
            ProjectionValueCount,
            "projection");
        if (viewport.Any(value => value < 0.0 || value > 1.0) ||
            viewport[0] >= viewport[1] || viewport[3] >= viewport[2] ||
            projection.All(value => Math.Abs(value) <= double.Epsilon))
            throw new InvalidOperationException("TTW stage-10 viewport/projection is invalid.");
        return new TtwFo3Cg00Stage10Camera(
            world,
            left,
            right,
            top,
            bottom,
            near,
            far,
            horizontalFov,
            verticalFov,
            viewport);
    }

    private static TtwFo3Cg00Stage10Transform ReadCamera1st(JsonElement source)
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
        RequiredUnsignedInteger(source, "flags");
        if (RequiredString(source, "name") != "Camera1st" ||
            RequiredBoolean(source, "app_culled") ||
            !RequiredBoolean(source, "visible"))
            throw new InvalidOperationException("TTW stage-10 Camera1st is absent or culled.");
        _ = ReadTransform(RequiredObject(source, "local_transform"), "camera1st.local");
        return ReadTransform(RequiredObject(source, "world_transform"), "camera1st.world");
    }

    private static IReadOnlyDictionary<string, TtwFo3Cg00Stage10Participant>
        ReadParticipants(
            JsonElement source,
            IReadOnlyDictionary<string, PackageIdleJoin> packageJoins,
            TtwFo3Cg00Stage10Camera camera)
    {
        RequireExactProperties(source, "participants", ExpectedParticipants.Keys);
        var result = new Dictionary<string, TtwFo3Cg00Stage10Participant>(StringComparer.Ordinal);
        foreach (var pair in ExpectedParticipants)
        {
            var row = RequiredObject(source, pair.Key);
            RequireExactProperties(
                row,
                $"participants.{pair.Key}",
                "reference_form_key", "stable_local_form_id", "runtime_form_id",
                "live_reference_address", "rendered_node_address", "visible", "app_culled",
                "rendered_world_transform", "section01_sequence", "camera_local_game_units",
                "rendered_root_depth_game_units",
                "rendered_root_near_plane_separation_game_units");
            var expected = pair.Value;
            var formKey = RequiredFormKey(row, "reference_form_key");
            var stableLocalFormId = RequiredFormId(row, "stable_local_form_id");
            var runtimeFormId = RequiredFormId(row, "runtime_form_id");
            if ((expected.FormKey is not null && formKey != expected.FormKey) ||
                !FormKeyMatchesStableLocal(formKey, stableLocalFormId) ||
                !RequiredBoolean(row, "visible") || RequiredBoolean(row, "app_culled"))
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} rendered reference identity differs.");
            RequiredPositiveAddress(row, "live_reference_address");
            RequiredPositiveAddress(row, "rendered_node_address");
            var transform = ReadTransform(
                RequiredObject(row, "rendered_world_transform"),
                $"participants.{pair.Key}.rendered_world_transform");
            var sequence = ReadControllerSequence(
                RequiredObject(row, "section01_sequence"),
                expected.SequenceName,
                pair.Key);
            var statedCameraLocal = ReadFiniteArray(
                RequiredArray(row, "camera_local_game_units"),
                TranslationValueCount,
                $"participants.{pair.Key}.camera_local_game_units");
            var derivedCameraLocal = WorldToLocal(
                camera.WorldTransform,
                transform.TranslationGameUnits);
            if (!VectorsApproximately(statedCameraLocal, derivedCameraLocal))
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} camera-local root differs.");
            var statedDepth = RequiredFinite(row, "rendered_root_depth_game_units");
            var statedSeparation = RequiredFinite(
                row,
                "rendered_root_near_plane_separation_game_units");
            if (!Approximately(statedDepth, derivedCameraLocal[0]) ||
                !Approximately(statedSeparation, statedDepth - camera.NearGameUnits))
                throw new InvalidOperationException(
                    $"TTW stage-10 {pair.Key} near-plane telemetry differs.");
            var package = packageJoins[pair.Key];
            result.Add(
                pair.Key,
                new TtwFo3Cg00Stage10Participant(
                    pair.Key,
                    formKey,
                    runtimeFormId,
                    transform,
                    package.PackageFormKey,
                    package.PackageRuntimeFormId,
                    package.IdleFormKey,
                    package.IdleRuntimeFormId,
                    sequence.Name,
                    sequence.Frequency,
                    sequence.BeginTimeSeconds,
                    sequence.EndTimeSeconds,
                    sequence.CycleType,
                    sequence.LastScaledTimeSeconds,
                    statedCameraLocal,
                    statedSeparation));
        }
        return result;
    }

    private static ControllerSequence ReadControllerSequence(
        JsonElement source,
        string expectedName,
        string role)
    {
        RequireExactProperties(
            source,
            $"participants.{role}.section01_sequence",
            "address", "name", "name_address", "cycle_type", "frequency",
            "begin_time_seconds", "end_time_seconds", "last_time_seconds",
            "last_scaled_time_seconds", "state", "accumulation_root",
            "actor_node_ancestry_join");
        RequiredPositiveAddress(source, "address");
        RequiredPositiveAddress(source, "name_address");
        RequiredUnsignedInteger(source, "accumulation_root");
        var name = RequiredString(source, "name");
        var cycleType = RequiredUnsignedInteger(source, "cycle_type");
        var frequency = RequiredPositive(source, "frequency");
        var begin = RequiredFinite(source, "begin_time_seconds");
        var end = RequiredFinite(source, "end_time_seconds");
        var last = RequiredFinite(source, "last_time_seconds");
        var lastScaled = RequiredFinite(source, "last_scaled_time_seconds");
        var state = RequiredUnsignedInteger(source, "state");
        if (name != expectedName || cycleType > 2 || end <= begin ||
            last < begin || last > end || lastScaled < begin || lastScaled > end || state == 0 ||
            source.GetProperty("actor_node_ancestry_join").ValueKind is
                not JsonValueKind.Null and not JsonValueKind.String)
            throw new InvalidOperationException(
                $"TTW stage-10 {role} controller sequence differs.");
        return new ControllerSequence(
            name,
            frequency,
            begin,
            end,
            checked((int)cycleType),
            lastScaled);
    }

    private static TtwFo3Cg00Stage10Transform ReadTransform(
        JsonElement source,
        string label)
    {
        RequireExactProperties(
            source,
            label,
            "rotation_row_major", "translation_game_units", "scale");
        var rotation = ReadFiniteArray(
            RequiredArray(source, "rotation_row_major"),
            RotationValueCount,
            $"{label}.rotation");
        var translation = ReadFiniteArray(
            RequiredArray(source, "translation_game_units"),
            TranslationValueCount,
            $"{label}.translation");
        var scale = RequiredPositive(source, "scale");
        if (scale < MinimumScale)
            throw new InvalidOperationException($"TTW {label} scale is singular.");
        return new TtwFo3Cg00Stage10Transform(rotation, translation, scale);
    }

    private static void ValidateCoordinateContract(JsonElement source)
    {
        RequireExactProperties(
            source,
            "coordinate_contract",
            "source_units", "matrix_storage", "world_to_local", "camera_forward_axis",
            "evidence");
        if (RequiredString(source, "source_units") != ExpectedSourceUnits ||
            RequiredString(source, "matrix_storage") != ExpectedMatrixStorage ||
            RequiredString(source, "world_to_local") != ExpectedWorldToLocal ||
            RequiredString(source, "camera_forward_axis") != ExpectedCameraForwardAxis ||
            string.IsNullOrWhiteSpace(RequiredString(source, "evidence")))
            throw new InvalidOperationException("TTW stage-10 coordinate contract differs.");
    }

    private static void VerifyIdentityFiles(Identity identity)
    {
        if (!Directory.Exists(identity.SourceRoot))
            throw new InvalidOperationException("TTW source root is missing.");
        VerifyFile(identity.SourceProfilePath, identity.SourceProfileSha256, "TTW source profile");
        VerifyFile(
            identity.SourceNamespacePath,
            identity.SourceNamespaceSha256,
            "TTW source namespace");
        VerifyFile(identity.OpeningProfilePath, identity.OpeningProfileSha256, "TTW opening profile");

        using var profileDocument = JsonDocument.Parse(File.ReadAllBytes(identity.SourceProfilePath));
        var profile = profileDocument.RootElement;
        if (RequiredString(profile, "schema") != ExpectedSourceProfileSchema ||
            RequiredString(profile, "status") != ExpectedSourceProfileStatus ||
            RequiredSha256(profile, "pluginStackId") != identity.PluginStackId ||
            RequiredString(profile, "saveCompatibilityId") != identity.SaveCompatibilityId ||
            !RequiredArray(profile, "sourceRoots").EnumerateArray().Any(value =>
                Path.GetFullPath(ValueString(value, "source root")).Equals(
                    identity.SourceRoot,
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("TTW source profile binding differs.");

        using var namespaceDocument = JsonDocument.Parse(
            File.ReadAllBytes(identity.SourceNamespacePath));
        var sourceNamespace = namespaceDocument.RootElement;
        var namespaceProfile = RequiredObject(sourceNamespace, "sourceProfile");
        if (RequiredString(sourceNamespace, "schema") != ExpectedSourceNamespaceSchema ||
            RequiredString(sourceNamespace, "status") != ExpectedSourceNamespaceStatus ||
            RequiredSha256(namespaceProfile, "sha256") != identity.SourceProfileSha256 ||
            RequiredSha256(namespaceProfile, "pluginStackId") != identity.PluginStackId ||
            RequiredString(namespaceProfile, "saveCompatibilityId") != identity.SaveCompatibilityId)
            throw new InvalidOperationException("TTW effective-source namespace binding differs.");

        using var openingDocument = JsonDocument.Parse(
            File.ReadAllBytes(identity.OpeningProfilePath));
        var opening = openingDocument.RootElement;
        var openingProfile = RequiredObject(opening, "sourceProfile");
        var openingNamespace = RequiredObject(opening, "sourceNamespace");
        if (RequiredString(opening, "schema") != ExpectedOpeningProfileSchema ||
            RequiredString(opening, "status") != ExpectedOpeningProfileStatus ||
            RequiredSha256(openingProfile, "sha256") != identity.SourceProfileSha256 ||
            RequiredSha256(openingProfile, "pluginStackId") != identity.PluginStackId ||
            RequiredString(openingProfile, "saveCompatibilityId") != identity.SaveCompatibilityId ||
            RequiredSha256(openingNamespace, "sha256") != identity.SourceNamespaceSha256)
            throw new InvalidOperationException("TTW opening-profile source binding differs.");
    }

    private static IReadOnlyList<double> WorldToLocal(
        TtwFo3Cg00Stage10Transform world,
        IReadOnlyList<double> point)
    {
        var deltaX = point[0] - world.TranslationGameUnits[0];
        var deltaY = point[1] - world.TranslationGameUnits[1];
        var deltaZ = point[2] - world.TranslationGameUnits[2];
        var rotation = world.RotationRowMajor;
        return new[]
        {
            (rotation[0] * deltaX + rotation[3] * deltaY +
                rotation[RotationRowTwoColumnZeroIndex] * deltaZ) /
                world.Scale,
            (rotation[1] * deltaX + rotation[4] * deltaY +
                rotation[RotationRowTwoColumnOneIndex] * deltaZ) /
                world.Scale,
            (rotation[2] * deltaX + rotation[RotationRowTwoColumnZeroIndex - 1] * deltaY +
                rotation[RotationRowTwoColumnTwoIndex] * deltaZ) /
                world.Scale,
        };
    }

    private static void VerifyFile(string path, string sha256, string label)
    {
        var resolved = Path.GetFullPath(path);
        if (!File.Exists(resolved))
            throw new InvalidOperationException($"TTW {label} is missing.");
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(resolved)))
            .ToLowerInvariant();
        if (actual != sha256)
            throw new InvalidOperationException($"TTW {label} hash differs.");
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
            throw new InvalidOperationException($"TTW {label} is not an object.");
        var expected = names.ToHashSet(StringComparer.Ordinal);
        var actual = source.EnumerateObject().Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual))
            throw new InvalidOperationException($"TTW {label} property set differs.");
    }

    private static JsonElement RequiredObject(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"TTW field {name} is not an object.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"TTW field {name} is not an array.");
        return value;
    }

    private static string RequiredString(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"TTW field {name} is absent.");
        return ValueString(value, name);
    }

    private static string ValueString(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"TTW {label} is not a non-empty string.");
        return value.GetString()!;
    }

    private static string RequiredSha256(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (value.Length != Sha256Characters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"TTW field {name} is not SHA-256.");
        return value.ToLowerInvariant();
    }

    private static string RequiredFormId(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (value.Length != FormIdCharacters || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"TTW field {name} is not a FormID.");
        return value.ToLowerInvariant();
    }

    private static string RequiredFormKey(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1 ||
            !value[..separator].EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
            value[(separator + 1)..].Length != FormIdCharacters - 2 ||
            value[(separator + 1)..].Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"TTW field {name} is not a stable FormKey.");
        return value;
    }

    private static bool FormKeyMatchesStableLocal(string formKey, string stableLocalFormId)
    {
        var local = formKey[(formKey.LastIndexOf(':') + 1)..];
        return stableLocalFormId.EndsWith(local, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiredBoolean(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"TTW field {name} is not Boolean.");
        return value.GetBoolean();
    }

    private static int RequiredInteger(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"TTW field {name} is not an integer.");
        return result;
    }

    private static int? OptionalInteger(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value))
            throw new InvalidOperationException($"TTW field {name} is absent.");
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetInt32(out var result))
            throw new InvalidOperationException($"TTW field {name} is not nullable integer.");
        return result;
    }

    private static ulong RequiredPositiveAddress(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) ||
            !value.TryGetUInt64(out var result) || result == 0)
            throw new InvalidOperationException($"TTW field {name} is not a positive address.");
        return result;
    }

    private static uint RequiredUnsignedInteger(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) || !value.TryGetUInt32(out var result))
            throw new InvalidOperationException($"TTW field {name} is not unsigned integer.");
        return result;
    }

    private static double RequiredFinite(JsonElement source, string name)
    {
        if (!source.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new InvalidOperationException($"TTW field {name} is not finite.");
        return result;
    }

    private static double RequiredPositive(JsonElement source, string name)
    {
        var value = RequiredFinite(source, name);
        if (value <= 0.0)
            throw new InvalidOperationException($"TTW field {name} is not positive.");
        return value;
    }

    private static double RequiredPerspective(JsonElement source, string name)
    {
        var value = RequiredFinite(source, name);
        if (value <= 0.0 || value >= double.RadiansToDegrees(Math.PI))
            throw new InvalidOperationException($"TTW field {name} is not perspective FOV.");
        return value;
    }

    private static IReadOnlyList<double> ReadFiniteArray(
        JsonElement source,
        int count,
        string label)
    {
        var values = source.EnumerateArray().Select(value =>
        {
            if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
                throw new InvalidOperationException($"TTW {label} contains a non-finite value.");
            return result;
        }).ToArray();
        if (values.Length != count)
            throw new InvalidOperationException($"TTW {label} length differs.");
        return values;
    }

    private static bool Approximately(double left, double right) =>
        Math.Abs(left - right) <= NumericTolerance;

    private static bool VectorsApproximately(
        IReadOnlyList<double> left,
        IReadOnlyList<double> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => Approximately(pair.First, pair.Second));

    private static double RadiansToDegrees(double radians) =>
        double.RadiansToDegrees(radians);

    private sealed record FileIdentity(string Path, string Sha256);

    private sealed record Identity(
        string SourceRoot,
        string SourceProfilePath,
        string SourceProfileSha256,
        string SourceNamespacePath,
        string SourceNamespaceSha256,
        string OpeningProfilePath,
        string OpeningProfileSha256,
        string PluginStackId,
        string SaveCompatibilityId);

    private sealed record ExpectedParticipant(
        string? FormKey,
        string SequenceName,
        int? ActivationStage);

    private sealed record PackageIdleJoin(
        string PackageFormKey,
        string PackageStableLocalFormId,
        string PackageRuntimeFormId,
        string IdleFormKey,
        string IdleStableLocalFormId,
        string IdleRuntimeFormId,
        string SequenceName,
        int? ActivationStage);

    private sealed record ControllerSequence(
        string Name,
        double Frequency,
        double BeginTimeSeconds,
        double EndTimeSeconds,
        int CycleType,
        double LastScaledTimeSeconds);
}
