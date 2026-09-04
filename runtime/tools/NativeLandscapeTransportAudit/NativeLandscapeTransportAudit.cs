using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public partial class NativeLandscapeTransportAudit : Node
{
    private const float GameUnitsToMeters = 0.0142875f;
    private const int ExpectedSurfaceCount = 4;
    private const int ExpectedVerticesPerSurface = 17 * 17;
    private const int ExpectedIndicesPerSurface = 16 * 16 * 2 * 3;

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
            RuntimeOwnedContentSource.Configure(
                root.GetProperty("roots")[0].GetProperty("root").GetString()!,
                manifest,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                root.GetProperty("stackId").GetString());
            exitCode = Audit(arguments.GetValueOrDefault("cell", "FalloutNV.esm:103df9"));
        }
        catch (Exception error)
        {
            GD.PrintErr($"OPENNV_NATIVE_LAND_TRANSPORT_AUDIT_ERROR {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
    }

    private int Audit(string cellText)
    {
        var separator = cellText.LastIndexOf(':');
        if (separator <= 0 || !uint.TryParse(
                cellText[(separator + 1)..],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var objectId))
            throw new ArgumentException("CELL key must be plugin:hex-object-id.", nameof(cellText));
        using var stack = FalloutPluginStack.Load(RuntimeOwnedContentSource.Current!.PluginSources);
        var sourceScene = FalloutCellSceneReader.Read(
            stack, new FalloutFormKey(cellText[..separator], objectId));
        var transition = FalloutDoorTransitionResolver.ResolveSingleInteriorExit(stack, sourceScene);
        var source = FalloutLandscapeTransportResolver.Resolve(stack, transition);
        var transported = RuntimeNativeLandscapeTransportBuilder.Build(source, GameUnitsToMeters);
        AddChild(transported);
        if (transported.Geometry.Mesh is not ArrayMesh mesh ||
            mesh.GetSurfaceCount() != ExpectedSurfaceCount ||
            transported.Geometry.Visible || transported.Textures.Count != source.Textures.Count ||
            transported.Textures.Values.Any(value =>
                value.Diffuse.GetWidth() <= 0 || value.Diffuse.GetHeight() <= 0 ||
                value.Normal is { } normal && (normal.GetWidth() <= 0 || normal.GetHeight() <= 0)))
            throw new InvalidDataException("Godot LAND transport resources differ from the source graph.");
        for (var surface = 0; surface < mesh.GetSurfaceCount(); ++surface)
        {
            var arrays = mesh.SurfaceGetArrays(surface);
            if (arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length !=
                    ExpectedVerticesPerSurface ||
                arrays[(int)Mesh.ArrayType.Index].AsInt32Array().Length != ExpectedIndicesPerSurface)
                throw new InvalidDataException(
                    $"Godot LAND quadrant {surface} geometry differs from the 17x17 source lattice.");
        }
        var normalTextures = transported.Textures.Values.Count(value => value.Normal is not null);
        GD.Print(
            $"OPENNV_NATIVE_LAND_TRANSPORT_AUDIT_OK persistentCell={source.PersistentDestinationCell} " +
            $"activeCell={source.ActiveCell} coordinates={source.ActiveCoordinates} land={source.Landscape} " +
            $"world={source.Worldspace} flags=0x{source.Flags:x8} vertices={source.Heights.Length} " +
            $"surfaces={mesh.GetSurfaceCount()} triangles={ExpectedSurfaceCount * ExpectedIndicesPerSurface / 3} " +
            $"baseLayers={source.BaseLayers.Count} alphaLayers={source.AlphaLayers.Count} " +
            $"defaultLayers={source.AlphaLayers.Count(value => value.UsesQuadrantDefault)} " +
            $"diffuseDds={transported.Textures.Count} normalDds={normalTextures} " +
            "geometry=transported material=pending-vtxt-shader source=live-owned-stack cache=none writes=zero");
        return 0;
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
