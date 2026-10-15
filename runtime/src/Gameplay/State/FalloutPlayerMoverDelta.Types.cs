using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerMoverDeltaStep
{
    DefaultVector, CommonAnimation, View, FirstAnimation, AnimationEquality,
    Activity, ActivitySix, Life, SpecialMotion, Override, AnimationVector, Controller, ControllerContact,
    Direction, Turning, ReferenceFlags, DirectPosition, MenuPredicate, MenuMotion,
    MovementController, ReferenceData, ActorMovement, ParentCell, ExteriorWater, Returned, Failed
}
internal sealed record FalloutPlayerMoverDeltaViewRead(string Source, string Stack, Guid Process, Guid Constructor,
    FalloutPlayerSourceCaller Caller, long Sequence, byte View);
internal sealed record FalloutPlayerMoverDeltaFirstAnimationRead(string Source, string Stack, Guid Process,
    Guid Factory, Guid Constructor, FalloutCombatActorIdentity Actor, long Epoch, long Sequence, Guid? Context);
internal sealed record FalloutPlayerMoverDeltaMenuRead(string Source, string Stack, Guid Process, Guid Constructor,
    long Sequence, Guid? Manager, byte Enabled, byte Dialog, bool Value);
internal enum FalloutMoverDeltaProcessCell { SpecialMotion, DialogMotion }
internal sealed record FalloutMoverDeltaProcessFields(byte SpecialMotion, byte DialogMotion,
    string? SpecialBoundary = null, string? DialogBoundary = null)
{
    internal static FalloutMoverDeltaProcessFields Constructed => new(0, 0);
}
internal sealed record FalloutMoverDeltaProcessRead(FalloutProcessSceneQuery Common,
    FalloutMoverDeltaProcessCell Cell, byte Value);
internal sealed record FalloutPlayerMoverDeltaControllerRead(FalloutProcessSceneContextQuery Context,
    Guid? Controller, Guid? Factory, Guid? RuntimeProcess, long? Epoch, byte? ContactFlags, int? Contacts);
internal sealed record FalloutPlayerMoverDeltaPublication(Guid PlayerProcess, Guid Factory, Guid Constructor,
    Guid CommonProcess, FalloutCombatActorIdentity Actor, long Epoch, long Published,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPlayerMoverDeltaCall(FalloutPlayerLateFrameInvocation Invocation, Guid Factory,
    long Ordinal, long Entered, long Changed, FalloutPlayerMoverDeltaStep Step,
    FalloutPlayerMoverDeltaStep? FailedAt, FalloutActorMoverFrameStoreOperation DefaultVector,
    FalloutPlayerMoverFields Mover, IReadOnlyList<FalloutProcessSceneQuery> ProcessReads,
    FalloutPlayerMoverDeltaViewRead? View, FalloutPlayerMoverDeltaFirstAnimationRead? FirstAnimation,
    Guid? SelectedAnimation, IReadOnlyList<FalloutProcessPhysicalActivityQuery> Activities,
    FalloutPlayerFollowingLifeRead? Life, IReadOnlyList<FalloutMoverDeltaProcessRead> ProcessBytes,
    bool? Override, IReadOnlyList<FalloutReferenceFlagRead> Flags,
    IReadOnlyList<FalloutPlayerMoverDeltaControllerRead> Controllers,
    FalloutPlayerMoverDeltaMenuRead? Menu, FalloutReferenceModelDataRead? Data,
    FalloutActorMoverVector Delta, FalloutReferencePositionCall? Position,
    FalloutReferenceParentCellRead? Parent, bool? MovementReturned,
    bool Returned, string? Failure, FalloutPlayerActorMovementCall? ActorMovement = null,
    IReadOnlyList<FalloutLoadedAnimationGroupRead>? AnimationGroups = null,
    FalloutLoadedAnimationMovementRead? AnimationMovement = null);
internal sealed record FalloutPlayerMoverDeltaSnapshot(FalloutPlayerMoverDeltaSource Source, string Stack,
    Guid PlayerProcess, Guid Factory, Guid Constructor, long Sequence, long Calls,
    IReadOnlyList<FalloutPlayerMoverDeltaPublication> Publications,
    FalloutPlayerMoverDeltaCall? LastCall, string? Failure);

// These are existing concrete authorities plus leaf borrows at World/Scene
// composition. No supplied callback is allowed to certify a movement body.
internal sealed record FalloutPlayerMoverDeltaOwners(FalloutActorMoverState Mover,
    FalloutActorProcessCommonState Common, FalloutActorProcessRuntimeState Runtime,
    FalloutActorPackageCommandFields Fields, FalloutReferenceScriptExtraInitializer Flags,
    FalloutReferenceModelDataState ReferenceData, FalloutStandaloneInterfaceState Interface,
    Func<FalloutPlayerLateFrameInvocation, FalloutPlayerMoverDeltaViewRead> ReadView,
    Func<FalloutPlayerLateFrameInvocation, FalloutReferenceParentCellRead> ReadParent,
    Func<FalloutPlayerLateFrameInvocation, Func<FalloutReferencePositionInvocation, FalloutReferencePositionCall>,
        FalloutReferencePositionCall> WithPosition,
    FalloutSourceReferencePositionState Position, IFalloutReferencePositionConsumers PositionConsumers,
    FalloutPlayerActorMovementState? ActorMovement = null,
    Func<Guid, FalloutSourceAnimationContextState>? RequireAnimationContext = null,
    Func<FalloutPlayerLateFrameInvocation, FalloutReferenceActorYawModeRead>? ReadAnimationActorMode = null,
    Func<FalloutPlayerLateFrameInvocation, FalloutReferenceScaleRead>? ReadAnimationScale = null);
