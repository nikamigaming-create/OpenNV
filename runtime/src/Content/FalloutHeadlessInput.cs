namespace OpenNV.Runtime.Content;

// Explicit virtual keyboard/mouse input for the same control, script and native
// Data consumers as desktop play. This owner never claims a Windows device/HWND.
internal sealed class FalloutHeadlessInput
{
    private readonly byte[] _keyboard = new byte[FalloutInputLayout.KeyboardKeyCount];
    private readonly byte[] _mouse = new byte[FalloutInputLayout.MouseButtonCount];
    private int _x, _y, _wheel;
    internal FalloutDirectInputState State { get; }

    internal FalloutHeadlessInput(FalloutDirectInputState state)
    {
        if (state.Provider != FalloutInputProvider.HeadlessCommands || state.Window != 0)
            throw new InvalidDataException("Headless input requires its declared windowless command provider.");
        State = state;
        State.ReadManagerOwner();
    }

    internal void Key(int code, bool down)
    {
        State.ReadManagerOwner();
        if ((uint)code >= FalloutInputLayout.WheelUp)
            throw new NotSupportedException("Headless keyboard/mouse input code is unbound: " + code);

        var value = down ? FalloutInputLayout.ButtonDown : (byte)0;
        if (code < FalloutInputLayout.FirstMouseButton)
            _keyboard[code] = value;
        else
            _mouse[code - FalloutInputLayout.FirstMouseButton] = value;
    }

    internal void Mouse(int x, int y, int wheel = 0)
    {
        State.ReadManagerOwner();
        var nextX = checked(_x + x);
        var nextY = checked(_y + y);
        var nextWheel = checked(_wheel + wheel);
        _x = nextX;
        _y = nextY;
        _wheel = nextWheel;
    }

    internal void Publish()
    {
        State.ReadManagerOwner();
        var x = _x;
        var y = _y;
        var wheel = _wheel;
        // A script callback may fail after the sample commits. Consumed relative
        // motion/wheel must not replay when that failure is observed or retried.
        _x = _y = _wheel = 0;
        State.Publish(_keyboard, _mouse, x, y, wheel);
    }
}
