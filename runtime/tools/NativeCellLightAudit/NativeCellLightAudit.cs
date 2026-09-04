using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Tools;

public partial class NativeCellLightAudit : Node
{
    private const float GameUnitsToMeters = 0.0142875f;
    private const float EnergyScale = 1.0f;
    private const float MinimumEnergy = 0.01f;

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
            RuntimeOwnedContentSource.Configure(
                dataRoot,
                manifest,
                Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
                root.GetProperty("stackId").GetString());
            exitCode = Audit(arguments.GetValueOrDefault("cell", "FalloutNV.esm:103df9"));
        }
        catch (Exception error)
        {
            GD.PrintErr($"OPENNV_NATIVE_CELL_LIGHT_AUDIT_ERROR {error.GetType().Name}: {error.Message}");
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
        var source = RuntimeOwnedContentSource.Current!;
        using var stack = FalloutPluginStack.Load(source.PluginSources);
        var cell = FalloutCellSceneReader.Read(
            stack, new FalloutFormKey(cellText[..separator], objectId));
        var host = new Node3D { Name = "NativeCellLightAuditHost" };
        AddChild(host);
        foreach (var reference in cell.References.Where(reference =>
                     !FalloutCellSceneReader.IsInitiallyDisabled(reference) &&
                     cell.BaseObjects[reference.Base].Light is not null))
        {
            var transform = new Transform3D(
                GamebryoCoordinate.ConvertReferenceEuler(
                    new Vector3(reference.RotationRadians[0], reference.RotationRadians[1],
                        reference.RotationRadians[2]), reference.Scale),
                GamebryoCoordinate.ConvertVector(
                    new Vector3(reference.Position[0], reference.Position[1], reference.Position[2])) *
                GameUnitsToMeters);
            host.AddChild(RuntimeNativePlacedLightBuilder.Build(
                reference, cell.BaseObjects[reference.Base], transform,
                GameUnitsToMeters, EnergyScale, MinimumEnergy, shadows: false));
        }
        var lights = host.GetChildren().OfType<OmniLight3D>().ToArray();
        if (lights.Length == 0 || lights.Any(light =>
                light.OmniRange <= 0.0f || light.LightEnergy <= 0.0f ||
                light.OmniAttenuation != 0.0f ||
                !light.HasMeta("opennv_ligh_reference") ||
                !light.HasMeta("opennv_ligh_base")))
            throw new InvalidDataException("Materialized native placed lights failed their Godot contract.");
        GD.Print(
            $"OPENNV_NATIVE_CELL_LIGHT_AUDIT_OK cell={cell.Cell.FormKey} lights={lights.Length} " +
            $"range={lights.Min(light => light.OmniRange):R}..{lights.Max(light => light.OmniRange):R} " +
            $"energy={lights.Min(light => light.LightEnergy):R}..{lights.Max(light => light.LightEnergy):R} " +
            "kind=OmniLight3D source=live-owned-stack cache=none");
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
