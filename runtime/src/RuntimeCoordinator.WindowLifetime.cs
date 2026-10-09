using Godot;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private Window? _nativeQuitWindow;

    private void AttachNativeCloseRequest()
    {
        if (_nativeQuitWindow is not null) return;
        var window = GetWindow();
        window.CloseRequested += OnNativeCloseRequested;
        _nativeQuitWindow = window;
    }

    private void DetachNativeCloseRequest()
    {
        if (_nativeQuitWindow is { } window && GodotObject.IsInstanceValid(window))
            window.CloseRequested -= OnNativeCloseRequested;
        _nativeQuitWindow = null;
    }
}
