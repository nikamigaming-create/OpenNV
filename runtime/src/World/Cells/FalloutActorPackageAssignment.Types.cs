using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutActorAssignmentStep
{
    Entered, Process, Procedure, Status, ExceptionalStatus, Alternate, Primary, Adjustment, AdjustmentReread,
    PositiveAdjustment, AssignmentByteRead, AssignmentByteStore, Eligibility, ExtraDerivation, CurrentSetter, ActorCallback,
    PostAssignmentByte, PostProcess, CommonAnimation, AnimationStatus, AnimationStatusNine,
    AnimationStatusFour, AnimationMark, LinkedReference, HighReset, MoverByte, BasePredicate, MoverReset,
    Followers, Returned, Failed
}
internal enum FalloutActorLinkedOccurrence { ActorAssignment, SharedReset }
internal enum FalloutActorLinkedStep
{ Entered, Equality, PreviousCleanup, PointerStore, PackageSelection, ExtraStore, NullCleanup, NonnullEffects, Returned, Failed }
internal enum FalloutActorHighResetStep
{
    Entered, ResetMode, CommonWord, IndependentByte, FlagTwo, FlagOne, FlagForty, FlagTwenty,
    LinkedReference, InlineList, Loading, LinkedEffect, OtherEffect, Tail, Returned, Failed
}
internal enum FalloutActorAssignmentLane
{ Adjustment, LinkedReference, InlineList, LinkedEffectIndex, OtherEffectActor, ResetMode, IndependentResetByte, ResetFields, Tier, CommonFlags, CommonWord }
internal enum FalloutActorAssignmentCell
{
    Adjustment, LinkedReference, InlineValue, InlineLink, LinkedEffectIndex, OtherEffectActor, Tier,
    ResetMode, IndependentResetByte, TailByte, TailFirstWord, TailIndexWord, TailFinalWord,
    TailFirstPointer, TailClock, TailSecondPointer, TailSecondByte, TailThirdPointer,
    TailThirdByte, TailFourthPointer, TailFinalByte, CommonWord, CommonFlagTwo, CommonFlagOne, CommonFlagForty
}
internal sealed record FalloutActorAssignmentValues(uint Adjustment, FalloutPackageExtraReference? LinkedReference,
    Guid? InlineValue, Guid? InlineLink, int LinkedEffectIndex, FalloutPackageExtraReference? OtherEffectActor,
    int Tier, ushort ResetMode, byte IndependentResetByte, byte TailByte, ushort TailFirstWord,
    ushort TailIndexWord, ushort TailFinalWord, Guid? TailFirstPointer, uint TailClock, Guid? TailSecondPointer,
    byte TailSecondByte, Guid? TailThirdPointer, byte TailThirdByte, Guid? TailFourthPointer, byte TailFinalByte)
{
    internal static FalloutActorAssignmentValues Constructed => new(0, null, null, null, -1, null, 0,
        FalloutActorPackageAssignmentSource.ResetMode, 0, 0, 0, ushort.MaxValue, 0, null, 0, null, 0, null, 0, null, 0);
}
internal sealed record FalloutActorAssignmentConstruction(string Source, FalloutCombatActorIdentity Actor,
    Guid Process, Guid Factory, long Epoch, long Changed, Guid CurrentPackageFactory, Guid PrimaryPackageFactory,
    bool InitialLowCopy, FalloutActorAssignmentValues Values);
internal sealed record FalloutActorAssignmentPublication(Guid Process, Guid Factory, long Changed,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutActorAssignmentRead<T>(FalloutPackageCommandFieldInvocation Invocation,
    FalloutProcessSceneQuery Process, string Source, Guid Factory, long Sequence, T Value);
internal sealed record FalloutActorAssignmentStore(FalloutPackageCommandFieldInvocation Invocation,
    FalloutProcessSceneQuery Process, Guid Factory, FalloutActorAssignmentCell Cell, long Changed,
    uint? BeforeBits, uint? AfterBits, FalloutPackageExtraReference? BeforeReference = null,
    FalloutPackageExtraReference? AfterReference = null, Guid? BeforeObject = null, Guid? AfterObject = null,
    bool BeforeKnown = true, uint? OperandBits = null);
internal sealed record FalloutActorAssignmentWriter(FalloutPackageCommandFieldInvocation Invocation,
    FalloutProcessSceneQuery Process, FalloutActorAssignmentLane Lane, long Changed, string Boundary);
internal sealed record FalloutActorLinkedCall(FalloutPackageCommandFieldInvocation Invocation, string Source,
    Guid Factory, Guid Identity, FalloutProcessSceneQuery Process, FalloutActorLinkedOccurrence Occurrence, FalloutPackageExtraReference? Desired,
    long Entered, long Changed, FalloutActorLinkedStep Step, FalloutActorLinkedStep? FailedAt,
    FalloutPackageExtraReference? Before, bool Equal, IReadOnlyList<FalloutActorAssignmentStore> Stores,
    bool NullCleanupReturned, FalloutProcessPackageRead? Selected, bool Returned, string? Failure)
{
    internal FalloutActorMoverResetChildReceipt MoverReceipt => new(FalloutActorMoverResetChild.LinkedReference,
        Identity, Invocation, Process, Entered, Changed, Returned, Failure);
}
internal sealed record FalloutActorHighResetCall(FalloutPackageCommandFieldInvocation Invocation, string Source,
    Guid Factory, Guid Identity, FalloutProcessSceneQuery Process, long Entered, long Changed, FalloutActorHighResetStep Step,
    FalloutActorHighResetStep? FailedAt, IReadOnlyList<FalloutActorAssignmentStore> Stores,
    FalloutProcessPackageRequestFlagStore? RequestFlag, FalloutActorLinkedCall? Linked,
    FalloutDataHandlerLoadingRead? Loading, bool InlineListEmpty, bool Returned, string? Failure)
{
    internal FalloutActorMoverResetChildReceipt MoverReceipt => new(FalloutActorMoverResetChild.HighReset,
        Identity, Invocation, Process, Entered, Changed, Returned, Failure);
}
internal sealed record FalloutActorAssignmentBaseRead(string Source, FalloutFormKey Form, byte Kind,
    uint? CreatureFlags, long Sequence);
internal sealed record FalloutActorPackageExtraRead(string Source, FalloutFormKey Package, string RecordSha256,
    byte? TargetKind, FalloutPackageExtraReference? Extra, bool CurrentTargetAvailable, string? Failure = null,
    FalloutPackageRuntimeTargetRead? TargetRead = null);
internal sealed record FalloutActorFollowerExtraRead(string Source, FalloutCombatActorIdentity Actor,
    Guid Process, Guid Factory, Guid Constructor, long Sequence, bool Present, Guid? Extra, Guid? Payload,
    FalloutReferencePositionInvocation? ReferenceInvocation = null);
internal sealed record FalloutActorPackageAssignmentCall(FalloutPackageCommandFieldInvocation Invocation,
    string Source, Guid Factory, FalloutFormKey Package, bool Primary, bool Alternate, long Entered, long Changed,
    FalloutActorAssignmentStep Step, FalloutActorAssignmentStep? FailedAt, string? Failure,
    IReadOnlyList<FalloutActorAssignmentStep> ReturnedChildren, FalloutProcessSceneQuery? InitialProcess = null,
    int? Procedure = null, sbyte? Kind = null, IReadOnlyList<FalloutReferenceActorYawModeRead>? StatusReads = null,
    FalloutActorAssignmentRead<uint>? Adjustment = null, FalloutReferenceActorAssignmentByteRead? AssignmentByte = null,
    FalloutReferencePositionCall? ByteStore = null, FalloutPackageStateMutation? PackageMutation = null,
    FalloutProcessSceneQuery? AnimationProcess = null, FalloutActorLinkedCall? Linked = null,
    FalloutActorHighResetCall? HighReset = null, FalloutActorAssignmentBaseRead? Base = null,
    bool MoverReturned = false, FalloutActorFollowerExtraRead? Followers = null, bool Returned = false,
    FalloutActorPackageExtraRead? Extra = null);
internal sealed record FalloutProcessActorAssignmentFields(FalloutActorPackageAssignmentSource Source,
    FalloutActorAssignmentConstruction Construction, FalloutActorAssignmentPublication Publication,
    FalloutActorAssignmentValues Values, IReadOnlyList<FalloutActorAssignmentPublication> Publications,
    IReadOnlyList<FalloutActorAssignmentStore> Stores, IReadOnlyList<FalloutActorAssignmentWriter> Writers,
    IReadOnlyDictionary<FalloutActorAssignmentLane, string> UnownedWriters,
    IReadOnlyList<FalloutActorLinkedCall> LinkedCalls, IReadOnlyList<FalloutActorHighResetCall> HighCalls,
    IReadOnlyList<FalloutActorPackageAssignmentCall> Calls);

internal interface IFalloutActorPackageAssignmentConsumers : IFalloutPackageStateServices
{
    FalloutActorPackageAssignmentSource Source { get; }
    string Stack { get; }
    FalloutProcessSceneQuery? ReadCurrentProcess(FalloutPackageCommandFieldInvocation invocation);
    int EnsureCurrentProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package);
    FalloutReferenceActorYawModeRead ReadAssignmentStatus(FalloutPackageCommandFieldInvocation invocation);
    FalloutReferenceActorAssignmentByteRead ReadAssignmentByte(FalloutPackageCommandFieldInvocation invocation);
    FalloutReferencePositionCall StoreAssignmentByteZero(FalloutPackageCommandFieldInvocation invocation);
    FalloutActorPackageExtraRead DeriveCurrentPackageExtra(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package);
    FalloutDataHandlerLoadingRead ReadDataHandlerLoading(FalloutPackageCommandFieldInvocation invocation);
    FalloutActorAssignmentBaseRead ReadCurrentBaseForMover(FalloutPackageCommandFieldInvocation invocation);
    void ResetActualMover(FalloutPackageCommandFieldInvocation invocation, FalloutActorMoverResetJoin join);
    FalloutActorFollowerExtraRead ReadFollowerExtra(FalloutPackageCommandFieldInvocation invocation);
}
