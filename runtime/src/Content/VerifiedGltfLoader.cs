using System.Security.Cryptography;
using System.Text.Json;
using Godot;


namespace OpenNV.Runtime.Content;

internal static class VerifiedGltfLoader
{
    private const string SidecarSchemaV1 = "opennv-static-nif-gltf/v1";
    private const string SidecarSchemaV2 = "opennv-static-nif-gltf/v2";
    private const string SidecarSchemaV3 = "opennv-static-nif-gltf/v3";
    private const string LandscapeSidecarSchema = "opennv-landscape-gltf/v1";
    private const string AuthoredCollisionSchemaV2 = "opennv-authored-collision-gltf/v2";
    private const string AuthoredConvexShapeType = "convex-hull-points";
    private const float HavokToGameUnits = 7.0f;
    private const string AuthoredRigidBodyTransformPolicy =
        "articulation-target-local;bhkRigidBody-pose-evidence-only;godot-axis-converted";
    private const string AuthoredRigidBodyTTransformPolicy =
        "articulation-target-local;bhkRigidBodyT-pose-applied;godot-axis-converted";

    internal static LoadedGltf Load(string modelPath, string sidecarPath)
    {
        var sidecarFile = ResolvePath(sidecarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(sidecarFile));
        var root = document.RootElement;
        var schema = root.GetProperty("schema").GetString();
        if (schema != SidecarSchemaV1 && schema != SidecarSchemaV2 &&
            schema != SidecarSchemaV3 && schema != LandscapeSidecarSchema)
            throw new InvalidOperationException($"Unexpected sidecar schema: {sidecarPath}");
        var status = root.GetProperty("status").GetString();
        var expectedStatus = schema == LandscapeSidecarSchema
            ? "layered-material"
            : "geometry-only";
        if (status != expectedStatus)
            throw new InvalidOperationException(
                $"Static slice requires {expectedStatus} status: {sidecarPath}");

        var modelFile = ResolvePath(modelPath);
        var outputs = root.GetProperty("outputs");
        var gltf = outputs.GetProperty("gltf");
        VerifyHash(modelFile, gltf.GetProperty("sha256").GetString()!);
        var buffer = outputs.GetProperty("buffer");
        var bufferFile = Path.Combine(Path.GetDirectoryName(modelFile)!, buffer.GetProperty("file").GetString()!);
        VerifyHash(bufferFile, buffer.GetProperty("sha256").GetString()!);

        var scene = LoadScene(modelFile);
        Node3D? collisionScene = null;
        IReadOnlyList<AuthoredConvexBodyContract> authoredConvexBodies =
            Array.Empty<AuthoredConvexBodyContract>();
        if (outputs.TryGetProperty("collisionGltf", out var collisionGltf))
        {
            var collisionFile = Path.Combine(
                Path.GetDirectoryName(modelFile)!,
                collisionGltf.GetProperty("file").GetString()!);
            VerifyHash(collisionFile, collisionGltf.GetProperty("sha256").GetString()!);
            var collisionBuffer = outputs.GetProperty("collisionBuffer");
            var collisionBufferFile = Path.Combine(
                Path.GetDirectoryName(modelFile)!,
                collisionBuffer.GetProperty("file").GetString()!);
            VerifyHash(collisionBufferFile, collisionBuffer.GetProperty("sha256").GetString()!);
            authoredConvexBodies = ReadAuthoredConvexBodies(
                root,
                collisionFile);
            collisionScene = LoadScene(collisionFile);
        }
        else if (HasAuthoredConvexBodies(root))
        {
            throw new InvalidOperationException(
                $"Authored convex collision has no verified glTF payload: {sidecarPath}");
        }
        var compiler = root.GetProperty("compiler");
        var dynamicBodies = ReadDynamicBodies(root);
        string? articulationJson = null;
        if (root.TryGetProperty("articulation", out var articulation) &&
            articulation.ValueKind != JsonValueKind.Null)
        {
            if (articulation.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException(
                    $"Static NIF articulation is not an object: {sidecarPath}");
            articulationJson = articulation.GetRawText();
        }
        return new LoadedGltf(
            scene,
            collisionScene,
            authoredConvexBodies,
            dynamicBodies,
            articulationJson,
            root.GetProperty("source").GetProperty("sha256").GetString()!,
            compiler.GetProperty("name").GetString()!,
            compiler.GetProperty("sha256").GetString()!);
    }

    private static bool HasAuthoredConvexBodies(JsonElement root) =>
        root.GetProperty("coverage")
            .TryGetProperty("staticConvexBodies", out var bodies) &&
        bodies.GetArrayLength() > 0;

    private static IReadOnlyList<AuthoredConvexBodyContract> ReadAuthoredConvexBodies(
        JsonElement root,
        string collisionFile)
    {
        var coverage = root.GetProperty("coverage");
        if (!coverage.TryGetProperty("staticConvexBodies", out var sourceRows))
            return Array.Empty<AuthoredConvexBodyContract>();
        var rows = sourceRows
            .EnumerateArray()
            .Select(ReadAuthoredConvexBody)
            .OrderBy(body => body.BodyBlock)
            .ToArray();
        if (rows.Length == 0)
            return rows;
        if (rows.Select(body => body.BodyBlock).Distinct().Count() != rows.Length)
            throw new InvalidOperationException(
                $"Authored convex collision body blocks are not unique: {collisionFile}");

        using var collisionDocument = JsonDocument.Parse(File.ReadAllText(collisionFile));
        var collisionRoot = collisionDocument.RootElement;
        var collisionExtras = collisionRoot.GetProperty("extras");
        var schema = collisionExtras.GetProperty("openNvSchema").GetString();
        if (schema != AuthoredCollisionSchemaV2 ||
            collisionExtras.GetProperty("sourceSha256").GetString() !=
                root.GetProperty("source").GetProperty("sha256").GetString())
            throw new InvalidOperationException(
                $"Authored convex collision schema/source identity is invalid: {collisionFile}");

        var nodes = collisionRoot.GetProperty("nodes").EnumerateArray().ToArray();
        var meshes = collisionRoot.GetProperty("meshes").EnumerateArray().ToArray();
        var accessors = collisionRoot.GetProperty("accessors").EnumerateArray().ToArray();
        var expectedNames = rows.Select(NodeNameFor).ToHashSet(StringComparer.Ordinal);
        var declaredNames = nodes
            .Where(node => node.TryGetProperty("extras", out var extras) &&
                extras.TryGetProperty("openNvCollisionShapeType", out var shapeType) &&
                shapeType.GetString() == AuthoredConvexShapeType)
            .Select(node => node.GetProperty("name").GetString()!)
            .ToArray();
        if (declaredNames.Length != expectedNames.Count ||
            !declaredNames.ToHashSet(StringComparer.Ordinal).SetEquals(expectedNames))
            throw new InvalidOperationException(
                $"Authored convex collision node membership differs from its sidecar: {collisionFile}");

        foreach (var body in rows)
        {
            var expectedName = NodeNameFor(body);
            var matches = nodes.Where(node =>
                    node.GetProperty("name").GetString() == expectedName)
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Authored convex collision node is missing or duplicated: {expectedName}");
            ValidateAuthoredConvexNode(
                matches[0],
                body,
                meshes,
                accessors,
                collisionFile);
        }
        return rows;
    }

    private static AuthoredConvexBodyContract ReadAuthoredConvexBody(JsonElement source)
    {
        var points = source.GetProperty("pointsGodotGameUnits")
            .EnumerateArray()
            .Select(ReadFiniteVector3)
            .ToArray();
        var ownerTargetId = source.GetProperty("ownerTargetId").GetString();
        var targetName = source.GetProperty("targetName").GetString();
        var bodyType = source.GetProperty("bodyType").GetString();
        var transformPolicy = source.GetProperty("shapeTransformPolicy").GetString();
        var sourceBodyTranslation = ReadFiniteVector3(
            source.GetProperty("sourceBodyTranslationHavokUnits"));
        var sourceBodyRotation = ReadQuaternion(source.GetProperty("sourceBodyRotation"));
        var mass = source.GetProperty("mass").GetSingle();
        var friction = source.GetProperty("friction").GetSingle();
        var restitution = source.GetProperty("restitution").GetSingle();
        var linearDamping = source.GetProperty("linearDamping").GetSingle();
        var angularDamping = source.GetProperty("angularDamping").GetSingle();
        var radiusHavokUnits = source.GetProperty("radiusHavokUnits").GetSingle();
        var radiusGameUnits = source.GetProperty("radiusGameUnits").GetSingle();
        var collisionObjectBlock = source.GetProperty("collisionObjectBlock").GetInt32();
        var bodyBlock = source.GetProperty("bodyBlock").GetInt32();
        var shapeBlock = source.GetProperty("shapeBlock").GetInt32();
        var targetBlock = source.GetProperty("targetBlock").GetInt32();
        var layer = source.GetProperty("layer").GetInt32();
        var flagsAndPartNumber = source.GetProperty("flagsAndPartNumber").GetInt32();
        var unknownShort = source.GetProperty("unknownShort").GetInt32();
        var material = source.GetProperty("material").GetInt32();
        var motionSystem = source.GetProperty("motionSystem").GetInt32();
        var qualityType = source.GetProperty("qualityType").GetInt32();
        if (string.IsNullOrWhiteSpace(ownerTargetId) ||
            string.IsNullOrWhiteSpace(targetName) ||
            bodyType is not ("bhkRigidBody" or "bhkRigidBodyT") ||
            transformPolicy != (bodyType == "bhkRigidBody"
                ? AuthoredRigidBodyTransformPolicy
                : AuthoredRigidBodyTTransformPolicy) ||
            source.GetProperty("shapeType").GetString() != AuthoredConvexShapeType ||
            !float.IsFinite(sourceBodyRotation.X) ||
            !float.IsFinite(sourceBodyRotation.Y) ||
            !float.IsFinite(sourceBodyRotation.Z) ||
            !float.IsFinite(sourceBodyRotation.W) ||
            (bodyType == "bhkRigidBodyT" &&
                !Mathf.IsEqualApprox(sourceBodyRotation.LengthSquared(), 1.0f)) ||
            mass != 0.0f ||
            !AreFiniteNonNegative(
                friction,
                restitution,
                linearDamping,
                angularDamping,
                radiusHavokUnits,
                radiusGameUnits) ||
            !Mathf.IsEqualApprox(radiusGameUnits, radiusHavokUnits * HavokToGameUnits) ||
            collisionObjectBlock < 0 || bodyBlock < 0 || shapeBlock < 0 || targetBlock < 0 ||
            layer < 0 || flagsAndPartNumber < 0 || unknownShort < 0 || material < 0 ||
            motionSystem < 0 || qualityType < 0 ||
            source.GetProperty("vertices").GetInt32() != points.Length ||
            source.GetProperty("triangles").GetInt32() != 0 ||
            points.Length < 4 || points.Distinct().Count() < 4 || !HasVolume(points))
            throw new InvalidOperationException(
                $"Unsupported or incomplete authored convex collision body: {bodyBlock}");

        return new AuthoredConvexBodyContract(
            collisionObjectBlock,
            bodyBlock,
            shapeBlock,
            targetBlock,
            targetName,
            ownerTargetId,
            bodyType,
            transformPolicy,
            sourceBodyTranslation,
            sourceBodyRotation,
            mass,
            friction,
            restitution,
            linearDamping,
            angularDamping,
            motionSystem,
            qualityType,
            layer,
            flagsAndPartNumber,
            unknownShort,
            material,
            radiusHavokUnits,
            radiusGameUnits,
            points);
    }

    private static void ValidateAuthoredConvexNode(
        JsonElement node,
        AuthoredConvexBodyContract body,
        IReadOnlyList<JsonElement> meshes,
        IReadOnlyList<JsonElement> accessors,
        string collisionFile)
    {
        var extras = node.GetProperty("extras");
        if (extras.GetProperty("openNvCollisionShapeType").GetString() !=
                AuthoredConvexShapeType ||
            extras.GetProperty("openNvCollisionBodyBlock").GetInt32() != body.BodyBlock ||
            extras.GetProperty("openNvArticulationTargetId").GetString() != body.OwnerTargetId ||
            node.TryGetProperty("children", out _) ||
            node.TryGetProperty("translation", out _) ||
            node.TryGetProperty("rotation", out _) ||
            node.TryGetProperty("scale", out _) ||
            node.TryGetProperty("matrix", out _))
            throw new InvalidOperationException(
                $"Authored convex collision node identity is invalid: {NodeNameFor(body)}");

        var meshIndex = node.GetProperty("mesh").GetInt32();
        if (meshIndex < 0 || meshIndex >= meshes.Count)
            throw new InvalidOperationException(
                $"Authored convex collision mesh index is invalid: {NodeNameFor(body)}");
        var primitives = meshes[meshIndex].GetProperty("primitives").EnumerateArray().ToArray();
        if (primitives.Length != 1)
            throw new InvalidOperationException(
                $"Authored convex collision must have one point primitive: {NodeNameFor(body)}");
        var primitive = primitives[0];
        var attributes = primitive.GetProperty("attributes");
        var positionAccessor = attributes.GetProperty("POSITION").GetInt32();
        if (primitive.GetProperty("mode").GetInt32() != 0 ||
            primitive.TryGetProperty("indices", out _) ||
            attributes.EnumerateObject().Count() != 1 ||
            positionAccessor < 0 || positionAccessor >= accessors.Count)
            throw new InvalidOperationException(
                $"Authored convex collision point primitive is invalid: {NodeNameFor(body)}");
        var accessor = accessors[positionAccessor];
        if (accessor.GetProperty("componentType").GetInt32() != 5126 ||
            accessor.GetProperty("type").GetString() != "VEC3" ||
            accessor.GetProperty("count").GetInt32() != body.PointsGodotGameUnits.Count)
            throw new InvalidOperationException(
                $"Authored convex collision point accessor differs from its sidecar: {collisionFile}");
    }

    private static string NodeNameFor(AuthoredConvexBodyContract body) =>
        $"OPENNV_ARTICULATION_COLLISION_BODY_{body.BodyBlock}";

    private static bool HasVolume(IReadOnlyList<Vector3> points)
    {
        var origin = points[0];
        for (var first = 1; first < points.Count - 2; first++)
        {
            var firstEdge = points[first] - origin;
            for (var second = first + 1; second < points.Count - 1; second++)
            {
                var area = firstEdge.Cross(points[second] - origin);
                for (var third = second + 1; third < points.Count; third++)
                {
                    if (MathF.Abs(area.Dot(points[third] - origin)) > 0.000001f)
                        return true;
                }
            }
        }
        return false;
    }

    private static bool AreFiniteNonNegative(params float[] values) =>
        values.All(value => float.IsFinite(value) && value >= 0.0f);

    private static IReadOnlyList<DynamicBodyContract> ReadDynamicBodies(JsonElement root)
    {
        var coverage = root.GetProperty("coverage");
        if (!coverage.TryGetProperty("dynamicPhysicsBodies", out var bodies))
            return Array.Empty<DynamicBodyContract>();
        return bodies.EnumerateArray().Select(body => new DynamicBodyContract(
            body.GetProperty("targetName").GetString()!,
            body.GetProperty("shapeType").GetString()!,
            body.GetProperty("shapeTransformPolicy").GetString()!,
            ReadVector3(body.GetProperty("sourceBodyTranslationHavokUnits")),
            ReadQuaternion(body.GetProperty("sourceBodyRotation")),
            body.GetProperty("mass").GetSingle(),
            body.GetProperty("friction").GetSingle(),
            body.GetProperty("restitution").GetSingle(),
            body.GetProperty("linearDamping").GetSingle(),
            body.GetProperty("angularDamping").GetSingle(),
            body.GetProperty("motionSystem").GetInt32(),
            body.GetProperty("qualityType").GetInt32(),
            body.GetProperty("layer").GetInt32(),
            body.GetProperty("hulls").EnumerateArray().Select(hull => new ConvexHullContract(
                hull.GetProperty("radiusGameUnits").GetSingle(),
                hull.GetProperty("pointsGodotGameUnits").EnumerateArray()
                    .Select(ReadVector3)
                    .ToArray()))
                .ToArray()))
            .ToArray();
    }

    private static Vector3 ReadVector3(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("Dynamic physics vector must contain three values.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector3 ReadFiniteVector3(JsonElement source)
    {
        var value = ReadVector3(source);
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidOperationException("Authored collision point must be finite.");
        return value;
    }

    private static Quaternion ReadQuaternion(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("Dynamic physics quaternion must contain four values.");
        return new Quaternion(values[0], values[1], values[2], values[3]);
    }

    private static Node3D LoadScene(string modelFile)
    {
        var gltfDocument = new GltfDocument();
        var state = new GltfState();
        var error = gltfDocument.AppendFromFile(modelFile, state, 0, Path.GetDirectoryName(modelFile)!);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Godot glTF import failed ({error}): {modelFile}");
        return gltfDocument.GenerateScene(state) as Node3D
            ?? throw new InvalidOperationException($"Godot generated no Node3D scene from glTF: {modelFile}");
    }

    internal static string ResolvePath(string path) =>
        path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    internal static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provenance hash mismatch: {path}");
    }

    internal readonly record struct LoadedGltf(
        Node3D Scene,
        Node3D? CollisionScene,
        IReadOnlyList<AuthoredConvexBodyContract> AuthoredConvexBodies,
        IReadOnlyList<DynamicBodyContract> DynamicPhysicsBodies,
        string? ArticulationJson,
        string SourceSha256,
        string CompilerName,
        string CompilerSha256);

    internal readonly record struct AuthoredConvexBodyContract(
        int CollisionObjectBlock,
        int BodyBlock,
        int ShapeBlock,
        int TargetBlock,
        string TargetName,
        string OwnerTargetId,
        string BodyType,
        string ShapeTransformPolicy,
        Vector3 SourceBodyTranslationHavokUnits,
        Quaternion SourceBodyRotation,
        float Mass,
        float Friction,
        float Restitution,
        float LinearDamping,
        float AngularDamping,
        int MotionSystem,
        int QualityType,
        int Layer,
        int FlagsAndPartNumber,
        int UnknownShort,
        int Material,
        float RadiusHavokUnits,
        float RadiusGameUnits,
        IReadOnlyList<Vector3> PointsGodotGameUnits);

    internal readonly record struct DynamicBodyContract(
        string TargetName,
        string ShapeType,
        string ShapeTransformPolicy,
        Vector3 SourceBodyTranslationHavokUnits,
        Quaternion SourceBodyRotation,
        float Mass,
        float Friction,
        float Restitution,
        float LinearDamping,
        float AngularDamping,
        int MotionSystem,
        int QualityType,
        int Layer,
        IReadOnlyList<ConvexHullContract> Hulls);

    internal readonly record struct ConvexHullContract(
        float RadiusGameUnits,
        IReadOnlyList<Vector3> PointsGodotGameUnits);
}
