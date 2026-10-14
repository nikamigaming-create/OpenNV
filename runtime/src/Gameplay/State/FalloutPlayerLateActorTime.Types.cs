using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerLateFrameInvocation(FalloutPlayerSourceCaller Caller,
    Guid PlayerProcess, Guid PlayerConstructor, Guid Prelude,
    FalloutProcessSceneQuery Common, int ThreadId, uint ArgumentBits);
internal sealed record FalloutPlayerLateActorFields(byte MobileState, byte SelfPredicate, byte DateMismatch,
    byte AiBlocked)
{
    internal static FalloutPlayerLateActorFields Constructed => new(
        FalloutPlayerLateActorTimeSource.InitialMobileState,
        FalloutPlayerLateActorTimeSource.InitialSelfPredicate,
        FalloutPlayerLateActorTimeSource.InitialDateMismatch, FalloutPlayerLateActorTimeSource.InitialAiBlocked);
}
internal enum FalloutPlayerLateActorField { MobileState, SelfPredicate, RemainingScalar, ScalarWord, DateMismatch }
internal sealed record FalloutPlayerLateActorStore(FalloutPlayerLateActorField Field, long Changed, uint Before, uint After);
internal enum FalloutPlayerLateActorTimeStep
{
    ReferenceFlags, ActorUpdate, MobileUpdate, SelfPredicate, MainTimer, Scalar,
    FirstLife, PackedDay, CalendarDay, DateMismatch, Tier, Travel,
    SecondLife, AiBlocked, GlobalAi, RawLife, ThirdLife, HardwareThreads,
    ActorValues, Returned, Failed,
}
internal sealed record FalloutPlayerLatePackedDayRead(FalloutProcessSceneQuery Common, uint Word, int Day);
internal sealed record FalloutPlayerLateCalendarDayRead(FalloutFormKey Form, string WinningSha256,
    uint Bits, int SignedDay, FalloutPlayerFrameCalendarReadOwner? Owner = null);
internal sealed record FalloutPlayerLateGlobalAiRead(string Source, string Stack, Guid Process,
    long Sequence, long Revision, byte Value, string? Boundary);
internal sealed record FalloutPlayerLateActorTimeCall(string Source, string Stack, Guid Factory,
    FalloutPlayerLateFrameInvocation Invocation, long Ordinal, long Entered, long Changed,
    FalloutPlayerLateActorTimeStep Step, FalloutPlayerLateActorTimeStep? FailedAt,
    FalloutPlayerLateActorFields Before, FalloutPlayerLateActorFields After,
    FalloutPlayerSceneScalarFields BeforeScalars, FalloutPlayerSceneScalarFields AfterScalars,
    IReadOnlyList<FalloutPlayerLateActorStore> Stores,
    FalloutReferenceFlagRead? ReferenceFlags = null, FalloutActorProcessFact<bool>? ActorUpdate = null,
    IReadOnlyList<FalloutPlayerFollowingLifeRead>? Lives = null,
    FalloutSourceFrameDeltaRead? Timer = null, uint? ClockKind = null,
    FalloutPlayerLatePackedDayRead? PackedDay = null,
    FalloutPlayerLateCalendarDayRead? CalendarDay = null,
    FalloutActorProcessFact<int>? Travel = null, FalloutPlayerLateGlobalAiRead? GlobalAi = null,
    FalloutActorProcessFact<int>? RawLife = null, FalloutIniValue? HardwareThreads = null,
    FalloutReferenceFlagRead? ActorValueFlags = null, bool Returned = false, string? Failure = null);
internal sealed record FalloutPlayerLateActorPublication(Guid PlayerProcess, Guid Factory,
    Guid PlayerConstructor, Guid CommonProcess, FalloutCombatActorIdentity Actor, long Epoch,
    long Published, Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPlayerLateActorSnapshot(FalloutPlayerLateActorTimeSource Source,
    string Stack, Guid PlayerProcess, Guid Factory, Guid PlayerConstructor,
    FalloutCombatActorIdentity Actor, long Epoch, long Sequence, long Calls,
    FalloutPlayerLateActorFields Fields, FalloutPlayerLateActorTimeCall? LastCall,
    IReadOnlyList<FalloutPlayerLateActorPublication> Publications, string? WriterBoundary,
    string? Failure);
