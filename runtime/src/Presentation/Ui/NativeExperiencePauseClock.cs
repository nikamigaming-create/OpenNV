using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// Follow the actual gameplay tree's pause transaction. An Always-mode HUD (or
// an Always-mode invocation drain) does not resume the original engine timer.
internal sealed partial class NativeExperiencePauseClock : Node
{
    private readonly FalloutExperienceUiClock _clock;
    private readonly Func<long> _wall;
    private readonly Action<Exception> _failed;
    private Guid _lease;
    private string? _error;
    private NativeExperiencePauseClock(FalloutExperienceUiClock clock, Func<long> wall, Action<Exception> failed)
    {
        _clock = clock; _wall = wall; _failed = failed; Name = "ExperienceSourcePauseClock";
        ProcessMode = ProcessModeEnum.Pausable;
    }
    internal static NativeExperiencePauseClock Attach(Node gameplay, FalloutExperienceUiClock clock,
        Func<long> wall, Action<Exception> failed)
    {
        if (!gameplay.IsInsideTree()) throw new InvalidOperationException("XP pause clock has no living gameplay parent.");
        var result = new NativeExperiencePauseClock(clock, wall, failed);
        try
        {
            gameplay.AddChild(result);
            if (result._error is { } error) throw new InvalidOperationException(error);
            return result;
        }
        catch { if (GodotObject.IsInstanceValid(result)) result.Free(); throw; }
    }
    public override void _EnterTree()
    {
        try { _lease = _clock.Attach(_wall(), !CanProcess()); }
        catch (Exception error) { Fail(error); }
    }
    public override void _Notification(int what)
    {
        if (_lease == Guid.Empty || what != NotificationPaused && what != NotificationUnpaused) return;
        try { _clock.Pause(_lease, _wall(), !CanProcess()); }
        catch (Exception error) { Fail(error); }
    }
    public override void _ExitTree()
    {
        if (_lease == Guid.Empty) return;
        try { _clock.Detach(_lease); _lease = Guid.Empty; }
        catch (Exception error) { Fail(error); }
    }
    private void Fail(Exception error) { _error ??= error.Message; _failed(error); }
}
