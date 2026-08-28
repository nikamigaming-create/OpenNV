using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

public sealed partial class Fo2ArroyoCavesRenderProofHost : Node3D
{
    public override void _Ready()
    {
        try
        {
            var options = ParseOptions(OS.GetCmdlineUserArgs());
            var temple = Fo2TemplePresentationCatalog.Load(
                Require(options, "fo2-temple-cache"));
            var transition = Fo2TempleTransitionCatalog.Load(
                Require(options, "fo2-temple-transitions"),
                temple);
            var arroyo = Fo2ArroyoCavesPresentationCatalog.Load(
                Require(options, "fo2-arroyo-cache"),
                transition);
            var coverage = Fo2ArroyoCavesScene.Build(arroyo, this);
            _ = Fo2ArroyoCavesRenderProof.Run(
                this,
                coverage,
                Require(options, "fo2-arroyo-render-proof"));
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO2_ARROYO_RENDER_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private static IReadOnlyDictionary<string, string> ParseOptions(
        IReadOnlyList<string> arguments)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal) ||
                argument.Length == 2 || index + 1 >= arguments.Count ||
                arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Fallout 2 proof option is invalid: {argument}");
            if (!options.TryAdd(argument[2..], arguments[++index]))
                throw new ArgumentException($"Fallout 2 proof option is duplicated: {argument}");
        }
        return options;
    }

    private static string Require(
        IReadOnlyDictionary<string, string> options,
        string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"--{name} is required.");
}
