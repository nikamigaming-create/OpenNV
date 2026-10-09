using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainProcessOperation { SourceWorldLoad, SourceFullUpdate }
internal enum FalloutMainProcessPhase { Entered, ConsumersReturned, Complete, Failed }
internal sealed record FalloutMainProcessReceipt(Guid Invocation, FalloutMainProcessOperation Operation,
    string Owner, long Entered, long LastChanged, FalloutMainProcessPhase Phase, bool Before, string? Failure);
internal enum FalloutPlayerTravelPhase { Constructed, WorldHours, Destination, Complete, Failed }
internal sealed record FalloutPlayerTravelReceipt(Guid Invocation, string Owner, int InitialHours,
    int RemainingHours, int CompletedWorldHours, long Entered, long LastChanged,
    FalloutPlayerTravelPhase Phase, bool ConsumerEntered, string? Failure);
internal sealed record FalloutActorNeutralLifeEntry(FalloutCombatActorIdentity Source, int? Value,
    string Owner, long Changed, bool Retired, string? Failure);
internal sealed record FalloutActorProcessRuntimeHandoff(Guid PreviousProcess, Guid CurrentProcess, long Sequence);
internal sealed record FalloutActorProcessRuntimeSnapshot(string Schema, string Contract, string Stack,
    FalloutFormKey Player, Guid CapturedProcess, long Sequence, bool MainForcedProcessing, int PlayerTravelCounter,
    IReadOnlyList<FalloutMainProcessReceipt> MainOperations, FalloutPlayerTravelReceipt? Travel,
    IReadOnlyList<FalloutActorNeutralLifeEntry> Actors, FalloutActorProcessRuntimeHandoff? ColdHandoff,
    FalloutMainFrameSnapshot MainFrame);

// One ordered common field copy has an explicit extent even when a constructor
// leaves a field unwritten. An unwritten lane cannot be consumed as zero.
internal sealed record FalloutProcessCommonScalars(uint ClockBits, byte Flags,
    uint VectorFirstBits, uint VectorSecondBits, uint FirstWord, uint SecondWord, uint ThirdWord,
    uint? FinalFirstBits, uint FinalSecondBits, uint FinalThirdBits)
{
    internal static FalloutProcessCommonScalars Constructed => new(0xbf800000u, 0, 0, 0, 0, 0, 0, null, 0, 0);
}
internal sealed record FalloutProcessGameplayState(FalloutFormKey Actor, FalloutActorPackageAssignment? Package,
    FalloutActorPackageMotion? Motion, FalloutActorScriptPackageSnapshot? ScriptPackage,
    FalloutActorPackageChoice? PendingChoice, FalloutActorDeferredPackageContinuation? Deferred,
    FalloutActorFurnitureContinuation? Furniture, FalloutActorDialogueContinuation? Dialogue,
    FalloutActorSelectionFailure? SelectionFailure, FalloutActorPackageBindingFailure? BindingFailure,
    FalloutActorPendingPackageSelection? PendingSelection, string? ProcedureBlocker,
    IReadOnlyDictionary<string, FalloutActorValue> ActorValues);
internal sealed record FalloutProcessGameplayLease(FalloutCombatActorIdentity Source, Guid Ownership,
    long ProcessEpoch, long Changed, bool Retired);
internal enum FalloutProcessCommonPhase { Constructed, Copied, OldRetired, Published, Initialized, Retired }
internal sealed record FalloutProcessCommonEntry(FalloutCombatActorIdentity Source, long Epoch,
    FalloutDetectionProcessLevel Level, FalloutProcessCommonScalars Scalars,
    FalloutProcessGameplayLease? Gameplay, FalloutProcessCommonPhase Phase, string? Boundary,
    FalloutActorProcessBodyBinding? Body, long Changed, FalloutActorProcessFact<bool>? Source3D = null);
internal sealed record FalloutProcessCommonTransfer(FalloutFormKey Actor, long BeforeEpoch, long NewEpoch,
    FalloutDetectionProcessLevel Before, FalloutDetectionProcessLevel After, string Owner,
    Guid GameplayOwnership, FalloutProcessGameplayState ObservedBeforeCopy, string GameplaySha256,
    bool OldOwnershipCleared, bool OldRetired, bool Published, bool Initialized, string? Failure, long Changed);
internal sealed record FalloutProcessCommonSnapshot(string Schema, string Contract, string Stack, Guid CapturedProcess,
    long Sequence, IReadOnlyList<FalloutProcessCommonEntry> Current,
    IReadOnlyList<FalloutProcessCommonEntry> Retired, FalloutProcessCommonEntry? Pending, FalloutProcessCommonTransfer? Transfer,
    FalloutActorProcessRuntimeHandoff? ColdHandoff);

internal sealed record FalloutActorProcessNodeBinding(byte PartType, string SourceNode, int? Block);
internal sealed record FalloutActorProcessBodyBinding(FalloutFormKey Actor, string SkeletonPath,
    string SkeletonSha256, FalloutFormKey BodyPartSource, string BodyPartSha256, IReadOnlyList<FalloutActorProcessNodeBinding> Parts,
    string? HeadTarget, int? HeadBlock, string? TorsoTarget, int? TorsoBlock,
    int? Bip01Block, int? BoneLodController, string Owner);
