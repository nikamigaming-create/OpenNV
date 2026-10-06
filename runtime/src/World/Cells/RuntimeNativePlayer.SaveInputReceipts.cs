using Godot;
using OpenNV.Runtime.InputSystem;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private object? _lastSaveInputIngress, _lastSaveInputUnhandled, _lastSaveInputDispatch, _lastSaveInputReturned;

    // These receipts observe this real player's input path. They do not claim
    // that an event consumed by an earlier UI node reached this player.
    internal object SaveInputState => new
    {
        nativeOwner = GetInstanceId(),
        sourceControl = 25,
        profileRevision = _inputControls?.Revision,
        declaredKeyboard = _inputControls?.Get(25),
        declaredMouse = _inputControls?.Get(25, 1),
        sourceAction = RuntimeNativeInputControls.Action(25),
        aliasAction = _configuration.Player.DesktopInput.Save.Action,
        sourceMap = SaveActionMap(RuntimeNativeInputControls.Action(25)),
        aliasMap = SaveActionMap(_configuration.Player.DesktopInput.Save.Action),
        callbackBound = SaveGame is not null,
        canProcess = CanProcess(),
        inputEnabled = IsProcessingInput(),
        unhandledInputEnabled = IsProcessingUnhandledInput(),
        lastIngress = _lastSaveInputIngress,
        lastUnhandled = _lastSaveInputUnhandled,
        lastDispatch = _lastSaveInputDispatch,
        lastReturned = _lastSaveInputReturned
    };

    public override void _Input(InputEvent input) => ObserveSaveInput(input, "ingress");

    private void ObserveSaveInput(InputEvent input, string phase)
    {
        if (_configuration is null) return;
        if (input is not InputEventKey { Pressed: true } and not InputEventMouseButton { Pressed: true }) return;
        var code = SavePhysicalCode(input);
        var configuredPhysical = Enum.Parse<Key>(_configuration.Player.DesktopInput.Save.PhysicalKey, true);
        var configuredKey = input is InputEventKey keyboard &&
            (keyboard.PhysicalKeycode == configuredPhysical || keyboard.Keycode == configuredPhysical);
        var declaredCode = input is InputEventMouseButton ? _inputControls?.Get(25, 1) : _inputControls?.Get(25);
        if (!configuredKey && !(code is > 0 && code == declaredCode) &&
            !MatchesSaveAction(input, _configuration.Player.DesktopInput.Save.Action)) return;
        var receipt = new
        {
            eventOwner = input.GetInstanceId(),
            nativeOwner = GetInstanceId(),
            enginePhase = Engine.GetProcessFrames(),
            sampledMilliseconds = Time.GetTicksMsec(),
            phase,
            physicalCode = code,
            key = input is InputEventKey key ? key.PhysicalKeycode.ToString() : null,
            echo = input is InputEventKey { Echo: true },
            matchesAlias = MatchesSaveAction(input, _configuration.Player.DesktopInput.Save.Action),
            matchesSource = MatchesSaveAction(input, RuntimeNativeInputControls.Action(25)),
            modal = _modalInput,
            furniturePhase = _furniturePhase,
            xr = _xr is not null,
            paused = GetTree().Paused,
            callbackBound = SaveGame is not null,
            inputEnabled = IsProcessingInput(),
            unhandledInputEnabled = IsProcessingUnhandledInput(),
            canProcess = CanProcess()
        };
        if (phase == "ingress") _lastSaveInputIngress = receipt;
        else if (phase == "unhandled") _lastSaveInputUnhandled = receipt;
        else if (phase == "dispatch") _lastSaveInputDispatch = receipt;
        else if (phase == "returned") _lastSaveInputReturned = receipt;
        else throw new ArgumentException("Unknown player input receipt phase.", nameof(phase));
    }

    private static bool MatchesSaveAction(InputEvent input, string action) =>
        InputMap.HasAction(action) && input.IsActionPressed(action);

    private static int? SavePhysicalCode(InputEvent input) => input is InputEventKey key
        ? NativeScriptKeys.Code(key.PhysicalKeycode, key.Location)
        : NativeScriptKeys.Read(input).Select(value => (int?)value.Key).FirstOrDefault();

    private static object SaveActionMap(string action) => new
    {
        exists = InputMap.HasAction(action),
        events = InputMap.HasAction(action) ? InputMap.ActionGetEvents(action).Select(input => new
        {
            type = input.GetClass().ToString(),
            physicalCode = SavePhysicalCode(input),
            physicalKey = input is InputEventKey key ? key.PhysicalKeycode.ToString() : null,
            logicalKey = input is InputEventKey logical ? logical.Keycode.ToString() : null,
            location = input is InputEventKey located ? located.Location.ToString() : null,
            mouseButton = input is InputEventMouseButton mouse ? mouse.ButtonIndex.ToString() : null
        }).ToArray() : null
    };
}
