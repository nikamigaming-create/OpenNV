using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World;

internal sealed partial class RuntimeNativeImageSpaceClock(FalloutImageSpaceState state) : Node
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
        if (_error is not null || GetTree().Paused) return;
        try
        {
            foreach (var expired in state.Advance(delta))
                GD.Print($"OPENNV_NATIVE_IMAD_EXPIRED source={expired.Form} duration={expired.Duration:R} owner=gameplay-clock");
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            _error = error.Message;
            GD.PushError("OPENNV_IMAGE_SPACE_CLOCK_FAILURE " + error.Message);
        }
    }
}
