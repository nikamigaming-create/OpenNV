using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private void RebuildNativeXrPipBoy()
    {
        if (_nativeXr is null) return;
        var focused = _nativeOpeningStageDriver!.PipBoy.Open;
        // Dispose only the UI. The native player owns all arms and the single
        // equipped device, including their lifetime through equipment changes.
        DisposeNativeXrPipBoy();
        if (!_nativeOpeningStageDriver.PipBoy.Available || _nativePlayer?.XrPresentation is not { } actor)
        { _nativeOpeningStageDriver.PipBoy.SetOpen(false); return; }
        NativeOwnedPipBoyMenu? menu = null;
        try
        {
            menu = CreateNativePipBoyMenu(_nativePlayer);
            _nativePipBoy = new(menu, FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!), actor,
                actor.Actor.Appearance.Female, NativeAmbient(_nativeActiveCell!.Cell), _configuration.Player.DesktopInput.PipBoy.Action,
                () => FocusNativeXrPipBoy(false), worldSpace: true);
            _nativePipBoyLayer = new CanvasLayer { Name = "NativePipBoyLayer", Layer = 100, ProcessMode = ProcessModeEnum.Always };
            AddChild(_nativePipBoyLayer); _nativePipBoyLayer.AddChild(_nativePipBoy);
            _nativeXr.PoseWristDevice = _nativePipBoy.PoseXrDevice;
            _nativeXr.ScrollPipBoy = _nativePipBoy.ScrollFromXr;
            menu = null;
            FocusNativeXrPipBoy(focused);
        }
        catch (Exception error)
        {
            DisposeNativeXrPipBoy();
            if (IsInstanceValid(menu)) menu!.Free();
            _nativeOpeningStageDriver.PipBoy.SetOpen(false);
            GD.PushError($"OPENNV_NATIVE_XR_WRIST_FAIL {error}");
        }
    }

    private void DisposeNativeXrPipBoy()
    {
        _nativeXr!.PointAtPipBoy = null;
        _nativeXr.PoseWristDevice = null;
        _nativeXr.ScrollPipBoy = null;
        var layer = _nativePipBoyLayer; var view = _nativePipBoy;
        _nativePipBoyLayer = null; _nativePipBoy = null;
        try { if (IsInstanceValid(view)) view!.RetireSourcePointerInput(); }
        finally
        {
            try { if (IsInstanceValid(layer)) layer!.Free(); }
            finally { if (IsInstanceValid(view)) view!.Free(); }
        }
    }

    private bool CanFocusNativeXrPipBoy() => _nativePlayer is { ModalInput: false, FurnitureActive: false } player &&
        IsInstanceValid(player) && player.IsInsideTree() && player.SourceControls.PipBoy &&
        !_nativeDoorLoading && !_nativeSessionTransitioning && !_retiringNativeSession && !GetTree().Paused &&
        _nativeOpeningStageDriver?.PipBoy.Available == true && _nativePipBoy is { } view &&
        IsInstanceValid(view) && view.InputReady;

    private void FocusNativeXrPipBoy(bool focused)
    {
        if (_nativeXr is null || _nativePipBoy is null) return;
        var wasFocused = _nativeXr.PointAtPipBoy is not null;
        focused = focused && CanFocusNativeXrPipBoy();
        _nativeOpeningStageDriver!.PipBoy.SetOpen(focused);
        if (focused)
        {
            var map = NativePipBoyMapContext(_nativePlayer!);
            _nativePipBoy.RefreshWorld(map.World, map.Position,
                MathF.Atan2(-_nativePlayer!.GlobalBasis.Z.Z, -_nativePlayer.GlobalBasis.Z.X));
        }
        _nativePipBoy.SetXrFocus(focused);
        _nativeXr.PointAtPipBoy = focused ? _nativePipBoy.PointFromXr : null;
        if (wasFocused && !focused && !_nativeSessionTransitioning && !_retiringNativeSession) SaveNativeInteraction();
    }
}
