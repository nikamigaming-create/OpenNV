using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

// Polls before script/physics consumers on the actual product thread. A failed
// sample remains unavailable; no synthetic Godot event certifies device bytes.
internal sealed partial class RuntimeNativeDirectInput : Node
{
    private readonly WindowsDirectInputDevice _device;
    internal FalloutDirectInputState State { get; }
    private bool _retired;
    private RuntimeNativeDirectInput(FalloutDirectInputState state, WindowsDirectInputDevice device)
    { State = state; _device = device; }
    internal static RuntimeNativeDirectInput Create(string source, FalloutInputControls controls)
    {
        if (DisplayServer.GetName() == "headless") throw new NotSupportedException("Headless has no complete native DirectInput product-window owner.");
        var window = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        var device = new WindowsDirectInputDevice(window);
        try
        {
            var state = new FalloutDirectInputState(source, checked((ulong)window), controls);
            return new(state, device);
        }
        catch { device.Dispose(); throw; }
    }
    public override void _EnterTree() { ProcessMode = ProcessModeEnum.Always; ProcessPriority = int.MinValue; }
    public override void _Process(double delta)
    {
        (byte[] Keyboard, byte[] MouseButtons, int X, int Y, int Wheel) sample;
        try { sample = _device.Read(); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or DirectInputUnavailableException)
        { State.DeviceUnavailable(error.Message); return; }
        // A reached script/event failure is not a failed device acquisition.
        // Keep the committed sample and let the real callback error propagate.
        State.Publish(sample.Keyboard, sample.MouseButtons, sample.X, sample.Y, sample.Wheel);
    }
    internal async Task WaitForFirstSample(Func<bool> cancelled)
    {
        if (_retired || !IsInsideTree()) throw new InvalidOperationException("Initial device admission requires its attached product owner.");
        string? lastFailure = null;
        while (State.Sample == 0)
        {
            if (cancelled() || _retired || !IsInsideTree())
                throw new OperationCanceledException("Product input admission ended before an actual device sample.");
            _Process(0);
            if (State.Failure is { } failure && failure != lastFailure)
            {
                lastFailure = failure;
                GD.Print("OPENNV_NATIVE_INPUT_ADMISSION phase=waiting-for-device detail=" + failure);
            }
            if (State.Sample == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        State.RequireCurrent();
    }
    internal void Poll()
    {
        if (_retired) throw new ObjectDisposedException(nameof(RuntimeNativeDirectInput));
        var snapshot = _device.Read(); State.Publish(snapshot.Keyboard, snapshot.MouseButtons, snapshot.X, snapshot.Y, snapshot.Wheel);
    }
    public override void _ExitTree()
    {
        if (_retired) return;
        if (State.NativeReaders == 0) Retire();
        else State.NativeReadersRetired += Retire;
        // The node may leave its tree before coordinator retirement. The
        // actual domain lease keeps the COM devices/state alive until native
        // image retirement or verified exact child closure.
    }
    internal void Retire()
    {
        if (_retired) return;
        if (State.NativeReaders != 0) throw new InvalidOperationException("Native child readers still retain the actual product input owner.");
        _device.Dispose(); State.Retire(); _retired = true;
        State.NativeReadersRetired -= Retire;
    }
}
