using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    private void SaveMapView()
    {
        if (_map is not null && _world is { } world) _mapView = (world, _map.CaptureView());
    }

    private void RunAction(Action action)
    {
        try
        {
            action();
            ActionError = null; RemoveMeta("opennv_pipboy_action_error");
        }
        catch (Exception error)
        {
            // Failed gameplay remains visible, but does not invalidate the
            // rendered device or prevent selecting another page and closing.
            ActionError = error.Message;
            SetMeta("opennv_pipboy_action_error", ActionError);
            GD.PushError($"OPENNV_PIPBOY_ACTION_FAILED {error}");
            ShowNotice(ActionError);
        }
        QueueRedraw();
    }
}
