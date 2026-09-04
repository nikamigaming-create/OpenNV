using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Diagnostics.Parity;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private readonly ParityObservationRegistry _parityObservations = new();
    private static readonly ulong ConfigurationField =
        ParityStableId.FromName("execution.configuration.sha256");
    private static readonly ulong CellField = ParityStableId.FromName("world.cell.form-key");
    private static readonly ulong ReferenceCountField =
        ParityStableId.FromName("world.cell.reference-count");
    private static readonly ulong PlayerPositionXField =
        ParityStableId.FromName("actor.player.root.position.x");
    private static readonly ulong PlayerPositionYField =
        ParityStableId.FromName("actor.player.root.position.y");
    private static readonly ulong PlayerPositionZField =
        ParityStableId.FromName("actor.player.root.position.z");
    private static readonly ulong PlayerRotationXField =
        ParityStableId.FromName("actor.player.root.rotation.x");
    private static readonly ulong PlayerRotationYField =
        ParityStableId.FromName("actor.player.root.rotation.y");
    private static readonly ulong PlayerRotationZField =
        ParityStableId.FromName("actor.player.root.rotation.z");
    private static readonly ulong PlayerRotationWField =
        ParityStableId.FromName("actor.player.root.rotation.w");
    private static readonly ulong CameraPositionXField =
        ParityStableId.FromName("camera.position.x");
    private static readonly ulong CameraPositionYField =
        ParityStableId.FromName("camera.position.y");
    private static readonly ulong CameraPositionZField =
        ParityStableId.FromName("camera.position.z");
    private static readonly ulong CameraRotationXField =
        ParityStableId.FromName("camera.rotation.x");
    private static readonly ulong CameraRotationYField =
        ParityStableId.FromName("camera.rotation.y");
    private static readonly ulong CameraRotationZField =
        ParityStableId.FromName("camera.rotation.z");
    private static readonly ulong CameraRotationWField =
        ParityStableId.FromName("camera.rotation.w");
    private static readonly ulong CameraFovField = ParityStableId.FromName("camera.fov-degrees");
    private static readonly ulong CameraNearField = ParityStableId.FromName("camera.near-meters");
    private static readonly ulong CameraFarField = ParityStableId.FromName("camera.far-meters");
    private static readonly ulong RendererField = ParityStableId.FromName("renderer.method");

    private void EnableParityPublisher(string channel, string? captureDirectory)
    {
        var publisher = new ParityOpenNvPublisher();
        publisher.Configure(channel, CaptureParityFrame, captureDirectory);
        AddChild(publisher);
        GD.Print($"OPENNV_PARITY_PUBLISHER_READY channel={channel}");
    }

    private ParityTelemetryFrame CaptureParityFrame(ulong sequence)
    {
        var fields = new List<ParityTelemetryField>
        {
            ParityTelemetryField.Utf8(
                ParityCategory.Execution,
                ConfigurationField,
                _configuration.Sha256),
            ParityTelemetryField.Utf8(
                ParityCategory.Renderer,
                RendererField,
                RenderingServer.GetCurrentRenderingMethod().ToString()),
        };
        var stateKey = "startup";
        if (_nativeActiveCell is { } cell)
        {
            stateKey = $"cell:{cell.Cell.FormKey}";
            fields.Add(ParityTelemetryField.Utf8(
                ParityCategory.World,
                CellField,
                cell.Cell.FormKey.ToString()));
            fields.Add(ParityTelemetryField.UInt64(
                ParityCategory.World,
                ReferenceCountField,
                (ulong)cell.References.Count));
        }
        if (_nativePlayer is { } player)
        {
            var position = player.GlobalPosition;
            var rotation = player.GlobalBasis.GetRotationQuaternion().Normalized();
            fields.Add(ParityTelemetryField.Float64(
                ParityCategory.Actor, PlayerPositionXField, position.X));
            fields.Add(ParityTelemetryField.Float64(
                ParityCategory.Actor, PlayerPositionYField, position.Y));
            fields.Add(ParityTelemetryField.Float64(
                ParityCategory.Actor, PlayerPositionZField, position.Z));
            fields.Add(ParityTelemetryField.Float64(
                ParityCategory.Actor, PlayerRotationXField, rotation.X));
            fields.Add(ParityTelemetryField.Float64(
                ParityCategory.Actor, PlayerRotationYField, rotation.Y));
            fields.Add(ParityTelemetryField.Float64(
                ParityCategory.Actor, PlayerRotationZField, rotation.Z));
            fields.Add(ParityTelemetryField.Float64(
                ParityCategory.Actor, PlayerRotationWField, rotation.W));
        }
        var camera = GetViewport().GetCamera3D();
        if (camera is not null)
        {
            var position = camera.GlobalPosition;
            var rotation = camera.GlobalBasis.GetRotationQuaternion();
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraPositionXField, position.X));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraPositionYField, position.Y));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraPositionZField, position.Z));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraRotationXField, rotation.X));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraRotationYField, rotation.Y));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraRotationZField, rotation.Z));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraRotationWField, rotation.W));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraFovField, camera.Fov));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraNearField, camera.Near));
            fields.Add(ParityTelemetryField.Float64(ParityCategory.Camera, CameraFarField, camera.Far));
        }
        var observations = _parityObservations.Snapshot();
        fields.AddRange(observations.Fields);
        var nanoseconds = checked((long)Math.Round(
            Stopwatch.GetTimestamp() * (1_000_000_000.0 / Stopwatch.Frequency),
            MidpointRounding.ToEven));
        return new ParityTelemetryFrame(
            ParityEngine.OpenNv,
            sequence,
            checked((long)Engine.GetPhysicsFrames()),
            nanoseconds,
            observations.EventOrdinal,
            stateKey,
            fields);
    }
}
