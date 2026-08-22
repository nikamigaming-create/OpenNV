using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class RetailActorStateContract
{
    private const string ContractSchema = "opennv-retail-actor-state-contract/v1";
    private const string ShotSchema = "opennv-retail-actor-shot-state/v1";
    private const string IdlePath = @"Characters\_Male\Locomotion\mtidle.kf";

    private static readonly string[] RequiredShots = ["front-portrait", "front-full-body"];
    private static readonly string[] RequiredArmBones =
    [
        "Bip01 L UpperArm",
        "Bip01 L Forearm",
        "Bip01 R UpperArm",
        "Bip01 R Forearm",
    ];

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

        var sequences = source.GetProperty("pose").GetProperty("activeSequences").EnumerateArray()
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

        var armBones = source.GetProperty("pose").GetProperty("armBones").EnumerateArray()
            .Select(bone => bone.GetProperty("name").GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        if (armBones.Count != RequiredArmBones.Length || RequiredArmBones.Any(name => !armBones.Contains(name)))
            throw new InvalidOperationException("Retail actor shot does not contain the four required arm bones.");

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
            RequiredArmBones,
            face.GetProperty("fnv1a32").GetUInt32(),
            hair.GetProperty("fnv1a32").GetUInt32());
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
        IReadOnlyList<string> ArmBones,
        uint FaceVertexHash,
        uint HairVertexHash);
}
