using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Actors;
using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.World.Actors;

internal static class ActorModelSlice
{
    private const string ActorSchema = "opennv-actor-gltf/v4";
    private const string WeaponSurfaceRole = "weapon";
    private const string AnimationObjectSurfaceRolePrefix = "animation-object-";
    private const string AuthoredPrnRootMarkerDisposition =
        "omit-authored-prn-root-marker";
    private const int TransformMatrixComponentCount = 16;

    internal static LoadedActor Load(
        string modelPath,
        string sidecarPath,
        Node3D parent)
    {
        var configuration = RuntimeConfiguration.Load();
        return Load(
            modelPath,
            sidecarPath,
            parent,
            configuration,
            true,
            BoundsContract.AnyActor);
    }

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
        VerifyFaceGenAnimationContract(root, configuration, resolvedSidecar);
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
        var importedMeshes = NodeTraversal.Descendants<MeshInstance3D>(scene)
            .Where(mesh => mesh.Mesh is not null)
            .ToArray();
        var surfaces = LoadSurfaces(root, importedMeshes, resolvedSidecar, configuration);
        if (boundsContract != BoundsContract.FirstPersonHand)
            ActorComplexionJoin.Apply(scene, surfaces);
        var omittedSurfaces = LoadOmittedSurfaces(root, resolvedSidecar);
        var skeletons = NodeTraversal.Descendants<Skeleton3D>(scene).ToArray();
        var players = NodeTraversal.Descendants<AnimationPlayer>(scene).ToArray();
        var runtimeAnimations = players
            .SelectMany(player => player.GetAnimationList()
                .Where(name => name != "RESET")
                .Select(name => (Player: player, Name: name)))
            .ToArray();
        if (importedMeshes.Length < 1 || skeletons.Length < 1 ||
            runtimeAnimations.Length < 1)
            throw new InvalidOperationException(
                $"Actor import is incomplete: meshes={importedMeshes.Length} " +
                $"skeletons={skeletons.Length} animations={runtimeAnimations.Length}");
        var poseContract = ActorPoseContract.Load(
            root,
            resolvedModel,
            scene,
            skeletons);
        var animationRows = root.GetProperty("animations").EnumerateArray().ToArray();
        if (animationRows.Length != runtimeAnimations.Length)
            throw new InvalidOperationException(
                "Actor sidecar and Godot import disagree on authored animation count: " +
                $"sidecar={animationRows.Length} runtime={runtimeAnimations.Length}.");
        var loadedAnimations = ResolveAnimations(
            animationRows,
            runtimeAnimations,
            resolvedSidecar);
        var animationName = runtimeAnimations.Single(row =>
            row.Player == loadedAnimations[0].Player &&
            row.Name == loadedAnimations[0].RuntimeName);
        animationName.Player.Play(animationName.Name);
        animationName.Player.Advance(0.0);
        var bounds = PosedWorldBounds(scene, surfaces, includeWeapons: true);
        var animation = root.GetProperty("animation");
        var animationLogicalPath = animation.GetProperty("logicalPath").GetString();
        var animationSourceSha256 = animation.GetProperty("sha256").GetString();
        var animationChannels = animation.GetProperty("channels").GetInt32();
        if (string.IsNullOrWhiteSpace(animationLogicalPath) ||
            string.IsNullOrWhiteSpace(animationSourceSha256) ||
            animationChannels < 1)
            throw new InvalidOperationException(
                "Actor animation source identity is incomplete.");
        var humanoidGateBounds = boundsContract == BoundsContract.Humanoid
            ? PosedWorldBounds(scene, surfaces, includeWeapons: false)
            : bounds;
        if (boundsContract == BoundsContract.Humanoid &&
            (humanoidGateBounds.Size.Y < configuration.DiagnosticPreview.ActorMinimumHeightMeters ||
             humanoidGateBounds.Size.Y > configuration.DiagnosticPreview.ActorMaximumHeightMeters))
            throw new InvalidOperationException(
                "Actor body height is outside the humanoid gate: " +
                $"{humanoidGateBounds.Size.Y:F3}m (full visual {bounds.Size.Y:F3}m).");
        return new LoadedActor(
            scene,
            root.GetProperty("actorFormId").GetString()!,
            root.GetProperty("actorName").GetString()!,
            importedMeshes.Length,
            skeletons.Length,
            runtimeAnimations.Length,
            animationLogicalPath,
            animationSourceSha256,
            animationChannels,
            animationName.Name.ToString(),
            animationName.Player,
            poseContract,
            bounds,
            root.GetProperty("coverage").GetProperty("surfaces").GetInt32(),
            root.GetProperty("coverage").GetProperty("textures").GetInt32(),
            surfaces,
            omittedSurfaces,
            loadedAnimations);
    }

    private static IReadOnlyList<LoadedAnimation> ResolveAnimations(
        IReadOnlyList<JsonElement> declared,
        IReadOnlyList<(AnimationPlayer Player, string Name)> runtime,
        string sidecarPath)
    {
        if (declared.Count == 1 && runtime.Count == 1)
            return new[] { ParseAnimation(declared[0], runtime[0], sidecarPath) };
        var remaining = runtime.ToList();
        var result = new List<LoadedAnimation>(declared.Count);
        foreach (var source in declared)
        {
            var logicalPath = RequireAnimationText(source, "logicalPath", sidecarPath);
            var expected = NormalizeAnimationPath(logicalPath);
            var matches = remaining.Where(candidate =>
                    NormalizeAnimationPath(candidate.Name.ToString()).Equals(
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Actor animation {logicalPath} maps to {matches.Length} Godot resources " +
                    $"in {sidecarPath}.");
            result.Add(ParseAnimation(source, matches[0], sidecarPath));
            remaining.Remove(matches[0]);
        }
        if (remaining.Count != 0)
            throw new InvalidOperationException(
                $"Actor import has animations absent from its sidecar: {sidecarPath}.");
        return result;
    }

    private static LoadedAnimation ParseAnimation(
        JsonElement source,
        (AnimationPlayer Player, string Name) runtime,
        string sidecarPath)
    {
        var logicalPath = RequireAnimationText(source, "logicalPath", sidecarPath);
        var sha256 = RequireAnimationText(source, "sha256", sidecarPath);
        var rootDisposition = RequireAnimationText(
            source,
            "accumulationRootTranslationDisposition",
            sidecarPath);
        if (rootDisposition is not
            ("owned-world-root-authoritative-zero-local-translation" or
            "preserve-hash-bound-owned-clip-root-curve"))
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has an unsupported root disposition " +
                $"in {sidecarPath}: {rootDisposition}.");
        var channels = source.GetProperty("channels").GetInt32();
        if (channels < 1)
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has no authored channels in {sidecarPath}.");
        var runtimeAnimation = runtime.Player.GetAnimation(runtime.Name) ??
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has no imported Godot animation in {sidecarPath}.");
        var sequenceName = source.TryGetProperty("sequenceName", out var sequenceSource)
            ? sequenceSource.GetString()
            : Path.GetFileNameWithoutExtension(logicalPath);
        var startSeconds = source.TryGetProperty("startSeconds", out var startSource)
            ? startSource.GetSingle()
            : 0.0f;
        var stopSeconds = source.TryGetProperty("stopSeconds", out var stopSource)
            ? stopSource.GetSingle()
            : (float)runtimeAnimation.Length;
        var cycleType = source.TryGetProperty("cycleType", out var cycleSource)
            ? cycleSource.GetInt32()
            : (int)runtimeAnimation.LoopMode;
        if (string.IsNullOrWhiteSpace(sequenceName))
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has no source or imported sequence name " +
                $"in {sidecarPath}.");
        if (!float.IsFinite(startSeconds) || !float.IsFinite(stopSeconds) ||
            startSeconds != 0.0f || stopSeconds <= startSeconds)
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has invalid source timing in {sidecarPath}.");
        var priorities = source.TryGetProperty(
                "transformPrioritiesByNode",
                out var prioritySource)
            ? prioritySource
                .EnumerateObject()
                .ToDictionary(
                    value => value.Name,
                    value => value.Value.GetInt32(),
                    StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);
        var textKeys = source.TryGetProperty("textKeys", out var textKeySource)
            ? textKeySource.EnumerateArray().Select(value => new LoadedTextKey(
                value.GetProperty("timeSeconds").GetSingle(),
                RequireAnimationText(value, "value", sidecarPath))).ToArray()
            : Array.Empty<LoadedTextKey>();
        if (priorities.Any(value => value.Value < 0))
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has invalid transform priorities " +
                $"in {sidecarPath}.");
        if (textKeys.Any(value => !float.IsFinite(value.TimeSeconds) ||
                value.TimeSeconds < startSeconds || value.TimeSeconds > stopSeconds) ||
            textKeys.Select(value => value.TimeSeconds).SequenceEqual(
                textKeys.Select(value => value.TimeSeconds).Order()) is false)
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has invalid source text keys " +
                $"in {sidecarPath}.");
        return new LoadedAnimation(
            logicalPath,
            sha256,
            rootDisposition,
            channels,
            sequenceName,
            startSeconds,
            stopSeconds,
            cycleType,
            priorities,
            runtime.Name,
            runtime.Player,
            source.TryGetProperty("role", out var roleSource) &&
                roleSource.ValueKind == JsonValueKind.String
                ? roleSource.GetString()
                : null,
            source.TryGetProperty("rootMotion", out var rootMotionSource) &&
                rootMotionSource.ValueKind == JsonValueKind.Object
                ? ParseAnimationRootMotion(rootMotionSource, logicalPath, sidecarPath)
                : null,
            textKeys);
    }

    private static LoadedRootMotion ParseAnimationRootMotion(
        JsonElement source,
        string logicalPath,
        string sidecarPath)
    {
        var displacement = source.GetProperty("displacementGodotGameUnits")
            .EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var result = new LoadedRootMotion(
            RequireAnimationText(source, "targetNode", sidecarPath),
            displacement.Length == 3
                ? new Vector3(displacement[0], displacement[1], displacement[2])
                : Vector3.Inf,
            source.GetProperty("speedGameUnitsPerSecond").GetSingle());
        if (!result.DisplacementGodotGameUnits.IsFinite() ||
            result.DisplacementGodotGameUnits.IsZeroApprox() ||
            !float.IsFinite(result.SpeedGameUnitsPerSecond) ||
            result.SpeedGameUnitsPerSecond <= 0.0f)
            throw new InvalidOperationException(
                $"Actor animation {logicalPath} has invalid source root motion " +
                $"in {sidecarPath}.");
        return result;
    }

    private static string RequireAnimationText(
        JsonElement source,
        string property,
        string sidecarPath)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Actor animation has no {property}: {sidecarPath}.");
        return value;
    }

    internal static string NormalizeAnimationPath(string value)
    {
        var normalized = value.Replace('/', '\\').TrimStart('\\');
        const string meshesPrefix = "meshes\\";
        normalized = normalized.StartsWith(meshesPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[meshesPrefix.Length..]
            : normalized;
        const string importedKfSuffix = "_kf";
        return normalized.EndsWith(importedKfSuffix, StringComparison.OrdinalIgnoreCase)
            ? normalized[..^importedKfSuffix.Length] + ".kf"
            : normalized;
    }

    private static IReadOnlyList<LoadedSurface> LoadSurfaces(
        JsonElement sidecar,
        IReadOnlyList<MeshInstance3D> importedMeshes,
        string sidecarPath,
        RuntimeConfiguration configuration)
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
            var sourceShape = RequireSurfaceText(surface, "sourceShape", sidecarPath);
            var modelPath = RequireSurfaceText(surface, "modelPath", sidecarPath);
            var modelSha256 = RequireSurfaceText(surface, "modelSha256", sidecarPath);
            var sourceVertexCount = surface.GetProperty("vertices").GetInt32();
            var sourceVertexFnv1a32 = OptionalSurfaceUInt32(
                surface, "sourceVertexFnv1a32", sidecarPath) ?? 0;
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
            var attachmentNode = skinned
                ? null
                : RequireSurfaceText(surface, "attachmentNode", sidecarPath);
            var sourceFormId = OptionalSurfaceText(surface, "sourceFormId", sidecarPath);
            var sourceSlot = OptionalSurfaceUInt32(surface, "sourceSlot", sidecarPath);
            var material = surface.GetProperty("material");
            var sourceShaderSkin = material.TryGetProperty("skin", out var skin) &&
                skin.ValueKind == JsonValueKind.Object;
            var rigidShapeTransformBaked = surface.GetProperty(
                "rigidShapeTransformBaked").GetBoolean();
            var sourceShapeTransformGodotMatrix = surface.GetProperty(
                    "sourceShapeTransformGodotMatrix")
                .EnumerateArray()
                .Select(value => value.GetSingle())
                .ToArray();
            if (sourceShapeTransformGodotMatrix.Length != TransformMatrixComponentCount ||
                sourceShapeTransformGodotMatrix.Any(value => !float.IsFinite(value)))
                throw new InvalidOperationException(
                    $"Actor surface has an invalid source-shape transform: " +
                    $"{role}/{shape} in {sidecarPath}.");
            var retailGeometryName = skinned
                ? null
                : OptionalSurfaceText(surface, "retailGeometryName", sidecarPath);
            var retailVisualNodePath = skinned
                ? null
                : OptionalSurfaceText(surface, "retailVisualNodePath", sidecarPath);
            var declaredMorphTargets = surface.GetProperty("faceGenMorphs")
                .GetProperty("targetNames")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            var runtimeMorphCount = matches[0].GetBlendShapeCount();
            if (runtimeMorphCount != declaredMorphTargets.Length)
                throw new InvalidOperationException(
                    $"Actor FaceGen target count disagrees for {role}/{shape} in {sidecarPath}: " +
                    $"declared={declaredMorphTargets.Length} runtime={runtimeMorphCount}.");
            if (runtimeMorphCount > 0)
            {
                if (matches[0].Mesh is not ArrayMesh arrayMesh)
                    throw new InvalidOperationException(
                        $"Actor FaceGen surface is not an ArrayMesh: {role}/{shape} in {sidecarPath}.");
                var runtimeMorphTargets = Enumerable.Range(0, runtimeMorphCount)
                    .Select(index => arrayMesh.GetBlendShapeName(index).ToString())
                    .ToArray();
                if (!runtimeMorphTargets.SequenceEqual(declaredMorphTargets, StringComparer.Ordinal))
                    throw new InvalidOperationException(
                        $"Actor FaceGen target names disagree for {role}/{shape} in {sidecarPath}: " +
                        $"declared=[{string.Join(",", declaredMorphTargets)}] " +
                        $"runtime=[{string.Join(",", runtimeMorphTargets)}].");
            }
            if (role.StartsWith(
                    AnimationObjectSurfaceRolePrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!rigidShapeTransformBaked)
                    throw new InvalidOperationException(
                        $"Actor animation object did not bake its exact owned " +
                        $"shape transform: {role}/{shape} in {sidecarPath}.");
                matches[0].Visible = false;
                GD.Print(
                    $"OPENNV_ACTOR_ANIMATION_OBJECT_DEFAULT_HIDDEN role={role} " +
                    $"source={sourceFormId} attachment={attachmentNode}");
            }
            RetailActorMaterial.Apply(
                matches[0],
                surface,
                sidecar.GetProperty("textures"),
                sidecarPath,
                configuration.ActorCompiler.FaceGenMaterial);
            loaded.Add(new LoadedSurface(
                role,
                shape,
                sourceShape,
                modelPath,
                modelSha256,
                sourceVertexCount,
                sourceVertexFnv1a32,
                runtimeNodeName,
                matches[0],
                skinned,
                attachmentNode,
                sourceFormId,
                sourceSlot,
                sourceShaderSkin,
                retailGeometryName,
                retailVisualNodePath,
                declaredMorphTargets,
                rigidShapeTransformBaked,
                sourceShapeTransformGodotMatrix));
        }
        if (declaredRuntimeNames.Count != importedByName.Count)
            throw new InvalidOperationException(
                $"Actor import contains a surface absent from its sidecar: {sidecarPath}.");
        return loaded;
    }

    private static void VerifyFaceGenAnimationContract(
        JsonElement sidecar,
        RuntimeConfiguration configuration,
        string sidecarPath)
    {
        var source = sidecar.GetProperty("faceGenAnimation");
        var contract = configuration.ActorCompiler.FaceGenAnimation;
        if (source.GetProperty("schema").GetString() != contract.Schema)
            throw new InvalidOperationException(
                $"Actor FaceGen animation schema differs in {sidecarPath}.");
        var lipTargets = source.GetProperty("lipTargetNames").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        if (!lipTargets.SequenceEqual(contract.Lip.TargetNames, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Actor FaceGen LIP target order differs in {sidecarPath}.");
        var morphTargets = source.GetProperty("morphTargetNames").EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Null ? null : value.GetString())
            .ToArray();
        if (!morphTargets.SequenceEqual(contract.Lip.MorphTargetNames, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Actor FaceGen morph binding order differs in {sidecarPath}.");
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

    private static IReadOnlyList<OmittedSurface> LoadOmittedSurfaces(
        JsonElement sidecar,
        string sidecarPath)
    {
        var declared = sidecar.GetProperty("omittedSurfaces").EnumerateArray()
            .Select(surface => LoadOmittedSurface(surface, sidecarPath))
            .ToArray();
        var expected = sidecar.GetProperty("coverage").GetProperty("omittedSurfaces").GetInt32();
        if (declared.Length != expected)
            throw new InvalidOperationException(
                $"Actor omitted-surface count disagrees in {sidecarPath}: " +
                $"declared={declared.Length} coverage={expected}.");
        return declared;
    }

    private static OmittedSurface LoadOmittedSurface(
        JsonElement surface,
        string sidecarPath)
    {
        var disposition = RequireSurfaceText(surface, "disposition", sidecarPath);
        var attachmentNode = OptionalSurfaceText(
            surface,
            "attachmentNode",
            sidecarPath);
        var attachmentSource = OptionalSurfaceText(
            surface,
            "attachmentSource",
            sidecarPath);
        if ((attachmentNode is null) != (attachmentSource is null) ||
            (disposition == AuthoredPrnRootMarkerDisposition &&
             attachmentNode is null))
            throw new InvalidOperationException(
                $"Actor omitted surface has an invalid attachment contract: {sidecarPath}.");
        return new OmittedSurface(
            RequireSurfaceText(surface, "role", sidecarPath),
            RequireSurfaceText(surface, "modelPath", sidecarPath),
            RequireSurfaceText(surface, "modelSha256", sidecarPath),
            RequireSurfaceText(surface, "shape", sidecarPath),
            attachmentNode,
            attachmentSource,
            disposition,
            RequireSurfaceText(surface, "authority", sidecarPath));
    }

    private static string? OptionalSurfaceText(
        JsonElement surface,
        string property,
        string sidecarPath)
    {
        if (!surface.TryGetProperty(property, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException(
                $"Actor sidecar surface has invalid {property}: {sidecarPath}.");
        return value.GetString();
    }

    private static uint? OptionalSurfaceUInt32(
        JsonElement surface,
        string property,
        string sidecarPath)
    {
        if (!surface.TryGetProperty(property, out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt32(out var result))
            throw new InvalidOperationException(
                $"Actor sidecar surface has invalid {property}: {sidecarPath}.");
        return result;
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

    internal static Aabb WorldBounds(Node3D root)
    {
        return WorldBounds(NodeTraversal.Descendants<MeshInstance3D>(root));
    }

    internal static Aabb PosedWorldBounds(
        LoadedActor actor,
        bool includeWeapons = true) =>
        PosedWorldBounds(actor.Root, actor.Surfaces, includeWeapons);

    internal static Aabb PosedWorldBounds(
        LoadedActor actor,
        LoadedSurface surface) =>
        PosedWorldBounds(actor.Root, [surface], includeWeapons: true);

    internal static Vector3? PosedSemanticCenter(
        LoadedActor actor,
        params string[] roles)
    {
        var roleSet = roles.ToHashSet(StringComparer.Ordinal);
        var surfaces = actor.Surfaces
            .Where(surface => roleSet.Contains(surface.Role))
            .ToArray();
        return surfaces.Length == 0
            ? null
            : PosedWorldBounds(actor.Root, surfaces, includeWeapons: true).GetCenter();
    }

    internal static IReadOnlyList<Vector3> PosedWorldVertices(
        LoadedActor actor,
        bool includeWeapons = true)
    {
        var vertices = new List<Vector3>();
        VisitPosedWorldVertices(
            actor.Root,
            actor.Surfaces,
            includeWeapons,
            vertices.Add);
        if (vertices.Count == 0)
            throw new InvalidOperationException(
                "Actor scene contains no finite posed visual vertices.");
        return vertices;
    }

    internal static IReadOnlyList<Vector3> PosedWorldVertices(
        LoadedActor actor,
        LoadedSurface surface)
    {
        var vertices = new List<Vector3>();
        VisitPosedWorldVertices(
            actor.Root,
            [surface],
            includeWeapons: true,
            vertices.Add);
        if (vertices.Count == 0)
            throw new InvalidOperationException(
                $"Actor surface contains no finite posed vertices: " +
                $"{surface.Role}/{surface.Shape}.");
        return vertices;
    }

    internal static IReadOnlyList<PosedTriangle> PosedWorldTriangles(
        LoadedActor actor,
        LoadedSurface surface)
    {
        var mesh = surface.Mesh.Mesh
            ?? throw new InvalidOperationException(
                $"Actor surface has no runtime mesh: {surface.Role}/{surface.Shape}.");
        var skeleton = surface.Skinned
            ? ResolveSkeleton(actor.Root, surface.Mesh)
            : null;
        var palette = surface.Skinned
            ? BuildSkinPalette(surface, skeleton!)
            : Array.Empty<Transform3D>();
        var triangles = new List<PosedTriangle>();
        for (var surfaceIndex = 0;
             surfaceIndex < mesh.GetSurfaceCount();
             surfaceIndex++)
        {
            var arrays = mesh.SurfaceGetArrays(surfaceIndex);
            var vertices = PoseSurfaceVertices(surface, surfaceIndex, arrays, palette);
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (indices.Length == 0)
            {
                if (vertices.Length % 3 != 0)
                    throw new InvalidOperationException(
                        $"Unindexed actor triangle surface has a partial triangle: " +
                        $"{surface.Role}/{surface.Shape}.");
                for (var index = 0; index < vertices.Length; index += 3)
                    triangles.Add(new PosedTriangle(
                        vertices[index],
                        vertices[index + 1],
                        vertices[index + 2]));
                continue;
            }
            if (indices.Length % 3 != 0 ||
                indices.Any(index => index < 0 || index >= vertices.Length))
                throw new InvalidOperationException(
                    $"Indexed actor triangle surface is invalid: " +
                    $"{surface.Role}/{surface.Shape}.");
            for (var index = 0; index < indices.Length; index += 3)
                triangles.Add(new PosedTriangle(
                    vertices[indices[index]],
                    vertices[indices[index + 1]],
                    vertices[indices[index + 2]]));
        }
        if (triangles.Count == 0)
            throw new InvalidOperationException(
                $"Actor surface contains no posed triangles: " +
                $"{surface.Role}/{surface.Shape}.");
        return triangles;
    }

    private static Aabb PosedWorldBounds(
        Node3D actorRoot,
        IReadOnlyList<LoadedSurface> surfaces,
        bool includeWeapons)
    {
        var minimum = new Vector3(
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity);
        var maximum = new Vector3(
            float.NegativeInfinity,
            float.NegativeInfinity,
            float.NegativeInfinity);
        var points = 0;
        VisitPosedWorldVertices(actorRoot, surfaces, includeWeapons, Expand);
        if (points < 1 || !minimum.IsFinite() || !maximum.IsFinite())
            throw new InvalidOperationException(
                "Actor scene contains no finite posed visual bounds.");
        return new Aabb(minimum, maximum - minimum);

        void Expand(Vector3 point)
        {
            minimum = minimum.Min(point);
            maximum = maximum.Max(point);
            points++;
        }
    }

    private static void VisitPosedWorldVertices(
        Node3D actorRoot,
        IReadOnlyList<LoadedSurface> surfaces,
        bool includeWeapons,
        Action<Vector3> visit)
    {
        foreach (var surface in surfaces.Where(surface =>
                     includeWeapons || surface.Role != WeaponSurfaceRole))
        {
            var mesh = surface.Mesh.Mesh
                ?? throw new InvalidOperationException(
                    $"Actor surface has no runtime mesh: {surface.Role}/{surface.Shape}.");
            var skeleton = surface.Skinned
                ? ResolveSkeleton(actorRoot, surface.Mesh)
                : null;
            var palette = surface.Skinned
                ? BuildSkinPalette(surface, skeleton!)
                : Array.Empty<Transform3D>();
            for (var surfaceIndex = 0;
                 surfaceIndex < mesh.GetSurfaceCount();
                 surfaceIndex++)
            {
                var arrays = mesh.SurfaceGetArrays(surfaceIndex);
                foreach (var vertex in PoseSurfaceVertices(
                             surface,
                             surfaceIndex,
                             arrays,
                             palette))
                    Visit(vertex);
            }
        }

        void Visit(Vector3 point)
        {
            if (!point.IsFinite())
                throw new InvalidOperationException(
                    "Actor posed visual bounds contain a non-finite vertex.");
            visit(point);
        }
    }

    private static Vector3[] PoseSurfaceVertices(
        LoadedSurface surface,
        int surfaceIndex,
        Godot.Collections.Array arrays,
        IReadOnlyList<Transform3D> palette)
    {
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (vertices.Length < 1)
            throw new InvalidOperationException(
                $"Actor surface contains no vertices: {surface.Role}/{surface.Shape}.");
        if (!surface.Skinned)
            return vertices.Select(surface.Mesh.ToGlobal).ToArray();

        var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
        var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
        var arrayMesh = surface.Mesh.Mesh as ArrayMesh
            ?? throw new InvalidOperationException(
                $"Actor surface is not an ArrayMesh: {surface.Role}/{surface.Shape}.");
        var format = arrayMesh.SurfaceGetFormat(surfaceIndex);
        var influences = (format & Mesh.ArrayFormat.FlagUse8BoneWeights) != 0
            ? RenderingServer.ArrayWeightsSize * 2
            : RenderingServer.ArrayWeightsSize;
        if (bones.Length != vertices.Length * influences ||
            weights.Length != bones.Length)
            throw new InvalidOperationException(
                $"Actor skin arrays disagree for {surface.Role}/{surface.Shape}: " +
                $"vertices={vertices.Length} bones={bones.Length} " +
                $"weights={weights.Length} influences={influences}.");
        var posed = new Vector3[vertices.Length];
        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            var deformed = Vector3.Zero;
            var weightSum = 0.0f;
            var influenceStart = vertexIndex * influences;
            for (var influence = 0; influence < influences; influence++)
            {
                var weight = weights[influenceStart + influence];
                if (weight <= 0.0f)
                    continue;
                var bindIndex = bones[influenceStart + influence];
                if (bindIndex < 0 || bindIndex >= palette.Count ||
                    !float.IsFinite(weight))
                    throw new InvalidOperationException(
                        $"Actor skin influence is invalid for " +
                        $"{surface.Role}/{surface.Shape}.");
                deformed += palette[bindIndex] * vertices[vertexIndex] * weight;
                weightSum += weight;
            }
            if (!float.IsFinite(weightSum) || weightSum <= 0.0f)
                throw new InvalidOperationException(
                    $"Actor skin vertex has no finite weight for " +
                    $"{surface.Role}/{surface.Shape}.");
            posed[vertexIndex] = deformed / weightSum;
        }
        return posed;
    }

    private static Skeleton3D ResolveSkeleton(
        Node3D actorRoot,
        MeshInstance3D mesh)
    {
        var direct = mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton);
        if (direct is not null)
            return direct;
        var skeletons = NodeTraversal.Descendants<Skeleton3D>(actorRoot).ToArray();
        if (skeletons.Length != 1)
            throw new InvalidOperationException(
                $"Actor skinned surface {mesh.Name} resolves {skeletons.Length} skeletons.");
        return skeletons[0];
    }

    private static Transform3D[] BuildSkinPalette(
        LoadedSurface surface,
        Skeleton3D skeleton)
    {
        var skin = surface.Mesh.Skin
            ?? throw new InvalidOperationException(
                $"Actor skinned surface has no Skin: {surface.Role}/{surface.Shape}.");
        var palette = new Transform3D[skin.GetBindCount()];
        for (var bindIndex = 0;
             bindIndex < skin.GetBindCount();
             bindIndex++)
        {
            var bindName = skin.GetBindName(bindIndex).ToString();
            var boneIndex = string.IsNullOrEmpty(bindName)
                ? skin.GetBindBone(bindIndex)
                : skeleton.FindBone(bindName);
            if (boneIndex < 0 || boneIndex >= skeleton.GetBoneCount())
                throw new InvalidOperationException(
                    $"Actor skin bind {bindIndex} does not resolve a skeleton bone for " +
                    $"{surface.Role}/{surface.Shape}.");
            palette[bindIndex] = skeleton.GlobalTransform *
                skeleton.GetBoneGlobalPose(boneIndex) *
                skin.GetBindPose(bindIndex);
        }
        return palette;
    }

    private static Aabb WorldBounds(IEnumerable<MeshInstance3D> meshes)
    {
        var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var points = 0;
        foreach (var mesh in meshes)
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

    internal readonly record struct LoadedActor(
        Node3D Root,
        string FormId,
        string Name,
        int Meshes,
        int Skeletons,
        int Animations,
        string AnimationLogicalPath,
        string AnimationSourceSha256,
        int AnimationChannels,
        string PlayingAnimation,
        AnimationPlayer AnimationPlayer,
        ActorPoseContract PoseContract,
        Aabb Bounds,
        int AuthoredSurfaces,
        int AuthoredTextures,
        IReadOnlyList<LoadedSurface> Surfaces,
        IReadOnlyList<OmittedSurface> OmittedSurfaces,
        IReadOnlyList<LoadedAnimation> LoadedAnimations);

    internal readonly record struct LoadedAnimation(
        string LogicalPath,
        string SourceSha256,
        string AccumulationRootTranslationDisposition,
        int Channels,
        string SequenceName,
        float StartSeconds,
        float StopSeconds,
        int CycleType,
        IReadOnlyDictionary<string, int> TransformPrioritiesByNode,
        string RuntimeName,
        AnimationPlayer Player,
        string? Role = null,
        LoadedRootMotion? RootMotion = null,
        IReadOnlyList<LoadedTextKey>? TextKeys = null);

    internal readonly record struct LoadedRootMotion(
        string TargetNode,
        Vector3 DisplacementGodotGameUnits,
        float SpeedGameUnitsPerSecond);

    internal readonly record struct LoadedTextKey(
        float TimeSeconds,
        string Value);

    internal readonly record struct PosedTriangle(Vector3 A, Vector3 B, Vector3 C);

    internal readonly record struct LoadedSurface(
        string Role,
        string Shape,
        string SourceShape,
        string ModelPath,
        string ModelSha256,
        int SourceVertexCount,
        uint SourceVertexFnv1a32,
        string RuntimeNodeName,
        MeshInstance3D Mesh,
        bool Skinned,
        string? AttachmentNode,
        string? SourceFormId,
        uint? SourceSlot,
        bool SourceShaderSkin,
        string? RetailGeometryName,
        string? RetailVisualNodePath,
        IReadOnlyList<string> FaceGenMorphTargets,
        bool RigidShapeTransformBaked,
        IReadOnlyList<float> SourceShapeTransformGodotMatrix);

    internal readonly record struct OmittedSurface(
        string Role,
        string ModelPath,
        string ModelSha256,
        string Shape,
        string? AttachmentNode,
        string? AttachmentSource,
        string Disposition,
        string Authority);

    internal enum BoundsContract
    {
        Humanoid,
        FirstPersonHand,
        AnyActor,
    }
}
