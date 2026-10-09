using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private CanvasLayer? _nativeTerminalLayer;
    private NativeOwnedComputersMenu? _nativeTerminalMenu;
    private FalloutFormKey? _nativeTerminalReference;

    private void OpenNativeTerminal(FalloutPlacedReference reference)
    {
        if (_nativeTerminalLayer is not null || _nativePlayer is not { ModalInput: false } player || _nativeDoorLoading) return;
        var driver = _nativeOpeningStageDriver ?? throw new InvalidOperationException("Terminal gameplay owner is absent.");
        var session = driver.CreateTerminalMenu(reference.FormKey);
        var wasPaused = GetTree().Paused;
        void Close()
        {
            session.Close();
            _nativeTerminalLayer?.QueueFree(); _nativeTerminalLayer = null;
            _nativeTerminalMenu = null; _nativeTerminalReference = null;
            GetTree().Paused = wasPaused; player.SetModalInput(false);
            Input.MouseMode = Input.MouseModeEnum.Captured;
            if (session.Error is null) RequestNativeInteractionSave(reference.FormKey);
        }
        void Fail(Exception error)
        {
            session.ReportPresentationFailure(error);
            _nativeReferenceDivergences[reference.FormKey.ToString()] = "Terminal presentation/result: " + error.Message;
            GD.PushError($"OPENNV_NATIVE_TERMINAL_FAIL reference={reference.FormKey} {error.Message}");
        }
        try
        {
            var menu = new NativeOwnedComputersMenu(_nativePluginStack!, session, Close, Fail);
            _nativeTerminalMenu = menu;
            _nativeTerminalReference = reference.FormKey;
            _nativeTerminalLayer = new CanvasLayer
            { Name = "NativeTerminalLayer", Layer = 100, ProcessMode = ProcessModeEnum.Always };
            AddChild(_nativeTerminalLayer); _nativeTerminalLayer.AddChild(menu);
            if (menu.Error is { } error) throw new NotSupportedException(error);
            player.SetModalInput(true); Input.MouseMode = Input.MouseModeEnum.Visible;
            GetTree().Paused = _nativeXr is null;
            GD.Print($"OPENNV_NATIVE_TERMINAL_OPEN reference={reference.FormKey} page={session.CurrentPage.Record.FormKey} source=winning-TERM-computers-menu");
        }
        catch (Exception error) { Fail(error); Close(); throw; }
    }
}
