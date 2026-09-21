using Godot;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private sealed record ViewportTiming(string Name, double CpuMilliseconds, double GpuMilliseconds);
    private sealed record RenderTiming(long Timestamp, double SetupMilliseconds, ViewportTiming[] Viewports, string? Error);
    private RenderTiming? _renderTiming;
    private int _renderTimingPending;
    private string _renderingMethod = "";
    private string _graphicsAdapter = "";

    private RenderTiming? SampleRenderTiming()
    {
        // Synchronous render-server queries make a Separate renderer wait for
        // gameplay diagnostics. Queue one immutable measurement batch instead.
        if (Interlocked.CompareExchange(ref _renderTimingPending, 1, 0) == 0)
        {
            var viewports = _viewports.Select(viewport => (Name: viewport.Name.ToString(), Rid: viewport.GetViewportRid())).ToArray();
            RenderingServer.CallOnRenderThread(Callable.From(() =>
            {
                try
                {
                    var timings = viewports.Select(viewport => new ViewportTiming(viewport.Name,
                        RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.Rid),
                        RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.Rid))).ToArray();
                    Volatile.Write(ref _renderTiming, new(System.Diagnostics.Stopwatch.GetTimestamp(),
                        RenderingServer.GetFrameSetupTimeCpu(), timings, null));
                }
                catch (Exception error)
                {
                    Volatile.Write(ref _renderTiming, new(System.Diagnostics.Stopwatch.GetTimestamp(), 0, [], error.Message));
                }
                finally { Volatile.Write(ref _renderTimingPending, 0); }
            }));
        }
        return Volatile.Read(ref _renderTiming);
    }
}
