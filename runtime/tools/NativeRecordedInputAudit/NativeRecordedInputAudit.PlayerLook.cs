using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

public partial class NativeRecordedInputAudit
{
    private async Task ExercisePlayerLook(Func<object, Task<JsonElement>> send)
    {
        Require(DisplayServer.GetName() == "headless", "Player harness-look audit requires the actual headless display server.");
        var configuration = RuntimeConfiguration.Load();
        DesktopInputMap.Configure(configuration.Player.DesktopInput);
        var previousMouseMode = Input.MouseMode;
        var player = new RuntimeNativePlayer();
        try
        {
            player.Configure(configuration, Transform3D.Identity, FalloutCameraProjection.FromReferenceFov(75, 1));
            const float sensitivity = .003f;
            player.ConfigureFlatMouseLook(new FalloutNumericIniSettings(
                [new("fMouseSensitivity:Controls", FalloutIniCollection.Main, BitConverter.SingleToUInt32Bits(sensitivity)),
                 new("bInvertYValues:Controls", FalloutIniCollection.Main, 0)], [], "synthetic-input-profile"));
            AddChild(player); player.SetProcess(false); player.SetPhysicsProcess(false);
            Require(player.IsProcessingUnhandledInput(), "Actual player unhandled-input callback is disabled.");
            Input.MouseMode = Input.MouseModeEnum.Visible;
            var yaw = 0f; var pitch = 0f;
            await Look(17, -11);
            yaw -= 17 * sensitivity; pitch += 11 * sensitivity;
            RequirePose(yaw, pitch);
            Require(Input.MouseMode == Input.MouseModeEnum.Visible, "Headless harness look captured the system mouse.");

            foreach (var mode in new[] { "untagged", "false", "integer", "string" })
            {
                using var motion = new InputEventMouseMotion { Relative = new(41, 37) };
                if (mode == "false") motion.SetMeta("opennv_harness_input", false);
                if (mode == "integer") motion.SetMeta("opennv_harness_input", 1);
                if (mode == "string") motion.SetMeta("opennv_harness_input", "true");
                Require(!RuntimeLiveHarness.IsHeadlessLookInput(motion), "Invalid harness tag admitted uncaptured mouse look.");
                Input.ParseInputEvent(motion);
                await Frame(); RequirePose(yaw, pitch);
            }
            using (var key = new InputEventKey())
            {
                key.SetMeta("opennv_harness_input", true);
                Require(!RuntimeLiveHarness.IsHeadlessLookInput(key), "Harness look predicate admitted a non-motion event.");
            }

            player.SetModalInput(true);
            await Look(29, 31); RequirePose(yaw, pitch);
            player.SetModalInput(false);
            var controls = player.SourceControls;
            player.ApplySourceControls(controls with { Looking = false });
            await Look(29, 31); RequirePose(yaw, pitch);
            player.ApplySourceControls(controls);

            // The normal player clamp still owns harness-dispatched pitch.
            await Look(0, 1_000_000);
            pitch = -configuration.Player.VerticalLookLimitRadians; RequirePose(yaw, pitch);
            await Look(0, -2_000_000);
            pitch = configuration.Player.VerticalLookLimitRadians; RequirePose(yaw, pitch);

            // Source-authored camera transforms still receive the same pitch
            // owner; the adapter does not replace that camera with a free view.
            var authored = new Transform3D(new Basis(Vector3.Up, .2f), new Vector3(.3f, 1.2f, -.4f));
            player.ApplySourceCamera(authored);
            await Look(0, 13);
            pitch -= 13 * sensitivity;
            var expectedCamera = authored * new Transform3D(new Basis(Vector3.Right, pitch), Vector3.Zero);
            Require(player.Camera.Transform.IsEqualApprox(expectedCamera), "Harness look displaced or replaced the authored source camera.");
            Require(Input.MouseMode == Input.MouseModeEnum.Visible, "Guarded harness look changed the system mouse mode.");
            GD.Print("OPENNV_HEADLESS_PLAYER_LOOK_PASS actualPlayer=true realHarnessDispatch=true unhandledCallback=true yawPitch=true pitchClamp=true modalAndLookingGuards=true untaggedFalseAndWrongTypeRefused=true sourceCameraRetained=true systemMouseUncaptured=true furnitureAndXR=existingGuards ordinaryCampaign=unverified");
        }
        finally { player.Free(); Input.MouseMode = previousMouseMode; }

        async Task Look(float dx, float dy)
        {
            await send(new { op = "look", dx, dy });
            await Frame();
        }
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        void RequirePose(float yaw, float pitch)
        {
            Require(player.Basis.IsEqualApprox(new Basis(Vector3.Up, yaw)) &&
                player.Camera.Basis.IsEqualApprox(new Basis(Vector3.Right, pitch)),
                "Actual player look did not retain its expected yaw/pitch or refusal state.");
        }
    }
}
