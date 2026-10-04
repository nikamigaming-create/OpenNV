using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private CanvasLayer? _nativeLoadingLayer;
    private NativeOwnedLoadingScreens? _nativeLoadingScreens;
    private LoadingProgressIndicator? _nativeLoadingProgress;
    private Task? _nativePlayerMoveRead;
    private Task? _nativeDoorRead;
    private bool _nativeLoadingWasPaused;

    private async Task ShowNativeLoadingScreens(FalloutFormKey destination)
    {
        if (_nativeLoadingLayer is not null) throw new InvalidOperationException("A loading presentation is already active.");
        _nativeLoadingWasPaused = GetTree().Paused;
        _nativeLoadingLayer = new CanvasLayer { Name = "NativeLoadingLayer", Layer = 101, ProcessMode = ProcessModeEnum.Always };
        AddChild(_nativeLoadingLayer);
        var feedback = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 1,
            AnchorRight = 1,
            AnchorTop = 1,
            AnchorBottom = 1,
            OffsetLeft = -440,
            OffsetRight = -28,
            OffsetTop = -112,
            OffsetBottom = -28,
        };
        feedback.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.008f, 0.012f, 0.011f, 0.9f),
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
        });
        _nativeLoadingProgress = new LoadingProgressIndicator("Loading area");
        _nativeLoadingLayer.AddChild(feedback);
        feedback.AddChild(_nativeLoadingProgress);
        _loadingStartedMilliseconds = _loadingPhaseStartedMilliseconds = Time.GetTicksMsec();
        SetLoadingStatus("Loading area");
        GetTree().Paused = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_nativeSessionTransitioning || _retiringNativeSession) throw new OperationCanceledException("Loading session retired.");
        var cell = FalloutCellSceneReader.ReadDefinition(_nativePluginStack!, destination);
        var screens = new NativeOwnedLoadingScreens(_nativePluginStack!, cell,
            _nativeQuestScripts!.Scripts.Session.LocationSpecificLoadScreensOnly);
        _nativeLoadingScreens = screens;
        _nativeLoadingLayer.AddChild(screens);
        _nativeLoadingLayer.MoveChild(screens, 0);
        screens.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        GD.Print($"OPENNV_NATIVE_LOADING_SCREENS destination={destination} locationSpecificOnly={_nativeQuestScripts.Scripts.Session.LocationSpecificLoadScreensOnly} " +
            "source=winning-LSCR-and-LSCT ancillary-ui=unbound parity=unverified");
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (_nativeSessionTransitioning || _retiringNativeSession) throw new OperationCanceledException("Loading session retired.");
    }

    private void CloseNativeLoadingScreens()
    {
        if (_nativeLoadingLayer is null) return;
        _nativeLoadingLayer.QueueFree();
        _nativeLoadingLayer = null;
        _nativeLoadingScreens = null;
        _nativeLoadingProgress = null;
        if (_loadingPhase is not null)
        {
            CompleteLoadingPhase();
            GD.Print($"OPENNV_LOAD_COMPLETE elapsedMs={Time.GetTicksMsec() - _loadingStartedMilliseconds}");
            _loadingPhase = null;
        }
        GetTree().Paused = _nativeLoadingWasPaused || _nativeSessionTransitioning || _retiringNativeSession;
    }
}
