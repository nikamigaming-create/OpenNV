using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoy
{
    private bool _flatPointerPressed;
    private bool _sourcePointerRetired;
    private string? _sourcePointerFailure;

    internal void RetireSourcePointerInput()
    {
        if (_sourcePointerRetired) return;
        _sourcePointerRetired = true;
        SetProcessInput(false);
        if (!GodotObject.IsInstanceValid(_canvas) || !_canvas.IsInsideTree())
        {
            // EXIT_TREE may follow the canvas's own exit. That native owner
            // has already lost its GUI capture; never deliver into a dead tree
            // or turn destruction into a source action receipt.
            _flatPointerPressed = _xrPressed = _xrButtonPressed = false;
            _lastPointer = null;
            return;
        }
        var failures = new List<Exception>();
        try { ReleaseXrPointer(); }
        catch (Exception error) { failures.Add(error); }
        try { CancelFlatPointer(); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0)
            throw new AggregateException("Native Pip-Boy pointer retirement failed.", failures);
    }

    private void CancelFlatPointer()
    {
        if (!_flatPointerPressed) return;
        _flatPointerPressed = false;
        try { CancelSourcePointer(); }
        finally { _lastPointer = null; }
    }

    private void CancelSourcePointer()
    {
        try { NativeMenuPointerInput.CancelLeft(_canvas); }
        catch (Exception error) { _sourcePointerFailure ??= error.ToString(); throw; }
    }

    private void DeliverSourcePointer(InputEvent input)
    {
        try { _canvas.PushInput(input, true); }
        catch (Exception error) { _sourcePointerFailure ??= error.ToString(); throw; }
    }
}
