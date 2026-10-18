using Godot;

namespace OpenNV.Runtime.Presentation.OpenXR;

internal sealed partial class NativeXrRig
{
    private bool _pipBoyRequestedFocus;
    private bool _pipBoyToggleRequested;
    private string? _pipBoyInputFailure;

    private void TogglePipBoyFocus()
    {
        _pipBoyToggleRequested = true;
    }

    private void SamplePipBoyInput(bool tracked)
    {
        if (_pipBoyInputFailure is not null) return;
        try
        {
            var allowed = SetPipBoyHeld is not null && CanFocusPipBoy?.Invoke() == true;
            // Focus is an explicit mode entered by a button tap. Squeeze only
            // belongs to physical hand interaction, never to menu activation.
            var focused = tracked && allowed && (_pipBoyToggleRequested ? PointAtPipBoy is null : PointAtPipBoy is not null);
            _pipBoyToggleRequested = false;
            if (_pipBoyRequestedFocus == focused && (focused || PointAtPipBoy is null)) return;
            _pipBoyRequestedFocus = focused;
            SetPipBoyHeld?.Invoke(focused);
        }
        catch (Exception error)
        {
            _pipBoyInputFailure = error.ToString();
            GD.PushError("OPENNV_XR_PIPBOY_INPUT_FAILED " + error);
            try { SetPipBoyHeld?.Invoke(false); }
            catch (Exception closeError) { GD.PushError("OPENNV_XR_PIPBOY_CLOSE_FAILED " + closeError); }
            finally { PointAtPipBoy = null; _pipBoyRequestedFocus = false; }
        }
    }
}
