using Godot;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class RuntimeLiveHarness
{
    private sealed record ViewportTiming(string Name, double CpuMilliseconds, double GpuMilliseconds);
    private sealed record RenderTiming(long Timestamp, double SetupMilliseconds, ViewportTiming[] Viewports, string? Error,
        ulong RenderThreadId, ulong MainThreadId);
    private sealed record RendererIdentity(string? RenderingMethod, string? GraphicsAdapter);
    private sealed record ViewportResolution(string Name, int WidthPixels, int HeightPixels, float Scaling3DScale);
    private sealed record WindowPresentation(int Id, int WidthPixels, int HeightPixels, int Screen,
        int ScreenWidthPixels, int ScreenHeightPixels, double? ReportedRefreshHz, string VsyncMode, string Mode);
    private sealed record HeadsetResolution(double WidthPixels, double HeightPixels);
    private sealed record HeadsetConfiguration(string Interface, bool Initialized, string? SessionState,
        Dictionary<string, string> SystemInfo, double? DisplayRefreshHz, double[] AvailableDisplayRefreshHz,
        HeadsetResolution? RenderTarget, HeadsetResolution? RecommendedTarget, int? ViewCount,
        double? RenderTargetSizeMultiplier, string? Error)
    {
        public string PhysicalHeadsetCompositorAndMissedFrames =>
            "unverified; runtime identity/settings do not establish physical headset presentation or simulator equivalence";
    }
    private sealed record PresentationConfiguration(long Timestamp, string DisplayServer, int MaximumFramesPerSecond,
        WindowPresentation? Window, ViewportResolution[] Viewports, string? Error, HeadsetConfiguration? Headset = null);
    private RenderTiming? _renderTiming;
    private int _renderTimingPending;
    private ulong _drawCallbackThreadId;
    private RendererIdentity? _rendererIdentity;
    private PresentationConfiguration? _presentationConfiguration;
    private readonly LiveHarnessPublicationSelection _publicationSelection = new();

    private static int? CurrentHybridCoreTypeCode()
    {
        if (!System.Runtime.Intrinsics.X86.X86Base.IsSupported ||
            System.Runtime.Intrinsics.X86.X86Base.CpuId(0, 0).Eax < 0x1a) return null;
        return (int)((uint)System.Runtime.Intrinsics.X86.X86Base.CpuId(0x1a, 0).Eax >> 24);
    }

    private PresentationConfiguration SamplePresentationConfiguration()
    {
        // Display queries belong to the main thread. Cache independently from
        // draw callbacks, but refresh so resizing/rebinding displays stays visible.
        if (_presentationConfiguration is { } previous &&
            System.Diagnostics.Stopwatch.GetElapsedTime(previous.Timestamp).TotalSeconds < 1) return previous;
        var timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var displayServer = DisplayServer.GetName();
        var maximumFramesPerSecond = Engine.MaxFps;
        var headset = SampleHeadsetConfiguration();
        try
        {
            var resolutions = _viewports.Select(viewport =>
            {
                var texture = viewport.GetTexture();
                return new ViewportResolution(viewport.Name.ToString(), texture.GetWidth(), texture.GetHeight(), viewport.Scaling3DScale);
            }).ToArray();
            WindowPresentation? window = null;
            var windowId = GetWindow().GetWindowId();
            if (displayServer != "headless" && windowId >= 0)
            {
                var size = DisplayServer.WindowGetSize(windowId);
                var screen = DisplayServer.WindowGetCurrentScreen(windowId);
                var screenSize = DisplayServer.ScreenGetSize(screen);
                var refresh = DisplayServer.ScreenGetRefreshRate(screen);
                window = new(windowId, size.X, size.Y, screen, screenSize.X, screenSize.Y,
                    double.IsFinite(refresh) && refresh > 0 ? refresh : null, DisplayServer.WindowGetVsyncMode(windowId).ToString(),
                    DisplayServer.WindowGetMode(windowId).ToString());
            }
            return _presentationConfiguration = new(timestamp, displayServer, maximumFramesPerSecond, window, resolutions, null, headset);
        }
        catch (Exception error)
        {
            return _presentationConfiguration = new(timestamp, displayServer, maximumFramesPerSecond, null, [], error.Message, headset);
        }
    }

    private static HeadsetConfiguration? SampleHeadsetConfiguration()
    {
        // Observe only the already-selected primary interface. Never initialize
        // XR, select a runtime, or request a refresh rate from diagnostics.
        if (XRServer.PrimaryInterface is not OpenXRInterface xr) return null;
        var sample = new HeadsetConfiguration(xr.GetName().ToString(), xr.IsInitialized(), null,
            new(StringComparer.Ordinal), null, [], null, null, null, null, null);
        if (!sample.Initialized) return sample;
        try
        {
            using var information = xr.GetSystemInfo();
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in information) strings.Add(entry.Key.AsString(), entry.Value.ToString());
            using var available = xr.GetAvailableDisplayRefreshRates();
            var rates = available.Select(rate => rate.AsDouble()).Where(rate => double.IsFinite(rate) && rate > 0).ToArray();
            var refresh = xr.DisplayRefreshRate;
            var target = xr.GetRenderTargetSize(); var recommended = xr.GetRecommendedTargetSize();
            var multiplier = xr.RenderTargetSizeMultiplier; var views = checked((int)xr.GetViewCount());
            return sample with
            {
                SessionState = xr.GetSessionState().ToString(), SystemInfo = strings,
                DisplayRefreshHz = double.IsFinite(refresh) && refresh > 0 ? refresh : null, AvailableDisplayRefreshHz = rates,
                RenderTarget = new(target.X, target.Y), RecommendedTarget = new(recommended.X, recommended.Y),
                ViewCount = views > 0 ? views : null,
                RenderTargetSizeMultiplier = double.IsFinite(multiplier) && multiplier > 0 ? multiplier : null
            };
        }
        catch (Exception error) { return sample with { Error = error.Message }; }
    }

    private RenderTiming? SampleRenderTiming()
    {
        if (Engine.GetFramesDrawn() == 0) return Volatile.Read(ref _renderTiming);
        // Synchronous render-server queries make a Separate renderer wait for
        // gameplay diagnostics. Queue one immutable measurement batch instead.
        if (Interlocked.CompareExchange(ref _renderTimingPending, 1, 0) == 0)
        {
            var viewports = _viewports.Select(viewport => (Name: viewport.Name.ToString(), Rid: viewport.GetViewportRid())).ToArray();
            RenderingServer.CallOnRenderThread(Callable.From(() =>
            {
                try
                {
                    // _EnterTree can precede renderer initialization. Retry an
                    // unavailable name after draws begin; never invent a GPU.
                    if (_rendererIdentity is not { RenderingMethod: not null, GraphicsAdapter: not null })
                    {
                        var method = RenderingServer.GetCurrentRenderingMethod();
                        var adapter = RenderingServer.GetVideoAdapterName();
                        Volatile.Write(ref _rendererIdentity, new(string.IsNullOrWhiteSpace(method) ? null : method,
                            string.IsNullOrWhiteSpace(adapter) ? null : adapter));
                    }
                    var timings = viewports.Select(viewport => new ViewportTiming(viewport.Name,
                        RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport.Rid),
                        RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.Rid))).ToArray();
                    Volatile.Write(ref _renderTiming, new(System.Diagnostics.Stopwatch.GetTimestamp(),
                        RenderingServer.GetFrameSetupTimeCpu(), timings, null, OS.GetThreadCallerId(), OS.GetMainThreadId()));
                }
                catch (Exception error)
                {
                    Volatile.Write(ref _renderTiming, new(System.Diagnostics.Stopwatch.GetTimestamp(), 0, [], error.Message,
                        OS.GetThreadCallerId(), OS.GetMainThreadId()));
                }
                finally { Volatile.Write(ref _renderTimingPending, 0); }
            }));
        }
        return Volatile.Read(ref _renderTiming);
    }
}
