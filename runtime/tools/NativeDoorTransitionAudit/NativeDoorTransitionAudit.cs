using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Tools;

public partial class NativeDoorTransitionAudit : Node
{
    private const float GameUnitsToMeters = 0.0142875f;

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
            GD.PrintErr($"OPENNV_NATIVE_DOOR_TRANSITION_AUDIT_ERROR {error.GetType().Name}: {error.Message}");
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
        Node3D? streamedRoot = null;
        var portal = new RuntimeNativeDoorPortal();
        portal.Configure(
            transition.SourceDoor.FormKey,
            transition.DestinationDoor.FormKey,
            transition.DestinationScene.Cell.FormKey,
            transition.DestinationWorldspace,
            () =>
            {
                streamedRoot = new Node3D { Name = $"NativeCell_{transition.DestinationScene.Cell.FormKey}" };
                streamedRoot.SetMeta("opennv_cell", transition.DestinationScene.Cell.FormKey.ToString());
                streamedRoot.SetMeta("opennv_world", transition.DestinationWorldspace.ToString());
                streamedRoot.AddChild(new Camera3D
                {
                    Name = $"NativeDoorEntry_{transition.DestinationDoor.FormKey}",
                    Transform = TeleportTransform(transition.SourceDoor.Teleport!),
                });
                AddChild(streamedRoot);
            });
        AddChild(portal);
        portal.Activate();
        if (streamedRoot is null ||
            streamedRoot.GetChildren().OfType<Camera3D>().SingleOrDefault() is not { } camera ||
            !camera.Transform.Origin.IsFinite() ||
            portal.Reference != transition.SourceDoor.FormKey ||
            portal.Destination != transition.DestinationDoor.FormKey ||
            portal.DestinationCell != transition.DestinationScene.Cell.FormKey ||
            portal.DestinationWorldspace != transition.DestinationWorldspace)
            throw new InvalidDataException("Godot native door live-load entry differs from the XTEL graph.");
        GD.Print(
            $"OPENNV_NATIVE_DOOR_TRANSITION_AUDIT_OK sourceCell={transition.SourceScene.Cell.FormKey} " +
            $"sourceDoor={transition.SourceDoor.FormKey} destinationDoor={transition.DestinationDoor.FormKey} " +
            $"destinationCell={transition.DestinationScene.Cell.FormKey} " +
            $"coordinates={transition.DestinationScene.Cell.Coordinates} " +
            $"world={transition.DestinationWorldspace} worldEditorId={transition.DestinationWorldspaceEditorId} " +
            $"entryMeters={camera.Transform.Origin} reciprocal=true godotEntry=activated " +
            "source=live-owned-stack cache=none");
        return 0;
    }

    private static Transform3D TeleportTransform(FalloutTeleportDestination destination) => new(
        GamebryoCoordinate.ConvertReferenceEuler(
            new Vector3(
                destination.RotationRadians[0],
                destination.RotationRadians[1],
                destination.RotationRadians[2]),
            1.0f),
        GamebryoCoordinate.ConvertVector(new Vector3(
            destination.Position[0], destination.Position[1], destination.Position[2])) *
        GameUnitsToMeters);

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
