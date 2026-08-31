using System.Security.Cryptography;
using System.Text.Json;
using Godot;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1CreatureModelNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point25f = 0.25f;
    internal const float PresentationFloat0Point35f = 0.35f;
    internal const float PresentationFloat1Point8f = 1.8f;
    internal const int PresentationInt5 = 5;
}

internal static class Fo1CreatureModel
{
    private const string ActorSchema = "opennv-actor-gltf/v4";

    internal static Template Load(JsonElement source, Node3D measurementParent)
    {
        var modelPath = VerifiedGltfLoader.ResolvePath(source.GetProperty("model").GetString()!);
        var sidecarPath = VerifiedGltfLoader.ResolvePath(source.GetProperty("sidecar").GetString()!);
        using var sidecarDocument = JsonDocument.Parse(File.ReadAllText(sidecarPath));
        var sidecar = sidecarDocument.RootElement;
        if (sidecar.GetProperty("schema").GetString() != ActorSchema ||
            sidecar.GetProperty("status").GetString() != "skinned-animated")
            throw new InvalidOperationException($"Unexpected Fallout creature sidecar: {sidecarPath}");
        var outputs = sidecar.GetProperty("outputs");
        VerifyHash(modelPath, outputs.GetProperty("gltf").GetProperty("sha256").GetString()!);
        var binaryPath = Path.Combine(
            Path.GetDirectoryName(modelPath)!,
            outputs.GetProperty("buffer").GetProperty("file").GetString()!);
        VerifyHash(binaryPath, outputs.GetProperty("buffer").GetProperty("sha256").GetString()!);
        foreach (var texture in sidecar.GetProperty("textures").EnumerateArray())
            VerifyHash(
                Path.Combine(Path.GetDirectoryName(sidecarPath)!, texture.GetProperty("png").GetString()!),
                texture.GetProperty("pngSha256").GetString()!);

        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(modelPath, state);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot rejected Fallout creature glTF with {error}: {modelPath}");
        var prototype = document.GenerateScene(state) as Node3D
            ?? throw new InvalidOperationException($"Godot generated no Fallout creature scene: {modelPath}");
        prototype.Name = $"CREATURE_{source.GetProperty("formId").GetString()}_{source.GetProperty("editorId").GetString()}";
        prototype.Scale = Vector3.One * source.GetProperty("unitsToMeters").GetSingle();
        var prototypeMeshes = NodeTraversal.Descendants<MeshInstance3D>(prototype)
            .Where(mesh => mesh.Mesh is not null)
            .ToArray();
        var meshes = prototypeMeshes.Length;
        var skeletons = NodeTraversal.Descendants<Skeleton3D>(prototype).Count();
        var players = NodeTraversal.Descendants<AnimationPlayer>(prototype).ToArray();
        if (meshes != sidecar.GetProperty("coverage").GetProperty("surfaces").GetInt32() ||
            skeletons < 1 || players.Length != 1)
            throw new InvalidOperationException(
                $"Fallout creature import is incomplete: meshes={meshes} skeletons={skeletons} players={players.Length}");
        var sourceShapesByRuntimeNodeName = sidecar.GetProperty("surfaces")
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("runtimeNodeName").GetString()!,
                row => row.GetProperty("sourceShape").GetString()!,
                StringComparer.Ordinal);
        if (sourceShapesByRuntimeNodeName.Count != prototypeMeshes.Length ||
            prototypeMeshes.Any(mesh => !sourceShapesByRuntimeNodeName.ContainsKey(mesh.Name.ToString())))
            throw new InvalidOperationException(
                $"Fallout creature source-shape identity drift: {sidecarPath}");
        var availableAnimations = players[0].GetAnimationList()
            .Select(name => name.ToString())
            .Where(name => name != "RESET")
            .ToArray();
        var normalizedAnimations = availableAnimations
            .GroupBy(ActorModelSlice.NormalizeAnimationPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Single(),
                StringComparer.OrdinalIgnoreCase);
        var roles = source.GetProperty("animations").EnumerateArray().ToDictionary(
            row => row.GetProperty("role").GetString()!,
            row =>
            {
                var logicalPath = row.GetProperty("logicalPath").GetString()!;
                var expected = ActorModelSlice.NormalizeAnimationPath(logicalPath);
                return normalizedAnimations.TryGetValue(expected, out var runtimeName)
                    ? runtimeName
                    : throw new InvalidOperationException(
                        $"Fallout creature animation {logicalPath} is absent from the Godot import.");
            },
            StringComparer.Ordinal);
        if (roles.Count < Fo1CreatureModelNumericContracts.PresentationInt5)
            throw new InvalidOperationException(
                $"Fallout creature animation identity drift: available={string.Join(",", availableAnimations)}");
        foreach (var role in new[] { "idle", "move", "turn" })
        {
            var animation = players[0].GetAnimation(roles[role]);
            animation.LoopMode = Animation.LoopModeEnum.Linear;
        }
        prototype.Visible = false;
        measurementParent.AddChild(prototype);
        var bounds = Bounds(prototype);
        measurementParent.RemoveChild(prototype);
        prototype.Visible = true;
        var horizontalLong = MathF.Max(bounds.Size.X, bounds.Size.Z);
        var horizontalShort = MathF.Min(bounds.Size.X, bounds.Size.Z);
        if (bounds.Size.Y is < Fo1CreatureModelNumericContracts.PresentationFloat0Point35f or > Fo1CreatureModelNumericContracts.PresentationFloat1Point8f || horizontalLong < 1.0f || horizontalShort < Fo1CreatureModelNumericContracts.PresentationFloat0Point25f)
            throw new InvalidOperationException(
                $"Fallout giant-rat bounds are implausible: position={bounds.Position} size={bounds.Size}");
        return new Template(
            prototype,
            source.GetProperty("formId").GetString()!,
            source.GetProperty("editorId").GetString()!,
            roles,
            sourceShapesByRuntimeNodeName,
            bounds,
            meshes,
            skeletons,
            availableAnimations.Length);
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Fallout creature artifact hash mismatch: {path}");
    }

    private static Aabb Bounds(Node3D root)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var count = 0;
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = mesh.GlobalTransform * new Vector3(x, y, z);
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                    }
            count++;
        }
        if (count == 0)
            throw new InvalidOperationException("Fallout creature scene has no renderable bounds.");
        return new Aabb(minimum, maximum - minimum);
    }

    internal sealed record Template(
        Node3D Prototype,
        string FormId,
        string EditorId,
        IReadOnlyDictionary<string, string> AnimationRoles,
        IReadOnlyDictionary<string, string> SourceShapesByRuntimeNodeName,
        Aabb Bounds,
        int Meshes,
        int Skeletons,
        int Animations)
    {
        internal Instance Instantiate()
        {
            var root = Prototype.Duplicate() as Node3D
                ?? throw new InvalidOperationException("Could not duplicate Fallout creature presentation.");
            var players = NodeTraversal.Descendants<AnimationPlayer>(root).ToArray();
            if (players.Length != 1)
                throw new InvalidOperationException("Duplicated Fallout creature lost its AnimationPlayer.");
            return new Instance(
                root,
                players[0],
                AnimationRoles,
                SourceShapesByRuntimeNodeName,
                Bounds);
        }
    }

    internal sealed record Instance(
        Node3D Root,
        AnimationPlayer Player,
        IReadOnlyDictionary<string, string> AnimationRoles,
        IReadOnlyDictionary<string, string> SourceShapesByRuntimeNodeName,
        Aabb Bounds);
}
