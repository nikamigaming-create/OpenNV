using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class CellActorLoader
{
    private const string ActorSceneSchema = "opennv-actor-scene/v5";
    private const string ActorSceneSetSchema = "opennv-cell-actor-scenes/v1";
    private const string WorldActorSceneSetSchema = "opennv-world-actor-scenes/v2";

    internal static IReadOnlyList<string> LoadManifest(
        string manifestPath,
        IReadOnlySet<string> acceptedCellFormIds)
    {
        var resolvedManifest = VerifiedGltfLoader.ResolvePath(manifestPath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedManifest));
        var root = document.RootElement;
        var schema = root.GetProperty("schema").GetString();
        if (schema != ActorSceneSetSchema && schema != WorldActorSceneSetSchema)
            throw new InvalidOperationException($"Unexpected OpenNV cell actor manifest: {resolvedManifest}");
        if (schema == ActorSceneSetSchema &&
            !acceptedCellFormIds.Contains(root.GetProperty("cellFormId").GetString()!))
            throw new InvalidOperationException($"Legacy actor manifest belongs to another CELL: {resolvedManifest}");
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scenes = new List<string>();
        foreach (var row in root.GetProperty("actors").EnumerateArray())
        {
            var reference = row.GetProperty("referenceFormId").GetString()!;
            if (!references.Add(reference))
                throw new InvalidOperationException($"Cell actor manifest duplicates ACHR {reference}.");
            var scene = VerifiedGltfLoader.ResolvePath(row.GetProperty("scene").GetString()!);
            using var stream = File.OpenRead(scene);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actual.Equals(row.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cell actor scene hash mismatch: {scene}");
            var cellFormId = schema == WorldActorSceneSetSchema
                ? row.GetProperty("cellFormId").GetString()!
                : root.GetProperty("cellFormId").GetString()!;
            if (acceptedCellFormIds.Contains(cellFormId))
                scenes.Add(scene);
        }
        if (scenes.Count < 1)
            throw new InvalidOperationException("Cell actor manifest contains no actor scenes.");
        return scenes;
    }

    internal static PlacedActor? Load(
        string actorScenePath,
        IReadOnlySet<string> acceptedCellFormIds,
        Node3D cellRoot,
        Vector3 cellOriginGameUnits,
        RuntimeConfiguration configuration,
        bool proofEnableInitiallyDisabled)
    {
        var resolvedManifest = VerifiedGltfLoader.ResolvePath(actorScenePath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedManifest));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ActorSceneSchema ||
            root.GetProperty("status").GetString() != "skinned-animated")
            throw new InvalidOperationException($"Unexpected OpenNV actor scene: {resolvedManifest}");
        configuration.VerifyCompiledConfiguration(root);
        if (!acceptedCellFormIds.Contains(root.GetProperty("cellFormId").GetString()!))
            throw new InvalidOperationException("Actor scene belongs to another CELL.");
        var reference = root.GetProperty("reference");
        var initiallyDisabled = reference.GetProperty("initiallyDisabled").GetBoolean();
        if (initiallyDisabled && !proofEnableInitiallyDisabled)
            return null;
        var authoredPosition = ReadVector(reference.GetProperty("positionGameUnits"));
        var position = new Vector3(
            authoredPosition.X - cellOriginGameUnits.X,
            authoredPosition.Z - cellOriginGameUnits.Z,
            -(authoredPosition.Y - cellOriginGameUnits.Y));
        var rotation = ReadQuaternion(reference.GetProperty("rotationGodotQuaternion"));
        var placement = new Node3D
        {
            Name = $"ACHR_{reference.GetProperty("formId").GetString()}",
            Position = position,
            Basis = new Basis(rotation),
            Scale = Vector3.One * reference.GetProperty("scale").GetSingle(),
        };
        cellRoot.AddChild(placement);
        var outputs = root.GetProperty("outputs");
        var actor = root.GetProperty("actor");
        var recordType = actor.GetProperty("recordType").GetString();
        if (recordType is not ("NPC_" or "CREA"))
            throw new InvalidOperationException(
                $"Actor scene has unsupported record type: {recordType}");
        var actorRoot = Path.GetDirectoryName(resolvedManifest)!;
        var modelPath = Path.Combine(actorRoot, outputs.GetProperty("gltf").GetString()!);
        var sidecarPath = Path.Combine(actorRoot, outputs.GetProperty("sidecar").GetString()!);
        VerifyHash(modelPath, outputs.GetProperty("gltfSha256").GetString()!);
        VerifyHash(sidecarPath, outputs.GetProperty("sidecarSha256").GetString()!);
        using (var sidecarDocument = JsonDocument.Parse(File.ReadAllText(sidecarPath)))
        {
            var buffer = sidecarDocument.RootElement.GetProperty("outputs").GetProperty("buffer");
            if (!buffer.GetProperty("sha256").GetString()!.Equals(
                    outputs.GetProperty("bufferSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Actor scene and sidecar disagree on the buffer hash.");
        }
        var loaded = ActorModelSlice.Load(
            modelPath,
            sidecarPath,
            placement,
            configuration,
            false,
            recordType == "CREA"
                ? ActorModelSlice.BoundsContract.AnyActor
                : ActorModelSlice.BoundsContract.Humanoid);
        return new PlacedActor(
            placement,
            reference.GetProperty("formId").GetString()!,
            reference.GetProperty("baseFormId").GetString()!,
            initiallyDisabled,
            proofEnableInitiallyDisabled && initiallyDisabled,
            actor.GetProperty("raceFormId").GetString()!,
            actor.GetProperty("hairFormId").GetString()!,
            actor.GetProperty("eyesFormId").GetString()!,
            actor.GetProperty("outfitFormIds").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            actor.GetProperty("headPartFormIds").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            root.GetProperty("idleAnimation").GetString()!,
            loaded);
    }

    private static Vector3 ReadVector(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Actor scene vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Quaternion ReadQuaternion(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("Actor scene quaternion must contain four values.");
        var quaternion = new Quaternion(values[0], values[1], values[2], values[3]);
        if (!quaternion.IsNormalized())
            throw new InvalidOperationException("Actor scene quaternion must be normalized.");
        return quaternion;
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Actor scene artifact hash mismatch: {path}");
    }

    internal readonly record struct PlacedActor(
        Node3D Placement,
        string ReferenceFormId,
        string BaseFormId,
        bool InitiallyDisabled,
        bool ProofEnabled,
        string RaceFormId,
        string HairFormId,
        string EyesFormId,
        IReadOnlyList<string> OutfitFormIds,
        IReadOnlyList<string> HeadPartFormIds,
        string IdleAnimationPath,
        ActorModelSlice.LoadedActor Actor);
}
