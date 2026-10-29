using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task LockpickInput()
    {
        var configuration = RuntimeConfiguration.Load();
        DesktopInputMap.Configure(configuration.Player.DesktopInput);
        var player = new RuntimeNativePlayer();
        player.Configure(configuration, Transform3D.Identity, FalloutCameraProjection.FromReferenceFov(75, 1));
        var controls = new FalloutInputControls(name => name switch
        {
            "Forward" => "0011FFFF",
            "Back" => "001FFFFF",
            "Slide Left" => "001EFFFF",
            "Slide Right" => "0020FFFF",
            _ => "00FFFFFF"
        });
        player.ConfigureInputControls(controls, _ => false);
        AddChild(player); player.SetProcess(false); player.SetPhysicsProcess(false);
        var tree = GetTree(); var paused = tree.Paused; var mouse = Input.MouseMode;
        NativeLockpickMenu? menu = null;
        Action? release = null;
        var closed = 0; var success = 0; var failure = 0; var errors = 0; var pins = 0;
        var rebound = RuntimeNativeInputControls.Action(2);
        async Task Press(Godot.Key key)
        {
            using var down = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true };
            Input.ParseInputEvent(down);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            using var up = new InputEventKey { PhysicalKeycode = key, Keycode = key };
            Input.ParseInputEvent(up);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        FalloutLockpickSession Open(int chance, bool throwing = false, bool manual = false, int length = 100,
            Func<uint, uint>? random = null)
        {
            var draw = random ?? (bound => bound == uint.MaxValue ? 0x80000000u : 0);
            void Complete() { ++success; if (throwing) throw new InvalidOperationException("deliberate-XP-prefix"); }
            var session = manual
                ? FalloutLockpickSession.CreateManual(() => chance, draw, Complete, () => ++failure,
                    1000, 500, length, 100, 1, 1, () => { ++pins; return 0; }, () => { }, new())
                : new FalloutLockpickSession(chance, draw, Complete, () => ++failure);
            release = player.AcquireModalInput();
            var localRelease = release; var retired = false;
            menu = new(session, _ =>
            {
                if (retired) return;
                retired = true; ++closed;
                try { tree.Paused = paused; Input.MouseMode = mouse; }
                finally { localRelease(); }
            }, _ => ++errors);
            AddChild(menu); tree.Paused = true; Input.MouseMode = Input.MouseModeEnum.Visible;
            return session;
        }
        try
        {
            var velocity = new Vector3(1, 2, 3);
            player.Velocity = velocity;
            var randomBefore = FalloutSharedUnsignedRandom.Process.Observation;
            var refused = false;
            try { Open(100, manual: true, length: 0, random: FalloutSharedUnsignedRandom.Process.NextBounded); }
            catch (NotSupportedException) { refused = true; }
            Require(refused && FalloutSharedUnsignedRandom.Process.Observation == randomBefore &&
                player.Velocity == velocity && !player.ModalInput && release is null && menu is null &&
                tree.Paused == paused && Input.MouseMode == mouse && success == 0 && failure == 0 && pins == 0,
                "Rejected manual preparation changed shared RNG, player input, velocity or effects.");
            var session = Open(100);
            Require(player.ModalInput && player.Velocity == Vector3.Zero, "Legitimate modal admission did not acquire/cancel real player input.");
            await Press(Godot.Key.F); await Press(Godot.Key.F);
            Require(session.Turning && closed == 0 && success == 0 && errors == 0,
                "Duplicate Force input cancelled a successful pending turn.");
            await ToSignal(tree.CreateTimer(1.15, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            Require(success == 1 && closed == 1 && !player.ModalInput && tree.Paused == paused && Input.MouseMode == mouse,
                "Successful source-clock turn did not restore real input/pause/mouse state.");
            menu!.Free(); menu = null;
            Open(0); await Press(Godot.Key.F);
            Require(failure == 1 && closed == 2 && !player.ModalInput && errors == 0,
                "Failed Force did not retire and restore real player input once.");
            menu!.Free(); menu = null;
            session = Open(100); await Press(Godot.Key.F); await Press(Godot.Key.Escape);
            Require(session.Finished && closed == 3 && success == 1 && !player.ModalInput,
                "Cancel committed a pending unlock or retained modal input.");
            menu!.Free(); menu = null;
            player.SetModalInput(true); Open(100); menu!.Free(); menu = null;
            Require(player.ModalInput && closed == 4, "Menu disposal released another owner's requested modal input.");
            player.SetModalInput(false);
            Open(100, throwing: true); await Press(Godot.Key.F);
            await ToSignal(tree.CreateTimer(1.15, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            Require(errors == 1 && closed == 5 && !player.ModalInput && tree.Paused == paused && Input.MouseMode == mouse,
                "An exceptional completion stranded ordinary player controls.");
            menu!.Free(); menu = null;
            Open(100); menu!.Notification((int)NotificationApplicationFocusOut);
            Require(closed == 6 && !player.ModalInput, "Focus loss retained the force panel's input lease.");
            menu!.Free(); menu = null;
            controls.Set(2, 0x24); // Actual source-profile remap from A to J.
            session = Open(100, manual: true);
            await Press(Godot.Key.A);
            Require(session.ManualPhase == 2, "Manual turn ignored the rebound source control.");
            using var held = new InputEventKey { PhysicalKeycode = Godot.Key.J, Keycode = Godot.Key.J, Pressed = true };
            Input.ParseInputEvent(held);
            await ToSignal(tree.CreateTimer(1.2, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            using var lifted = new InputEventKey { PhysicalKeycode = Godot.Key.J, Keycode = Godot.Key.J };
            Input.ParseInputEvent(lifted);
            Require(session.Won && success == 3 && closed == 7 && pins == 0 && !player.ModalInput,
                "Actual rebound manual input did not complete the aligned source turn.");
            menu!.Free(); menu = null;
            session = Open(100, manual: true);
            using var motion = new InputEventMouseMotion { Relative = new(300, 0), ScreenRelative = new(300, 0) };
            Input.ParseInputEvent(motion);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Require(session.Position == 800, $"Actual relative mouse input did not reach the pick owner: position={session.Position}, phase={session.ManualPhase}.");
            Input.ParseInputEvent(held);
            await ToSignal(tree.CreateTimer(1.3, processAlways: true), SceneTreeTimer.SignalName.Timeout);
            Input.ParseInputEvent(lifted);
            Require(pins == 1 && closed == 8 && !session.Won && !player.ModalInput,
                "Actual obstructed input did not retire on exactly one depleted pin.");
            menu!.Free(); menu = null;
            // Recipe and other modals may keep the world unpaused. Player
            // input must not consume Escape before the modal receives it.
            session = Open(100);
            tree.Paused = false;
            await Press(Godot.Key.Escape);
            Require(session.Finished && closed == 9 && !player.ModalInput,
                "Unpaused player consumed modal Cancel before the menu could retire.");
            menu!.Free(); menu = null;
            GD.Print("OPENNV_LOCKPICK_INPUT_PASS actualKeyIngress=true duplicateDuringTurn=true retainedSourceClock=true success=true failure=true cancel=true " +
                "exception=true disposal=true nestedModal=true unpausedModalCancel=true focusLoss=true originalPauseMouse=true rejectedAdmissionUnchanged=true reboundManual=true relativeMouse=true obstructionPin=true actualWorldUnlockAndOwnedActivation=unverified");
        }
        finally
        {
            if (menu is not null && GodotObject.IsInstanceValid(menu)) menu.Free();
            try { release?.Invoke(); }
            finally
            {
                Input.ActionRelease(rebound);
                tree.Paused = paused; Input.MouseMode = mouse; player.Free();
            }
        }
    }
}
