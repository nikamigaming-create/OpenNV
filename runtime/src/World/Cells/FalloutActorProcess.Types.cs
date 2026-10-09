using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorProcessFact<T>(T? Value, string Owner, string? Failure = null) where T : struct
{
    internal T Require()
    {
        if (Value is null || string.IsNullOrWhiteSpace(Owner) || Failure is not null)
            throw new NotSupportedException(Failure ?? "Original actor process input is unowned: " + Owner);
        return Value.Value;
    }
}

internal enum FalloutActorProcessFactoryPhase
{ Created, RemovedOldRegistration, ConstructedNew, CopiedCommon, CopiedPerception, RetiredOld, PublishedNew, RegisteredNew, InitializedNew, Complete }
internal enum FalloutActorProcessSourceOperation { EnsureHigh, Reevaluate, SourceRetirement }
internal enum FalloutActorDetectionVisitKind
{ NullSlot, NonActor, UpdateDisabled, Missing3D, PlayerOutsideCohort, Countdown, RejectedLifeState, CategoryClockHeld, ProducerEntered, ProducerEarlyReturn, ProducerCompleted }

internal sealed record FalloutActorProcessElection(FalloutFormKey Actor, long Epoch,
    FalloutActorProcessFact<int> PlayerTransitionCount, FalloutActorProcessFact<bool> ForcedProcessing,
    FalloutActorProcessFact<bool> RegisteredProcessingTree, FalloutActorProcessFact<byte> SourceCellPhase,
    FalloutActorProcessFact<bool> CellHasExtraProcessingOwner, string Owner);

internal sealed record FalloutActorProcessConstruction(FalloutFormKey Actor,
    FalloutActorProcessFact<bool> RegistrationRequested,
    FalloutActorProcessFact<bool> AlternateRegistrationMode, string Owner, FalloutActorConstructorSourceReceipt? Source = null)
{
    internal string? Boundary()
    {
        if (string.IsNullOrWhiteSpace(Owner)) throw new InvalidDataException("Actor construction has no original caller owner.");
        if (RegistrationRequested is null || AlternateRegistrationMode is null)
            throw new InvalidDataException("Actor construction omitted its original registration inputs.");
        if (RegistrationRequested.Value is null || string.IsNullOrWhiteSpace(RegistrationRequested.Owner) || RegistrationRequested.Failure is not null)
            return RegistrationRequested.Failure ?? "original-actor-constructor-registration-request-unowned:" + RegistrationRequested.Owner;
        if (!RegistrationRequested.Value.Value) return null;
        if (AlternateRegistrationMode.Value is null || string.IsNullOrWhiteSpace(AlternateRegistrationMode.Owner) || AlternateRegistrationMode.Failure is not null)
            return AlternateRegistrationMode.Failure ?? "original-actor-constructor-registration-mode-unowned:" + AlternateRegistrationMode.Owner;
        return AlternateRegistrationMode.Value.Value ? "original-alternate-actor-registration-owner-unbound:" + AlternateRegistrationMode.Owner : null;
    }
}

internal sealed record FalloutActorProcessScheduleObservation(FalloutFormKey Actor, long Epoch,
    bool Source3D, FalloutActorProcessFact<bool> ActorUpdateEnabled, FalloutActorProcessFact<int> LifeState,
    FalloutActorProcessFact<bool> ProcessEligibility, FalloutActorProcessFact<bool> BaseEligibility,
    float[] SourcePosition, string Owner)
{
    internal void Validate(FalloutFormKey actor, long epoch)
    {
        if (Actor != actor || Epoch != epoch || string.IsNullOrWhiteSpace(Owner) ||
            SourcePosition is not { Length: 3 } || SourcePosition.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Detection schedule observation belongs to a foreign actor/process/source pose.");
    }
}

internal sealed record FalloutActorProcessProducerGuards(FalloutFormKey Actor, long Epoch,
    FalloutActorProcessFact<bool> ActorRejectsDetection, FalloutActorProcessFact<bool> DyingWithBlockingEffect,
    FalloutActorProcessFact<bool> GlobalDetectionEnabled, double OriginalMaximumPlayerDistance, string Owner);
internal sealed record FalloutActorProcessPlayerDetection(FalloutFormKey Target, int Score,
    bool VisibilityFlag, bool OtherFlag, string Owner);
internal sealed record FalloutActorProcessPlayerHostility(FalloutFormKey Actor,
    FalloutActorProcessFact<bool> Reaction, FalloutActorProcessFact<bool> ControllerTargetsPlayer,
    FalloutActorProcessFact<int> ReactionClass, FalloutActorProcessFact<bool> BaseFallback,
    FalloutActorProcessFact<bool> BlockingEffect, string Owner);

// Producer receipts are created by the actual C# source producer after its own
// operation. A native callback cannot assert that a cache or timer completed.
internal sealed record FalloutActorProcessProducerReceipt(FalloutFormKey Actor, long Epoch,
    string OriginalEarlyReturn, int CompletedPairs, bool ReachedFinalTimerWrite, float? Timer, long Revision);
internal sealed record FalloutActorProcessVisit(int Slot, FalloutFormKey? Actor, long Epoch,
    FalloutActorDetectionVisitKind Kind, float? TimerBefore, float? TimerAfter, uint? SharedRandomWord,
    float? RandomInterval, int CompletedPairs, string Owner);
internal sealed record FalloutActorProcessScheduleReceipt(long Sequence, float Seconds, float Delta,
    int HighStart, int HighEndAtEntry, float RandomWindow, int NextSlot,
    IReadOnlyList<FalloutActorProcessVisit> Visits, int CompletedCommits, bool Complete, string? Failure,
    IReadOnlyList<FalloutFormKey?> HighSlotsAtEntry, long CohortRevision, FalloutActorProcessVisit? InFlight);
internal sealed record FalloutActorProcessFault(long Sequence, FalloutFormKey? Actor, string Owner, string Error);
internal sealed record FalloutActorProcessRegistration(FalloutCombatActorIdentity Source, long Epoch,
    FalloutDetectionProcessLevel? Level, bool Registered, bool Retired, string? Boundary,
    float? DetectionTimer, uint? DetectionGeneration, bool? DetectionUpdated, string? LastTimerOwner,
    FalloutActorProcessConstruction? Construction);
internal sealed record FalloutActorProcessColdHandoff(Guid PreviousProcess, Guid CurrentProcess, long Sequence,
    string Owner);
internal sealed record FalloutActorProcessFactorySnapshot(FalloutFormKey Actor, long BeforeEpoch,
    long NewEpoch, FalloutDetectionProcessLevel Before, FalloutDetectionProcessLevel After,
    FalloutActorProcessSourceOperation Operation, string Owner, FalloutActorProcessFactoryPhase Phase,
    FalloutPerceptionActorSnapshot BeforePerception, FalloutDetectionCacheSnapshot? NewCache,
    FalloutDetectionLightSnapshot? NewLight, string? Failure);
internal sealed record FalloutActorProcessesSnapshot(string Schema, string Contract, string Stack,
    FalloutFormKey Player, Guid CapturedProcess, float Seconds, long Sequence,
    FalloutActorProcessCohortSnapshot Cohort, IReadOnlyList<FalloutActorProcessRegistration> Actors,
    FalloutActorProcessFactorySnapshot? Factory, FalloutActorProcessScheduleReceipt? Schedule,
    IReadOnlyList<FalloutActorProcessFault> Faults, FalloutActorProcessColdHandoff? ColdHandoff,
    bool? PlayerScheduleFlag);

internal sealed class FalloutActorProcessInputs(
    Func<FalloutFormKey, long, FalloutActorProcessElection> election,
    Func<FalloutFormKey, long, FalloutActorProcessScheduleObservation> observe,
    Func<FalloutFormKey, long, FalloutActorProcessProducerGuards> producerGuards,
    Func<float> categoryOneClock, Func<uint> sharedRandomWord,
    Func<FalloutFormKey, FalloutActorProcessPlayerDetection> playerDetection,
    Func<FalloutFormKey, FalloutActorProcessPlayerHostility> playerHostility,
    Action<FalloutFormKey> removePlayerTarget, Action<FalloutFormKey, bool, bool> addPlayerTarget,
    Action<FalloutFormKey> registerShadowCandidate,
    Action endFallout3PlayerDetection, Func<string, float> setting,
    Action<FalloutActorProcessFactorySnapshot> copyCommon,
    Action<FalloutActorProcessFactorySnapshot> retireOld,
    Action<FalloutActorProcessFactorySnapshot> initializeNew,
    Func<FalloutFormKey, FalloutActorProcessConstruction>? construction = null)
{
    internal FalloutActorProcessConstruction Construction(FalloutFormKey actor) => construction?.Invoke(actor) ??
        new(actor, new(null, "original-actor-constructor-requesting-caller-unbound"),
            new(null, "original-actor-constructor-registration-mode-unbound"), "actual-actor-constructor-registration-inputs");
    internal FalloutActorProcessElection Election(FalloutFormKey actor, long epoch) => election(actor, epoch);
    internal FalloutActorProcessScheduleObservation Observe(FalloutFormKey actor, long epoch) => observe(actor, epoch);
    internal FalloutActorProcessProducerGuards Guards(FalloutFormKey actor, long epoch) => producerGuards(actor, epoch);
    internal float CategoryOneClock() => categoryOneClock();
    internal uint SharedWord() => sharedRandomWord();
    internal FalloutActorProcessPlayerDetection PlayerDetection(FalloutFormKey actor) => playerDetection(actor);
    internal FalloutActorProcessPlayerHostility Hostility(FalloutFormKey actor) => playerHostility(actor);
    internal void RemovePlayerTarget(FalloutFormKey actor) => removePlayerTarget(actor);
    internal void AddPlayerTarget(FalloutFormKey actor, bool hostile, bool hidden) => addPlayerTarget(actor, hostile, hidden);
    internal void Shadow(FalloutFormKey actor) => registerShadowCandidate(actor);
    internal void EndFallout3Player() => endFallout3PlayerDetection();
    internal float Setting(string name)
    {
        var value = setting(name);
        return float.IsFinite(value) ? value : throw new InvalidDataException("Actor process setting is non-finite: " + name);
    }
    internal void CopyCommon(FalloutActorProcessFactorySnapshot state) => copyCommon(state);
    internal void RetireOld(FalloutActorProcessFactorySnapshot state) => retireOld(state);
    internal void InitializeNew(FalloutActorProcessFactorySnapshot state) => initializeNew(state);
}
