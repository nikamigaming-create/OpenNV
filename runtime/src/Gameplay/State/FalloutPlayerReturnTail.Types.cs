using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutPlayerReturnTailStep
{
    DeathDelay, RawLife, DeathTimer, DeathChild, AnimationBracketEnter, AnimationClock,
    CommonAnimation, FirstAnimation, ProcessAnimationQueue, ReferenceAnimationExtra,
    AnimationBracketLeave, PrepareAnimation, FirstViewToggle, FirstStatus,
    FirstAnimationFrame, SecondViewToggle, SecondStatus, SecondAnimationFrame,
    Camera, CellAttachment, ModelOffer, CellWater, WaterStore, ThirdInhibition,
    ControlTwentySix, FirstLife, LifeOne, ActorValues, ControlFive,
    PlayerThreadExit, Returned, Failed,
}
internal enum FalloutPlayerReturnTailField { DeathTimer, AnimationBracket }
internal sealed record FalloutPlayerReturnTailStore(FalloutPlayerReturnTailField Field,
    long Changed, uint? Before, uint After, FalloutPlayerAnimationBracketStore? SharedBracket = null);
internal enum FalloutPlayerAnimationBracketChild { Updater, FirstFrame, SecondFrame }
internal sealed record FalloutPlayerAnimationBracketStore(FalloutPlayerLateFrameInvocation Frame,
    string Source, Guid SceneProcess, Guid SceneConstructor, Guid ConsumerFactory,
    FalloutPlayerAnimationBracketChild Child, bool Enter, long Changed, byte Before, byte After);
internal sealed record FalloutPlayerTailSettingRead(string Name, uint Bits, Guid RecordsFactory,
    string RecordsSource);
internal sealed record FalloutPlayerAnimationClockRead(FalloutPlayerLateFrameInvocation Frame,
    Guid SceneProcess, Guid SceneConstructor, long SceneSequence, uint Kind,
    uint NumeratorBits, uint DenominatorBits, FalloutSourceFrameDeltaRead Timer, uint DeltaBits);
internal sealed record FalloutPlayerAnimationPointerRead(FalloutPlayerLateFrameInvocation Frame,
    FalloutProcessSceneQuery? Common, FalloutPlayerMoverDeltaFirstAnimationRead? First)
{
    internal Guid? Context => Common is { } common ? common.AnimationContext : First?.Context;
}
internal sealed record FalloutPlayerReturnTailCall(FalloutPlayerLateFrameInvocation Invocation,
    Guid Factory, long Ordinal, long Entered, long Changed, FalloutPlayerReturnTailStep Step,
    FalloutPlayerReturnTailStep? FailedAt, FalloutPlayerTailSettingRead? Delay,
    FalloutActorProcessFact<int>? RawLife, IReadOnlyList<FalloutPlayerReturnTailStore> Stores,
    FalloutPlayerAnimationClockRead? Clock,
    IReadOnlyList<FalloutPlayerAnimationPointerRead> Animations,
    IReadOnlyList<FalloutSourceAnimationContextCall> ContextUpdates,
    FalloutProcessAnimationQueueCall? Queue, FalloutReferenceAnimationNodeExtraRead? Extra,
    bool Returned, string? Failure);
internal sealed record FalloutPlayerReturnTailPublication(Guid Process, Guid Factory, Guid Constructor,
    Guid CommonProcess, long Published, Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutPlayerReturnTailSnapshot(FalloutPlayerReturnTailSource Source, string Stack,
    Guid Process, Guid Factory, Guid Constructor, Guid CommonProcess, long Sequence, long Calls,
    uint DeathTimerBits, string? DeathTimerWriter,
    IReadOnlyList<FalloutPlayerReturnTailPublication> Publications,
    FalloutPlayerReturnTailCall? LastCall, string? Failure);

internal sealed record FalloutPlayerReturnTailOwners(FalloutPluginStack Records,
    FalloutActorProcessRuntimeState Runtime, FalloutActorProcessCommonState Common,
    FalloutActorPackageCommandFields Fields, FalloutReferenceScriptExtraInitializer ReferenceExtras,
    FalloutSourceFrameTimerState Timer,
    Func<FalloutPlayerLateFrameInvocation, Guid, FalloutPlayerAnimationClockRead> ReadAnimationClock,
    Func<FalloutPlayerLateFrameInvocation, Guid, FalloutPlayerAnimationBracketChild, bool,
        FalloutPlayerAnimationBracketStore> StoreAnimationBracket,
    Func<Guid, FalloutSourceAnimationContextState> RequireAnimationContext);
