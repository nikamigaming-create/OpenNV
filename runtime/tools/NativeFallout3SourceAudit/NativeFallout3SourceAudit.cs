using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Tools;

public partial class NativeFallout3SourceAudit : Node
{
    private const string InitialPlugin = "Fallout3.esm";
    private const uint InitialCellObjectId = 0x28138;
    private const uint PlayerStartObjectId = 0x39562;
    private const string OpeningAnimation =
        @"meshes\characters\_male\idleanims\cg00playersection04.kf";
    private const float GameUnitsToMetres = 0.0142875f;

    public override void _Ready()
    {
        var exitCode = 1;
        try
        {
            var arguments = ParseArguments(OS.GetCmdlineUserArgs());
            var manifestPath = Path.GetFullPath(arguments["source-stack"]);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(manifestBytes);
            var manifest = document.RootElement;
            var dataRoot = Path.GetFullPath(manifest.GetProperty("roots")[0]
                .GetProperty("root").GetString()!);
            RuntimeOwnedContentSource.Configure(
                dataRoot,
                manifestPath,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                manifest.GetProperty("stackId").GetString());
            var source = RuntimeOwnedContentSource.Current!;
            if (source.Game != RuntimeOwnedContentSource.Fallout3Game)
                throw new InvalidDataException("The audit requires a standalone Fallout 3 source stack.");
            foreach (var pluginSource in source.PluginSources)
            {
                using var master = FalloutPlugin.Open(pluginSource.AbsolutePath, pluginSource.Name);
                var namespaces = master.Records
                    .Where(record => record.Signature != "TES4")
                    .GroupBy(record => record.RawFormId >> FalloutFormKey.ObjectIdBits)
                    .Select(group => $"{group.Key}:{group.Count()}");
                GD.Print(
                    $"OPENNV_NATIVE_FO3_RAW_NAMESPACES plugin={pluginSource.Name} " +
                    $"masters={master.Masters.Count} namespaces={string.Join(',', namespaces)}");
            }
            using var stack = FalloutPluginStack.Load(source.PluginSources);
            var cell = FalloutCellSceneReader.Read(
                stack,
                new FalloutFormKey(InitialPlugin, InitialCellObjectId));
            var playerStart = cell.References.Single(reference =>
                reference.FormKey == new FalloutFormKey(InitialPlugin, PlayerStartObjectId));

            var modelPaths = cell.BaseObjects.Values
                .Select(value => value.ModelPath)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var builtModels = 0;
            var decodedTextures = 0;
            string? firstBlocker = null;
            foreach (var modelPath in modelPaths)
            {
                if (!source.TryRead(modelPath, null, out var bytes, out _))
                    throw new FileNotFoundException($"Winning Fallout 3 model is missing: {modelPath}");
                try
                {
                    var built = NativeNifMeshBuilder.Build(bytes, GameUnitsToMetres);
                    builtModels++;
                    decodedTextures += CountTextures(built.Root);
                    built.Root.Free();
                }
                catch (InvalidDataException error)
                {
                    firstBlocker ??= $"{modelPath}:{error.Message}";
                }
                catch (NotSupportedException error)
                {
                    firstBlocker ??= $"{modelPath}:{error.Message}";
                }
            }
            if (builtModels == 0 || decodedTextures == 0)
                throw new InvalidDataException(
                    $"No Fallout 3 CELL model completed NIF/DDS loading. First blocker: {firstBlocker}");

            if (!source.TryRead(OpeningAnimation, null, out var kfBytes, out var kfSource))
                throw new FileNotFoundException($"Fallout 3 opening KF is missing: {OpeningAnimation}");
            var kf = FalloutNifFile.Read(kfBytes);
            if (kf.Blocks.Count == 0)
                throw new InvalidDataException("Fallout 3 opening KF contains no blocks.");

            GD.Print(
                $"OPENNV_NATIVE_FO3_SOURCE_AUDIT_OK game={source.Game} plugins={stack.Plugins.Count} " +
                $"records={stack.EffectiveRecordCount} cell={cell.Cell.FormKey} references={cell.References.Count} " +
                $"playerStart={playerStart.FormKey} models={modelPaths.Length} built={builtModels} " +
                $"textures={decodedTextures} xprmBases={cell.BaseObjects.Values.Count(value => value.Signature == "XPRM")} " +
                $"kfBlocks={kf.Blocks.Count} kfSource={kfSource} " +
                $"firstBlocker={firstBlocker ?? "none"} source=standalone-owned-fallout3 cache=none writes=zero");
            exitCode = 0;
        }
        catch (Exception error)
        {
            GD.PrintErr(
                $"OPENNV_NATIVE_FO3_SOURCE_AUDIT_ERROR {error.GetType().Name}: {error.Message} " +
                $"inner={error.InnerException?.Message ?? "none"}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
    }

    private static int CountTextures(Node node)
    {
        var count = 0;
        if (node is MeshInstance3D mesh && mesh.Mesh is not null)
        {
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); ++surface)
            {
                if (mesh.Mesh.SurfaceGetMaterial(surface) is StandardMaterial3D material &&
                    material.AlbedoTexture is not null)
                    count++;
            }
        }
        foreach (var child in node.GetChildren())
            count += CountTextures(child);
        return count;
    }

    private static Dictionary<string, string> ParseArguments(IReadOnlyList<string> args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; ++index)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
                throw new ArgumentException("Audit arguments must be --name value pairs.");
            result.Add(args[index][2..], args[++index]);
        }
        if (!result.ContainsKey("source-stack"))
            throw new ArgumentException("Native Fallout 3 audit requires --source-stack.");
        return result;
    }
}
