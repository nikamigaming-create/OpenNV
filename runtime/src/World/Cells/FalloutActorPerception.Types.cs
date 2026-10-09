using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutPerceptionFramePhase { Idle, Computing, Committing, NotifyingController, UpdatingLight }
internal enum FalloutPerceptionProcessOperation { FreshConstruction, SourceFactoryRequest, SourceRetirement, UnownedSourceTransition }

internal sealed record FalloutPerceptionNativeObservation(FalloutFormKey Actor, string Owner,
    long NativeLease, FalloutFormKey Cell, float[] SourcePosition, float SourceFacing, bool HasSource3D,
    bool RawInCombat, bool CommandInCombat, bool Sneaking, bool Running, bool Moving,
    bool Dead, string? Failure = null)
{
    internal void Validate()
    {
        if (NativeLease < 0 || HasSource3D && NativeLease == 0 || string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId == 0 ||
            SourcePosition is not { Length: 3 } || SourcePosition.Any(value => !float.IsFinite(value)) || !float.IsFinite(SourceFacing))
            throw new InvalidDataException("Perception observation has no actual actor/cell/source pose owner.");
    }
    internal FalloutPerceptionNativeObservation Copy() => this with { SourcePosition = SourcePosition.ToArray() };
}

// A filtered source query is a separate receipt. A single Godot mask ray does
// not construct this receipt or supply unknown collision groups/part tables.
internal sealed record FalloutPerceptionVisibility(bool? LineOfSight, bool? Cone,
    string Owner, string? Failure = null)
{
    internal void Require()
    {
        if (LineOfSight is null || Cone is null || string.IsNullOrWhiteSpace(Owner) || Failure is not null)
            throw new NotSupportedException(Failure ?? "Detection has no complete source-filtered visibility/cone producer: " + Owner);
    }
}

internal sealed record FalloutPerceptionPairInput(FalloutFormKey Receiver, FalloutFormKey Target,
    long ReceiverProcessEpoch, long TargetProcessEpoch, FalloutDetectionScoreInputs? Scalar,
    FalloutPerceptionVisibility Visibility, string Owner, string? Failure = null);
internal sealed record FalloutPerceptionControllerNotification(bool? Present, bool? DetectionUpdatedApplied, string Owner);

internal sealed record FalloutPerceptionProcessReceipt(FalloutFormKey Actor, long Epoch,
    FalloutPerceptionProcessOperation Operation, FalloutDetectionProcessLevel? Before,
    FalloutDetectionProcessLevel? After, string Owner, long Revision);
internal sealed record FalloutPerceptionFailure(long Revision, FalloutFormKey? Actor, FalloutFormKey? Target,
    string Owner, string Error);
internal sealed record FalloutPerceptionComputingPair(FalloutFormKey Receiver, FalloutFormKey Target,
    long ReceiverProcessEpoch, long TargetProcessEpoch, bool CallerStagesNonplayer);
internal sealed record FalloutPerceptionPairReceipt(FalloutFormKey Receiver, FalloutFormKey Target,
    long ReceiverProcessEpoch, long TargetProcessEpoch, float Seconds, FalloutDetectionScoreResult Result,
    string Owner, long Revision);
internal sealed record FalloutPerceptionActorSnapshot(FalloutCombatActorIdentity Source, long ProcessEpoch,
    FalloutDetectionProcessPresence? Process, string? ProcessBoundary, bool Source3D,
    FalloutDetectionCacheSnapshot? Cache, FalloutDetectionLightSnapshot? Light,
    FalloutDetectionActionSoundSnapshot ActionSound, long NativePublicationRevision,
    FalloutPerceptionNativeObservation? LastObservation, bool Retired);
internal sealed record FalloutActorPerceptionSnapshot(string Schema, string Contract, string StackIdentity,
    FalloutFormKey Player, float SimulationSeconds, long Revision, long CompletedCacheCommits,
    FalloutPerceptionFramePhase Phase, FalloutPerceptionComputingPair? ComputingPair,
    FalloutPerceptionSourceFrameReceipt? LastSourceFrame, IReadOnlyList<FalloutFormKey> HighCohort,
    IReadOnlyList<FalloutFormKey> FrameReceivers, int FrameCursor, int PairCursor,
    IReadOnlyList<FalloutPerceptionActorSnapshot> Actors,
    IReadOnlyList<FalloutPerceptionProcessReceipt> ProcessHistory,
    IReadOnlyList<FalloutPerceptionPairReceipt> Pairs, IReadOnlyList<FalloutPerceptionFailure> Failures);

internal sealed class FalloutPerceptionInputs(
    Func<FalloutFormKey, FalloutPerceptionNativeObservation> observe,
    Func<FalloutFormKey, FalloutFormKey, long, long, FalloutPerceptionPairInput> pair,
    Func<FalloutFormKey, FalloutPerceptionControllerNotification> notifyController,
    Func<FalloutFormKey, FalloutDetectionLightSample> light,
    Func<FalloutDetectionScoreSettings> settings,
    Func<float> actionTimer)
{
    internal FalloutPerceptionNativeObservation Observe(FalloutFormKey actor) => observe(actor);
    internal FalloutPerceptionPairInput Pair(FalloutFormKey from, FalloutFormKey to, long fromEpoch, long toEpoch) => pair(from, to, fromEpoch, toEpoch);
    internal FalloutPerceptionControllerNotification Notify(FalloutFormKey actor) => notifyController(actor);
    internal FalloutDetectionLightSample Light(FalloutFormKey actor) => light(actor);
    internal FalloutDetectionScoreSettings Settings() => settings();
    internal float ActionTimer() => actionTimer();
}
