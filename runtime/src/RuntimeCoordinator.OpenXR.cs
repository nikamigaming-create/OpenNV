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
        var layer = _nativePipBoyLayer; var view = _nativePipBoy;
        _nativePipBoyLayer = null; _nativePipBoy = null;
        if (IsInstanceValid(layer)) layer!.Free();
        if (IsInstanceValid(view)) view!.Free();
    }

    private void FocusNativeXrPipBoy(bool focused)
    {
        if (_nativeXr is null || _nativePipBoy is null) return;
        focused &= _nativePlayer is { ModalInput: false } && !_nativeDoorLoading && _nativeOpeningStageDriver!.PipBoy.Available;
        _nativeOpeningStageDriver!.PipBoy.SetOpen(focused);
        if (focused)
            _nativePipBoy.RefreshWorld(_nativeActiveCell!.Cell.Worldspace, NativePipBoyMapPosition(_nativePlayer!),
                MathF.Atan2(-_nativePlayer!.GlobalBasis.Z.Z, -_nativePlayer.GlobalBasis.Z.X));
        _nativePipBoy.SetXrFocus(focused);
        _nativeXr.PointAtPipBoy = focused ? _nativePipBoy.PointFromXr : null;
        if (!focused) SaveNativeInteraction();
    }
}
