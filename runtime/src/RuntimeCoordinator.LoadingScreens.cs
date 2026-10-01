using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private CanvasLayer? _nativeLoadingLayer;
    private NativeOwnedLoadingScreens? _nativeLoadingScreens;
    private Task? _nativePlayerMoveRead;
    private Task? _nativeDoorRead;
    private bool _nativeLoadingWasPaused;

    private async Task ShowNativeLoadingScreens(FalloutFormKey destination)
    {
        if (_nativeLoadingLayer is not null) throw new InvalidOperationException("A loading presentation is already active.");
        var cell = FalloutCellSceneReader.ReadDefinition(_nativePluginStack!, destination);
        var screens = new NativeOwnedLoadingScreens(_nativePluginStack!, cell,
            _nativeQuestScripts!.Scripts.Session.LocationSpecificLoadScreensOnly);
        _nativeLoadingWasPaused = GetTree().Paused;
        _nativeLoadingLayer = new CanvasLayer { Name = "NativeLoadingLayer", Layer = 101, ProcessMode = ProcessModeEnum.Always };
        _nativeLoadingScreens = screens;
        AddChild(_nativeLoadingLayer);
        _nativeLoadingLayer.AddChild(screens);
        screens.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        GetTree().Paused = true;
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
        GetTree().Paused = _nativeLoadingWasPaused || _nativeSessionTransitioning || _retiringNativeSession;
    }
}
