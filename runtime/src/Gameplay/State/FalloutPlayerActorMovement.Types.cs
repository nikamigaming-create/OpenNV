using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerActorMovementStep
{
    Controller, FormKind, RawPosition, Angles, AngleReset, SkipFrames, DisabledWord, Status,
    Camera, Position, RawLife, FirstLife, LifeOne, Activity, WarpDistance,
    DisabledFlag, SecondLife, Pose, TimedFlags, MoverWord, ControlledChild, DeltaChild,
    ControllerFlags, ControllerPosition, CorrectionPosition, ActorPosition, TesCell, Ground,
    Attachments, BasePosition, FallFlags, Returned, Failed
}
internal sealed record FalloutPlayerActorMovementInvocation(Guid Call, FalloutPlayerLateFrameInvocation Frame,
    FalloutActorMoverVector Delta, uint MoverWord, FalloutReferenceModelDataRead Data);
internal sealed record FalloutReferenceFormKindRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, byte Kind);
internal sealed record FalloutReferenceRawPositionRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, FalloutPackageRawPosition Position);
internal sealed record FalloutPlayerFrameAnglesRead(string Source, string Stack, Guid Process, Guid Factory,
    FalloutPlayerReferenceOrientationBinding Binding, long Sequence, FalloutPhysicalVector3 Angles);
internal sealed record FalloutPlayerFrameDisabledRead(string Source, string Stack, Guid Process, Guid Factory,
    long Sequence, ushort Word);
internal sealed record FalloutPlayerFrameCameraRead(string Source, string Stack, Guid World, Guid RootFactory,
    long Sequence, long Updates, FalloutPhysicalVector3 Translation);
internal sealed record FalloutPlayerActorMovementLifeOneRead(FalloutActorProcessFact<int> Raw, bool Value);
internal sealed record FalloutPlayerFrameTesCellRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, long Sequence, FalloutFormKey? Cell);
internal sealed record FalloutPlayerActorMovementCall(FalloutPlayerActorMovementInvocation Invocation,
    Guid Factory, long Ordinal, long Entered, long Changed, FalloutPlayerActorMovementStep Step,
    FalloutPlayerActorMovementStep? FailedAt, FalloutProcessSceneContextQuery? Context,
    FalloutReferenceFormKindRead? FormKind, FalloutReferenceRawPositionRead? RawPosition,
    FalloutPlayerFrameAnglesRead? Angles, FalloutPlayerFrameAnglesRead? ResetAngles,
    FalloutIniValue? SkipFrames, FalloutPlayerFrameDisabledRead? Disabled,
    FalloutReferenceActorYawModeRead? Status, FalloutPlayerFrameCameraRead? Camera,
    IReadOnlyList<FalloutReferencePositionRead> Positions, FalloutActorProcessFact<int>? RawLife,
    IReadOnlyList<FalloutPlayerFollowingLifeRead> Lives, FalloutPlayerActorMovementLifeOneRead? LifeOne,
    FalloutProcessPhysicalActivityQuery? Activity,
    FalloutIniValue? WarpDistance, uint? CameraDistanceSquared, FalloutControllerMovementFlagStore? DisabledFlag,
    FalloutControllerControlledMovementCall? Controlled, FalloutControllerMoverDeltaCall? DeltaChild,
    IReadOnlyList<FalloutControllerMovementFlagsRead> ControllerFlags,
    FalloutControllerReferencePositionRead? ControllerPosition, FalloutReferencePositionCall? ActorPosition,
    FalloutPlayerFrameTesCellRead? TesCell, FalloutControllerMovementAttachmentsRead? Attachments,
    FalloutReferencePositionCall? BasePosition, FalloutPackageRawPosition? FinalPosition,
    bool Returned, string? Failure, uint? FreshMoverWord = null);
internal sealed record FalloutPlayerActorMovementPublication(Guid PlayerProcess, Guid Factory, Guid Constructor,
    Guid CommonProcess, FalloutCombatActorIdentity Actor, long Epoch, long Published,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPlayerActorMovementSnapshot(FalloutPlayerActorMovementSource Source, string Stack,
    Guid PlayerProcess, Guid Factory, Guid Constructor, long Sequence, long Calls,
    IReadOnlyList<FalloutPlayerActorMovementPublication> Publications,
    FalloutPlayerActorMovementCall? LastCall, string? Failure);

// Every callback below is one typed leaf at the existing World/reference scope.
// The movement body, branches and controller mutation remain this C# owner.
internal sealed record FalloutPlayerActorMovementOwners(FalloutPluginStack Records, FalloutActorProcessCommonState Common,
    FalloutActorProcessRuntimeState Runtime, FalloutActorMoverState Mover, FalloutSourceReferencePositionState Position,
    FalloutReferenceModelDataState Data, FalloutPlayerReferenceOrientationOwner Orientation,
    FalloutPlayerControlFieldsState Controls, FalloutWorldProjectionPublication Projection,
    FalloutSourceInitialPlacementCells Cells, FalloutPlayerMoverSpeedCacheState Speed,
    FalloutNumericIniSettings Settings,
    Func<FalloutPlayerLateFrameInvocation, FalloutReferenceFormKindRead> ReadFormKind,
    Func<FalloutPlayerLateFrameInvocation, FalloutReferenceRawPositionRead> ReadRawPosition,
    Func<FalloutPlayerLateFrameInvocation, FalloutReferencePositionRead> ReadPosition,
    Func<FalloutPlayerLateFrameInvocation, FalloutReferenceActorYawModeRead> ReadStatus,
    Func<FalloutPlayerLateFrameInvocation, FalloutPlayerFrameTesCellRead> ReadTesCell,
    Func<FalloutPlayerLateFrameInvocation, FalloutPackageRawPosition, FalloutReferencePositionCall> StoreActorPosition,
    Func<FalloutPlayerLateFrameInvocation, FalloutPackageRawPosition, FalloutReferencePositionCall> StoreBasePosition,
    IFalloutReferenceOrientationChangeConsumer RotationChanges);
