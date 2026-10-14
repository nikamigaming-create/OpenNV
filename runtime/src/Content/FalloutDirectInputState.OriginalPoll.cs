using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutDirectInputState
{
    private long _pollSequence;
    private FalloutOriginalInputPoll? _originalPoll;
    private FalloutOriginalInputUnownedWriter? _originalWriter;

    // Device callbacks write directly into this owner's initialized buffers.
    // The original poll ignores Poll/GetDeviceState HRESULTs, but the admitted
    // managed hook enters only for GetDeviceState == DI_OK.
    internal FalloutOriginalInputPoll PollSourceDevices(IFalloutOriginalInputDevices devices)
    {
        RequireOriginal(); ArgumentNullException.ThrowIfNull(devices);
        var sequence = checked(++_pollSequence);
        var edges = new List<(int Key, bool Down)>();
        _keyboardPoll = _mousePoll = null;
        _originalPoll = new(sequence, _originalSource!.Identity, Source, Factory, _originalHooks!.Lane, null, null, null, false, null);
        _originalBusy = true; _originalFault = false;
        try
        {
            _originalPlatform!.RequireCurrent(); CheckOriginalFault(); devices.RequireCurrent(this); CheckOriginalFault();
            _controller.CopyTo(_previousController, 0); ++_bufferRevision;
            _originalSelection = new(sequence, null, null, null, null);
            try
            {
                var status = _originalPlatform.ReadControllerState(_controller); ++_bufferRevision; CheckOriginalFault();
                _originalSelection = _originalSelection with { Status = status };
                if (status == 0 && !_suppressionKnown)
                    throw new NotSupportedException("source-original-input-controller-suppression-current-writer-unowned");
                _originalSelection = _originalSelection with
                {
                    Suppression = status == 0 ? _controllerSuppression : null,
                    Selected = status == 0 && _controllerSuppression == 0
                };
            }
            catch (Exception error) { _originalSelection = _originalSelection with { Failure = error.Message }; throw; }
            Read(FalloutOriginalInputDevice.Keyboard); Read(FalloutOriginalInputDevice.Mouse);
            devices.RequireCurrent(this); _originalPlatform.RequireCurrent(); CheckOriginalFault();
            if (_keyboardPoll is { ReadStatus: 0, Failure: null } && _mousePoll is { ReadStatus: 0, Failure: null })
            { Sample = checked(Sample + 1); _acquired = true; Failure = null; }
            else
            {
                _acquired = false;
                Failure = "Original input hook sample incomplete: keyboard=" + _keyboardPoll?.AcquireStatus + "/" + _keyboardPoll?.ReadStatus +
                    ";mouse=" + _mousePoll?.AcquireStatus + "/" + _mousePoll?.ReadStatus;
            }
            _originalPoll = _originalPoll with { Selection = _originalSelection, Keyboard = _keyboardPoll, Mouse = _mousePoll, Returned = true };
        }
        catch (Exception error)
        {
            _originalPoll = _originalPoll with { Selection = _originalSelection, Keyboard = _keyboardPoll, Mouse = _mousePoll, Failure = error.Message };
            _acquired = false; Failure = error.Message; throw;
        }
        finally { _originalBusy = false; }
        // Publication precedes callbacks; failure never replays taps or shifts
        // previous buffers back to a fictional atomic keyboard/mouse sample.
        GameEdges?.Invoke(edges);
        return _originalPoll;

        void Read(FalloutOriginalInputDevice device)
        {
            if (!devices.Present(device)) { CheckOriginalFault(); return; }
            CheckOriginalFault();
            var current = device == FalloutOriginalInputDevice.Keyboard ? _engineKeyboard : _engineMouse;
            var previous = device == FalloutOriginalInputDevice.Keyboard ? _previousKeyboard : _previousMouse;
            var receipt = new FalloutOriginalInputDevicePoll(device, sequence, FalloutOriginalInputPollStep.Entered, null, null, null, null, false, null);
            try
            {
                receipt = receipt with { Step = FalloutOriginalInputPollStep.PreviousStore };
                current.CopyTo(previous, 0);
                if (device == FalloutOriginalInputDevice.Keyboard) _previousKeyboardKnown = _keyboardKnown;
                else { _previousMouseKnown = _mouseKnown; _modeFourMouse.AsSpan().Clear(); _modeFourKnown = true; }
                ++_bufferRevision;
                receipt = receipt with { Step = FalloutOriginalInputPollStep.Acquire };
                var acquired = devices.Acquire(device); receipt = receipt with { AcquireStatus = acquired }; CheckOriginalFault();
                if (acquired is not (0 or 1))
                {
                    receipt = receipt with { Step = FalloutOriginalInputPollStep.CurrentClear }; current.AsSpan().Clear();
                    if (device == FalloutOriginalInputDevice.Keyboard) _keyboardKnown = true; else _mouseKnown = true;
                    ++_bufferRevision;
                }
                else
                {
                    receipt = receipt with { Step = FalloutOriginalInputPollStep.DevicePoll };
                    var polled = devices.Poll(device); receipt = receipt with { PollStatus = polled }; CheckOriginalFault();
                    receipt = receipt with { Step = FalloutOriginalInputPollStep.StateRead };
                    // A throwing/unowned producer may have partially written.
                    if (device == FalloutOriginalInputDevice.Keyboard) _keyboardKnown = false; else _mouseKnown = false;
                    var read = devices.ReadState(device, current); ++_bufferRevision;
                    receipt = receipt with { ReadStatus = read }; CheckOriginalFault();
                    if (device == FalloutOriginalInputDevice.Keyboard) _keyboardKnown = true; else _mouseKnown = true;
                    if (read == 0)
                    {
                        receipt = receipt with { Step = FalloutOriginalInputPollStep.Hook };
                        UpdateSourceHook(device, current, edges); receipt = receipt with { HookApplied = _sourceHooks };
                    }
                }
                receipt = receipt with { Step = FalloutOriginalInputPollStep.Returned };
            }
            catch (Exception error)
            { receipt = receipt with { FailedAt = receipt.Step, Step = FalloutOriginalInputPollStep.Failed, Failure = error.Message }; throw; }
            finally { if (device == FalloutOriginalInputDevice.Keyboard) _keyboardPoll = receipt; else _mousePoll = receipt; }
        }
    }

    private void UpdateSourceHook(FalloutOriginalInputDevice device, Span<byte> current, List<(int Key, bool Down)> edges)
    {
        if (device == FalloutOriginalInputDevice.Keyboard)
        {
            for (var index = 0; index < current.Length; ++index)
            {
                var game = PublishHookKey(index, current[index] != 0, edges);
                if (_sourceHooks) current[index] = game ? FalloutInputLayout.ButtonDown : (byte)0;
            }
        }
        else
        {
            for (var button = 0; button < FalloutInputLayout.MouseButtonCount; ++button)
            {
                var game = PublishHookKey(FalloutInputLayout.FirstMouseButton + button, current[MouseButtonsOffset + button] != 0, edges);
                if (_sourceHooks) current[MouseButtonsOffset + button] = game ? FalloutInputLayout.ButtonDown : (byte)0;
            }
            var wheel = BinaryPrimitives.ReadInt32LittleEndian(current[8..]);
            _ = PublishHookKey(FalloutInputLayout.WheelUp, wheel > 0, edges);
            _ = PublishHookKey(FalloutInputLayout.WheelDown, wheel < 0, edges);
            _x = BinaryPrimitives.ReadInt32LittleEndian(current); _y = BinaryPrimitives.ReadInt32LittleEndian(current[4..]); _wheel = wheel;
        }
        ++Revision;
    }

    internal void RetainUnownedSourceWriter(FalloutInputInvocation invocation, FalloutOriginalInputField field, string producer)
    {
        RequireOriginal();
        if (!Enum.IsDefined(field) || string.IsNullOrWhiteSpace(producer)) throw new InvalidDataException("Original input writer has no typed field/producer.");
        _originalBusy = true; _originalFault = false;
        try
        {
            _requireOriginalInvocation!(invocation); CheckOriginalFault();
            switch (field)
            {
                case FalloutOriginalInputField.KeyboardCurrent: _keyboardKnown = false; break;
                case FalloutOriginalInputField.KeyboardPrevious: _previousKeyboardKnown = false; break;
                case FalloutOriginalInputField.MouseCurrent: _mouseKnown = false; break;
                case FalloutOriginalInputField.MousePrevious: _previousMouseKnown = false; break;
                case FalloutOriginalInputField.MouseModeFour: _modeFourKnown = false; break;
                case FalloutOriginalInputField.MouseSwap: _swapKnown = false; break;
                case FalloutOriginalInputField.ControllerSuppression: _suppressionKnown = false; break;
                case FalloutOriginalInputField.MenuWord: _menuKnown = false; break;
            }
            _originalWriter = new(invocation, field, producer, checked(++_bufferRevision));
        }
        finally { _originalBusy = false; }
    }
}
