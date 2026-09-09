using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeDoorMotion : Node
{
    private readonly FalloutReferenceInstance _state;
    private readonly Action _changed;
    private readonly RuntimeNifControllerPlayer _controller;
    private readonly string _open, _close;
    private bool _moving;

    internal RuntimeNativeDoorMotion(FalloutReferenceInstance state, IReadOnlyList<RuntimeNifControllerPlayer> controllers, Action changed)
    {
        _state = state; _changed = changed;
        var candidates = controllers.Where(controller =>
            controller.SequenceNames.Any(name => name.Equals("Open", StringComparison.OrdinalIgnoreCase)) &&
            controller.SequenceNames.Any(name => name.Equals("Close", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (candidates.Length != 1) throw new NotSupportedException("Door has no unique source Open/Close controller.");
        _controller = candidates[0];
        _open = _controller.SequenceNames.Single(name => name.Equals("Open", StringComparison.OrdinalIgnoreCase));
        _close = _controller.SequenceNames.Single(name => name.Equals("Close", StringComparison.OrdinalIgnoreCase));
    }
    public override void _Ready()
    {
        var sequence = _state.DoorOpen ? _open : _close;
        _controller.PlaySourceSequence(sequence);
        _controller.SeekSourceTime(_controller.SequenceRange(sequence).StopTime);
        _controller.SetProcess(false);
    }
    internal void Activate()
    {
        if (_moving) return;
        var open = !_state.DoorOpen;
        _controller.PlaySourceSequence(open ? _open : _close);
        _state.DoorOpen = open;
        _moving = true;
    }
    public override void _Process(double delta)
    {
        if (!_moving || _controller.IsProcessing()) return;
        _moving = false; _changed();
        GD.Print($"OPENNV_NATIVE_DOOR_MOTION reference={_state.Reference} open={_state.DoorOpen} sourceClock=complete");
    }
}
