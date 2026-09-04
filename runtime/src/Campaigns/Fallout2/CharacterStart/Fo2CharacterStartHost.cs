using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Native;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

public sealed partial class Fo2CharacterStartHost : Node3D
{
    public override void _Ready()
    {
        try
        {
            var options = ParseOptions(OS.GetCmdlineUserArgs());
            var installRoot = Require(options, "fo2-install-root");
            var savePath = Require(options, "save-path");
            using var source = Fo2NativeOwnedSource.LoadInstall(installRoot);
            var coverage = Fo2NativeMap3Presentation.Build(this, source);
            SetMeta("authoritative_save_path", savePath);
            SetMeta("native_map3_presentation_only", true);
            GD.Print(
                $"OPENNV_FO2_NATIVE_INSTALL_READY profile={coverage.SourceProfileId} " +
                $"map={coverage.MapIndex} floors={coverage.FloorPatches} " +
                "source=live-owned-install writes=save-only gameplay=fail-closed");
            if (DisplayServer.GetName() == "headless")
                GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_NATIVE_INSTALL_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(string[] arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= arguments.Length)
                continue;
            result[argument[2..]] = arguments[++index];
        }
        return result;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Fallout 2 requires --{key}.");
}
