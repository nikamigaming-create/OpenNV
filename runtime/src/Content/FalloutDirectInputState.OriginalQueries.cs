using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutDirectInputState
{
    private const int MouseStateExtent = 20;
    private const int MouseButtonsOffset = 12;
    private const ushort MenuMask = 8;
    private readonly byte[] _engineKeyboard = new byte[FalloutInputLayout.KeyboardKeyCount];
    private readonly byte[] _previousKeyboard = new byte[FalloutInputLayout.KeyboardKeyCount];
    private readonly byte[] _engineMouse = new byte[MouseStateExtent];
    private readonly byte[] _previousMouse = new byte[MouseStateExtent];
    private readonly byte[] _modeFourMouse = new byte[FalloutInputLayout.MouseButtonCount];
    private readonly byte[] _controller = new byte[16], _previousController = new byte[16];
    private FalloutOriginalInputSource? _originalSource;
    private IFalloutOriginalInputPlatform? _originalPlatform;
    private FalloutOriginalInputHookBinding? _originalHooks;
    private Action<FalloutInputInvocation>? _requireOriginalInvocation;
    private FalloutOriginalInputConstruction? _originalConstruction;
    private FalloutOriginalInputSelection? _originalSelection;
    private FalloutOriginalInputQuery? _originalQuery;
    private FalloutOriginalInputDevicePoll? _keyboardPoll, _mousePoll;
    private long _bufferRevision, _querySequence;
    private int _swap;
    private uint _doubleClickMilliseconds;
    private byte _controllerSuppression;
    private ushort _menuWord;
    private bool _originalBusy, _originalFault, _sourceHooks, _swapKnown, _modeFourKnown;
    private bool _suppressionKnown, _menuKnown;
    private bool _keyboardKnown, _previousKeyboardKnown, _mouseKnown, _previousMouseKnown;

    internal bool HasSourceControlQueries => _originalSource is not null;
    internal FalloutOriginalInputQuery? LastSourceControlRead { get { RequireOwner(); return _originalQuery; } }
    internal void BindSourceControlQueries(FalloutOriginalInputSource source, IFalloutOriginalInputPlatform platform,
        Action<FalloutInputInvocation> requireInvocation, FalloutOriginalInputHookBinding hooks)
    {
        RequireOwner(); source.Validate(); ArgumentNullException.ThrowIfNull(platform); ArgumentNullException.ThrowIfNull(requireInvocation);
        ArgumentNullException.ThrowIfNull(hooks); RequireIdleOriginal(); hooks.RequireOwner(this);
        if (_originalSource is not null || _originalBusy || platform.Factory == Guid.Empty || string.IsNullOrWhiteSpace(platform.Owner))
            throw new InvalidOperationException("Original input changed its actual source/platform construction owner.");
        _originalSource = source; _originalPlatform = platform; _requireOriginalInvocation = requireInvocation;
        _originalHooks = hooks; _sourceHooks = hooks.Lane == FalloutOriginalInputHookLane.ManagedControls;
        _originalConstruction = new(platform.Factory, platform.Owner, null, null, false, null);
        _originalBusy = true; _originalFault = false;
        try
        {
            // This source store precedes the platform children. CLR zeroed
            // arrays do not establish the later source buffer zero stores.
            _menuWord = 0; _menuKnown = true;
            platform.RequireCurrent(); CheckOriginalFault();
            platform.ConstructMouse(value =>
            {
                RequireOwner();
                if (_originalConstruction.Swap is not null) { _originalFault = true; throw new InvalidOperationException("Original mouse swap constructor repeated its store."); }
                _swap = value; _swapKnown = true;
                _originalConstruction = _originalConstruction with { Swap = value };
            }, value =>
            {
                RequireOwner();
                if (!_swapKnown || _originalConstruction.DoubleClickMilliseconds is not null)
                { _originalFault = true; throw new InvalidOperationException("Original mouse constructor changed its store order."); }
                _doubleClickMilliseconds = value;
                _originalConstruction = _originalConstruction with { DoubleClickMilliseconds = value };
            });
            platform.RequireCurrent(); CheckOriginalFault();
            if (!_swapKnown || _originalConstruction.DoubleClickMilliseconds is null)
                throw new InvalidDataException("Original input platform omitted its actual constructor stores.");
            _engineKeyboard.AsSpan().Clear(); _previousKeyboard.AsSpan().Clear();
            _engineMouse.AsSpan().Clear(); _previousMouse.AsSpan().Clear();
            _keyboardKnown = _previousKeyboardKnown = _mouseKnown = _previousMouseKnown = true; ++_bufferRevision;
            // The selected constructor stores its independent manager byte
            // after the platform and buffer fields. Failed earlier children
            // cannot establish this current suppression value.
            _controllerSuppression = 0; _suppressionKnown = true;
            _originalConstruction = _originalConstruction with { Returned = true };
        }
        catch (Exception error) { _originalConstruction = _originalConstruction with { Failure = error.Message }; throw; }
        finally { _originalBusy = false; }
    }

    internal FalloutOriginalInputQuery ReadSourceControl(FalloutInputInvocation invocation, int control, int mode)
    {
        RequireOriginal();
        if (invocation.PlayerProcess == Guid.Empty || invocation.Main == Guid.Empty || invocation.Ordinal < 1 || invocation.Invocation == Guid.Empty)
            throw new InvalidDataException("Original input query has no actual Player/Main invocation.");
        var observations = new List<FalloutOriginalInputObservation>(2);
        _originalQuery = new(invocation, _originalSource!.Identity, Source, Factory, Provider, Window, Sample, Revision,
            Controls.Revision, _bufferRevision, checked(++_querySequence), control, mode, FalloutOriginalInputQueryStep.Entered,
            null, null, [], 0, _menuWord, _menuWord, null);
        _originalBusy = true; _originalFault = false;
        try
        {
            _originalPlatform!.RequireCurrent(); _originalHooks!.RequireOwner(this);
            _requireOriginalInvocation!(invocation); CheckOriginalFault();
            Step(FalloutOriginalInputQueryStep.Selection);
            var selection = _originalSelection is { Selected: { } selected, Failure: null } ? selected :
                throw new NotSupportedException("source-current-input-controller-selection-poll-unbound");
            _originalQuery = _originalQuery with { ControllerSelected = selection };
            if (selection)
            {
                if (control >= 29) return Returned();
                throw new NotSupportedException("source-current-original-gamepad-query-owner-unbound");
            }
            if (control < 0) throw new NotSupportedException("source-original-negative-control-adjacent-fields-unowned");
            if (control >= FalloutInputControls.Count)
            {
                if (control <= 30)
                {
                    Step(FalloutOriginalInputQueryStep.Device);
                    var binding = control switch { 28 => (byte)1, 29 => (byte)0x29, _ => (byte)0xb7 };
                    var observation = ReadKeyboard(binding, 1);
                    if (control != 28) observation = observation with { Result = ((observation.Current!.Value | observation.Previous!.Value) & FalloutInputLayout.ButtonDown) != 0 ? 1 : 0 };
                    observations.Add(observation); _originalQuery = _originalQuery with { Count = observation.Result };
                }
                return Returned();
            }
            Step(FalloutOriginalInputQueryStep.Binding);
            var bindings = Controls.ReadSourceBinding(control);
            CheckOriginalFault();
            _originalQuery = _originalQuery with { BindingsRevision = Controls.Revision };
            Read(FalloutOriginalInputDevice.Keyboard, bindings.Keyboard);
            Read(FalloutOriginalInputDevice.Mouse, bindings.Mouse);
            if (control == 14 && _originalQuery.Count > 0)
            {
                Step(FalloutOriginalInputQueryStep.MenuWord);
                if (!_menuKnown) throw new NotSupportedException("source-original-input-menu-word-current-writer-unowned");
                if ((_menuWord & MenuMask) != 0)
                {
                    var beforeMenu = _menuWord;
                    _menuWord = (ushort)(_menuWord & ~MenuMask);
                    _originalQuery = _originalQuery with { Count = 0, AfterMenuWord = _menuWord };
                    if (mode is 0 or 1) _menuWord |= MenuMask;
                    _originalQuery = _originalQuery with { AfterMenuWord = _menuWord }; ++_bufferRevision;
                    _originalMenuWordWrites.Add(new(invocation.Invocation, FalloutOriginalMenuWordWriter.ControlQuery,
                        _bufferRevision, beforeMenu, _menuWord));
                }
            }
            return Returned();
        }
        catch (Exception error)
        {
            _originalQuery = _originalQuery with
            {
                FailedAt = _originalQuery.Step,
                Step = FalloutOriginalInputQueryStep.Failed,
                Observations = Array.AsReadOnly(observations.ToArray()),
                Failure = error.Message
            }; throw;
        }
        finally { _originalBusy = false; }

        void Step(FalloutOriginalInputQueryStep step) => _originalQuery = _originalQuery! with { Step = step };
        void Read(FalloutOriginalInputDevice device, byte binding)
        {
            if (binding == byte.MaxValue) return;
            Step(FalloutOriginalInputQueryStep.Device);
            var observation = device == FalloutOriginalInputDevice.Keyboard ? ReadKeyboard(binding, mode) : ReadMouse(binding, mode);
            observations.Add(observation);
            _originalQuery = _originalQuery! with { Count = checked(_originalQuery.Count + observation.Result) };
        }
        FalloutOriginalInputQuery Returned()
        {
            _requireOriginalInvocation!(invocation); _originalPlatform!.RequireCurrent(); CheckOriginalFault();
            return _originalQuery = _originalQuery! with
            {
                Step = FalloutOriginalInputQueryStep.Returned,
                Observations = Array.AsReadOnly(observations.ToArray())
            };
        }
    }

    private FalloutOriginalInputObservation ReadKeyboard(byte binding, int mode)
    {
        if ((uint)mode > 3) return new(FalloutOriginalInputDevice.Keyboard, binding, null, null, null, null, null, 0);
        if (!_keyboardKnown || mode != 0 && !_previousKeyboardKnown)
            throw new NotSupportedException("source-original-keyboard-current-or-previous-writer-unowned");
        var current = _engineKeyboard[binding]; var previous = mode == 0 ? (byte?)null : _previousKeyboard[binding];
        return new(FalloutOriginalInputDevice.Keyboard, binding, null, current, previous, null, null, Edge(current, previous, mode));
    }
    private FalloutOriginalInputObservation ReadMouse(byte binding, int mode)
    {
        if (binding is 8 or 9)
        {
            if (!_mouseKnown) throw new NotSupportedException("source-original-mouse-wheel-current-writer-unowned");
            var wheel = BinaryPrimitives.ReadInt32LittleEndian(_engineMouse.AsSpan(8));
            return new(FalloutOriginalInputDevice.Mouse, binding, null, null, null, null, wheel,
                (binding == 8 ? wheel > 0 : wheel < 0) ? 1 : 0);
        }
        if (binding >= FalloutInputLayout.MouseButtonCount)
            throw new NotSupportedException("source-original-mouse-adjacent-object-byte-unowned");
        int? swap = null; var button = (int)binding;
        if (button <= 1)
        {
            if (!_swapKnown) throw new NotSupportedException("source-original-input-mouse-swap-current-writer-unowned");
            swap = _swap; if (_swap != 0) button ^= 1;
        }
        if ((uint)mode > 4) return new(FalloutOriginalInputDevice.Mouse, binding, button, null, null, swap, null, 0);
        if (mode == 4)
        {
            if (!_modeFourKnown) throw new NotSupportedException("source-original-mouse-mode-four-field-unwritten");
            return new(FalloutOriginalInputDevice.Mouse, binding, button, _modeFourMouse[button], null, swap, null,
                _modeFourMouse[button] != 0 ? 1 : 0);
        }
        if (!_mouseKnown || mode != 0 && !_previousMouseKnown)
            throw new NotSupportedException("source-original-mouse-current-or-previous-writer-unowned");
        var current = _engineMouse[MouseButtonsOffset + button]; var previous = mode == 0 ? (byte?)null : _previousMouse[MouseButtonsOffset + button];
        return new(FalloutOriginalInputDevice.Mouse, binding, button, current, previous, swap, null, Edge(current, previous, mode));
    }
    internal static int Edge(byte current, byte? previous, int mode)
    {
        var now = (current & FalloutInputLayout.ButtonDown) != 0;
        var before = (previous.GetValueOrDefault() & FalloutInputLayout.ButtonDown) != 0;
        return mode switch
        {
            0 => now ? 1 : 0,
            1 => now && !before ? 1 : 0,
            2 => !now && before ? 1 : 0,
            3 => now != before ? 1 : 0,
            _ => 0
        };
    }
    private void RequireOriginal()
    {
        RequireOwner();
        RequireIdleOriginal();
        if (_originalSource is null || _originalConstruction is not { Returned: true, Failure: null })
            throw new NotSupportedException("source-original-input-source-and-platform-constructor-unbound");
    }
    private void RequireIdleOriginal()
    {
        if (_originalBusy) { _originalFault = true; throw new InvalidOperationException("Original input cannot reenter a current constructor, poll or query."); }
    }
    private void CheckOriginalFault()
    {
        if (_originalFault) throw new InvalidOperationException("Original input provider or caller swallowed a forbidden current-owner reentry.");
    }
}
