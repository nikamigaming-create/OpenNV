using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Tools;

public partial class NativeNifAudit : Node
{
    public override void _Ready()
    {
        var exitCode = 1;
        try
        {
            var arguments = ParseArguments(OS.GetCmdlineUserArgs());
            var manifest = Path.GetFullPath(arguments["source-stack"]);
            var manifestBytes = File.ReadAllBytes(manifest);
            using var document = JsonDocument.Parse(manifestBytes);
            var root = document.RootElement;
            var dataRoot = root.GetProperty("roots")[0].GetProperty("root").GetString()!;
            var hash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            RuntimeOwnedContentSource.Configure(
                dataRoot, manifest, hash, root.GetProperty("stackId").GetString());
            exitCode = arguments.TryGetValue("model", out var model)
                ? AuditModel(model)
                : Audit(arguments.GetValueOrDefault("cell", "FalloutNV.esm:103df9"));
        }
        catch (Exception error)
        {
            GD.PrintErr($"OPENNV_NATIVE_NIF_AUDIT_ERROR {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
    }

    private static int AuditModel(string model)
    {
        var source = RuntimeOwnedContentSource.Current!;
        if (!source.TryRead(model, null, out var payload, out var resolvedSource))
            throw new FileNotFoundException($"Winning model is missing: {model}");
        var nif = FalloutNifFile.Read(payload);
        var materialOnlySurfaces = nif.Blocks
            .Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
            .Select(block => nif.ReadGeometry(block.Index))
            .Count(geometry => geometry.Properties.Count(reference => reference != -1) == 1 &&
                nif.ReadObject(geometry.Properties.Single(reference => reference != -1)) is
                    FalloutNifMaterialProperty);
        var scene = RuntimeNativeNifMeshBuilder.Build(payload, 0.0142875f);
        var builtVertexMaterials = Descendants<MeshInstance3D>(scene.Root)
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(surface => mesh.Mesh?.SurfaceGetMaterial(surface)))
            .Count(material => material?.ResourceName.StartsWith(
                "NIF vertex material ", StringComparison.Ordinal) == true);
        scene.Root.Free();
        if (materialOnlySurfaces == 0 || builtVertexMaterials != materialOnlySurfaces)
            throw new InvalidDataException(
                $"Source vertex-material surfaces did not build exactly: " +
                $"source={materialOnlySurfaces} built={builtVertexMaterials}.");
        GD.Print($"OPENNV_NATIVE_NIF_MODEL_AUDIT_OK model={model} " +
            $"sha256={Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()} " +
            $"blocks={nif.Blocks.Count} vertexMaterialSurfaces={builtVertexMaterials} " +
            $"source={resolvedSource} cache=none writes=zero rendered=true parityReviewed=false");
        return 0;
    }

    private static int Audit(string cellText)
    {
        var separator = cellText.LastIndexOf(':');
        if (separator <= 0 || !uint.TryParse(
                cellText[(separator + 1)..],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var objectId))
            throw new ArgumentException("CELL key must be plugin:hex-object-id.", nameof(cellText));
        var source = RuntimeOwnedContentSource.Current!;
        using var stack = FalloutPluginStack.Load(source.PluginSources);
        var cell = FalloutCellSceneReader.Read(
            stack, new FalloutFormKey(cellText[..separator], objectId));
        var models = cell.BaseObjects.Values.Select(value => value.ModelPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var categories = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        var collisionFailures = new List<string>();
        var built = 0;
        var collisionAttachments = 0;
        var collisionBodies = 0;
        var collisionShapes = 0;
        var collisionTriangles = 0;
        var blendBlockers = 0;
        var controllerPlayers = 0;
        var controllerSequences = 0;
        var integerExtraData = new Dictionary<string, int>(StringComparer.Ordinal);
        var boneLodControllers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            try
            {
                if (!source.TryRead(model, null, out var payload, out _))
                    throw new FileNotFoundException("Winning model is missing.");
                var nif = FalloutNifFile.Read(payload);
                foreach (var block in nif.Blocks.Where(block => block.TypeName == "NiIntegerExtraData"))
                {
                    var value = (FalloutNifIntegerExtraData)nif.ReadObject(block.Index);
                    var identity = $"{value.Name}=0x{value.Value:x8}";
                    integerExtraData[identity] = integerExtraData.GetValueOrDefault(identity) + 1;
                }
                foreach (var block in nif.Blocks.Where(block => block.TypeName == "NiBSBoneLODController"))
                {
                    var value = (FalloutNifBoneLodController)nif.ReadObject(block.Index);
                    var identity = $"lod={value.Lod}/lods={value.LodCount}/" +
                        $"declaredGroups={value.DeclaredNodeGroupCount}/" +
                        $"groupSizes={string.Join('+', value.NodeGroups.Select(group => group.Length))}/" +
                        $"target={value.Time.Target}/next={value.Time.NextController}/" +
                        $"flags=0x{value.Time.Flags:x4}";
                    boneLodControllers[identity] = boneLodControllers.GetValueOrDefault(identity) + 1;
                }
                foreach (var block in nif.Blocks)
                {
                    if (block.TypeName == "bhkBlendCollisionObject")
                    {
                        blendBlockers++;
                        continue;
                    }
                    if (block.TypeName != "bhkCollisionObject")
                        continue;
                    collisionAttachments++;
                    try
                    {
                        var attachment = (FalloutNifCollisionObject)nif.ReadObject(block.Index);
                        var collision = NativeNifCollisionBuilder.Build(nif, attachment, 0.0142875f);
                        collisionShapes += collision.Shapes;
                        collisionTriangles += collision.Triangles;
                        collisionBodies++;
                        collision.Body.Free();
                    }
                    catch (Exception error)
                    {
                        collisionFailures.Add($"{model} block={block.Index} => " +
                            $"{error.GetType().Name}: {error.Message}");
                    }
                }
                var scene = RuntimeNativeNifMeshBuilder.Build(payload, 0.0142875f);
                foreach (var player in Descendants<RuntimeNifControllerPlayer>(scene.Root))
                {
                    controllerPlayers++;
                    controllerSequences += player.SequenceNames.Count;
                    foreach (var name in player.SequenceNames)
                    {
                        var range = player.SequenceRange(name);
                        player.PlaySourceSequence(name);
                        player.SeekSourceTime((range.StartTime + range.StopTime) * 0.5);
                    }
                }
                scene.Root.Free();
                built++;
            }
            catch (Exception error)
            {
                var category = Category(error.Message);
                categories[category] = categories.GetValueOrDefault(category) + 1;
                failures.Add($"{model} => {category}: {error.Message}");
            }
        }
        GD.Print($"OPENNV_NATIVE_NIF_AUDIT models={models.Length} built={built} failures={failures.Count} " +
            $"controllerPlayers={controllerPlayers} controllerSequences={controllerSequences}");
        GD.Print(
            $"OPENNV_NATIVE_COLLISION_AUDIT attachments={collisionAttachments} bodies={collisionBodies} " +
            $"shapes={collisionShapes} triangles={collisionTriangles} " +
            $"failures={collisionFailures.Count} blendBlockers={blendBlockers}");
        GD.Print("OPENNV_NATIVE_NIF_AUDIT_CATEGORIES " + string.Join(',',
            categories.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key)
                .Select(pair => $"{pair.Key}={pair.Value}")));
        GD.Print("OPENNV_NATIVE_NIF_INTEGER_EXTRA_DATA " +
            (integerExtraData.Count == 0
                ? "none"
                : string.Join(',', integerExtraData.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{pair.Value}"))));
        GD.Print("OPENNV_NATIVE_NIF_BONE_LOD " +
            (boneLodControllers.Count == 0
                ? "none"
                : string.Join(',', boneLodControllers.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}:{pair.Value}"))));
        foreach (var failure in failures)
            GD.Print($"OPENNV_NATIVE_NIF_AUDIT_FAILURE {failure}");
        foreach (var failure in collisionFailures)
            GD.Print($"OPENNV_NATIVE_COLLISION_AUDIT_FAILURE {failure}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T value)
                yield return value;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private static string Category(string message)
    {
        if (message.Contains("unsupported BSX flags", StringComparison.OrdinalIgnoreCase))
            return "visual-block-bsx";
        if (message.Contains("environment-map light-fade", StringComparison.OrdinalIgnoreCase))
            return "shader-envmap-light-fade";
        if (message.Contains("incomplete window environment-map", StringComparison.OrdinalIgnoreCase))
            return "shader-window-environment";
        if (message.Contains("unsupported texture set", StringComparison.OrdinalIgnoreCase))
            return "shader-texture-set";
        if (message.Contains("no-lighting shader", StringComparison.OrdinalIgnoreCase))
            return "shader-no-lighting";
        if (message.Contains("lighting semantics", StringComparison.OrdinalIgnoreCase))
            return "shader-flags";
        if (message.Contains("controller", StringComparison.OrdinalIgnoreCase))
            return "controller";
        if (message.Contains("collision", StringComparison.OrdinalIgnoreCase))
            return "collision";
        if (message.Contains("extra-data", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("extra data", StringComparison.OrdinalIgnoreCase))
            return "extra-data";
        if (message.Contains("shader", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("texture set", StringComparison.OrdinalIgnoreCase))
            return "shader";
        if (message.Contains("property", StringComparison.OrdinalIgnoreCase))
            return "property";
        if (message.Contains("visual block", StringComparison.OrdinalIgnoreCase))
            return "visual-block";
        if (message.Contains("mesh", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("geometry", StringComparison.OrdinalIgnoreCase))
            return "geometry";
        return "other";
    }

    private static Dictionary<string, string> ParseArguments(string[] source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < source.Length; ++index)
        {
            if (!source[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= source.Length)
                continue;
            result[source[index][2..]] = source[++index];
        }
        if (!result.ContainsKey("source-stack"))
            throw new ArgumentException("--source-stack is required.");
        return result;
    }
}
