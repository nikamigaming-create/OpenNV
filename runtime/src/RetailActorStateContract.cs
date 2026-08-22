using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class RetailActorStateContract
{
    private const string ContractSchema = "opennv-retail-actor-state-contract/v2";
    private const string ShotSchema = "opennv-retail-actor-shot-state/v2";
    private const string IdlePath = @"Characters\_Male\Locomotion\mtidle.kf";

    private static readonly string[] RequiredShots = ["front-portrait", "front-full-body"];
    private static readonly string[] RequiredArmBones =
    [
        "Bip01 L UpperArm",
        "Bip01 L Forearm",
        "Bip01 R UpperArm",
        "Bip01 R Forearm",
    ];
    private static readonly HashSet<string> NonDeformingPoseNodes = new(
        [
            "Bip01",
            "Bip01 NonAccum",
            "Bip01 Neck",
            "Bip01 R ForeTwistDriver",
            "Bip01 LPauldron",
            "Bip01 RPauldron",
        ],
        StringComparer.Ordinal);

    internal static Contract Load(
        string path,
        string expectedReferenceFormId,
        string expectedBaseFormId)
    {
        var resolvedPath = Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedPath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ContractSchema)
            throw new InvalidOperationException($"Unexpected retail actor state contract: {resolvedPath}");
        var shots = root.GetProperty("shots").EnumerateArray()
            .Select(ParseShot)
            .ToDictionary(shot => shot.Kind, StringComparer.Ordinal);
        if (shots.Count != RequiredShots.Length ||
            RequiredShots.Any(kind => !shots.ContainsKey(kind)))
            throw new InvalidOperationException("Retail actor state contract must contain exactly two keyed shots.");
        var expectedReference = NormalizeForm(expectedReferenceFormId);
        var expectedBase = NormalizeForm(expectedBaseFormId);
        if (shots.Values.Any(shot =>
                NormalizeForm(shot.ReferenceFormId) != expectedReference ||
                NormalizeForm(shot.BaseFormId) != expectedBase))
            throw new InvalidOperationException("Retail actor state contract belongs to another ACHR or actor base.");
        var contextSets = shots.Values
            .Select(shot => shot.ContextActors.Select(actor => NormalizeForm(actor.ReferenceFormId))
                .OrderBy(value => value, StringComparer.Ordinal).ToArray())
            .ToArray();
        if (contextSets[0].Length < 1 ||
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

    private static Shot ParseShot(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != ShotSchema)
            throw new InvalidOperationException("Unexpected retail actor shot schema.");
        var target = source.GetProperty("target");
        var kind = source.GetProperty("shotKind").GetString()
            ?? throw new InvalidOperationException("Retail actor shot has no kind.");
        if (!RequiredShots.Contains(kind, StringComparer.Ordinal))
            throw new InvalidOperationException($"Unsupported retail actor shot kind: {kind}");
        var referenceTransform = source.GetProperty("referenceTransform");
        var referencePosition = ReadVector(referenceTransform.GetProperty("position"), "reference position");
        var referenceRotation = ReadNumbers(referenceTransform.GetProperty("rotation"), 3, "reference rotation");
        var camera = source.GetProperty("camera");
        var projection = camera.GetProperty("projection");
        var projectionStatus = projection.GetProperty("status").GetString() ?? "";
        var exactProjection = projection.GetProperty("exact").GetBoolean();
        if ((exactProjection && projectionStatus != "resolved") ||
            (!exactProjection && projectionStatus != "provisional"))
            throw new InvalidOperationException("Retail actor projection status is inconsistent.");
        var fovYDegrees = projection.GetProperty("fovYDegrees").GetSingle();
        var aspect = projection.GetProperty("aspect").GetSingle();
        if (!float.IsFinite(fovYDegrees) || fovYDegrees <= 1.0f || fovYDegrees >= 179.0f ||
            !float.IsFinite(aspect) || aspect <= 0.0f)
            throw new InvalidOperationException("Retail actor projection is not finite perspective state.");

        var pose = source.GetProperty("pose");
        var sequences = pose.GetProperty("activeSequences").EnumerateArray()
            .Where(sequence => string.Equals(
                NormalizePath(sequence.GetProperty("file").GetString() ?? ""),
                NormalizePath(IdlePath),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sequences.Length != 1 ||
            MathF.Abs(sequences[0].GetProperty("weight").GetSingle() - 1.0f) > 0.001f)
            throw new InvalidOperationException("Retail actor shot does not contain one full-weight mtidle sequence.");
        var phase = sequences[0].GetProperty("lastScaled").GetDouble();
        var begin = sequences[0].GetProperty("begin").GetDouble();
        var end = sequences[0].GetProperty("end").GetDouble();
        if (!double.IsFinite(phase) || phase < begin || phase > end)
            throw new InvalidOperationException("Retail actor idle phase is outside the sequence interval.");

        var poseBones = ReadPoseBones(pose.GetProperty("bones"), "target");
        if (RequiredArmBones.Any(name => !poseBones.Any(bone => bone.Name == name)))
            throw new InvalidOperationException("Retail actor shot does not contain the required arm bones.");
        var armBones = ReadBoneNames(pose.GetProperty("armBones"), "target arms", 4);
        if (armBones.Count != RequiredArmBones.Length ||
            RequiredArmBones.Any(name => !armBones.Contains(name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Retail actor arm-bone summary is incomplete.");

        var contextActors = source.GetProperty("contextActors").EnumerateArray()
            .Select(ParseContextActor)
            .ToArray();
        if (contextActors.Length < 1 ||
            contextActors.Select(actor => NormalizeForm(actor.ReferenceFormId)).Distinct().Count()
                != contextActors.Length)
            throw new InvalidOperationException("Retail shot has no unique context actors.");

        var geometry = source.GetProperty("geometry").GetProperty("shapes").EnumerateArray().ToArray();
        var face = geometry.Single(shape => shape.GetProperty("name").GetString() == "FaceGenFace");
        var hair = geometry.Single(shape => shape.GetProperty("name").GetString() == "FaceGenHairNoHat");
        if (face.GetProperty("vertexCount").GetInt32() != 1211 ||
            hair.GetProperty("vertexCount").GetInt32() != 962 ||
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

    private static ContextActor ParseContextActor(JsonElement source)
    {
        var sequences = source.GetProperty("activeSequences").EnumerateArray()
            .Where(sequence =>
                sequence.TryGetProperty("file", out var file) &&
                !string.IsNullOrWhiteSpace(file.GetString()) &&
                sequence.GetProperty("weight").GetSingle() >= 0.99f)
            .ToArray();
        if (sequences.Length != 1)
            throw new InvalidOperationException("Retail context actor must have one full-weight sequence.");
        var phase = sequences[0].GetProperty("lastScaled").GetDouble();
        if (!double.IsFinite(phase) || phase < 0.0)
            throw new InvalidOperationException("Retail context actor has an invalid animation phase.");
        var bones = ReadPoseBones(source.GetProperty("bones"), "context actor");
        var furniture = source.GetProperty("furnitureState");
        var geometry = source.GetProperty("geometry").GetProperty("shapes").EnumerateArray().ToArray();
        var face = geometry.Single(shape => shape.GetProperty("name").GetString() == "FaceGenFace");
        if (face.GetProperty("vertexCount").GetInt32() != 1211 ||
            face.GetProperty("fnv1a32").GetUInt32() == 0)
            throw new InvalidOperationException("Retail context actor face geometry is incomplete.");
        var rotation = ReadNumbers(source.GetProperty("rotation"), 3, "context rotation");
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

    private static IReadOnlyList<PoseBone> ReadPoseBones(JsonElement source, string label)
    {
        var bones = source.EnumerateArray()
            .Where(row => !NonDeformingPoseNodes.Contains(
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
        if (bones.Length < 50 || bones.Any(bone => string.IsNullOrWhiteSpace(bone.Name)) ||
            bones.Select(bone => bone.Name).Distinct(StringComparer.Ordinal).Count() != bones.Length)
            throw new InvalidOperationException($"Retail {label} skeleton is incomplete or ambiguous.");
        return bones;
    }

    private static Basis ReadConvertedBasis(JsonElement source, float scale, string label)
    {
        var game = ReadNumbers(source, 9, label);
        var rows = new[,]
        {
            { game[0], game[1], game[2] },
            { game[3], game[4], game[5] },
            { game[6], game[7], game[8] },
        };
        var conversion = new[,]
        {
            { 1.0f, 0.0f, 0.0f },
            { 0.0f, 0.0f, 1.0f },
            { 0.0f, -1.0f, 0.0f },
        };
        var converted = Multiply(conversion, Multiply(rows, Transpose(conversion)));
        var basis = new Basis(
            new Vector3(converted[0, 0], converted[1, 0], converted[2, 0]),
            new Vector3(converted[0, 1], converted[1, 1], converted[2, 1]),
            new Vector3(converted[0, 2], converted[1, 2], converted[2, 2]));
        if (!float.IsFinite(scale) || scale <= 0.0f)
            throw new InvalidOperationException($"Retail {label} has invalid scale.");
        return basis.Scaled(Vector3.One * scale);
    }

    private static float[,] Multiply(float[,] left, float[,] right)
    {
        var result = new float[3, 3];
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
                for (var axis = 0; axis < 3; axis++)
                    result[row, column] += left[row, axis] * right[axis, column];
        return result;
    }

    private static float[,] Transpose(float[,] source)
    {
        var result = new float[3, 3];
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
                result[row, column] = source[column, row];
        return result;
    }

    private static IReadOnlyList<string> ReadBoneNames(
        JsonElement source,
        string label,
        int minimum = 50)
    {
        var names = source.EnumerateArray()
            .Select(bone => bone.GetProperty("name").GetString() ?? "")
            .Where(name => !NonDeformingPoseNodes.Contains(name))
            .ToArray();
        if (names.Length < minimum || names.Any(string.IsNullOrWhiteSpace) ||
            names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidOperationException($"Retail {label} skeleton is incomplete or ambiguous.");
        return names;
    }

    private static Vector3 ReadVector(JsonElement source, string label)
    {
        var values = ReadNumbers(source, 3, label);
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

    private static string NormalizeForm(string value)
    {
        var text = value.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (text.Length is < 1 or > 8 || text.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Retail actor state contains an invalid form ID: {value}");
        return text.PadLeft(8, '0').ToLowerInvariant();
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
