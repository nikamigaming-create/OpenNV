using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class RetailActorStateContract
{
    private const string ContractSchema = "opennv-retail-actor-state-contract/v2";
    private const string ShotSchema = "opennv-retail-actor-shot-state/v2";
    private const int SpatialDimension = GamebryoCoordinate.SpatialDimensions;
    private const float PerspectiveMinimumDegrees = 0.0f;
    private const float PerspectiveMaximumDegrees = 180.0f;

    internal static Contract Load(
        string path,
        string expectedReferenceFormId,
        string expectedBaseFormId,
        RuntimeConfiguration configuration)
    {
        var resolvedPath = Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedPath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ContractSchema)
            throw new InvalidOperationException($"Unexpected retail actor state contract: {resolvedPath}");
        var shots = root.GetProperty("shots").EnumerateArray()
            .Select(source => ParseShot(source, configuration.RetailActorState))
            .ToDictionary(shot => shot.Kind, StringComparer.Ordinal);
        var requiredShots = configuration.RetailActorState.RequiredShotKinds;
        if (shots.Count != requiredShots.Count || requiredShots.Any(kind => !shots.ContainsKey(kind)))
            throw new InvalidOperationException("Retail actor state contract does not contain the configured keyed shots.");
        var expectedReference = FalloutFormId.Normalize(expectedReferenceFormId);
        var expectedBase = FalloutFormId.Normalize(expectedBaseFormId);
        if (shots.Values.Any(shot =>
                FalloutFormId.Normalize(shot.ReferenceFormId) != expectedReference ||
                FalloutFormId.Normalize(shot.BaseFormId) != expectedBase))
            throw new InvalidOperationException("Retail actor state contract belongs to another ACHR or actor base.");
        var contextSets = shots.Values
            .Select(shot => shot.ContextActors.Select(actor => FalloutFormId.Normalize(actor.ReferenceFormId))
                .OrderBy(value => value, StringComparer.Ordinal).ToArray())
            .ToArray();
        if (contextSets[0].Length < configuration.RetailActorState.MinimumContextActors ||
            contextSets.Skip(1).Any(values => !values.SequenceEqual(contextSets[0])))
            throw new InvalidOperationException("Retail context-actor identities disagree across shots.");
        var exactProjectionResolved = root.GetProperty("exactProjectionResolved").GetBoolean();
        if (exactProjectionResolved != shots.Values.All(shot => shot.ExactProjection))
            throw new InvalidOperationException("Retail projection summary disagrees with its shots.");
        using var stream = File.OpenRead(resolvedPath);
        return new Contract(
            resolvedPath,
            Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
            exactProjectionResolved,
            shots);
    }

    private static Shot ParseShot(JsonElement source, RetailActorStateConfiguration configuration)
    {
        if (source.GetProperty("schema").GetString() != ShotSchema)
            throw new InvalidOperationException("Unexpected retail actor shot schema.");
        var target = source.GetProperty("target");
        var kind = source.GetProperty("shotKind").GetString()
            ?? throw new InvalidOperationException("Retail actor shot has no kind.");
        if (!configuration.RequiredShotKinds.Contains(kind, StringComparer.Ordinal))
            throw new InvalidOperationException($"Unsupported retail actor shot kind: {kind}");
        var referenceTransform = source.GetProperty("referenceTransform");
        var referencePosition = ReadVector(referenceTransform.GetProperty("position"), "reference position");
        var referenceRotation = ReadNumbers(
            referenceTransform.GetProperty("rotation"),
            SpatialDimension,
            "reference rotation");
        var camera = source.GetProperty("camera");
        var projection = camera.GetProperty("projection");
        var projectionStatus = projection.GetProperty("status").GetString() ?? "";
        var exactProjection = projection.GetProperty("exact").GetBoolean();
        if ((exactProjection && projectionStatus != "resolved") ||
            (!exactProjection && projectionStatus != "provisional"))
            throw new InvalidOperationException("Retail actor projection status is inconsistent.");
        var fovYDegrees = projection.GetProperty("fovYDegrees").GetSingle();
        var aspect = projection.GetProperty("aspect").GetSingle();
        if (!float.IsFinite(fovYDegrees) ||
            fovYDegrees <= PerspectiveMinimumDegrees || fovYDegrees >= PerspectiveMaximumDegrees ||
            !float.IsFinite(aspect) || aspect <= 0.0f)
            throw new InvalidOperationException("Retail actor projection is not finite perspective state.");

        var pose = source.GetProperty("pose");
        var sequences = pose.GetProperty("activeSequences").EnumerateArray()
            .Where(sequence =>
                !string.IsNullOrWhiteSpace(sequence.GetProperty("file").GetString()) &&
                MathF.Abs(
                    sequence.GetProperty("weight").GetSingle() - configuration.FullSequenceWeight) <=
                    configuration.SequenceWeightTolerance)
            .ToArray();
        if (sequences.Length != 1)
            throw new InvalidOperationException("Retail actor shot does not contain one configured full-weight sequence.");
        var phase = sequences[0].GetProperty("lastScaled").GetDouble();
        var begin = sequences[0].GetProperty("begin").GetDouble();
        var end = sequences[0].GetProperty("end").GetDouble();
        if (!double.IsFinite(phase) || phase < begin || phase > end)
            throw new InvalidOperationException("Retail actor idle phase is outside the sequence interval.");

        var poseBones = ReadPoseBones(pose.GetProperty("bones"), "target", configuration);
        var armBones = ReadBoneNames(
            pose.GetProperty("armBones"),
            "target arms",
            configuration.MinimumArmBones,
            configuration);
        if (armBones.Any(name => !poseBones.Any(bone => bone.Name == name)))
            throw new InvalidOperationException("Retail actor arm-bone summary is incomplete.");

        var contextActors = source.GetProperty("contextActors").EnumerateArray()
            .Select(actor => ParseContextActor(actor, configuration))
            .ToArray();
        if (contextActors.Length < configuration.MinimumContextActors ||
            contextActors.Select(actor => FalloutFormId.Normalize(actor.ReferenceFormId)).Distinct().Count()
                != contextActors.Length)
            throw new InvalidOperationException("Retail shot has no unique context actors.");

        var geometry = source.GetProperty("geometry").GetProperty("shapes").EnumerateArray().ToArray();
        var face = geometry.Single(shape => shape.GetProperty("name").GetString() == "FaceGenFace");
        var hair = geometry.Single(shape => shape.GetProperty("name").GetString() == "FaceGenHairNoHat");
        if (face.GetProperty("vertexCount").GetInt32() <= 0 ||
            hair.GetProperty("vertexCount").GetInt32() <= 0 ||
            face.GetProperty("fnv1a32").GetUInt32() == 0 ||
            hair.GetProperty("fnv1a32").GetUInt32() == 0)
            throw new InvalidOperationException("Retail actor final face/hair geometry gate failed.");

        return new Shot(
            kind,
            target.GetProperty("referenceForm").GetString()!,
            target.GetProperty("baseForm").GetString()!,
            referencePosition,
            referenceRotation[2],
            ReadVector(camera.GetProperty("position"), "camera position"),
            ReadVector(camera.GetProperty("aim"), "camera aim"),
            ReadPositive(camera.GetProperty("distance"), "camera distance"),
            fovYDegrees,
            aspect,
            exactProjection,
            projectionStatus,
            projection.GetProperty("source").GetString()!,
            projection.GetProperty("confidence").GetString()!,
            sequences[0].GetProperty("file").GetString()!,
            phase,
            poseBones,
            armBones,
            contextActors,
            face.GetProperty("fnv1a32").GetUInt32(),
            hair.GetProperty("fnv1a32").GetUInt32());
    }

    private static ContextActor ParseContextActor(
        JsonElement source,
        RetailActorStateConfiguration configuration)
    {
        var sequences = source.GetProperty("activeSequences").EnumerateArray()
            .Where(sequence =>
                sequence.TryGetProperty("file", out var file) &&
                !string.IsNullOrWhiteSpace(file.GetString()) &&
                sequence.GetProperty("weight").GetSingle() >= configuration.MinimumContextSequenceWeight)
            .ToArray();
        if (sequences.Length != 1)
            throw new InvalidOperationException("Retail context actor must have one full-weight sequence.");
        var phase = sequences[0].GetProperty("lastScaled").GetDouble();
        if (!double.IsFinite(phase) || phase < 0.0)
            throw new InvalidOperationException("Retail context actor has an invalid animation phase.");
        var bones = ReadPoseBones(source.GetProperty("bones"), "context actor", configuration);
        var furniture = source.GetProperty("furnitureState");
        var geometry = source.GetProperty("geometry").GetProperty("shapes").EnumerateArray().ToArray();
        var face = geometry.Single(shape => shape.GetProperty("name").GetString() == "FaceGenFace");
        if (face.GetProperty("vertexCount").GetInt32() <= 0 ||
            face.GetProperty("fnv1a32").GetUInt32() == 0)
            throw new InvalidOperationException("Retail context actor face geometry is incomplete.");
        var rotation = ReadNumbers(source.GetProperty("rotation"), SpatialDimension, "context rotation");
        return new ContextActor(
            source.GetProperty("referenceForm").GetString()!,
            source.GetProperty("baseForm").GetString()!,
            ReadVector(source.GetProperty("position"), "context position"),
            rotation[2],
            furniture.GetProperty("actorSitSleepState").GetInt32(),
            furniture.GetProperty("usedFurnitureRefForm").GetUInt32(),
            furniture.GetProperty("usedFurnitureBaseForm").GetUInt32(),
            sequences[0].GetProperty("file").GetString()!,
            phase,
            bones,
            face.GetProperty("fnv1a32").GetUInt32());
    }

    private static IReadOnlyList<PoseBone> ReadPoseBones(
        JsonElement source,
        string label,
        RetailActorStateConfiguration configuration)
    {
        var bones = source.EnumerateArray()
            .Where(row => !configuration.ExcludedPoseNodes.Contains(
                row.GetProperty("name").GetString() ?? ""))
            .Select(row =>
            {
                var transform = row.GetProperty("transform");
                return new PoseBone(
                    row.GetProperty("name").GetString()!,
                    ReadVector(transform.GetProperty("worldTranslation"), $"{label} bone position"),
                    ReadConvertedBasis(
                        transform.GetProperty("worldRotation"),
                        transform.GetProperty("worldScale").GetSingle(),
                        $"{label} bone rotation"));
            })
            .ToArray();
        if (bones.Length < configuration.MinimumPoseBones ||
            bones.Any(bone => string.IsNullOrWhiteSpace(bone.Name)) ||
            bones.Select(bone => bone.Name).Distinct(StringComparer.Ordinal).Count() != bones.Length)
            throw new InvalidOperationException($"Retail {label} skeleton is incomplete or ambiguous.");
        return bones;
    }

    private static Basis ReadConvertedBasis(JsonElement source, float scale, string label)
    {
        var game = ReadNumbers(source, SpatialDimension * SpatialDimension, label);
        return GamebryoCoordinate.ConvertBasis(game, scale, label);
    }

    private static IReadOnlyList<string> ReadBoneNames(
        JsonElement source,
        string label,
        int minimum,
        RetailActorStateConfiguration configuration)
    {
        var names = source.EnumerateArray()
            .Select(bone => bone.GetProperty("name").GetString() ?? "")
            .Where(name => !configuration.ExcludedPoseNodes.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (names.Length < minimum || names.Any(string.IsNullOrWhiteSpace) ||
            names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidOperationException($"Retail {label} skeleton is incomplete or ambiguous.");
        return names;
    }

    private static Vector3 ReadVector(JsonElement source, string label)
    {
        var values = ReadNumbers(source, SpatialDimension, label);
        return new Vector3(values[0], values[1], values[2]);
    }

    private static float[] ReadNumbers(JsonElement source, int count, string label)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != count || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException($"Retail actor {label} must contain {count} finite values.");
        return values;
    }

    private static float ReadPositive(JsonElement source, string label)
    {
        var value = source.GetSingle();
        if (!float.IsFinite(value) || value <= 0.0f)
            throw new InvalidOperationException($"Retail actor {label} must be finite and positive.");
        return value;
    }

    private static string NormalizePath(string value) => value.Replace('/', '\\');

    internal sealed record Contract(
        string Path,
        string Sha256,
        bool ExactProjectionResolved,
        IReadOnlyDictionary<string, Shot> Shots);

    internal readonly record struct Shot(
        string Kind,
        string ReferenceFormId,
        string BaseFormId,
        Vector3 ReferencePositionGameUnits,
        float ReferenceYawRadians,
        Vector3 CameraPositionGameUnits,
        Vector3 CameraAimGameUnits,
        float CameraDistanceGameUnits,
        float VerticalFovDegrees,
        float Aspect,
        bool ExactProjection,
        string ProjectionStatus,
        string ProjectionSource,
        string ProjectionConfidence,
        string AnimationFile,
        double AnimationPhaseSeconds,
        IReadOnlyList<PoseBone> PoseBones,
        IReadOnlyList<string> ArmBones,
        IReadOnlyList<ContextActor> ContextActors,
        uint FaceVertexHash,
        uint HairVertexHash);

    internal readonly record struct ContextActor(
        string ReferenceFormId,
        string BaseFormId,
        Vector3 PositionGameUnits,
        float YawRadians,
        int ActorSitSleepState,
        uint FurnitureReferenceFormId,
        uint FurnitureBaseFormId,
        string AnimationFile,
        double AnimationPhaseSeconds,
        IReadOnlyList<PoseBone> PoseBones,
        uint FaceVertexHash);

    internal readonly record struct PoseBone(
        string Name,
        Vector3 WorldPositionGameUnits,
        Basis WorldBasis);
}
