using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceTargetAudit
{
    private static void DoorObservation(string phase, FalloutFormKey key, Node3D target, RuntimeNativePlayer player,
        NativeReferenceGeometryObservation geometry, RuntimeNativeDoorMotion motion, RuntimeNifControllerPlayer controller)
    {
        GD.Print("OPENNV_OWNED_DOOR_GEOMETRY_OBSERVATION " + JsonSerializer.Serialize(new
        {
            phase,
            reference = key.ToString(),
            pivot = Point(target.GlobalPosition),
            feet = Point(player.GlobalPosition),
            visibleMinimum = Point(geometry.Bounds.Position),
            visibleMaximum = Point(geometry.Bounds.End),
            nativeFloor = Point(geometry.Target),
            surface = Point(geometry.Aim),
            geometry.AimCollider,
            geometry.AimShape,
            geometry.FloorCollider,
            geometry.FloorShape,
            horizontalPivotOffset = new Vector2(target.GlobalPosition.X - geometry.Target.X, target.GlobalPosition.Z - geometry.Target.Z).Length(),
            cameraToSurface = player.Camera.GlobalPosition.DistanceTo(geometry.Aim),
            sourceController = controller.SourceController,
            sourceSha256 = controller.SourceSha256,
            sourceClock = controller.SourceTimeSeconds,
            doorState = motion.OpenState(),
            unboundSourceTextKeys = controller.UnboundTextKeys,
            geometryComponentOnly = true,
            audio = false,
            campaign = false
        }));
    }

    private async Task ExerciseDoorGeometry(FalloutFormKey key, Node3D target, RuntimeNativePlayer player,
        RuntimeNativeDoorMotion motion, RuntimeNifControllerPlayer controller, Transform3D[] closedModels)
    {
        var placement = target.GlobalTransform;
        Transform3D[] Models() => target.FindChildren("*", nameof(MeshInstance3D), true, false)
            .OfType<MeshInstance3D>().Select(mesh => mesh.GlobalTransform).ToArray();
        foreach (var open in new[] { true, false })
        {
            motion.SetOpen(open); controller.SetProcess(false);
            controller._Process(controller.FiniteEffectDuration); motion.Synchronize(); await Sync();
            Require(!controller.Playing && motion.OpenState() == (open ? 1 : 3) && target.GlobalTransform == placement,
                "Source door clock did not settle without changing the reference placement.");
            var geometry = Observe(target, player);
            DoorObservation(open ? "open" : "closed-again", key, target, player, geometry, motion, controller);
            Require(geometry.FloorCollider != 0, "Source door geometry used its pivot as fabricated support.");
            if (open) Require(!Models().SequenceEqual(closedModels), "Source Open clock did not move any authored mesh.");
            await Approach(target, player, geometry);
        }
        var closedAgain = Models();
        Require(closedAgain.Length == closedModels.Length && closedAgain.Zip(closedModels)
            .All(pair => pair.First.IsEqualApprox(pair.Second)), "Source Close did not return to its authored closed mesh pose.");
        GD.Print("OPENNV_OWNED_DOOR_GEOMETRY_PASS reference=" + key +
            " closedOpenClosed=true actualFloor=true actualCapsuleApproach=true ordinaryRayAndRange=true sourcePlacementUnchanged=true" +
            " component=true audio=false campaign=false pixels=false recording=false");
    }
}
