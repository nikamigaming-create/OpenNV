using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class CellActorLoader
{
    private const string ActorSceneSchema = "opennv-actor-scene/v1";

    internal static PlacedActor? Load(
        string actorScenePath,
        string expectedCellFormId,
        Node3D cellRoot,
        bool proofEnableInitiallyDisabled)
    {
        var resolvedManifest = VerifiedGltfLoader.ResolvePath(actorScenePath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedManifest));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ActorSceneSchema ||
            root.GetProperty("status").GetString() != "skinned-animated")
            throw new InvalidOperationException($"Unexpected OpenNV actor scene: {resolvedManifest}");
        if (root.GetProperty("cellFormId").GetString() != expectedCellFormId)
            throw new InvalidOperationException("Actor scene belongs to another CELL.");
        var reference = root.GetProperty("reference");
        var initiallyDisabled = reference.GetProperty("initiallyDisabled").GetBoolean();
        if (initiallyDisabled && !proofEnableInitiallyDisabled)
            return null;
        var position = ReadVector(reference.GetProperty("positionGodotUnits"));
        var yaw = reference.GetProperty("yawRadians").GetSingle();
        var placement = new Node3D
        {
            Name = $"ACHR_{reference.GetProperty("formId").GetString()}",
            Position = position,
            Rotation = new Vector3(0.0f, yaw, 0.0f),
        };
        cellRoot.AddChild(placement);
        var outputs = root.GetProperty("outputs");
        var actor = root.GetProperty("actor");
        var actorRoot = Path.GetDirectoryName(resolvedManifest)!;
        var loaded = ActorModelSlice.Load(
            Path.Combine(actorRoot, outputs.GetProperty("gltf").GetString()!),
            Path.Combine(actorRoot, outputs.GetProperty("sidecar").GetString()!),
            placement,
            false);
        return new PlacedActor(
            placement,
            reference.GetProperty("formId").GetString()!,
            reference.GetProperty("baseFormId").GetString()!,
            initiallyDisabled,
            proofEnableInitiallyDisabled && initiallyDisabled,
            actor.GetProperty("raceFormId").GetString()!,
            actor.GetProperty("hairFormId").GetString()!,
            actor.GetProperty("eyesFormId").GetString()!,
            actor.GetProperty("outfitFormId").GetString()!,
            actor.GetProperty("headPartFormIds").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray(),
            loaded);
    }

    private static Vector3 ReadVector(JsonElement array)
    {
        var values = array.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Actor scene vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
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
        string OutfitFormId,
        IReadOnlyList<string> HeadPartFormIds,
        ActorModelSlice.LoadedActor Actor);
}
