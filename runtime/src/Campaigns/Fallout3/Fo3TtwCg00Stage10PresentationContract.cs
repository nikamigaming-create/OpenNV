using System.Security.Cryptography;
using System.Text.Json;
using Godot;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3TtwCg00Stage10Transform(
    IReadOnlyList<float> RotationRowMajor,
    Vector3 TranslationGameUnits,
    float Scale);

internal sealed record Fo3TtwCg00Stage10Camera(
    Fo3TtwCg00Stage10Transform WorldTransform,
    float LeftGameUnits,
    float RightGameUnits,
    float TopGameUnits,
    float BottomGameUnits,
    float NearGameUnits,
    float FarGameUnits,
    float VerticalFovDegrees,
    IReadOnlyList<float> Viewport);

internal sealed record Fo3TtwCg00Stage10Participant(
    string Role,
    string ReferenceFormKey,
    string RuntimeFormId,
    bool Visible,
    bool AppCulled,
    Fo3TtwCg00Stage10Transform RenderedWorldTransform,
    string PackageFormKey,
    string IdleFormKey,
    string SequenceName,
    string AnimationLogicalPath,
    float BeginSeconds,
    float EndSeconds,
    float LastScaledSeconds,
    float Frequency,
    int CycleType,
    int State);

internal sealed record Fo3TtwCg00Stage10PresentationContract(
    string Path,
    string Sha256,
    string RawObservationPath,
    string RawObservationSha256,
    string PluginStackId,
    string SaveCompatibilityId,
    Fo3TtwCg00Stage10Camera Camera,
    Fo3TtwCg00Stage10Transform Camera1stWorldTransform,
    IReadOnlyDictionary<string, Fo3TtwCg00Stage10Participant> Participants)
{
    internal const string ExpectedSchema =
        "opennv.fo3-ttw-oracle-cg00-stage10-presentation/v1";
    internal const string ExpectedClassification =
        "private-plan-driven-live-ttw-stage10-presentation-not-standalone-retail-parity";
    internal const string ExpectedSourcePlan =
        "exact-CG00-stage0-MoveTo-then-live-stage10-package-evaluation";
    internal const string ExpectedRuntime = "FalloutNV-1.4.0.525";
    internal const string ExpectedTargetSha256 =
        "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57";
    internal const string ExpectedObserverSha256 =
        "51746091059c5776de9d22d4a495aee455e0e646e07db61b2e8912c12a71d7d9";
    internal const string ExpectedSourceRoot = @"D:\TTW\Installed";
    internal const string ExpectedPlayerSectionDisposition =
        "not-live-under-plan-driven-stage10-shortcut;not-used-for-NPC-staging";
    internal const int ExpectedStage = 10;

    private const int RotationValueCount = 9;
    private const int TranslationValueCount = 3;
    private const int ProjectionValueCount = 16;
    private const int FrustumValueCount = 7;
    private const int ViewportValueCount = 4;
    private const int ExpectedFrame = 540;
    private const int NiAvObjectAppCulledFlag = 1;
    private const float MinimumScale = 1.0e-6f;
    private const float NumericTolerance = 1.0e-4f;
    private const float RadiansToDegrees = 180.0f / MathF.PI;

    private static readonly IReadOnlyList<string> ExpectedPluginOrder =
    [
        "FalloutNV.esm", "DeadMoney.esm", "HonestHearts.esm", "OldWorldBlues.esm",
        "LonesomeRoad.esm", "GunRunnersArsenal.esm", "Fallout3.esm", "Anchorage.esm",
        "ThePitt.esm", "BrokenSteel.esm", "PointLookout.esm", "Zeta.esm",
        "CaravanPack.esm", "ClassicPack.esm", "MercenaryPack.esm", "TribalPack.esm",
        "TaleOfTwoWastelands.esm", "YUPTTW.esm",
    ];

    private static readonly IReadOnlyDictionary<string, ExpectedParticipant> ExpectedParticipants =
        new Dictionary<string, ExpectedParticipant>(StringComparer.Ordinal)
        {
            ["father"] = new(
                "Fallout3.esm:0290a7", "060290a7", "FalloutNV.esm:06b245",
                "FalloutNV.esm:068ab0", "SpecialIdle_CG00DadSection01",
                @"Meshes\Characters\_Male\IdleAnims\CG00DadSection01.kf"),
            ["doctor"] = new(
                "Fallout3.esm:0290a5", "060290a5", "FalloutNV.esm:06a813",
                "FalloutNV.esm:068ab1", "SpecialIdle_CG00DrLiSection01",
                @"Meshes\Characters\_Male\IdleAnims\CG00DrLiSection01.kf"),
            ["mother"] = new(
                "Fallout3.esm:05ede0", "0605ede0", "FalloutNV.esm:06b244",
                "FalloutNV.esm:069ef4", "SpecialIdle_CG00MomSection01",
                @"Meshes\Characters\_Male\IdleAnims\CG00MomSection01.kf"),
        };

    internal static Fo3TtwCg00Stage10PresentationContract Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        RequireExactProperties(
            root,
            "schema", "classification", "capturedUtc", "campaign", "edition", "stage",
            "evidence", "ttw", "camera", "camera1st", "participants", "promotion");
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "classification") != ExpectedClassification ||
            RequiredString(root, "campaign") != "Fallout3" ||
            RequiredString(root, "edition") != "TTW" ||
            RequiredInteger(root, "stage") != ExpectedStage)
            throw new InvalidOperationException("FO3 TTW stage-10 presentation identity differs.");

        var evidence = RequiredObject(root, "evidence");
        RequireExactProperties(
            evidence,
            "rawPath", "rawSha256", "manifestPath", "manifestSha256", "runScriptPath",
            "runScriptSha256", "targetSha256", "observerSha256", "runtime", "sourcePlan");
        if (RequiredString(evidence, "targetSha256") != ExpectedTargetSha256 ||
            RequiredString(evidence, "observerSha256") != ExpectedObserverSha256 ||
            RequiredString(evidence, "runtime") != ExpectedRuntime ||
            RequiredString(evidence, "sourcePlan") != ExpectedSourcePlan)
            throw new InvalidOperationException("FO3 TTW stage-10 observer/source-plan identity differs.");
        var rawPath = VerifyFile(evidence, "rawPath", "rawSha256", "raw observation");
        var manifestPath = VerifyFile(
            evidence, "manifestPath", "manifestSha256", "oracle manifest");
        _ = VerifyFile(evidence, "runScriptPath", "runScriptSha256", "source plan");
        VerifyManifest(manifestPath, rawPath, RequiredString(evidence, "rawSha256"));

        var ttw = RequiredObject(root, "ttw");
        RequireExactProperties(
            ttw,
            "sourceRoot", "openingProfilePath", "openingProfileSha256", "pluginStackId",
            "saveCompatibilityId", "plugins");
        if (!System.IO.Path.GetFullPath(RequiredString(ttw, "sourceRoot")).Equals(
                System.IO.Path.GetFullPath(ExpectedSourceRoot),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FO3 TTW stage-10 source root differs.");
        _ = VerifyFile(ttw, "openingProfilePath", "openingProfileSha256", "TTW opening profile");
        var pluginStackId = RequiredSha256(ttw, "pluginStackId");
        var saveCompatibilityId = RequiredString(ttw, "saveCompatibilityId");
        if (saveCompatibilityId != $"ttw:{pluginStackId}" ||
            !ReadStrings(ttw, "plugins").SequenceEqual(ExpectedPluginOrder))
            throw new InvalidOperationException("FO3 TTW stage-10 plugin/save namespace differs.");

        var camera = ReadCamera(RequiredObject(root, "camera"));
        var camera1stSource = RequiredObject(root, "camera1st");
        RequireExactProperties(
            camera1stSource,
            "referenceFormKey", "runtimeFormId", "runtimeFlags", "localTransform",
            "worldTransform", "playerSection01Disposition");
        if (RequiredString(camera1stSource, "referenceFormKey") != "FalloutNV.esm:000014" ||
            RequiredString(camera1stSource, "runtimeFormId") != "00000014" ||
            RequiredString(camera1stSource, "playerSection01Disposition") !=
                ExpectedPlayerSectionDisposition ||
            (RequiredUnsigned(camera1stSource, "runtimeFlags") & NiAvObjectAppCulledFlag) != 0)
            throw new InvalidOperationException("FO3 TTW stage-10 Camera1st identity differs.");
        _ = ReadTransform(RequiredObject(camera1stSource, "localTransform"), "Camera1st local");
        var camera1st = ReadTransform(
            RequiredObject(camera1stSource, "worldTransform"), "Camera1st world");
        var participants = ReadParticipants(RequiredObject(root, "participants"));
        VerifyRawObservation(rawPath, camera, camera1st, participants);
        return new Fo3TtwCg00Stage10PresentationContract(
            fullPath,
            Hash(bytes),
            rawPath,
            RequiredString(evidence, "rawSha256"),
            pluginStackId,
            saveCompatibilityId,
            camera,
            camera1st,
            participants);
    }

    private static Fo3TtwCg00Stage10Camera ReadCamera(JsonElement source)
    {
        RequireExactProperties(
            source,
            "frame", "worldTransform", "projectionRowMajor", "frustum", "viewport",
            "verticalFovRadians");
        if (RequiredInteger(source, "frame") != ExpectedFrame)
            throw new InvalidOperationException("FO3 TTW stage-10 camera beat differs.");
        var transform = ReadTransform(RequiredObject(source, "worldTransform"), "camera");
        var projection = ReadArray(source, "projectionRowMajor", ProjectionValueCount);
        if (projection.All(value => MathF.Abs(value) <= float.Epsilon))
            throw new InvalidOperationException("FO3 TTW stage-10 projection is empty.");
        var frustum = ReadArray(source, "frustum", FrustumValueCount);
        var viewport = ReadArray(source, "viewport", ViewportValueCount);
        var fovDegrees = RequiredFinite(source, "verticalFovRadians") * RadiansToDegrees;
        // NiCamera stores the frustum sides as near-plane-normalized slopes.  The
        // separate near/far members are clip distances, not a divisor for the sides.
        if (frustum[4] <= 0.0f || frustum[5] <= frustum[4] || frustum[6] != 0.0f ||
            !Approximately(fovDegrees,
                (MathF.Atan(frustum[2]) - MathF.Atan(frustum[3])) * RadiansToDegrees) ||
            !viewport.SequenceEqual([0.0f, 1.0f, 1.0f, 0.0f]))
            throw new InvalidOperationException("FO3 TTW stage-10 frustum/FOV differs.");
        return new Fo3TtwCg00Stage10Camera(
            transform, frustum[0], frustum[1], frustum[2], frustum[3], frustum[4],
            frustum[5], fovDegrees, viewport);
    }

    private static IReadOnlyDictionary<string, Fo3TtwCg00Stage10Participant> ReadParticipants(
        JsonElement source)
    {
        RequireExactProperties(source, ExpectedParticipants.Keys.ToArray());
        var result = new Dictionary<string, Fo3TtwCg00Stage10Participant>(StringComparer.Ordinal);
        foreach (var pair in ExpectedParticipants)
        {
            var row = RequiredObject(source, pair.Key);
            RequireExactProperties(
                row,
                "referenceFormKey", "runtimeFormId", "baseRuntimeFormId", "visible",
                "appCulled", "runtimeFlags", "renderedWorldTransform", "packageFormKey",
                "idleFormKey", "section01");
            var expected = pair.Value;
            var flags = RequiredUnsigned(row, "runtimeFlags");
            if (RequiredString(row, "referenceFormKey") != expected.ReferenceFormKey ||
                RequiredString(row, "runtimeFormId") != expected.RuntimeFormId ||
                !RequiredBoolean(row, "visible") || RequiredBoolean(row, "appCulled") ||
                (flags & NiAvObjectAppCulledFlag) != 0 ||
                RequiredString(row, "packageFormKey") != expected.PackageFormKey ||
                RequiredString(row, "idleFormKey") != expected.IdleFormKey)
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {pair.Key} identity/visibility differs.");
            var sequence = RequiredObject(row, "section01");
            RequireExactProperties(
                sequence,
                "logicalPath", "beginSeconds", "endSeconds", "lastScaledSeconds",
                "frequency", "cycleType", "state", "accumulationRoot");
            var logicalPath = RequiredString(sequence, "logicalPath");
            var begin = RequiredFinite(sequence, "beginSeconds");
            var end = RequiredFinite(sequence, "endSeconds");
            var phase = RequiredFinite(sequence, "lastScaledSeconds");
            var frequency = RequiredFinite(sequence, "frequency");
            var cycle = RequiredInteger(sequence, "cycleType");
            var state = RequiredInteger(sequence, "state");
            if (!ActorModelSlice.NormalizeAnimationPath(logicalPath).Equals(
                    ActorModelSlice.NormalizeAnimationPath(expected.AnimationLogicalPath),
                    StringComparison.OrdinalIgnoreCase) ||
                RequiredString(sequence, "accumulationRoot") != "Bip01" || begin != 0.0f ||
                end <= begin || phase < begin || phase > end || frequency <= 0.0f ||
                cycle != 2 || state != 1)
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {pair.Key} Section01 controller differs.");
            result.Add(
                pair.Key,
                new Fo3TtwCg00Stage10Participant(
                    pair.Key,
                    expected.ReferenceFormKey,
                    expected.RuntimeFormId,
                    true,
                    false,
                    ReadTransform(
                        RequiredObject(row, "renderedWorldTransform"),
                        $"{pair.Key} rendered root"),
                    expected.PackageFormKey,
                    expected.IdleFormKey,
                    expected.SequenceName,
                    logicalPath,
                    begin,
                    end,
                    phase,
                    frequency,
                    cycle,
                    state));
        }
        return result;
    }

    private static void VerifyRawObservation(
        string path,
        Fo3TtwCg00Stage10Camera camera,
        Fo3TtwCg00Stage10Transform camera1st,
        IReadOnlyDictionary<string, Fo3TtwCg00Stage10Participant> participants)
    {
        var cameraMatched = false;
        var pluginMatched = false;
        var actorMatches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            if (!row.TryGetProperty("event", out var eventSource))
                continue;
            var eventName = eventSource.GetString();
            if (eventName == "review-camera-observation" &&
                RequiredInteger(row, "frame") == ExpectedFrame)
            {
                var observed = ReadRawCamera(row);
                cameraMatched = TransformsApproximately(
                        observed.WorldTransform, camera.WorldTransform) &&
                    Approximately(observed.NearGameUnits, camera.NearGameUnits) &&
                    Approximately(observed.FarGameUnits, camera.FarGameUnits) &&
                    Approximately(observed.VerticalFovDegrees, camera.VerticalFovDegrees);
            }
            else if (eventName == "runtime-plugin-stack")
            {
                var plugins = row.GetProperty("plugins").EnumerateArray()
                    .OrderBy(value => value.GetProperty("loadOrderIndex").GetInt32())
                    .Select(value => RequiredString(value, "name"))
                    .ToArray();
                pluginMatched = RequiredBoolean(row, "readable") &&
                    plugins.SequenceEqual(ExpectedPluginOrder);
            }
            else if (eventName == "actor-frame" &&
                RequiredInteger(row, "frame") == ExpectedFrame)
            {
                var runtimeFormId = RequiredUnsigned(row, "refForm").ToString("x8");
                var match = participants.Values.SingleOrDefault(value =>
                    value.RuntimeFormId == runtimeFormId);
                if (match is not null && RawActorMatches(row, match))
                    actorMatches.Add(match.Role);
                else if (runtimeFormId == "00000014")
                {
                    var node = row.GetProperty("bones").EnumerateArray().Single(value =>
                        RequiredString(value, "name") == "Camera1st");
                    var observed = ReadRawTransform(RequiredObject(node, "transform"), "world");
                    if (!TransformsApproximately(observed, camera1st))
                        throw new InvalidOperationException(
                            "FO3 TTW stage-10 Camera1st differs from raw evidence.");
                }
            }
        }
        if (!cameraMatched || !pluginMatched || !actorMatches.SetEquals(participants.Keys))
            throw new InvalidOperationException(
                "FO3 TTW stage-10 normalized contract does not join its raw evidence.");
    }

    private static bool RawActorMatches(JsonElement row, Fo3TtwCg00Stage10Participant expected)
    {
        var root = row.GetProperty("bones").EnumerateArray().Single(value =>
            RequiredString(value, "name") == "Scene Root");
        var transform = ReadRawTransform(RequiredObject(root, "transform"), "world");
        var sequence = row.GetProperty("animDataSequences").EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Single(value => ActorModelSlice.NormalizeAnimationPath(
                    RequiredString(value, "file")).Equals(
                ActorModelSlice.NormalizeAnimationPath(expected.AnimationLogicalPath),
                StringComparison.OrdinalIgnoreCase));
        return TransformsApproximately(transform, expected.RenderedWorldTransform) &&
            Approximately(RequiredFinite(sequence, "lastScaled"), expected.LastScaledSeconds) &&
            Approximately(RequiredFinite(sequence, "begin"), expected.BeginSeconds) &&
            Approximately(RequiredFinite(sequence, "end"), expected.EndSeconds) &&
            RequiredInteger(sequence, "cycle") == expected.CycleType &&
            RequiredInteger(sequence, "state") == expected.State;
    }

    private static Fo3TtwCg00Stage10Camera ReadRawCamera(JsonElement source)
    {
        var world = RequiredObject(source, "cameraWorld");
        var transform = new Fo3TtwCg00Stage10Transform(
            ReadArray(world, "rotation", RotationValueCount),
            ReadVector(world, "translation"),
            RequiredFinite(world, "scale"));
        var frustum = ReadArray(source, "frustum", FrustumValueCount);
        return new Fo3TtwCg00Stage10Camera(
            transform,
            frustum[0], frustum[1], frustum[2], frustum[3], frustum[4], frustum[5],
            RequiredFinite(source, "fovYRadians") * RadiansToDegrees,
            ReadArray(source, "viewport", ViewportValueCount));
    }

    private static Fo3TtwCg00Stage10Transform ReadRawTransform(
        JsonElement source,
        string prefix) => new(
            ReadArray(source, $"{prefix}Rotation", RotationValueCount),
            ReadVector(source, $"{prefix}Translation"),
            RequiredFinite(source, $"{prefix}Scale"));

    private static Fo3TtwCg00Stage10Transform ReadTransform(JsonElement source, string label)
    {
        RequireExactProperties(source, "rotationRowMajor", "translationGameUnits", "scale");
        var transform = new Fo3TtwCg00Stage10Transform(
            ReadArray(source, "rotationRowMajor", RotationValueCount),
            ReadVector(source, "translationGameUnits"),
            RequiredFinite(source, "scale"));
        if (transform.Scale <= MinimumScale ||
            transform.RotationRowMajor.All(value => MathF.Abs(value) <= float.Epsilon))
            throw new InvalidOperationException($"FO3 TTW stage-10 {label} transform is invalid.");
        return transform;
    }

    private static void VerifyManifest(string manifestPath, string rawPath, string rawSha256)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = document.RootElement;
        if (RequiredString(root, "schema") != "nikami-fnv-retail-oracle-run-manifest/v1" ||
            RequiredString(root, "status") != "passed")
            throw new InvalidOperationException("FO3 TTW oracle manifest is not passed evidence.");
        var capture = RequiredObject(RequiredObject(root, "evidence"), "capture");
        if (!System.IO.Path.GetFullPath(RequiredString(capture, "path")).Equals(
                System.IO.Path.GetFullPath(rawPath),
                StringComparison.OrdinalIgnoreCase) ||
            RequiredString(capture, "sha256") != rawSha256)
            throw new InvalidOperationException("FO3 TTW oracle capture identity differs.");
    }

    private static string VerifyFile(
        JsonElement source,
        string pathName,
        string shaName,
        string label)
    {
        var path = System.IO.Path.GetFullPath(RequiredString(source, pathName));
        var expected = RequiredSha256(source, shaName);
        var actual = Hash(File.ReadAllBytes(path));
        if (actual != expected)
            throw new InvalidOperationException($"FO3 TTW stage-10 {label} hash differs.");
        return path;
    }

    private static Vector3 ReadVector(JsonElement source, string name)
    {
        var values = ReadArray(source, name, TranslationValueCount);
        return new Vector3(values[0], values[1], values[2]);
    }

    private static float[] ReadArray(JsonElement source, string name, int count)
    {
        var value = source.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count)
            throw new InvalidOperationException($"FO3 TTW stage-10 {name} cardinality differs.");
        return value.EnumerateArray().Select(item =>
        {
            var result = item.GetSingle();
            if (!float.IsFinite(result))
                throw new InvalidOperationException($"FO3 TTW stage-10 {name} is not finite.");
            return result;
        }).ToArray();
    }

    private static string[] ReadStrings(JsonElement source, string name) =>
        source.GetProperty(name).EnumerateArray()
            .Select(value => value.GetString() ?? throw new InvalidOperationException(
                $"FO3 TTW stage-10 {name} contains null."))
            .ToArray();

    private static JsonElement RequiredObject(JsonElement source, string name)
    {
        var value = source.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"FO3 TTW stage-10 {name} is not an object.");
        return value;
    }

    private static string RequiredString(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"FO3 TTW stage-10 {name} is empty.")
            : value;
    }

    private static string RequiredSha256(JsonElement source, string name)
    {
        var value = RequiredString(source, name).ToLowerInvariant();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"FO3 TTW stage-10 {name} is not SHA-256.");
        return value;
    }

    private static int RequiredInteger(JsonElement source, string name) =>
        source.GetProperty(name).GetInt32();

    private static uint RequiredUnsigned(JsonElement source, string name) =>
        source.GetProperty(name).GetUInt32();

    private static bool RequiredBoolean(JsonElement source, string name) =>
        source.GetProperty(name).GetBoolean();

    private static float RequiredFinite(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetSingle();
        return float.IsFinite(value)
            ? value
            : throw new InvalidOperationException($"FO3 TTW stage-10 {name} is not finite.");
    }

    private static bool Approximately(float left, float right) =>
        MathF.Abs(left - right) <= NumericTolerance;

    private static bool TransformsApproximately(
        Fo3TtwCg00Stage10Transform left,
        Fo3TtwCg00Stage10Transform right) =>
        left.RotationRowMajor.Zip(right.RotationRowMajor)
            .All(pair => Approximately(pair.First, pair.Second)) &&
        left.TranslationGameUnits.IsEqualApprox(right.TranslationGameUnits) &&
        Approximately(left.Scale, right.Scale);

    private static void RequireExactProperties(JsonElement source, params string[] names)
    {
        var expected = names.ToHashSet(StringComparer.Ordinal);
        var actual = source.EnumerateObject().Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
            throw new InvalidOperationException(
                "FO3 TTW stage-10 contract properties differ: " +
                $"expected={string.Join(',', expected.Order())} " +
                $"actual={string.Join(',', actual.Order())}");
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ExpectedParticipant(
        string ReferenceFormKey,
        string RuntimeFormId,
        string PackageFormKey,
        string IdleFormKey,
        string SequenceName,
        string AnimationLogicalPath);
}

internal static class Fo3TtwCg00Stage10PresentationJoin
{
    internal const string CameraAuthority =
        "private-live-TTW-NiCamera-world-transform-and-frustum";
    internal const string ActorPlacementAuthority =
        "private-live-TTW-rendered-node-world-transform-no-marker-substitution";
    internal const string ControllerAuthority =
        "private-live-TTW-Section01-NiControllerSequence-last-scaled-time";

    private const string ExpectedActorRootTranslationDisposition =
        "owned-world-root-authoritative-zero-local-translation";
    private const float RuntimePhaseToleranceSeconds = 1.0e-4f;

    internal static Fo3Cg00RetailStage10JoinTelemetry ApplyAndMeasure(
        Fo3TtwCg00Stage10PresentationContract contract,
        Fo3TtwCg00Stage10SurfaceContract surfaceContract,
        Fo3Cg00EarlyBirthSequence sourceSequence,
        Fo3Vault101BirthSceneCoverage coverage)
    {
        if (!surfaceContract.PresentationSha256.Equals(
                contract.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "FO3 TTW stage-10 surface/presentation authority differs.");
        var actors = new Dictionary<string, CellActorLoader.PlacedActor>(StringComparer.Ordinal)
        {
            ["doctor"] = coverage.DoctorActor,
            ["father"] = coverage.DadActor,
            ["mother"] = coverage.MomActor,
        };
        PublishCamera(contract, coverage);
        var telemetry = new Dictionary<string, Fo3Cg00RetailParticipantTelemetry>(
            StringComparer.Ordinal);
        foreach (var entry in actors)
        {
            var observed = contract.Participants[entry.Key];
            var package = sourceSequence.PackageSections[entry.Key].Single(value =>
                value.Section == 1);
            var actor = entry.Value;
            if (!LocalFormIdMatches(actor.ReferenceFormId, observed.ReferenceFormKey) ||
                !LocalFormIdMatches(
                    sourceSequence.SceneParticipants[entry.Key].ReferenceFormId,
                    observed.ReferenceFormKey) ||
                !LocalFormIdMatches(package.PackageFormId, observed.PackageFormKey) ||
                !LocalFormIdMatches(package.IdleFormId, observed.IdleFormKey))
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {entry.Key} source/runtime identity differs.");
            var animation = actor.Actor.LoadedAnimations.Single(value =>
                value.SequenceName.Equals(observed.SequenceName, StringComparison.Ordinal) &&
                ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                    ActorModelSlice.NormalizeAnimationPath(observed.AnimationLogicalPath),
                    StringComparison.OrdinalIgnoreCase));
            if (animation.CycleType != observed.CycleType ||
                animation.AccumulationRootTranslationDisposition !=
                    ExpectedActorRootTranslationDisposition ||
                MathF.Abs(animation.StartSeconds - observed.BeginSeconds) >
                    RuntimePhaseToleranceSeconds ||
                MathF.Abs(animation.StopSeconds - observed.EndSeconds) >
                    RuntimePhaseToleranceSeconds)
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {entry.Key} actor animation differs.");
            foreach (var player in actor.Actor.LoadedAnimations
                         .Select(value => value.Player).Distinct())
                player.Stop();
            animation.Player.Play(animation.RuntimeName);
            animation.Player.Seek(observed.LastScaledSeconds, update: true);
            var published = animation.Player.CurrentAnimationPosition;
            var phaseError = Math.Abs(published - observed.LastScaledSeconds);
            if (phaseError > RuntimePhaseToleranceSeconds)
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {entry.Key} controller phase did not publish.");
            actor.Placement.Transform = new Transform3D(
                GamebryoCoordinate.ConvertBasis(
                    observed.RenderedWorldTransform.RotationRowMajor,
                    observed.RenderedWorldTransform.Scale,
                    $"FO3 TTW CG00 stage10 {entry.Key}"),
                GamebryoCoordinate.ConvertVector(
                    observed.RenderedWorldTransform.TranslationGameUnits -
                    coverage.Contract.EntryPositionGameUnits));
            actor.Placement.Visible = observed.Visible;
            actor.Placement.SetMeta("opennv_fo3_ttw_stage10_contract_sha256", contract.Sha256);
            var vertices = ActorModelSlice.PosedWorldVertices(actor.Actor, includeWeapons: false);
            var measured = Fo3Cg00RetailStage10Join.MeasureNearPlane(
                vertices, coverage.Camera.GlobalTransform, coverage.Camera.Near);
            telemetry.Add(
                entry.Key,
                new Fo3Cg00RetailParticipantTelemetry(
                    entry.Key,
                    actor.ReferenceFormId,
                    observed.PackageFormKey,
                    observed.IdleFormKey,
                    observed.SequenceName,
                    observed.LastScaledSeconds,
                    published,
                    phaseError,
                    vertices.Count,
                    measured.MinimumDepthMeters,
                    measured.MaximumDepthMeters,
                    coverage.Camera.Near,
                    measured.MinimumNearPlaneSeparationMeters,
                    measured.VerticesAtOrBehindNearPlane,
                    measured.FullMeshClearsNearPlane));
            ValidateSurfaceDepths(
                entry.Key,
                actor.Actor,
                surfaceContract.Participants[entry.Key],
                coverage.Camera.GlobalTransform,
                coverage.Contract.UnitsToMeters);
        }
        return new Fo3Cg00RetailStage10JoinTelemetry(
            contract.Path,
            contract.Sha256,
            CameraAuthority,
            ActorPlacementAuthority,
            ControllerAuthority,
            coverage.Camera.Near,
            coverage.Camera.Far,
            coverage.Camera.Fov,
            telemetry,
            telemetry.Values.All(value => value.FullMeshClearsNearPlane));
    }

    private static void ValidateSurfaceDepths(
        string participantRole,
        ActorModelSlice.LoadedActor actor,
        IReadOnlyList<Fo3TtwCg00Stage10SurfaceContract.Surface> expected,
        Transform3D cameraTransform,
        float unitsToMeters)
    {
        var remaining = actor.Surfaces.ToList();
        var cameraInverse = cameraTransform.AffineInverse();
        foreach (var retail in expected)
        {
            var exact = remaining.Where(surface =>
                    surface.SourceVertexCount == retail.VertexCount &&
                    surface.SourceVertexFnv1a32 == retail.SourceVertexFnv1a32)
                .ToArray();
            var candidates = exact.Length > 0
                ? exact
                : remaining.Where(surface =>
                    surface.SourceVertexCount == retail.VertexCount &&
                    SurfaceRoleMatches(surface.Role, retail.Name)).ToArray();
            if (candidates.Length != 1)
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {participantRole}/{retail.Name} native semantic " +
                    $"surface candidates differ: {candidates.Length}.");
            var surface = candidates[0];
            remaining.Remove(surface);
            var actual = ActorModelSlice.PosedWorldVertices(actor, surface)
                .Select(vertex => (double)(-(cameraInverse * vertex).Z / unitsToMeters))
                .Order().ToArray();
            if (actual.Length != retail.SortedDepthsGameUnits.Count)
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {participantRole}/{retail.Name} native posed " +
                    $"vertex count differs: {actual.Length}/{retail.SortedDepthsGameUnits.Count}.");
            var errors = actual.Zip(retail.SortedDepthsGameUnits, (left, right) =>
                Math.Abs(left - right)).ToArray();
            var maxError = errors.Length == 0 ? 0.0 : errors.Max();
            GD.Print(
                $"OPENNV_FO3_TTW_STAGE10_SURFACE_DEPTH role={participantRole} " +
                $"surface={retail.Name} vertices={actual.Length} " +
                $"minimumGameUnits={actual[0]:R} maximumGameUnits={actual[^1]:R} " +
                $"maximumAbsoluteErrorGameUnits={maxError:R}");
            if (maxError != 0.0)
                throw new InvalidOperationException(
                    $"FO3 TTW stage-10 {participantRole}/{retail.Name} exact native " +
                    $"posed depth distribution differs: maxErrorGameUnits={maxError:R}.");
        }
        if (remaining.Count != 0)
            throw new InvalidOperationException(
                $"FO3 TTW stage-10 {participantRole} has {remaining.Count} unmatched " +
                "native materialized surfaces.");
    }

    private static bool SurfaceRoleMatches(string role, string retailName) =>
        retailName switch
        {
            "FaceGenFace" => role == "head",
            "FaceGenMouth" => role == "mouth",
            "FaceGenTeethLower" => role == "teeth-lower",
            "FaceGenTeethUpper" => role == "teeth-upper",
            "FaceGenTongue" => role == "tongue",
            "FaceGenHairNoHat" => role == "hair",
            "FaceGenEyeLeft" => role == "eye-left",
            "FaceGenEyeRight" => role == "eye-right",
            "FaceGenAccessory" => role.StartsWith("head-part-", StringComparison.Ordinal),
            _ => false,
        };

    private static void PublishCamera(
        Fo3TtwCg00Stage10PresentationContract contract,
        Fo3Vault101BirthSceneCoverage coverage)
    {
        var observed = contract.Camera;
        var viewportSize = coverage.Camera.GetViewport().GetVisibleRect().Size;
        var sourceAspect = (observed.RightGameUnits - observed.LeftGameUnits) /
            (observed.TopGameUnits - observed.BottomGameUnits);
        var runtimeAspect = viewportSize.X / viewportSize.Y;
        if (!float.IsFinite(runtimeAspect) || viewportSize.Y <= 0.0f ||
            MathF.Abs(sourceAspect - runtimeAspect) >
                Fo3Cg00RetailStage10Contract.ProjectionAspectTolerance)
            throw new InvalidOperationException(
                "FO3 TTW stage-10 runtime viewport aspect differs from observed projection.");
        var local = new Transform3D(
            GamebryoCoordinate.ConvertCameraBasis(
                observed.WorldTransform.RotationRowMajor,
                "FO3 TTW CG00 stage10 NiCamera"),
            GamebryoCoordinate.ConvertVector(
                observed.WorldTransform.TranslationGameUnits -
                coverage.Contract.EntryPositionGameUnits));
        var world = coverage.CellRoot.GlobalTransform * local;
        coverage.Camera.GlobalTransform = new Transform3D(
            world.Basis.Orthonormalized(), world.Origin);
        coverage.Camera.Fov = observed.VerticalFovDegrees;
        coverage.Camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
        coverage.Camera.Near = observed.NearGameUnits * coverage.Contract.UnitsToMeters;
        coverage.Camera.Far = observed.FarGameUnits * coverage.Contract.UnitsToMeters;
        coverage.Camera.Current = true;
        coverage.Camera.SetMeta("opennv_fo3_ttw_stage10_contract_sha256", contract.Sha256);
    }

    private static bool LocalFormIdMatches(string formId, string formKey)
    {
        var normalized = FalloutFormId.Normalize(formId);
        var separator = formKey.IndexOf(':');
        return separator >= 0 && normalized.EndsWith(
            formKey[(separator + 1)..],
            StringComparison.OrdinalIgnoreCase);
    }
}
