using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerControlHudFrame(FalloutSourceFrameDeltaRead Clock, uint ControlMode, uint? DivisorBits);
internal sealed record FalloutPlayerControlHudTransition(Guid Identity, Guid Menu, uint ElapsedBits, uint DurationBits,
    uint ProgressBits, bool Removed);
internal sealed record FalloutPlayerControlHudTraitWrite(long Order, string Path, string Trait, uint ValueBits, long UiRevision);
internal sealed record FalloutPlayerControlHudInitialization(Guid Invocation, long Entered, long Changed,
    string Step, FalloutPlayerControlDisplayRead? Display, IReadOnlyList<FalloutPlayerControlHudTraitWrite> Writes,
    bool Returned, string? Failure);
internal sealed record FalloutPlayerControlHudCall(Guid Invocation, int Requested, int Selected, long Entered, long Changed,
    string Step, uint? DurationBits, FalloutSourceThreadKindScope? Allocation, FalloutPlayerControlHudFrame? Frame,
    IReadOnlyList<FalloutPlayerControlHudTraitWrite> Writes, bool Returned, string? Failure,
    Guid Process = default, Guid HudFactory = default);
internal sealed record FalloutPlayerControlHudColdHandoff(Guid PreviousProcess, Guid CurrentProcess,
    Guid PreviousFactory, Guid CurrentFactory, Guid OriginProcess, Guid OriginFactory, Guid? OriginDisplayFactory);
internal sealed record FalloutPlayerControlHudLifetime(Guid Process, Guid Factory);
internal sealed record FalloutPlayerControlHudTimerConstructor(Guid Process, Guid HudFactory,
    uint OriginWallTick, byte PauseCount, uint FixedMillisecondsBits, uint FractionalMillisecondsBits,
    uint DeltaSecondsBits, uint CachedMilliseconds, ushort StatusWord);
internal sealed record FalloutPlayerControlHudSnapshot(string Source, string Stack, Guid Process, Guid Factory,
    Guid UiFactory, long UiEpoch, long Sequence, uint MenuMode, int Mode, uint? VisibilityMask, uint AllowedMask,
    int MessageHandle, byte ForcedSubtitle, byte VisibilityChanged, Guid? PendingMessage,
    bool DisplayInitialized, bool InitializationReturned, FalloutPlayerControlDisplayRead? Display,
    IReadOnlyList<FalloutPlayerControlHudTransition> Transitions, FalloutPlayerControlHudCall? Call, string? Failure,
    FalloutPlayerControlHudInitialization? Initialization = null,
    IReadOnlyList<FalloutUiComponentValues>? Tiles = null, string? MessagePath = null,
    FalloutPlayerControlHudColdHandoff? ColdHandoff = null, bool Retired = false, string? ColdBindingFailure = null,
    IReadOnlyList<FalloutPlayerControlHudLifetime>? Lifetimes = null,
    FalloutPlayerControlHudTimerConstructor? LocalTimerConstructor = null);

// These reads come from genuine current source fields. An adapter must bind
// the real process/timer/interface/menu owners before a reached query returns.
internal interface IFalloutPlayerControlHudContext
{
    FalloutPlayerControlHudSource Source { get; }
    string Stack { get; }
    Guid Process { get; }
    Guid Factory { get; }
    void RequireCurrent();
    void PublishMenu(Guid menu, FalloutUiComponentStore ui, FalloutUiComponentBinding root);
    void ReplaceMenuTransition(Guid menu, uint durationBits);
    IReadOnlyList<FalloutPlayerControlHudTransition> AdvanceMenuTransitions(FalloutPlayerControlHudFrame frame);
    IReadOnlyList<FalloutPlayerControlHudTransition> CaptureMenuTransitions();
    FalloutPlayerControlHudFrame ReadTransitionFrame();
    bool ReadInterfaceMovementOverride();
    bool ReadMessageSuppression();
    bool ReadMenuActive(uint menu);
    int ReadDefaultMode(ushort disabledWord);
    int ReadForcedSubtitleMode(int mode);
    void UpdateDynamicActionView();
    bool ReadForcedSubtitleAvailable();
    void ChangeForcedSubtitle(bool enabled);
    bool ReadMessageSound(int handle);
    void SetMessageSoundPaused(int handle, bool paused);
}
