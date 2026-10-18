using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoy
{
    private bool _scrollArmed;
    private int _scrollDirection;
    private double _scrollDelay;
    private long _scrollEvents;

    private void ResetScroll()
    {
        _scrollArmed = false; _scrollDirection = 0; _scrollDelay = 0;
    }

    internal void ScrollFromXr(float axis, double delta)
    {
        if (!InputReady || !_xrFocused || _closing || _xrPressed ||
            _lastPointer is not { X: >= 0, Y: >= 0 } point || !float.IsFinite(axis))
        { ResetScroll(); return; }
        if (MathF.Abs(axis) < .25f) { _scrollArmed = true; _scrollDirection = 0; _scrollDelay = 0; return; }
        if (!_scrollArmed) return;
        var direction = MathF.Abs(axis) >= .6f ? Math.Sign(axis) : 0;
        if (direction != _scrollDirection) { _scrollDirection = direction; _scrollDelay = 0; }
        if (direction == 0 || (_scrollDelay -= delta) > 0) return;
        var wheel = direction > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
        using var press = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = wheel, Pressed = true };
        using var release = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = wheel };
        DeliverSourcePointer(press); DeliverSourcePointer(release);
        _scrollDelay = .18; ++_scrollEvents;
    }
}
