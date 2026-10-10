using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

// The input adapter delivers ordinary viewport events. Cancellation is a real
// outside motion before release: BaseButton caches pressing-inside on motion,
// and a release position alone does not cancel its captured action.
internal static class NativeMenuPointerInput
{
    internal static void Motion(SubViewport canvas, Vector2 point, Vector2 relative, bool leftDown)
    {
        Require(canvas);
        using var motion = new InputEventMouseMotion
        {
            Position = point,
            GlobalPosition = point,
            Relative = relative,
            ButtonMask = leftDown ? MouseButtonMask.Left : 0
        };
        canvas.PushInput(motion, true);
    }

    internal static void LeftButton(SubViewport canvas, Vector2 point, bool pressed)
    {
        Require(canvas);
        using var click = new InputEventMouseButton
        {
            Position = point,
            GlobalPosition = point,
            ButtonIndex = MouseButton.Left,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
            Pressed = pressed
        };
        canvas.PushInput(click, true);
    }

    internal static void CancelLeft(SubViewport canvas)
    {
        Require(canvas);
        var outside = new Vector2(-canvas.Size.X - 1, -canvas.Size.Y - 1);
        // This is not a drag displacement. A map capture must not pan by the
        // distance to the cancellation sentinel.
        Motion(canvas, outside, Vector2.Zero, true);
        LeftButton(canvas, outside, false);
    }

    private static void Require(SubViewport canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!GodotObject.IsInstanceValid(canvas) || !canvas.IsInsideTree())
            throw new InvalidOperationException("Menu pointer input has no living native canvas.");
    }
}
