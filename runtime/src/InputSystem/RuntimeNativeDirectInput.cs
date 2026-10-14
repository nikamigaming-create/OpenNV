using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.InputSystem;

// Polls before script/physics consumers on the actual product thread. A failed
// sample remains unavailable. An explicitly selected command provider adapts
// Godot command input; its samples never certify Windows device acquisition.
internal sealed partial class RuntimeNativeDirectInput : Node
{
    private readonly WindowsDirectInputDevice? _device;
    private readonly FalloutHeadlessInput? _headless;
    internal FalloutDirectInputState State { get; }
    private bool _retired, _retiring, _deviceRetired, _stateRetired, _platformRetired;
    private RuntimeNativeDirectInput(FalloutDirectInputState state, WindowsDirectInputDevice device)
    { State = state; _device = device; }
    private RuntimeNativeDirectInput(FalloutDirectInputState state)
    { State = state; _headless = new(state); }
    internal static RuntimeNativeDirectInput Create(string source, FalloutInputControls controls, bool commandInput = false)
    {
        var provider = RuntimeInputPresentationSelection.SelectInputProvider(commandInput, DisplayServer.GetName());
        if (provider == RuntimeNativeInputProvider.CommandInput)
            return new(FalloutDirectInputState.CreateHeadless(source, controls));
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
        if (_retired || _retiring) return;
        if (_headless is { } commands) { commands.Publish(); return; }
        if (State.HasSourceControlQueries) { State.PollSourceDevices(_device!); return; }
        (byte[] Keyboard, byte[] MouseButtons, int X, int Y, int Wheel) sample;
        try { sample = _device!.Read(); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or DirectInputUnavailableException)
        { State.DeviceUnavailable(error.Message); return; }
        // A reached script/event failure is not a failed device acquisition.
        // Keep the committed sample and let the real callback error propagate.
        State.Publish(sample.Keyboard, sample.MouseButtons, sample.X, sample.Y, sample.Wheel);
    }
    public override void _Input(InputEvent input)
    {
        if (_headless is not { } commands || _retired || _retiring) return;
        if (input is InputEventMouseMotion motion)
        {
            if (!motion.Relative.IsFinite()) throw new InvalidDataException("Command relative mouse input is nonfinite.");
            commands.Mouse(checked((int)Math.Round(motion.Relative.X)), checked((int)Math.Round(motion.Relative.Y)));
        }
        foreach (var (key, down) in NativeScriptKeys.Read(input))
            if (key < FalloutInputLayout.WheelUp) commands.Key(key, down);
            else if (down) commands.Mouse(0, 0,
                key == FalloutInputLayout.WheelUp ? FalloutInputLayout.WheelStep : -FalloutInputLayout.WheelStep);
        // Source-bound command events collect bytes only. The normal manager
        // poll owns previous/current transitions and successful hook updates.
        // Immediate UI filtering borrows a distinct read-only permission.
        if (!State.HasSourceControlQueries && input is (InputEventKey { Echo: false } or InputEventMouseButton)) commands.Publish();
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
        if (_retired || _retiring) throw new ObjectDisposedException(nameof(RuntimeNativeDirectInput));
        if (_headless is { } commands) { commands.Publish(); return; }
        if (State.HasSourceControlQueries) { State.PollSourceDevices(_device!); return; }
        var snapshot = _device!.Read(); State.Publish(snapshot.Keyboard, snapshot.MouseButtons, snapshot.X, snapshot.Y, snapshot.Wheel);
    }
    internal FalloutDirectInputState PlayerManager()
    {
        if (_retired || _retiring || !IsInsideTree() || GetParent() is null)
            throw new InvalidOperationException("Player input-manager getter requires the attached living product device owner.");
        return State.ReadManagerOwner();
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
        _retiring = true;
        var failures = new List<Exception>();
        Release(ref _deviceRetired, () => _device?.Dispose());
        Release(ref _stateRetired, State.Retire);
        Release(ref _platformRetired, () => _sourcePlatform?.Dispose());
        if (failures.Count != 0) throw new AggregateException("Actual product input retirement rejected one or more independent resources.", failures);
        _retired = true;
        State.NativeReadersRetired -= Retire;
        void Release(ref bool retired, Action release)
        {
            if (retired) return;
            try { release(); retired = true; }
            catch (Exception error) { failures.Add(error); }
        }
    }
}
