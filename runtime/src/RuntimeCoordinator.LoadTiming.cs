using System.Diagnostics;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private async Task YieldNativeLoadingFrame()
    {
        if (Godot.DisplayServer.GetName() != "headless")
            await ToSignal(Godot.RenderingServer.Singleton, Godot.RenderingServer.SignalName.FramePostDraw);
        if (_nativeSessionTransitioning || _retiringNativeSession)
            throw new OperationCanceledException("Loading session retired between publication steps.");
    }

    private void MeasureNativeLoadStep(string phase, Action action)
    {
        var started = Stopwatch.GetTimestamp();
        try { action(); }
        finally
        {
            Godot.GD.Print($"OPENNV_NATIVE_LOAD_STEP phase={phase} elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3}");
        }
    }
}
