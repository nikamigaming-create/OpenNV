namespace OpenNV.Runtime.Content;

internal readonly record struct FalloutInputInvocation(Guid PlayerProcess, Guid Main, long Ordinal, Guid Invocation);
internal enum FalloutOriginalInputDevice { Keyboard, Mouse }
internal enum FalloutOriginalInputQueryStep { Entered, Selection, Binding, Device, MenuWord, Returned, Failed }
internal enum FalloutOriginalInputPollStep { Entered, PreviousStore, Acquire, DevicePoll, StateRead, CurrentClear, Hook, Returned, Failed }
internal sealed record FalloutOriginalInputObservation(FalloutOriginalInputDevice Device, byte Binding,
    int? EffectiveButton, byte? Current, byte? Previous, int? Swap, int? Wheel, int Result);
internal sealed record FalloutOriginalInputQuery(FalloutInputInvocation Invocation, string Source, string Stack,
    Guid Factory, FalloutInputProvider Provider, ulong Window, ulong Sample, long InputRevision,
    long BindingsRevision, long BufferRevision, long Sequence, int Control, int Mode,
    FalloutOriginalInputQueryStep Step, FalloutOriginalInputQueryStep? FailedAt,
    bool? ControllerSelected, IReadOnlyList<FalloutOriginalInputObservation> Observations,
    int Count, ushort BeforeMenuWord, ushort AfterMenuWord, string? Failure);
internal sealed record FalloutOriginalInputDevicePoll(FalloutOriginalInputDevice Device, long Sequence,
    FalloutOriginalInputPollStep Step, FalloutOriginalInputPollStep? FailedAt, int? AcquireStatus,
    int? PollStatus, int? ReadStatus, bool HookApplied, string? Failure);
internal sealed record FalloutOriginalInputSelection(long Sequence, int? Status, byte? Suppression,
    bool? Selected, string? Failure);
internal sealed record FalloutOriginalInputConstruction(Guid Platform, string Owner, int? Swap,
    uint? DoubleClickMilliseconds, bool Returned, string? Failure);
internal enum FalloutOriginalInputHookLane { PlainDevice = 1, ManagedControls = 2 }
internal sealed class FalloutOriginalInputHookBinding
{
    private readonly FalloutDirectInputState _owner;
    internal FalloutOriginalInputHookLane Lane { get; }
    private FalloutOriginalInputHookBinding(FalloutDirectInputState owner, FalloutOriginalInputHookLane lane)
    { _owner = owner.ReadManagerOwner(); Lane = lane; }
    internal static FalloutOriginalInputHookBinding ManagedControls(FalloutDirectInputState owner) =>
        new(owner, FalloutOriginalInputHookLane.ManagedControls);
    internal static FalloutOriginalInputHookBinding PlainDevice(FalloutDirectInputState owner) =>
        new(owner, FalloutOriginalInputHookLane.PlainDevice);
    internal void RequireOwner(FalloutDirectInputState owner)
    {
        if (!ReferenceEquals(owner, _owner)) throw new InvalidOperationException("Original input hook binding belongs to a different current manager.");
        _owner.ReadManagerOwner();
    }
}
internal sealed record FalloutOriginalInputPoll(long Sequence, string Source, string Stack, Guid Factory,
    FalloutOriginalInputHookLane HookLane, FalloutOriginalInputSelection? Selection,
    FalloutOriginalInputDevicePoll? Keyboard, FalloutOriginalInputDevicePoll? Mouse, bool Returned, string? Failure,
    string DeviceScope = "first-party-keyboard-mouse-provider;original-joystick-enumeration-and-poll-unowned");
internal enum FalloutOriginalInputField
{
    KeyboardCurrent, KeyboardPrevious, MouseCurrent, MousePrevious,
    MouseModeFour, MouseSwap, ControllerSuppression, MenuWord
}
internal sealed record FalloutOriginalInputUnownedWriter(FalloutInputInvocation Invocation,
    FalloutOriginalInputField Field, string Producer, long BufferRevision);
internal sealed record FalloutOriginalInputKnown(bool Keyboard, bool PreviousKeyboard, bool Mouse,
    bool PreviousMouse, bool ModeFour, bool Swap, bool Suppression, bool MenuWord);
internal sealed record FalloutOriginalInputSnapshot(string Source, string Stack, Guid Factory,
    FalloutInputProvider Provider, ulong Window, FalloutOriginalInputHookLane HookLane,
    long BufferRevision, long PollSequence, long QuerySequence, IReadOnlyList<byte> Keyboard,
    IReadOnlyList<byte> PreviousKeyboard, IReadOnlyList<byte> Mouse, IReadOnlyList<byte> PreviousMouse,
    IReadOnlyList<byte> ModeFourMouse, IReadOnlyList<byte> Controller, IReadOnlyList<byte> PreviousController,
    int Swap, uint DoubleClickMilliseconds, byte ControllerSuppression, ushort MenuWord,
    FalloutOriginalInputKnown Known, FalloutOriginalInputConstruction Construction,
    FalloutOriginalInputSelection? Selection, FalloutOriginalInputPoll? Poll,
    FalloutOriginalInputQuery? Query, FalloutOriginalInputUnownedWriter? UnownedWriter,
    IReadOnlyList<FalloutOriginalMenuWordWrite>? MenuWordWrites = null,
    FalloutOriginalMenuClose? MenuClose = null,
    IReadOnlyList<FalloutOriginalMouseBufferFlush>? MenuMouseFlushes = null,
    IReadOnlyList<FalloutOriginalMenuClose>? MenuCloses = null);

// The device writes into the actual retained, initialized engine destination.
// A nonzero state HRESULT never licenses replacement output or all-released.
internal interface IFalloutOriginalInputDevices
{
    bool Present(FalloutOriginalInputDevice device);
    void RequireCurrent(FalloutDirectInputState owner);
    int Acquire(FalloutOriginalInputDevice device);
    int Poll(FalloutOriginalInputDevice device);
    int ReadState(FalloutOriginalInputDevice device, Span<byte> destination);
}
internal interface IFalloutOriginalInputPlatform
{
    Guid Factory { get; }
    string Owner { get; }
    void RequireCurrent();
    void ConstructMouse(Action<int> storeSwap, Action<uint> storeDoubleClickMilliseconds);
    int ReadControllerState(Span<byte> destination);
}
