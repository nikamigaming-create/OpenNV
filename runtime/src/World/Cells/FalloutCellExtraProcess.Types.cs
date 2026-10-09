using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutCellProcessListReference(FalloutCellProcessReference? Source,
    FalloutFormKey CurrentCell, long Membership, uint CurrentReferenceFlags,
    FalloutCombatActorIdentity? Actor, long? ProcessEpoch, FalloutDetectionProcessLevel? Level,
    FalloutActorProcessFact<bool>? HasProcess)
{
    internal FalloutFormKey Reference => Source?.Reference ?? (Actor is { EnginePlayer: true } player ? player.Reference :
        throw new InvalidDataException("Current CELL child omitted both its placed and genuine canonical runtime source."));
}
internal sealed record FalloutCellProcessReferenceList(FalloutCellProcessIdentity Cell, long Revision,
    IReadOnlyList<FalloutCellProcessListReference> References, string Owner);
internal sealed record FalloutCellExtraProcessEntry(FalloutCellProcessIdentity Source, bool Present,
    uint Count, long Generation, long Changed, string? Failure);
internal enum FalloutCellExtraProcessPhase
{ Entered, ExtraCreated, WalkingReferences, ConsumersReturned, CountStored, ExtraRemoved, Complete, Failed }
internal sealed record FalloutCellExtraProcessInvocation(Guid Identity, FalloutFormKey Cell, string Owner,
    bool Increase, bool BeforePresent, uint BeforeCount, long Generation,
    FalloutCellExtraProcessPhase Phase, FalloutCellProcessReferenceList? CurrentReferences,
    int NextReference, FalloutFormKey? InFlight, long Entered, long Changed, string? Failure);
internal sealed record FalloutCellExtraProcessSnapshot(string Schema, string Stack, string Contract,
    Guid CapturedProcess, long Sequence, IReadOnlyList<FalloutCellExtraProcessEntry> Cells,
    IReadOnlyList<FalloutCellExtraProcessInvocation> Invocations, FalloutActorProcessRuntimeHandoff? ColdHandoff);
internal enum FalloutProcessReevaluationPhase
{ Entered, AlreadyPending, ProcessAbsent, GuardsReturned, FlagStored, PendingInserted, RequestByteStored, Complete, Failed }
internal sealed record FalloutProcessReevaluationObservation(FalloutCombatActorIdentity Source, long Epoch,
    FalloutDetectionProcessLevel? Current, FalloutActorProcessElection? Election,
    FalloutActorProcessFact<int> NeutralLife, FalloutActorProcessFact<byte> CommonFlags,
    FalloutActorProcessFact<uint> ReferenceFlags, FalloutActorProcessFact<int> PlayerTransitionCount, string Owner);
internal sealed record FalloutProcessReevaluationEntry(FalloutCombatActorIdentity Source, long Epoch,
    bool? PendingReferenceFlag, bool? RequestByte, long Changed, bool Retired, string? Failure);
internal sealed record FalloutProcessReevaluationInvocation(Guid Identity, FalloutCombatActorIdentity Source,
    long Epoch, string Owner, FalloutProcessReevaluationPhase Phase,
    FalloutProcessReevaluationObservation? Observation, FalloutDetectionProcessLevel? Desired,
    bool? LifeGuard, bool? TierGuard, bool? CommonGuard, bool? ReferenceGuard, bool? PlayerGuard,
    bool Enqueued, long Entered, long Changed, string? Failure);
internal sealed record FalloutProcessReevaluationSnapshot(string Schema, string Stack, string Contract,
    Guid CapturedProcess, long Sequence, IReadOnlyList<FalloutProcessReevaluationEntry> Actors,
    IReadOnlyList<FalloutFormKey> Pending, IReadOnlyList<FalloutProcessReevaluationInvocation> Invocations,
    FalloutActorProcessRuntimeHandoff? ColdHandoff);
