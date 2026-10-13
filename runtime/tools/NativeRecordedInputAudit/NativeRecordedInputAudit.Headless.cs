using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.InputSystem;

public partial class NativeRecordedInputAudit
{
    private async Task ExerciseHeadlessInputOwner()
    {
        Require(DisplayServer.GetName() == "headless", "Headless input audit requires the actual headless backend.");
        var controls = new FalloutInputControls(name => name == "Forward" ? "0011FF13" : "00FFFFFF");
        var input = RuntimeNativeDirectInput.Create(new('2', 64), controls, commandInput: true);
        RuntimeNativeInputControls? adapter = null;
        try
        {
            AddChild(input); await input.WaitForFirstSample(() => false);
            adapter = new(controls, RuntimeConfiguration.Load().Player.DesktopInput, _ => false);
            adapter.BindDirectInput(input.State); AddChild(adapter);
            using var press = new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = true };
            Input.ParseInputEvent(press);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            input.Poll(); adapter._Process(0);
            Require(input.State.Pressed(17) && Input.IsActionPressed(RuntimeNativeInputControls.Action(0)) &&
                input.PlayerManager().Provider == FalloutInputProvider.HeadlessCommands && input.State.Window == 0,
                "Actual headless command delivery missed the shared Player control/input-manager owners.");
            using var release = new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = false };
            Input.ParseInputEvent(release);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            input.Poll(); adapter._Process(0);
            Require(!input.State.Pressed(17) && !Input.IsActionPressed(RuntimeNativeInputControls.Action(0)),
                "Headless release left its authoritative or Godot control held.");
            input.State.SetDisable(17, true, 1);
            Input.ParseInputEvent(press);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            input.Poll(); adapter._Process(0);
            Require(input.State.Pressed(17, 2) && !input.State.Pressed(17) && !Input.IsActionPressed(RuntimeNativeInputControls.Action(0)),
                "Headless input bypassed the existing source disable lane.");
            Input.ParseInputEvent(release);
            GD.Print("OPENNV_NATIVE_HEADLESS_INPUT_PASS backend=headless sharedPlayerControls=true window=none recording=false gameplay=UNEXECUTED");
        }
        finally
        {
            adapter?.Free(); input.Free();
            using var release = new InputEventKey { PhysicalKeycode = Key.W, Keycode = Key.W, Pressed = false };
            Input.ParseInputEvent(release);
        }
    }
}
