using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

// Translate physical desktop input to the published DirectInput IDs used by
// source scripts. This observes input before gameplay controls consume it.
internal static class NativeScriptKeys
{
    internal static InputEvent Create(int code)
    {
        if (code >= FalloutInputLayout.FirstMouseButton)
        {
            // Validate before native allocation. An object initializer allocates
            // its event before evaluating a fallible property value.
            var button = code switch
            {
                FalloutInputLayout.MouseLeft => MouseButton.Left,
                FalloutInputLayout.MouseRight => MouseButton.Right,
                FalloutInputLayout.MouseMiddle => MouseButton.Middle,
                FalloutInputLayout.MouseExtra1 => MouseButton.Xbutton1,
                FalloutInputLayout.MouseExtra2 => MouseButton.Xbutton2,
                FalloutInputLayout.WheelUp => MouseButton.WheelUp,
                FalloutInputLayout.WheelDown => MouseButton.WheelDown,
                _ => throw new NotSupportedException($"DirectInput mouse binding {code} has no native button."),
            };
            return new InputEventMouseButton { ButtonIndex = button };
        }
        foreach (var key in Enum.GetValues<Key>().Distinct())
            foreach (var location in key is Key.Shift or Key.Ctrl or Key.Alt
                ? new[] { KeyLocation.Left, KeyLocation.Right } : [KeyLocation.Unspecified])
                if (Code(key, location) == code) return new InputEventKey { PhysicalKeycode = key, Location = location };
        throw new NotSupportedException($"DirectInput keyboard binding {code} has no native physical key.");
    }

    // Factory events are caller-owned. InputMap retains separate native refs;
    // releasing these wrappers does not erase the published action bindings.
    internal static void DisposeCreatedEvents(IEnumerable<InputEvent> events, Exception? operationFailure)
    {
        List<Exception>? failures = null;
        foreach (var input in events)
            try { input.Dispose(); }
            catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is null) return;
        if (operationFailure is not null) failures.Insert(0, operationFailure);
        throw new AggregateException("Created input-event retirement failed.", failures);
    }

    internal static IEnumerable<(int Key, bool Down)> Read(InputEvent input)
    {
        if (input is InputEventKey { Echo: false } keyboard && Code(keyboard.PhysicalKeycode, keyboard.Location) is { } key)
            yield return (key, keyboard.Pressed);
        if (input is not InputEventMouseButton mouse) yield break;
        var button = mouse.ButtonIndex switch
        {
            MouseButton.Left => FalloutInputLayout.MouseLeft,
            MouseButton.Right => FalloutInputLayout.MouseRight,
            MouseButton.Middle => FalloutInputLayout.MouseMiddle,
            MouseButton.Xbutton1 => FalloutInputLayout.MouseExtra1,
            MouseButton.Xbutton2 => FalloutInputLayout.MouseExtra2,
            MouseButton.WheelUp => FalloutInputLayout.WheelUp,
            MouseButton.WheelDown => FalloutInputLayout.WheelDown,
            _ => 0,
        };
        if (button == 0) yield break;
        yield return (button, mouse.Pressed);
        if (button >= FalloutInputLayout.WheelUp && mouse.Pressed) yield return (button, false);
    }

    internal static int? Code(Key key, KeyLocation location = KeyLocation.Unspecified) => key switch
    {
        Key.Escape => 1,
        Key.Key1 => 2,
        Key.Key2 => 3,
        Key.Key3 => 4,
        Key.Key4 => 5,
        Key.Key5 => 6,
        Key.Key6 => 7,
        Key.Key7 => 8,
        Key.Key8 => 9,
        Key.Key9 => 10,
        Key.Key0 => 11,
        Key.Minus => 12,
        Key.Equal => 13,
        Key.Backspace => 14,
        Key.Tab => 15,
        Key.Q => 16,
        Key.W => 17,
        Key.E => 18,
        Key.R => 19,
        Key.T => 20,
        Key.Y => 21,
        Key.U => 22,
        Key.I => 23,
        Key.O => 24,
        Key.P => 25,
        Key.Bracketleft => 26,
        Key.Bracketright => 27,
        Key.Enter => 28,
        Key.Ctrl => location == KeyLocation.Right ? 157 : 29,
        Key.A => 30,
        Key.S => 31,
        Key.D => 32,
        Key.F => 33,
        Key.G => 34,
        Key.H => 35,
        Key.J => 36,
        Key.K => 37,
        Key.L => 38,
        Key.Semicolon => 39,
        Key.Apostrophe => 40,
        Key.Quoteleft => 41,
        Key.Shift => location == KeyLocation.Right ? 54 : 42,
        Key.Backslash => 43,
        Key.Z => 44,
        Key.X => 45,
        Key.C => 46,
        Key.V => 47,
        Key.B => 48,
        Key.N => 49,
        Key.M => 50,
        Key.Comma => 51,
        Key.Period => 52,
        Key.Slash => 53,
        Key.KpMultiply => 55,
        Key.Alt => location == KeyLocation.Right ? 184 : 56,
        Key.Space => 57,
        Key.Capslock => 58,
        Key.F1 => 59,
        Key.F2 => 60,
        Key.F3 => 61,
        Key.F4 => 62,
        Key.F5 => 63,
        Key.F6 => 64,
        Key.F7 => 65,
        Key.F8 => 66,
        Key.F9 => 67,
        Key.F10 => 68,
        Key.Numlock => 69,
        Key.Scrolllock => 70,
        Key.Kp7 => 71,
        Key.Kp8 => 72,
        Key.Kp9 => 73,
        Key.KpSubtract => 74,
        Key.Kp4 => 75,
        Key.Kp5 => 76,
        Key.Kp6 => 77,
        Key.KpAdd => 78,
        Key.Kp1 => 79,
        Key.Kp2 => 80,
        Key.Kp3 => 81,
        Key.Kp0 => 82,
        Key.KpPeriod => 83,
        Key.F11 => 87,
        Key.F12 => 88,
        Key.KpEnter => 156,
        Key.KpDivide => 181,
        Key.Home => 199,
        Key.Up => 200,
        Key.Pageup => 201,
        Key.Left => 203,
        Key.Right => 205,
        Key.End => 207,
        Key.Down => 208,
        Key.Pagedown => 209,
        Key.Insert => 210,
        Key.Delete => 211,
        _ => null,
    };
}
