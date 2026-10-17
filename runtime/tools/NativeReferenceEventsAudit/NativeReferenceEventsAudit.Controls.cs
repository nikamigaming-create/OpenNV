using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

public partial class NativeReferenceEventsAudit
{
    private async Task InputControls(FalloutPluginStack records)
    {
        var configuration = RuntimeConfiguration.Load().Player.DesktopInput;
        DesktopInputMap.Configure(configuration);
        var controls = new FalloutInputControls(name => name switch
        {
            "Forward" => "0011FF13",
            "Back" => "001FFF14",
            "Slide Left" => "001EFF17",
            "Slide Right" => "0020FF16",
            "Use" => "00FF0011",
            "Activate" => "0012FF0A",
            "Block" => "00380110",
            "Ready Item" => "0013FF0C",
            "Toggle POV" => "0021020F",
            _ => "00FFFFFF",
        });
        var keys = new FalloutScriptEvents();
        var adapter = new RuntimeNativeInputControls(controls, configuration, keys.IsKeyPressed);
        using var world = new FalloutReferenceWorld(records, controls: controls);
        var quests = new FalloutQuestState(records);
        var executor = new FalloutReferenceScripts(records, world, quests, new((_, _) => false,
            _ => throw new InvalidDataException("Control remap invented a source effect.")));
        var sourceInput = new RuntimeNativeScriptEvents(keys, () => executor.InvokeFunction) { Active = true };
        void Run(string command) => executor.ExecuteProgram(records.GetEffective(Key(0x600)), records.GetEffective(Key(0x510)),
            FalloutGameModeProgram.Read("begin GameMode\n" + command + "\nend"), 0);
        async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            AddChild(adapter);
            AddChild(sourceInput);
            using var forward = new InputEventKey { PhysicalKeycode = Godot.Key.W, Pressed = true };
            using var releaseForward = new InputEventKey { PhysicalKeycode = Godot.Key.W };
            Input.ParseInputEvent(forward); await Frame();
            Require(Input.GetVector(configuration.MoveLeft.Action, configuration.MoveRight.Action,
                configuration.MoveBackward.Action, configuration.MoveForward.Action).Y == 1,
                "Source control binding did not drive the ordinary native movement action.");
            Input.ParseInputEvent(releaseForward); await Frame();
            Run("SetControl 0 31\nSetControl 5 258 1\nSetControl 7 44\nSetControl 13 45");
            using var swapped = new InputEventKey { PhysicalKeycode = Godot.Key.S, Pressed = true };
            using var swappedUp = new InputEventKey { PhysicalKeycode = Godot.Key.S };
            using var activate = new InputEventMouseButton { ButtonIndex = MouseButton.Middle, Pressed = true };
            using var reload = new InputEventKey { PhysicalKeycode = Godot.Key.Z, Pressed = true };
            using var pov = new InputEventKey { PhysicalKeycode = Godot.Key.X, Pressed = true };
            Require(!forward.IsActionPressed(configuration.MoveForward.Action) && forward.IsActionPressed(configuration.MoveBackward.Action) &&
                swapped.IsActionPressed(configuration.MoveForward.Action) && activate.IsActionPressed(configuration.Activate.Action) &&
                reload.IsActionPressed(configuration.Reload.Action) && pov.IsActionPressed(RuntimeNativeInputControls.Action(13)),
                "Source remap did not swap movement or update keyboard/mouse player actions.");
            Input.ParseInputEvent(swapped); await Frame();
            Require(Input.IsActionPressed(configuration.MoveForward.Action), "Remapped movement has no live input state.");
            Input.ParseInputEvent(swappedUp); await Frame();
            using var fire = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            using var fireUp = new InputEventMouseButton { ButtonIndex = MouseButton.Left };
            Input.ParseInputEvent(fire); await Frame();
            Require(Input.IsActionPressed(configuration.Fire.Action), "Source fire binding has no held input state.");
            Run("SetControl 4 257 1");
            Require(!Input.IsActionPressed(configuration.Fire.Action) && !fire.IsActionPressed(configuration.Fire.Action) &&
                fire.IsActionPressed(RuntimeNativeInputControls.Action(6)), "A held remapped trigger remained stuck or failed its duplicate-key swap.");
            Input.ParseInputEvent(fireUp); await Frame();
            Run("SetControl 12 157");
            using var leftControl = new InputEventKey { PhysicalKeycode = Godot.Key.Ctrl, Location = KeyLocation.Left, Pressed = true };
            using var leftControlUp = new InputEventKey { PhysicalKeycode = Godot.Key.Ctrl, Location = KeyLocation.Left };
            using var rightControl = new InputEventKey { PhysicalKeycode = Godot.Key.Ctrl, Location = KeyLocation.Right, Pressed = true };
            using var rightControlUp = new InputEventKey { PhysicalKeycode = Godot.Key.Ctrl, Location = KeyLocation.Right };
            Input.ParseInputEvent(leftControl); await Frame();
            Run("SetControl 12 29");
            Require(Input.IsActionPressed(configuration.Jump.Action), "A held left modifier did not follow its remap.");
            Run("SetControl 12 157");
            Require(!Input.IsActionPressed(configuration.Jump.Action) && !leftControl.IsActionPressed(configuration.Jump.Action) &&
                rightControl.IsActionPressed(configuration.Jump.Action), "Left/right modifier identity collapsed during remapping.");
            Input.ParseInputEvent(rightControl); await Frame();
            Input.ParseInputEvent(leftControlUp); await Frame();
            Require(Input.IsActionPressed(configuration.Jump.Action), "Releasing the other modifier released the bound right key.");
            Input.ParseInputEvent(rightControlUp); await Frame();
            Require(!Input.IsActionPressed(configuration.Jump.Action), "Right modifier release left a held control.");
            var revision = controls.Revision;
            try { Run("SetControl 0 250"); throw new InvalidDataException("Unknown native key was accepted."); }
            catch (NotSupportedException) { }
            Require(controls.Revision == revision && controls.Get(0) == 31 && swapped.IsActionPressed(configuration.MoveForward.Action),
                "Rejected physical key corrupted the owner or native map.");
            adapter.Free();
            Run("SetControl 0 17");
            Require(swapped.IsActionPressed(configuration.MoveForward.Action), "A retired adapter continued changing native input.");
            GD.Print("OPENNV_NATIVE_INPUT_CONTROLS_PASS sourceCommands=true movement=true swap=true mouse=true reload=true pointOfView=true heldRemap=true modifierIdentity=true invalidAtomic=true retirement=true recording=false fixture=true parity=unverified");
        }
        finally
        {
            if (GodotObject.IsInstanceValid(adapter)) adapter.Free();
            if (GodotObject.IsInstanceValid(sourceInput)) sourceInput.Free();
            DesktopInputMap.Configure(configuration);
        }
    }
}
