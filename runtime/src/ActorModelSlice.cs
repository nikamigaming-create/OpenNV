using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class ActorModelSlice
{
    private const string ActorSchema = "opennv-actor-gltf/v2";

    internal static LoadedActor Load(
        string modelPath,
        string sidecarPath,
        Node3D parent,
        RuntimeConfiguration configuration,
        bool scaleToMeters = true,
        BoundsContract boundsContract = BoundsContract.Humanoid)
    {
        var resolvedModel = VerifiedGltfLoader.ResolvePath(modelPath);
        var resolvedSidecar = VerifiedGltfLoader.ResolvePath(sidecarPath);
        using var metadata = JsonDocument.Parse(File.ReadAllText(resolvedSidecar));
        var root = metadata.RootElement;
        if (root.GetProperty("schema").GetString() != ActorSchema ||
            root.GetProperty("status").GetString() != "skinned-animated")
            throw new InvalidOperationException($"Unexpected OpenNV actor sidecar: {resolvedSidecar}");
        var outputs = root.GetProperty("outputs");
        VerifyHash(resolvedModel, outputs.GetProperty("gltf").GetProperty("sha256").GetString()!);
        var binary = Path.Combine(Path.GetDirectoryName(resolvedModel)!, outputs.GetProperty("buffer").GetProperty("file").GetString()!);
        VerifyHash(binary, outputs.GetProperty("buffer").GetProperty("sha256").GetString()!);
        foreach (var texture in root.GetProperty("textures").EnumerateArray())
            VerifyHash(
                Path.Combine(Path.GetDirectoryName(resolvedSidecar)!, texture.GetProperty("png").GetString()!),
                texture.GetProperty("pngSha256").GetString()!);

        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(resolvedModel, state);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot rejected actor glTF with {error}: {resolvedModel}");
        var scene = document.GenerateScene(state) as Node3D
            ?? throw new InvalidOperationException($"Godot generated no actor scene: {resolvedModel}");
        scene.Name = $"ACTOR_{root.GetProperty("actorFormId").GetString()}_{root.GetProperty("actorName").GetString()}";
        scene.Scale = Vector3.One * (scaleToMeters ? configuration.World.GameUnitsToMeters : 1.0f);
        parent.AddChild(scene);
        var importedMeshes = Descendants<MeshInstance3D>(scene)
            .Where(mesh => mesh.Mesh is not null)
            .ToArray();
        var surfaces = LoadSurfaces(root, importedMeshes, resolvedSidecar);
        var skeletons = Descendants<Skeleton3D>(scene).ToArray();
        var players = Descendants<AnimationPlayer>(scene).ToArray();
        var animations = players.Sum(player => player.GetAnimationList().Length);
        if (importedMeshes.Length < 1 || skeletons.Length < 1 || animations < 1)
            throw new InvalidOperationException(
                $"Actor import is incomplete: meshes={importedMeshes.Length} " +
                $"skeletons={skeletons.Length} animations={animations}");
        var animationName = players
            .SelectMany(player => player.GetAnimationList().Select(name => (Player: player, Name: name)))
            .First(row => row.Name != "RESET");
        animationName.Player.Play(animationName.Name);
        var bounds = Bounds(scene);
        var animation = root.GetProperty("animation");
        if (animation.TryGetProperty("nonAccumOriginGodotUnits", out var originSource) &&
            originSource.ValueKind == JsonValueKind.Array)
        {
            var origin = ReadVector(originSource);
            bounds = new Aabb(bounds.Position - scene.GlobalBasis * origin, bounds.Size);
        }
        if (boundsContract == BoundsContract.Humanoid &&
            (bounds.Size.Y < configuration.DiagnosticPreview.ActorMinimumHeightMeters ||
             bounds.Size.Y > configuration.DiagnosticPreview.ActorMaximumHeightMeters))
            throw new InvalidOperationException($"Actor height is outside the humanoid gate: {bounds.Size.Y:F3}m");
        return new LoadedActor(
            scene,
            root.GetProperty("actorFormId").GetString()!,
            root.GetProperty("actorName").GetString()!,
            importedMeshes.Length,
            skeletons.Length,
            animations,
            animationName.Name.ToString(),
            animationName.Player,
            bounds,
            root.GetProperty("coverage").GetProperty("surfaces").GetInt32(),
            root.GetProperty("coverage").GetProperty("textures").GetInt32(),
            surfaces);
    }

    private static IReadOnlyList<LoadedSurface> LoadSurfaces(
        JsonElement sidecar,
        IReadOnlyList<MeshInstance3D> importedMeshes,
        string sidecarPath)
    {
        var declared = sidecar.GetProperty("surfaces").EnumerateArray().ToArray();
        var authoredSurfaceCount = sidecar.GetProperty("coverage").GetProperty("surfaces").GetInt32();
        if (declared.Length != authoredSurfaceCount || importedMeshes.Count != authoredSurfaceCount)
            throw new InvalidOperationException(
                $"Actor surface counts disagree in {sidecarPath}: " +
                $"declared={declared.Length} coverage={authoredSurfaceCount} imported={importedMeshes.Count}.");

        var importedByName = importedMeshes
            .GroupBy(mesh => mesh.Name.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var duplicateImportedNames = importedByName
            .Where(row => row.Value.Length != 1)
            .Select(row => row.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicateImportedNames.Length > 0)
            throw new InvalidOperationException(
                $"Actor import has duplicate runtime surface names in {sidecarPath}: " +
                string.Join(", ", duplicateImportedNames));

        var loaded = new List<LoadedSurface>(declared.Length);
        var declaredRuntimeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var surface in declared)
        {
            var role = RequireSurfaceText(surface, "role", sidecarPath);
            var shape = RequireSurfaceText(surface, "shape", sidecarPath);
            var runtimeNodeName = RequireSurfaceText(surface, "runtimeNodeName", sidecarPath);
            if (!declaredRuntimeNames.Add(runtimeNodeName))
                throw new InvalidOperationException(
                    $"Actor sidecar repeats runtime surface {runtimeNodeName} in {sidecarPath}.");
            if (!importedByName.TryGetValue(runtimeNodeName, out var matches) || matches.Length != 1)
                throw new InvalidOperationException(
                    $"Actor sidecar surface {role}/{shape} maps to no exact runtime node " +
                    $"{runtimeNodeName} in {sidecarPath}.");
            var skinned = surface.GetProperty("skinned").GetBoolean();
            if (skinned != (matches[0].Skin is not null))
                throw new InvalidOperationException(
                    $"Actor sidecar skin state disagrees for {role}/{shape} at {runtimeNodeName} " +
                    $"in {sidecarPath}.");
            loaded.Add(new LoadedSurface(role, shape, runtimeNodeName, matches[0], skinned));
        }
        if (declaredRuntimeNames.Count != importedByName.Count)
            throw new InvalidOperationException(
                $"Actor import contains a surface absent from its sidecar: {sidecarPath}.");
        return loaded;
    }

    private static string RequireSurfaceText(JsonElement surface, string property, string sidecarPath)
    {
        if (!surface.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException(
                $"Actor sidecar surface has no {property}: {sidecarPath}.");
        return value.GetString()!;
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Actor artifact hash mismatch: {path}");
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Actor vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Aabb Bounds(Node3D root)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var points = 0;
        foreach (var mesh in Descendants<MeshInstance3D>(root))
        {
            var bounds = mesh.GetAabb();
            foreach (var x in new[] { bounds.Position.X, bounds.End.X })
                foreach (var y in new[] { bounds.Position.Y, bounds.End.Y })
                    foreach (var z in new[] { bounds.Position.Z, bounds.End.Z })
                    {
                        var point = mesh.ToGlobal(new Vector3(x, y, z));
                        minimum = minimum.Min(point);
                        maximum = maximum.Max(point);
                        points++;
                    }
        }
        if (points == 0)
            throw new InvalidOperationException("Actor scene contains no bounds.");
        return new Aabb(minimum, maximum - minimum);
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    internal readonly record struct LoadedActor(
        Node3D Root,
        string FormId,
        string Name,
        int Meshes,
        int Skeletons,
        int Animations,
        string PlayingAnimation,
        AnimationPlayer AnimationPlayer,
        Aabb Bounds,
        int AuthoredSurfaces,
        int AuthoredTextures,
        IReadOnlyList<LoadedSurface> Surfaces);

    internal readonly record struct LoadedSurface(
        string Role,
        string Shape,
        string RuntimeNodeName,
        MeshInstance3D Mesh,
        bool Skinned);

    internal enum BoundsContract
    {
        Humanoid,
        FirstPersonHand,
        AnyActor,
    }
}
