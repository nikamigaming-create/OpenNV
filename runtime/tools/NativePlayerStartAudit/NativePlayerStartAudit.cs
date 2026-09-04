using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public partial class NativePlayerStartAudit : Node
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
            RuntimeOwnedContentSource.Configure(
                root.GetProperty("roots")[0].GetProperty("root").GetString()!,
                manifest,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                root.GetProperty("stackId").GetString());
            exitCode = Audit(arguments.GetValueOrDefault("cell", "FalloutNV.esm:103df9"));
        }
        catch (Exception error)
        {
            GD.PrintErr($"OPENNV_NATIVE_PLAYER_START_AUDIT_ERROR {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
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
        using var stack = FalloutPluginStack.Load(RuntimeOwnedContentSource.Current!.PluginSources);
        var cell = FalloutCellSceneReader.Read(
            stack, new FalloutFormKey(cellText[..separator], objectId));
        var start = FalloutNewGamePlayerStartResolver.Resolve(stack, cell);
        GD.Print(
            $"OPENNV_NATIVE_PLAYER_START_AUDIT_OK cell={cell.Cell.FormKey} " +
            $"reference={start.Reference.FormKey} editorId={start.Reference.EditorId} " +
            $"quest={start.Quest} stage={start.Stage} candidates={start.Candidates.Count} " +
            $"packageLinked={start.Candidates.Count(value => value.DirectPackageLocationCount > 0)} " +
            $"packageTargets={start.Candidates.Sum(value => value.DirectPackageLocationCount)} " +
            "source=live-owned-stack cache=none");
        foreach (var candidate in start.Candidates)
            GD.Print(
                $"OPENNV_NATIVE_PLAYER_START_CANDIDATE reference={candidate.Reference.FormKey} " +
                $"editorId={candidate.Reference.EditorId} flags=0x{candidate.Reference.Flags:x8} " +
                $"packageTargets={candidate.DirectPackageLocationCount} " +
                $"enableParent={candidate.Reference.EnableParent}");
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
