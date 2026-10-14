using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerLateFrameStep
{
    ViewRequest, CurrentView, DisabledControls, MoverFlags, SharedTimer,
    SecondInhibition, FirstInhibition, ViewComparison, ActorTime,
    DefaultVector, MoverDelta, Tail, Failed,
}

internal sealed record FalloutPlayerLateFrameCall(string Source, string Stack,
    FalloutPlayerLateFrameInvocation Invocation, long Entered, long Changed,
    FalloutPlayerLateFrameStep Step, FalloutPlayerLateFrameStep? FailedAt = null,
    byte? ViewRequest = null, byte? CurrentView = null, ushort? DisabledControls = null,
    ushort? MoverWord = null, FalloutActorMoverFrameStoreOperation? Flags = null,
    uint? SharedTimerBits = null, IReadOnlyList<FalloutPlayerInhibitionRead>? InhibitionReads = null,
    byte? DesiredView = null, byte? ComparedView = null,
    FalloutPlayerLateActorTimeCall? ActorTime = null,
    FalloutActorMoverFrameStoreOperation? DefaultVector = null, string? Failure = null,
    FalloutPlayerMoverDeltaCall? MoverDelta = null);

internal sealed record FalloutPlayerLateFrameSnapshot(FalloutPlayerLateFramePrefixSource Source,
    string Stack, Guid Process, Guid Constructor, byte ViewRequest, uint InhibitionTimerBits,
    bool TimerAvailable, string? TimerWriterBoundary, FalloutPlayerLateFrameCall? LastCall);

internal interface IFalloutPlayerLateFrameConsumers
{
    void UpdateLateFrame(FalloutPlayerSourceCaller caller, Action<FalloutPlayerLateFrameCall> observe);
}
