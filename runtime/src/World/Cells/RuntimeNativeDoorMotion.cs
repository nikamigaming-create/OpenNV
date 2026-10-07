using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeDoorMotion : Node
{
    private readonly FalloutReferenceInstance _state;
    private readonly Action _changed;
    private readonly RuntimeNifControllerPlayer _controller;
    private readonly string _open, _close;

    internal static bool HasOpenClose(IReadOnlyList<RuntimeNifControllerPlayer> controllers) => controllers.Any(controller =>
        controller.HasSequence("Open") || controller.HasSequence("Close"));

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
        _controller.RequireManagedFiniteSequence(_open);
        _controller.RequireManagedFiniteSequence(_close);
        RequireSavedSourceMatch(_state, _controller);
    }

    internal static void RequireSavedSourceMatch(FalloutReferenceInstance state, RuntimeNifControllerPlayer controller)
    {
        if (state.DoorMotion is { } saved)
        {
            var clocks = state.CaptureObjectAnimations?.Invoke() ?? state.ObjectAnimations;
            saved.Validate(state.DoorOpen, clocks);
            if (saved.Controller != controller.SourceController ||
                !saved.Sha256.Equals(controller.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Saved door motion differs from its winning model controller.");
            controller.ValidateScriptState(clocks!.Single(value => value.Controller == saved.Controller &&
                value.Sha256.Equals(saved.Sha256, StringComparison.OrdinalIgnoreCase)));
        }
    }
    public override void _Ready()
    {
        if (_state.DoorMotion is { } saved)
        {
            var clocks = _state.CaptureObjectAnimations?.Invoke() ?? _state.ObjectAnimations;
            saved.Validate(_state.DoorOpen, clocks);
            if (saved.Controller != _controller.SourceController ||
                !saved.Sha256.Equals(_controller.SourceSha256, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Saved door motion differs from its winning model controller.");
            var clock = clocks!.Single(value => value.Controller == saved.Controller &&
                value.Sha256.Equals(saved.Sha256, StringComparison.OrdinalIgnoreCase));
            _controller.RestoreScriptState(clock);
            if (saved.ScriptSequence is null && !saved.Moving && (_controller.Playing || clock.StartPending))
                throw new InvalidDataException("Saved settled door has an unfinished source animation.");
            return;
        }
        var sequence = _state.DoorOpen ? _open : _close;
        _controller.RequestSourceSequence(sequence, 1);
        _controller.SeekSourceTime(_controller.SequenceRange(sequence).StopTime);
        _controller.SetProcess(false);
        _state.DoorMotion = new(_controller.SourceController, _controller.SourceSha256, _state.DoorOpen, false);
    }
    internal void Activate()
    {
        if (_state.DoorMotion?.Moving == true) return;
        SetOpen(!_state.DoorOpen);
    }

    internal int OpenState()
    {
        Synchronize();
        return (_state.DoorMotion ?? throw new InvalidOperationException("Door source state is absent.")).OpenState;
    }

    internal void SetOpen(bool open)
    {
        Synchronize();
        var previous = _state.DoorMotion ?? throw new InvalidOperationException("Door source state is absent.");
        var requested = previous.Request(open);
        if (requested == previous) return;
        _controller.RequestSourceSequence(open ? _open : _close, 1);
        _state.DoorOpen = open;
        _state.DoorMotion = requested;
    }

    internal void RequireScriptSelection(RuntimeNifControllerPlayer controller)
    {
        if (controller != _controller) return;
        if (_state.DoorMotion?.Moving == true)
            throw new NotSupportedException("Replacing an unfinished door movement requires its interrupted motion owner.");
    }

    internal void ScriptSelected(RuntimeNifControllerPlayer controller)
    {
        if (controller != _controller) return;
        var state = _state.DoorMotion ?? throw new InvalidOperationException("Door source state is absent.");
        var clock = _controller.CaptureScriptState() ?? throw new InvalidDataException("Scripted door has no retained source animation.");
        _state.DoorMotion = state with { ScriptSequence = clock.Sequence };
    }
    public override void _Process(double delta)
    {
        Synchronize();
    }

    internal void Synchronize()
    {
        var state = _state.DoorMotion ?? throw new InvalidOperationException("Door source state is absent.");
        if (state.ScriptSequence is not null)
        {
            // A source PlayGroup changes presentation without inventing a
            // gameplay Open/Close transition. Its saved object clock also owns
            // any queued successor and its actual completion.
            ScriptSelected(_controller);
            return;
        }
        if (!_controller.ActiveSequence!.Equals(state.Open ? _open : _close, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Another animation command replaced the door's source motion.");
        if (!state.Moving || _controller.Playing) return;
        _state.DoorMotion = state with { Moving = false };
        _changed();
        GD.Print($"OPENNV_NATIVE_DOOR_MOTION reference={_state.Reference} open={_state.DoorOpen} sourceClock=complete");
    }
}
