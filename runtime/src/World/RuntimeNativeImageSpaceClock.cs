using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World;

internal sealed partial class RuntimeNativeImageSpaceClock(FalloutImageSpaceState state, FalloutGameTime time, Guid process) : Node
{
    private string? _error;
    internal object State => new
    {
        active = state.Active.Select(active => new
        { source = active.Source.Form.ToString(), active.ElapsedSeconds, active.Source.Duration }).ToArray(),
        error = _error,
        owner = "independent-gameplay-clock"
    };

    public override void _EnterTree()
    {
        Name = "NativeImageSpaceClock";
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = 90;
    }

    public override void _Process(double delta)
    {
        if (_error is not null) return;
        try
        {
            // Source index zero reads the calendar even while simulation is
            // paused. Draws, menu captures and save capture never write it.
            state.SampleDoubleVisionClock(time, process);
            if (GetTree().Paused) return;
            foreach (var expired in state.Advance(delta))
                GD.Print($"OPENNV_NATIVE_IMAD_EXPIRED source={expired.Form} duration={expired.Duration:R} owner=gameplay-clock");
        }
        catch (Exception error)
        {
            _error = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            GD.PushError("OPENNV_IMAGE_SPACE_CLOCK_FAILURE " + _error);
        }
    }
}
