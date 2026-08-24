using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal static class ActorReviewContract
{
    private const string ContractSchema = "opennv-actor-review-contract/v4";
    private const string PendingStatus = "retail-observed-godot-pending";
    private const string AppearanceSchema = "nikami-fnv-sidecar-appearance/v1";
    private const float NormalizedViewportMinimum = 0.0f;
    private const float NormalizedViewportMaximum = 1.0f;
    private const int PerspectiveFrustumElementCount = 7;
    private const int FrustumFarIndex = 5;
    private const int FrustumOrthographicIndex = 6;
    private const int HomogeneousMatrixElementCount = 16;
    private const int SkinMatrixRows = 3;
    private const int SkinMatrixColumns = 4;
    private const string CapturedSkinStatus = "captured";
    private const string UncachedSkinStatus = "not-render-cached";
    private const string SkinMatrixLayout = "row-major-3x4";
    private const string SkinMatrixStage = "retail-skin-shader-preprojection";
    private const string SkinMatrixSpace = "camera-origin-relative-gamebryo-world";
    private const string SkinTranslationOrigin = "validated-nicamera-world-translation";
    private const string FinalEyeProjectionStatus =
        "exact-retail-final-eye-d3d9-perspective";

    internal static Contract Load(
        string path,
        string expectedReviewKey,
        RuntimeConfiguration configuration)
    {
        var resolved = Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(resolved));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ContractSchema ||
            root.GetProperty("status").GetString() != PendingStatus)
            throw new InvalidOperationException($"Unexpected actor review contract: {resolved}");
        var review = root.GetProperty("review");
        var reviewKey = RequireText(review, "reviewKey", "review key");
        if (!reviewKey.Equals(expectedReviewKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Actor review contract belongs to another compiled actor.");
        var recordType = RequireText(root.GetProperty("assembly"), "recordType", "record type");
        if (recordType is not ("NPC_" or "CREA"))
            throw new InvalidOperationException($"Unsupported actor review record type: {recordType}");

        var retail = root.GetProperty("retail");
        var environment = ParseEnvironment(retail.GetProperty("environment"));
        var appearance = ParseAppearance(retail.GetProperty("appearance"));
        var shots = retail.GetProperty("shots").EnumerateArray().Select(ParseShot).ToArray();
        if (shots.Length == 0 ||
            shots.Select(shot => shot.Kind).Distinct(StringComparer.Ordinal).Count() != shots.Length)
            throw new InvalidOperationException("Actor review shots must be nonempty and uniquely named.");
        var samples = shots.SelectMany(shot => shot.Samples).ToArray();
        if (samples.Select(sample => sample.Frame).Distinct().Count() != samples.Length)
            throw new InvalidOperationException("Actor review source frames must be globally unique.");
        if (samples.Select(sample => (sample.SourceFrame.Width, sample.SourceFrame.Height))
                .Distinct().Count() != 1)
            throw new InvalidOperationException("Actor review source-frame dimensions change across shots.");
        foreach (var sample in samples)
            ValidateCamera(sample, configuration);
        var expectedNodes = samples[0].Nodes
            .Select(node => (node.NodePath, node.Name, node.ParentName))
            .ToArray();
        if (samples.Skip(1).Any(sample => !sample.Nodes
                .Select(node => (node.NodePath, node.Name, node.ParentName))
                .SequenceEqual(expectedNodes)))
            throw new InvalidOperationException("Retail named-node topology changes across actor review frames.");
        var expectedSkins = SkinTopology(samples[0].SkinPalette);
        if (samples.Skip(1).Any(sample => !SkinTopology(sample.SkinPalette)
                .SequenceEqual(expectedSkins)))
            throw new InvalidOperationException(
                "Retail skin-palette topology changes across actor review frames.");

        return new Contract(
            resolved,
            FileSha256(resolved),
            reviewKey,
            recordType,
            environment,
            appearance,
            shots,
            samples.All(sample => sample.ProjectionExact));
    }

    private static EnvironmentState ParseEnvironment(JsonElement source)
    {
        return new EnvironmentState(
            ReadColor(source.GetProperty("sunAmbient"), "sun ambient"),
            ReadColor(source.GetProperty("sunDirectional"), "sun directional"),
            ReadColor(source.GetProperty("sunFog"), "sun fog"),
            source.GetProperty("currentWeatherForm").GetUInt32(),
            source.GetProperty("gameHour").GetSingle());
    }

    private static AppearanceState ParseAppearance(JsonElement source)
    {
        var snapshot = source.GetProperty("snapshot");
        if (snapshot.GetProperty("schema").GetString() != AppearanceSchema ||
            !snapshot.GetProperty("complete").GetBoolean() ||
            snapshot.GetProperty("truncated").GetBoolean())
            throw new InvalidOperationException("Actor review appearance evidence is incomplete.");
        var parts = snapshot.GetProperty("renderParts").EnumerateArray().ToArray();
        if (parts.Length == 0)
            throw new InvalidOperationException("Actor review appearance evidence has no render parts.");
        var textureBindings = parts.Sum(part =>
            part.GetProperty("textureBindings").GetArrayLength());
        return new AppearanceState(
            source.GetProperty("frame").GetInt32(),
            RequireText(source, "eventSha256", "appearance event hash"),
            parts.Length,
            textureBindings);
    }

    private static Shot ParseShot(JsonElement source)
    {
        var kind = RequireText(source, "kind", "shot kind");
        var projection = source.GetProperty("projection");
        var projectionExact = projection.GetProperty("exact").GetBoolean();
        var projectionStatus = RequireText(projection, "status", "projection status");
        if (!projectionExact || projectionStatus != FinalEyeProjectionStatus)
            throw new InvalidOperationException(
                $"Actor review shot {kind} does not carry an exact retail perspective projection.");
        var samples = source.GetProperty("samples").EnumerateArray()
            .Select(sample => ParseSample(sample, kind, projectionExact, projectionStatus))
            .ToArray();
        if (samples.Length == 0)
            throw new InvalidOperationException($"Actor review shot {kind} has no samples.");
        return new Shot(
            kind,
            source.GetProperty("setFrame").GetInt32(),
            RequireText(source, "focusNode", "focus node"),
            RequireText(source, "focusKind", "focus kind"),
            source.GetProperty("cameraDistanceGameUnits").GetSingle(),
            projectionExact,
            projectionStatus,
            samples);
    }

    private static Sample ParseSample(
        JsonElement source,
        string shotKind,
        bool projectionExact,
        string projectionStatus)
    {
        var frame = source.GetProperty("frame").GetInt32();
        var frameEvidence = ParseFileEvidence(source.GetProperty("sourceFrame"));
        var root = ReadRootTransform(
            source.GetProperty("actorRoot"),
            $"frame {frame} actor root");
        var nodes = source.GetProperty("nodes").EnumerateArray()
            .Select(node => ParseNode(node, frame))
            .ToArray();
        if (nodes.Length == 0 ||
            nodes.Select(node => node.NodePath).Distinct(StringComparer.Ordinal).Count() != nodes.Length)
            throw new InvalidOperationException(
                $"Actor review frame {frame} has missing or duplicate named-node paths.");
        var camera = ParseCamera(source.GetProperty("camera"), root, frame);
        var skinPalette = ParseSkinPalette(source.GetProperty("skinPalette"), frame);
        if (!skinPalette.FinalProjectionEventSha256.Equals(
                camera.Surface.EventSha256,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Actor review frame {frame} skin and surface projection evidence differ.");
        return new Sample(
            frame,
            shotKind,
            frameEvidence,
            root,
            camera,
            nodes,
            skinPalette,
            source.GetProperty("animationLayers").GetArrayLength(),
            projectionExact,
            projectionStatus,
            RequireText(source, "visualSnapshotEventSha256", "visual snapshot event hash"));
    }

    private static SkinPaletteState ParseSkinPalette(JsonElement source, int frame)
    {
        if (!source.GetProperty("frameBoundToSourceBackbuffer").GetBoolean())
            throw new InvalidOperationException(
                $"Actor review frame {frame} skin palette is not bound to its retail source frame.");
        var summarySource = source.GetProperty("summary");
        var summary = new SkinPaletteSummary(
            summarySource.GetProperty("visitedNodes").GetInt32(),
            summarySource.GetProperty("geometryCandidates").GetInt32(),
            summarySource.GetProperty("skinInstances").GetInt32(),
            summarySource.GetProperty("capturedPalettes").GetInt32(),
            summarySource.GetProperty("notRenderCached").GetInt32(),
            summarySource.GetProperty("invalidPalettes").GetInt32(),
            summarySource.GetProperty("traversalTruncated").GetBoolean());
        if (summary.VisitedNodes <= 0 ||
            summary.GeometryCandidates < summary.SkinInstances ||
            summary.SkinInstances <= 0 ||
            summary.CapturedPalettes <= 0 ||
            summary.CapturedPalettes + summary.NotRenderCached != summary.SkinInstances ||
            summary.InvalidPalettes != 0 || summary.TraversalTruncated)
            throw new InvalidOperationException(
                $"Actor review frame {frame} skin-palette summary is incomplete.");

        var instances = source.GetProperty("instances").EnumerateArray()
            .Select(instance => ParseSkinInstance(instance, frame))
            .ToArray();
        if (instances.Length != summary.SkinInstances ||
            instances.Select(instance => instance.NodePath)
                .Distinct(StringComparer.Ordinal).Count() != instances.Length ||
            instances.Count(instance => instance.Status == CapturedSkinStatus) !=
                summary.CapturedPalettes ||
            instances.Count(instance => instance.Status == UncachedSkinStatus) !=
                summary.NotRenderCached)
            throw new InvalidOperationException(
                $"Actor review frame {frame} skin-palette instances disagree with their summary.");
        return new SkinPaletteState(
            summary,
            instances,
            RequireText(
                source,
                "finalProjectionEventSha256",
                $"frame {frame} skin final-projection event hash"));
    }

    private static SkinPaletteInstance ParseSkinInstance(JsonElement source, int frame)
    {
        var geometryName = RequireText(source, "geometryName", $"frame {frame} skin geometry");
        var status = RequireText(source, "status", $"frame {frame} {geometryName} skin status");
        var bones = Array.Empty<SkinPaletteBone>();
        if (status == CapturedSkinStatus)
        {
            if (RequireText(source, "matrixLayout", $"frame {frame} {geometryName} matrix layout") !=
                    SkinMatrixLayout ||
                RequireText(source, "matrixStage", $"frame {frame} {geometryName} matrix stage") !=
                    SkinMatrixStage ||
                RequireText(source, "matrixSpace", $"frame {frame} {geometryName} matrix space") !=
                    SkinMatrixSpace ||
                RequireText(
                    source,
                    "translationOrigin",
                    $"frame {frame} {geometryName} translation origin") !=
                    SkinTranslationOrigin ||
                !source.GetProperty("finalProjectionRequired").GetBoolean() ||
                source.GetProperty("registersPerMatrix").GetInt32() != SkinMatrixRows ||
                source.GetProperty("componentsPerRegister").GetInt32() != SkinMatrixColumns)
                throw new InvalidOperationException(
                    $"Actor review frame {frame} skin {geometryName} uses an unsupported matrix contract.");
            bones = source.GetProperty("bones").EnumerateArray()
                .Select((bone, index) => ParseSkinBone(bone, index, frame, geometryName))
                .ToArray();
            if (bones.Length == 0)
                throw new InvalidOperationException(
                    $"Actor review frame {frame} skin {geometryName} has no captured bones.");
        }
        else if (status != UncachedSkinStatus)
            throw new InvalidOperationException(
                $"Actor review frame {frame} skin {geometryName} has unsupported status {status}.");
        return new SkinPaletteInstance(
            RequireText(source, "nodePath", $"frame {frame} {geometryName} skin path"),
            geometryName,
            RequireText(source, "skinInstanceType", $"frame {frame} {geometryName} skin type"),
            RequireText(source, "rootParentName", $"frame {frame} {geometryName} skin root"),
            source.GetProperty("frameId").GetUInt32(),
            status,
            bones);
    }

    private static SkinPaletteBone ParseSkinBone(
        JsonElement source,
        int expectedIndex,
        int frame,
        string geometryName)
    {
        var index = source.GetProperty("skinIndex").GetInt32();
        if (index != expectedIndex)
            throw new InvalidOperationException(
                $"Actor review frame {frame} skin {geometryName} changed bone order.");
        return new SkinPaletteBone(
            index,
            RequireText(source, "name", $"frame {frame} {geometryName} skin bone"),
            ReadNumbers(
                source.GetProperty("matrixRowMajor3x4"),
                SkinMatrixRows * SkinMatrixColumns,
                $"frame {frame} {geometryName} skin matrix {index}"));
    }

    private static IReadOnlyList<string> SkinTopology(SkinPaletteState source) =>
        source.Instances.Select(instance => string.Join(
            '\u001f',
            new[]
            {
                instance.NodePath,
                instance.GeometryName,
                instance.SkinInstanceType,
                instance.RootParentName,
                instance.Status,
                string.Join('\u001e', instance.Bones.Select(bone => bone.Name)),
            })).ToArray();

    private static RetailNode ParseNode(JsonElement source, int frame)
    {
        var name = RequireText(source, "name", $"frame {frame} node name");
        var transform = source.GetProperty("transform");
        return new RetailNode(
            name,
            RequireText(source, "nodePath", $"frame {frame} node path"),
            source.GetProperty("depth").GetInt32(),
            source.GetProperty("parentName").ValueKind == JsonValueKind.Null
                ? ""
                : source.GetProperty("parentName").GetString() ?? "",
            ReadTransform(transform, $"frame {frame} {name} local", true),
            ReadTransform(transform, $"frame {frame} {name} world", false),
            ParseScreenProjection(
                source.GetProperty("retailScreen"),
                $"frame {frame} {name} retail screen"));
    }

    private static CameraState ParseCamera(
        JsonElement source,
        GamebryoTransform actorRoot,
        int frame)
    {
        var world = source.GetProperty("world");
        var rotation = ReadNumbers(
            world.GetProperty("rotation"),
            GamebryoCoordinate.SpatialDimensions * GamebryoCoordinate.SpatialDimensions,
            $"frame {frame} camera rotation");
        var worldTranslation = ReadVector(
            world.GetProperty("translation"), $"frame {frame} camera translation");
        var worldScale = world.GetProperty("scale").GetSingle();
        if (!float.IsFinite(worldScale) || worldScale <= 0.0f)
            throw new InvalidOperationException($"Actor review frame {frame} camera scale is invalid.");
        var offset = ReadVector(
            source.GetProperty("offsetGameUnits"), $"frame {frame} camera offset");
        var frustumValues = ReadNumbers(
            source.GetProperty("frustum"), PerspectiveFrustumElementCount,
            $"frame {frame} camera frustum");
        var orthographic = frustumValues[FrustumOrthographicIndex];
        if (frustumValues[0] >= frustumValues[1] ||
            frustumValues[3] >= frustumValues[2] ||
            frustumValues[4] <= 0.0f ||
            frustumValues[FrustumFarIndex] <= frustumValues[4] ||
            orthographic != 0.0f)
            throw new InvalidOperationException(
                $"Actor review frame {frame} camera frustum is not ordered perspective data.");
        var fov = source.GetProperty("fovYRadians").GetSingle();
        if (!float.IsFinite(fov) || fov <= 0.0f || fov >= MathF.PI)
            throw new InvalidOperationException($"Actor review frame {frame} camera FOV is invalid.");
        var eventSha256 = RequireText(
            source, "eventSha256", $"frame {frame} camera event hash");
        var surface = ParseSurfaceContract(source.GetProperty("surfaceContract"), frame);
        var culling = ParseCullingObservation(
            source.GetProperty("cullingObservation"), frame);
        if (!culling.EventSha256.Equals(eventSha256, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Actor review frame {frame} culling evidence changed camera identity.");
        return new CameraState(
            eventSha256,
            worldTranslation,
            worldScale,
            offset,
            GamebryoCoordinate.ConvertCameraBasis(rotation, $"frame {frame} camera basis"),
            fov,
            new FrustumState(
                frustumValues[0], frustumValues[1], frustumValues[2],
                frustumValues[3], frustumValues[4], frustumValues[FrustumFarIndex]),
            ReadNumbers(source.GetProperty("viewport"), 4, $"frame {frame} camera viewport"),
            ReadNumbers(
                source.GetProperty("worldToClipMatrix"), HomogeneousMatrixElementCount,
                $"frame {frame} camera world-to-clip matrix"),
            ReadNumbers(
                source.GetProperty("projectionMatrix"),
                HomogeneousMatrixElementCount,
                $"frame {frame} camera projection matrix"),
            surface,
            culling);
    }

    private static CullingObservationState ParseCullingObservation(
        JsonElement source,
        int frame)
    {
        var frustum = ReadNumbers(
            source.GetProperty("frustum"), PerspectiveFrustumElementCount,
            $"frame {frame} culling frustum");
        var fov = source.GetProperty("fovYRadians").GetSingle();
        if (!float.IsFinite(fov) || fov <= 0.0f || fov >= MathF.PI)
            throw new InvalidOperationException(
                $"Actor review frame {frame} culling FOV is invalid.");
        return new CullingObservationState(
            RequireText(source, "eventSha256", $"frame {frame} culling event hash"),
            fov,
            new FrustumState(
                frustum[0], frustum[1], frustum[2], frustum[3], frustum[4],
                frustum[FrustumFarIndex]),
            ReadNumbers(
                source.GetProperty("viewport"), 4,
                $"frame {frame} culling viewport"),
            ReadNumbers(
                source.GetProperty("worldToClipMatrix"), HomogeneousMatrixElementCount,
                $"frame {frame} culling world-to-clip matrix"),
            ReadNumbers(
                source.GetProperty("projectionMatrix"), HomogeneousMatrixElementCount,
                $"frame {frame} culling projection matrix"));
    }

    private static SurfaceContractState ParseSurfaceContract(JsonElement source, int frame)
    {
        var shader = source.GetProperty("vertexShader");
        var texture = source.GetProperty("matchedTexture");
        var renderTarget = source.GetProperty("renderTarget");
        var sceneColor = renderTarget.GetProperty("sceneColor");
        var backBuffer = renderTarget.GetProperty("backBuffer");
        var sceneDimensions = new Vector2I(
            sceneColor.GetProperty("width").GetInt32(),
            sceneColor.GetProperty("height").GetInt32());
        var backBufferDimensions = new Vector2I(
            backBuffer.GetProperty("width").GetInt32(),
            backBuffer.GetProperty("height").GetInt32());
        if (source.GetProperty("renderFrame").GetInt32() < 0 ||
            shader.GetProperty("getResult").GetInt32() != 0 ||
            shader.GetProperty("getFunctionResult").GetInt32() != 0 ||
            shader.GetProperty("byteCount").GetInt32() <= 0 ||
            shader.GetProperty("fnv1a32").GetUInt32() == 0 ||
            !shader.GetProperty("hasBonesParameter").GetBoolean() ||
            !shader.GetProperty("hasSkinModelViewProjectionParameter").GetBoolean() ||
            !renderTarget.GetProperty("matchesBackBufferDimensions").GetBoolean() ||
            sceneDimensions.X <= 0 || sceneDimensions.Y <= 0 ||
            sceneDimensions != backBufferDimensions)
            throw new InvalidOperationException(
                $"Actor review frame {frame} final-eye surface evidence is incomplete.");
        return new SurfaceContractState(
            RequireText(source, "eventSha256", $"frame {frame} surface event hash"),
            source.GetProperty("renderFrame").GetInt32(),
            RequireText(texture, "path", $"frame {frame} surface texture path"),
            RequireText(texture, "contentHash", $"frame {frame} surface texture hash"),
            shader.GetProperty("fnv1a32").GetUInt32(),
            renderTarget.GetProperty("isDirectBackBuffer").GetBoolean(),
            sceneDimensions,
            sceneColor.GetProperty("format").GetUInt32(),
            backBuffer.GetProperty("format").GetUInt32(),
            ReadNumbers(
                source.GetProperty("worldMatrix"),
                HomogeneousMatrixElementCount,
                $"frame {frame} surface world matrix"),
            ReadNumbers(
                source.GetProperty("viewMatrix"),
                HomogeneousMatrixElementCount,
                $"frame {frame} surface view matrix"),
            ReadNumbers(
                source.GetProperty("projectionMatrix"),
                HomogeneousMatrixElementCount,
                $"frame {frame} surface projection matrix"),
            ReadNumbers(
                source.GetProperty("worldToClipMatrix"),
                HomogeneousMatrixElementCount,
                $"frame {frame} surface world-to-clip matrix"));
    }

    private static ScreenProjection ParseScreenProjection(JsonElement source, string label)
    {
        var pixels = ReadNumbers(source.GetProperty("pixels"), 2, $"{label} pixels");
        var ndc = ReadNumbers(source.GetProperty("ndc"), 3, $"{label} NDC");
        var clipW = source.GetProperty("clipW").GetSingle();
        if (!float.IsFinite(clipW) || clipW <= 0.0f)
            throw new InvalidOperationException($"Actor review {label} clip W is invalid.");
        return new ScreenProjection(
            new Vector2(pixels[0], pixels[1]),
            new Vector3(ndc[0], ndc[1], ndc[2]),
            clipW,
            source.GetProperty("insideViewport").GetBoolean());
    }

    private static void ValidateCamera(
        Sample sample,
        RuntimeConfiguration configuration)
    {
        var camera = sample.Camera;
        if (camera.Surface.SceneDimensions != new Vector2I(
                sample.SourceFrame.Width,
                sample.SourceFrame.Height))
            throw new InvalidOperationException(
                $"Actor review frame {sample.Frame} surface dimensions differ from its source frame.");
        var expectedOffset = camera.WorldTranslationGameUnits - sample.ActorRoot.Translation;
        if (camera.OffsetGameUnits.DistanceTo(expectedOffset) >
            configuration.ActorParity.CameraPositionToleranceGameUnits)
            throw new InvalidOperationException(
                $"Actor review frame {sample.Frame} camera offset does not match its retail world transform.");

        var basis = camera.GodotBasis;
        var tolerance = configuration.ActorReview.CameraBasisTolerance;
        var axes = new[] { basis.X, basis.Y, basis.Z };
        if (axes.Any(axis => MathF.Abs(axis.Length() - camera.WorldScale) > tolerance) ||
            MathF.Abs(axes[0].Dot(axes[1])) > tolerance ||
            MathF.Abs(axes[0].Dot(axes[2])) > tolerance ||
            MathF.Abs(axes[1].Dot(axes[2])) > tolerance ||
            MathF.Abs(MathF.Abs(basis.Determinant()) -
                camera.WorldScale * camera.WorldScale * camera.WorldScale) > tolerance)
            throw new InvalidOperationException(
                $"Actor review frame {sample.Frame} camera basis is not orthogonal at its recorded scale.");

        var frustum = camera.Frustum;
        var sourceAspect = (float)sample.SourceFrame.Width / sample.SourceFrame.Height;
        var frustumAspect = (frustum.Right - frustum.Left) / (frustum.Top - frustum.Bottom);
        if (MathF.Abs(sourceAspect - frustumAspect) >
            configuration.ActorReview.ProjectionAspectTolerance)
            throw new InvalidOperationException(
                $"Actor review frame {sample.Frame} source dimensions and retail frustum have different aspects.");
        var expectedFov = MathF.Atan(frustum.Top) - MathF.Atan(frustum.Bottom);
        if (MathF.Abs(expectedFov - camera.FovYRadians) > tolerance)
            throw new InvalidOperationException(
                $"Actor review frame {sample.Frame} FOV does not match its retail frustum.");

        var expectedViewport = new[]
        {
            NormalizedViewportMinimum,
            NormalizedViewportMaximum,
            NormalizedViewportMaximum,
            NormalizedViewportMinimum,
        };
        ValidateValues(
            camera.Viewport,
            expectedViewport,
            tolerance,
            $"frame {sample.Frame} camera viewport");

        var depthRange = frustum.Far - frustum.Near;
        var expectedProjection = new[]
        {
            2.0f / (frustum.Right - frustum.Left), 0.0f, 0.0f, 0.0f,
            0.0f, 2.0f / (frustum.Top - frustum.Bottom), 0.0f, 0.0f,
            (frustum.Left + frustum.Right) / (frustum.Left - frustum.Right),
            (frustum.Top + frustum.Bottom) / (frustum.Bottom - frustum.Top),
            frustum.Far / depthRange, 1.0f,
            0.0f, 0.0f, -(frustum.Near * frustum.Far) / depthRange, 0.0f,
        };
        ValidateValues(
            camera.ProjectionMatrix,
            expectedProjection,
            tolerance,
            $"frame {sample.Frame} camera projection matrix");
        ValidateValues(
            camera.ProjectionMatrix,
            camera.Surface.ProjectionMatrix,
            tolerance,
            $"frame {sample.Frame} surface projection binding");
        ValidateValues(
            camera.WorldToClipMatrix,
            camera.Surface.WorldToClipMatrix,
            tolerance,
            $"frame {sample.Frame} surface world-to-clip binding");
    }

    private static void ValidateValues(
        IReadOnlyList<float> actual,
        IReadOnlyList<float> expected,
        float tolerance,
        string label)
    {
        if (actual.Count != expected.Count ||
            actual.Where((value, index) => MathF.Abs(value - expected[index]) > tolerance).Any())
            throw new InvalidOperationException($"Actor review {label} does not match its derived value.");
    }

    private static GamebryoTransform ReadTransform(
        JsonElement source,
        string label,
        bool local)
    {
        var prefix = local ? "local" : "world";
        var rotation = ReadNumbers(
            source.GetProperty($"{prefix}Rotation"),
            GamebryoCoordinate.SpatialDimensions * GamebryoCoordinate.SpatialDimensions,
            $"{label} rotation");
        var translation = ReadVector(source.GetProperty($"{prefix}Translation"), $"{label} translation");
        var scale = source.GetProperty($"{prefix}Scale").GetSingle();
        return new GamebryoTransform(
            translation,
            GamebryoCoordinate.ConvertBasis(rotation, scale, label));
    }

    private static GamebryoTransform ReadRootTransform(JsonElement source, string label)
    {
        var rotation = ReadNumbers(
            source.GetProperty("rotation"),
            GamebryoCoordinate.SpatialDimensions * GamebryoCoordinate.SpatialDimensions,
            $"{label} rotation");
        var translation = ReadVector(source.GetProperty("translation"), $"{label} translation");
        var scale = source.GetProperty("scale").GetSingle();
        return new GamebryoTransform(
            translation,
            GamebryoCoordinate.ConvertBasis(rotation, scale, label));
    }

    private static FileEvidence ParseFileEvidence(JsonElement source)
    {
        var path = Path.GetFullPath(RequireText(source, "path", "source-frame path"));
        var expectedBytes = source.GetProperty("bytes").GetInt64();
        var expectedHash = RequireText(source, "sha256", "source-frame hash");
        var width = source.GetProperty("width").GetInt32();
        var height = source.GetProperty("height").GetInt32();
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Retail actor source-frame dimensions are invalid.");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedBytes ||
            !FileSha256(path).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Retail actor source-frame evidence changed: {path}");
        return new FileEvidence(path, expectedBytes, expectedHash.ToLowerInvariant(), width, height);
    }

    private static Color ReadColor(JsonElement source, string label)
    {
        var values = ReadNumbers(source, GamebryoCoordinate.SpatialDimensions, label);
        if (values.Any(value => value < 0.0f || value > 1.0f))
            throw new InvalidOperationException($"Actor review {label} is outside normalized color space.");
        return new Color(values[0], values[1], values[2]);
    }

    private static Vector3 ReadVector(JsonElement source, string label)
    {
        var values = ReadNumbers(source, GamebryoCoordinate.SpatialDimensions, label);
        return new Vector3(values[0], values[1], values[2]);
    }

    private static float[] ReadNumbers(JsonElement source, int count, string label)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != count || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                $"Actor review {label} must contain {count} finite values.");
        return values;
    }

    private static string RequireText(JsonElement source, string property, string label)
    {
        var value = source.GetProperty(property).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Actor review {label} is empty.");
        return value;
    }

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal sealed record Contract(
        string Path,
        string Sha256,
        string ReviewKey,
        string RecordType,
        EnvironmentState Environment,
        AppearanceState Appearance,
        IReadOnlyList<Shot> Shots,
        bool ExactProjectionResolved);

    internal readonly record struct EnvironmentState(
        Color AmbientColor,
        Color DirectionalColor,
        Color FogColor,
        uint WeatherForm,
        float GameHour);

    internal readonly record struct AppearanceState(
        int Frame,
        string EventSha256,
        int RenderParts,
        int TextureBindings);

    internal readonly record struct Shot(
        string Kind,
        int SetFrame,
        string FocusNode,
        string FocusKind,
        float CameraDistanceGameUnits,
        bool ProjectionExact,
        string ProjectionStatus,
        IReadOnlyList<Sample> Samples);

    internal readonly record struct Sample(
        int Frame,
        string ShotKind,
        FileEvidence SourceFrame,
        GamebryoTransform ActorRoot,
        CameraState Camera,
        IReadOnlyList<RetailNode> Nodes,
        SkinPaletteState SkinPalette,
        int AnimationLayers,
        bool ProjectionExact,
        string ProjectionStatus,
        string VisualSnapshotEventSha256);

    internal readonly record struct SkinPaletteState(
        SkinPaletteSummary Summary,
        IReadOnlyList<SkinPaletteInstance> Instances,
        string FinalProjectionEventSha256);

    internal readonly record struct SkinPaletteSummary(
        int VisitedNodes,
        int GeometryCandidates,
        int SkinInstances,
        int CapturedPalettes,
        int NotRenderCached,
        int InvalidPalettes,
        bool TraversalTruncated);

    internal readonly record struct SkinPaletteInstance(
        string NodePath,
        string GeometryName,
        string SkinInstanceType,
        string RootParentName,
        uint FrameId,
        string Status,
        IReadOnlyList<SkinPaletteBone> Bones);

    internal readonly record struct SkinPaletteBone(
        int SkinIndex,
        string Name,
        float[] MatrixRowMajor3x4);

    internal readonly record struct RetailNode(
        string Name,
        string NodePath,
        int Depth,
        string ParentName,
        GamebryoTransform Local,
        GamebryoTransform World,
        ScreenProjection Screen);

    internal readonly record struct CameraState(
        string EventSha256,
        Vector3 WorldTranslationGameUnits,
        float WorldScale,
        Vector3 OffsetGameUnits,
        Basis GodotBasis,
        float FovYRadians,
        FrustumState Frustum,
        float[] Viewport,
        float[] WorldToClipMatrix,
        float[] ProjectionMatrix,
        SurfaceContractState Surface,
        CullingObservationState Culling);

    internal readonly record struct CullingObservationState(
        string EventSha256,
        float FovYRadians,
        FrustumState Frustum,
        float[] Viewport,
        float[] WorldToClipMatrix,
        float[] ProjectionMatrix);

    internal readonly record struct SurfaceContractState(
        string EventSha256,
        int RenderFrame,
        string MatchedTexturePath,
        string MatchedTextureHash,
        uint VertexShaderFnv1a32,
        bool IsDirectBackBuffer,
        Vector2I SceneDimensions,
        uint SceneColorFormat,
        uint BackBufferFormat,
        float[] WorldMatrix,
        float[] ViewMatrix,
        float[] ProjectionMatrix,
        float[] WorldToClipMatrix);

    internal readonly record struct FrustumState(
        float Left,
        float Right,
        float Top,
        float Bottom,
        float Near,
        float Far);

    internal readonly record struct ScreenProjection(
        Vector2 Pixels,
        Vector3 Ndc,
        float ClipW,
        bool InsideViewport);

    internal readonly record struct GamebryoTransform(Vector3 Translation, Basis Basis);

    internal readonly record struct FileEvidence(
        string Path,
        long Bytes,
        string Sha256,
        int Width,
        int Height);
}
