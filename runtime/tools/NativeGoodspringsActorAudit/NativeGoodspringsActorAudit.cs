using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Tools;

public sealed partial class NativeGoodspringsActorAudit : Node
{
    public override void _Ready()
    {
        var exitCode = 1;
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            var option = Array.IndexOf(arguments, "--source-stack");
            if (option < 0 || option + 1 >= arguments.Length)
                throw new ArgumentException("NativeGoodspringsActorAudit requires --source-stack.");
            var manifestPath = Path.GetFullPath(arguments[option + 1]);
            var manifestBefore = Snapshot(Path.GetDirectoryName(manifestPath)!);
            var manifestBytes = File.ReadAllBytes(manifestPath);
            using var document = JsonDocument.Parse(manifestBytes);
            var manifest = document.RootElement;
            var roots = manifest.GetProperty("roots").EnumerateArray()
                .Select(row => Path.GetFullPath(row.GetProperty("root").GetString()!)).ToArray();
            var rootBefore = roots.ToDictionary(root => root, Snapshot, StringComparer.OrdinalIgnoreCase);
            RuntimeOwnedContentSource.Configure(
                roots[0],
                manifestPath,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                manifest.GetProperty("stackId").GetString());
            var source = RuntimeOwnedContentSource.Current!;
            using var stack = FalloutPluginStack.Load(source.PluginSources);
            var appearance = FalloutHumanoidAppearanceResolver.ResolveGoodspringsSunny(stack, source);
            var node = FalloutHumanoidAppearanceResolver.BuildSourceIdentityNode(appearance);
            AddChild(node);
            if (appearance.Reference != FalloutHumanoidAppearanceResolver.GoodspringsSunnyReference ||
                appearance.Resources.Count == 0 || appearance.VisualBlockers.Count == 0 ||
                node.GetMeta("content_writes").AsInt32() != 0)
                throw new InvalidDataException("Goodsprings actor source-identity transport is incomplete.");
            if (!manifestBefore.SequenceEqual(Snapshot(Path.GetDirectoryName(manifestPath)!)) ||
                roots.Any(root => !rootBefore[root].SequenceEqual(Snapshot(root))))
                throw new InvalidOperationException("Goodsprings actor audit wrote to a source or profile root.");
            GD.Print(
                $"OPENNV_NATIVE_GOODSPRINGS_ACTOR_PASS reference={appearance.Reference} base={appearance.Base} " +
                $"traits={appearance.TraitsSource} model={appearance.ModelSource} " +
                $"race={appearance.Race} hair={appearance.Hair} eyes={appearance.Eyes} " +
                $"headParts={appearance.HeadParts.Count} female={appearance.Female} " +
                $"resources={appearance.Resources.Count} faceGenBytes={appearance.FaceGenCoordinateBytes} " +
                $"blockers={string.Join(',', appearance.VisualBlockers)} " +
                $"stackId={manifest.GetProperty("stackId").GetString()} cache=none writes=0 rendered=false");
            exitCode = 0;
        }
        catch (Exception error)
        {
            GD.PrintErr($"OPENNV_NATIVE_GOODSPRINGS_ACTOR_FAIL {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            RuntimeOwnedContentSource.Clear();
            GetTree().Quit(exitCode);
        }
    }

    private static string[] Snapshot(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return $"{Path.GetRelativePath(root, path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            })
            .OrderBy(value => value, StringComparer.Ordinal).ToArray()
        : [];
}
