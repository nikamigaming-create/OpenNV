using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorProcessManager? _actorProcesses;
    private FalloutActorProcessDeclaration? _actorProcessDeclaration;
    private string? _actorProcessStack;
    private FalloutActorProcessesSnapshot? _actorProcessRestore;
    private object? _actorProcessInputLease;
    private FalloutActorProcessInputs? _actorProcessInputs;
    internal bool ActorProcessesConfigured => _actorProcesses is not null;
    internal FalloutActorProcessManager ActorProcesses => _actorProcesses ??
        throw new NotSupportedException("Actual original actor process factory/cohort/schedule owner is absent.");
    internal object? ActorProcessState => _actorProcesses?.State;
    internal string? ActorProcessSaveBlocker => _actorProcesses is null ? "source-actor-process-manager-absent" : ActorProcesses.SaveBlocker;

    internal void ConfigureActorProcesses(FalloutActorProcessDeclaration declaration, string stack,
        FalloutActorProcessesSnapshot? restore = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_actorProcesses is not null) throw new InvalidOperationException("Actor process manager is already configured.");
        if (_perceptionDeclaration is null || _perceptionDeclaration.ExecutableSha256 != declaration.ExecutableSha256 || _perceptionStack != stack)
            throw new InvalidDataException("Actual process manager differs from the source perception lifetime.");
        if (!ActualProcessRuntimeConfigured) throw new NotSupportedException("Actual source runtime/common process providers are absent.");
        // Constructor order belongs to the actual retained reference factory.
        // Do not instantiate every source row or invent a sorted actor cohort.
        var candidate = new FalloutActorProcessManager(declaration, stack, _enginePlayer, ActorPerception,
            ReadCombatActorIdentity, SelectedPerceptionActor, new(ReadProcessElection, ReadProcessObservation, ReadProcessGuards,
                ReadProcessCategoryOneClock, ReadProcessSharedRandom, ReadProcessPlayerDetection, ReadProcessPlayerHostility,
                RemoveProcessPlayerTarget, AddProcessPlayerTarget, RegisterProcessShadowCandidate, EndProcessFallout3Player,
                name => FalloutGameSettingFloats.Read(records, name), CopyProcessCommon, RetireProcessOld, InitializeProcessNew,
                ReadProcessConstruction), restore);
        try
        {
            PrepareActualActorConstructorSource(candidate, declaration, stack, restore);
            if (restore is null)
                foreach (var actor in ActorPerception.ConstructedSourceActors)
                    if (!candidate.HasActor(actor)) candidate.Construct(actor);
            ProcessCommon.RequireActors(candidate.Capture().Actors);
        }
        catch (Exception error)
        {
            RetireFailedActorConstructorSetup(candidate, declaration, stack, restore, error);
            throw;
        }
        _actorProcesses = candidate; _actorProcessDeclaration = declaration; _actorProcessStack = stack; _actorProcessRestore = restore;
    }
    internal void RequireActorProcessBinding(FalloutActorProcessDeclaration declaration, string stack, FalloutActorProcessesSnapshot? restore)
    {
        if (_actorProcesses is null || _actorProcessDeclaration != declaration || _actorProcessStack != stack || !ReferenceEquals(_actorProcessRestore, restore))
            throw new InvalidDataException("Attached actor process manager differs from its selected source/cold owner.");
    }
    private void BindSourceActorProcessInstance(FalloutReferenceInstance instance)
    {
        if (_actorProcesses is not null && SelectedPerceptionActor(instance.Reference) && !ActorProcesses.HasActor(instance.Reference))
            ActorProcesses.Construct(instance.Reference);
    }
    internal IDisposable BindActualActorProcessSources(FalloutActorProcessInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (_actorProcessInputLease is not null) throw new InvalidOperationException("Source process producers already have a live input lease.");
        var lease = new object(); _actorProcessInputLease = lease; _actorProcessInputs = inputs;
        return new ActorProcessInputLease(this, lease);
    }
    private sealed class ActorProcessInputLease(FalloutReferenceWorld world, object lease) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (!ReferenceEquals(world._actorProcessInputLease, lease)) return;
            world._actorProcessInputLease = null; world._actorProcessInputs = null;
        }
    }
    private FalloutActorProcessScheduleObservation ReadProcessObservation(FalloutFormKey actor, long epoch)
    {
        if (_actorProcessInputs is not null) return JoinActualNeutralActorLife(JoinCurrentActorUpdate(_actorProcessInputs.Observe(actor, epoch)));
        var actual = ReadPerceptionObservation(actor);
        return JoinActualNeutralActorLife(JoinCurrentActorUpdate(new(actor, epoch, actual.HasSource3D, new(null, "original-current-actor-update-enabled-byte-producer-absent"),
            new(null, "original-current-actor-life-state-producer-absent"), new(null, "original-high-process-eligibility-producer-absent"),
            new(null, "original-actor-base-eligibility-producer-absent"), actual.SourcePosition.ToArray(), "actual-source-native-or-unloaded-actor-placement")));
    }
    private FalloutActorProcessElection ReadProcessElection(FalloutFormKey actor, long epoch) =>
        JoinActualProcessRuntime(JoinCurrentCellProcess(_actorProcessInputs?.Election(actor, epoch) ?? new(actor, epoch,
            new(null, "original-current-player-transition-counter-producer-absent"),
            new(null, "original-main-forced-processing-bit-producer-absent"),
            new(null, "original-processing-tree-membership-producer-absent"),
            new(null, "original-current-cell-load-phase-producer-absent"),
            new(null, "original-cell-extra-data-nine-producer-absent"), "original-current-tier-factory-inputs")));
    private FalloutActorProcessConstruction ReadProcessConstruction(FalloutFormKey actor) =>
        ReadActualSourceActorConstruction(actor);
    private FalloutActorProcessProducerGuards ReadProcessGuards(FalloutFormKey actor, long epoch) =>
        _actorProcessInputs?.Guards(actor, epoch) ?? new(actor, epoch,
            new(null, "original-actor-detection-reject-query-producer-absent"),
            new(null, "original-life-state-four-and-effect-query-producer-absent"),
            new(null, "original-global-detection-enabled-producer-absent"),
            _actorProcessDeclaration!.MaximumPlayerProducerDistance, "actual-selected-high-producer-guards");
    private float ReadProcessCategoryOneClock() => _actorProcessInputs?.CategoryOneClock() ??
        throw new NotSupportedException("Original category-one time manager observation is absent; world pause is a distinct owner.");
    private uint ReadProcessSharedRandom() => _actorProcessInputs?.SharedWord() ??
        throw new NotSupportedException("Actual original shared random stream/seed/call-order owner is absent.");
    private FalloutActorProcessPlayerDetection ReadProcessPlayerDetection(FalloutFormKey actor) =>
        _actorProcessInputs?.PlayerDetection(actor) ?? throw new NotSupportedException("Actual original Player mode-one query/refinement flags are absent.");
    private FalloutActorProcessPlayerHostility ReadProcessPlayerHostility(FalloutFormKey actor)
    {
        if (_actorProcessInputs is not null) return _actorProcessInputs.Hostility(actor);
        var reaction = ReadPerceptionReaction(actor, _enginePlayer);
        return new(actor, new(reaction.Allowed, reaction.Owner), new(null, "actual-controller-directed-player-target-producer-absent"),
            new(null, "actual-original-reaction-class-output-producer-absent"), new(null, "original-base-reaction-fallback-producer-absent"),
            new(null, "original-blocking-effect-six-query-producer-absent"), "actual-shared-current-faction-ai-and-perk-reaction");
    }
    private void RemoveProcessPlayerTarget(FalloutFormKey actor) =>
        (_actorProcessInputs ?? throw new NotSupportedException("Original Player detected-candidate list is absent; CombatManager lists are distinct.")).RemovePlayerTarget(actor);
    private void AddProcessPlayerTarget(FalloutFormKey actor, bool hostile, bool hidden) =>
        (_actorProcessInputs ?? throw new NotSupportedException("Original Player detected-candidate list is absent; CombatManager lists are distinct.")).AddPlayerTarget(actor, hostile, hidden);
    private void RegisterProcessShadowCandidate(FalloutFormKey actor) =>
        (_actorProcessInputs ?? throw new NotSupportedException("Original distance-selected shadow candidate membership/ordering is absent.")).Shadow(actor);
    private void EndProcessFallout3Player() =>
        (_actorProcessInputs ?? throw new NotSupportedException("Original Fallout3 Player detection final consumer is unowned.")).EndFallout3Player();
    private void CopyProcessCommon(FalloutActorProcessFactorySnapshot factory) =>
        ProcessCommon.Copy(factory);
    private void RetireProcessOld(FalloutActorProcessFactorySnapshot factory) =>
        ProcessCommon.RetireOld(factory);
    private void InitializeProcessNew(FalloutActorProcessFactorySnapshot factory) =>
        ProcessCommon.InitializeNew(factory);

    internal void AdvanceActualActorProcessSchedule(float seconds)
    {
        foreach (var actor in ActorPerception.ConstructedSourceActors)
            if (!ActorProcesses.HasActor(actor)) ActorProcesses.Construct(actor);
        ActorProcesses.AdvanceSourceDetection(seconds);
        // Completion above is scoped to this actual scheduler component. It
        // does not certify affecting-light, pair refinement or scene readiness.
    }
    internal void EnterActualHighActorFactory(FalloutFormKey actor, string originalOwner)
    {
        EnsurePerceptionActor(actor);
        if (!ActorProcesses.HasActor(actor)) ActorProcesses.Construct(actor);
        ActorProcesses.EnsureHigh(actor, ActorProcesses.Read(actor).Epoch, originalOwner);
    }
    // Native body publication supplies only 3D. It also invalidates any
    // assumption that a constructed Low process certifies the loaded tier.
    // Its actual original factory/election inputs must be independently bound.
    private void ObserveActorProcessSource3D(FalloutFormKey actor)
    {
        if (_actorProcesses is null || actor == _enginePlayer) return;
        if (!ActorProcesses.HasActor(actor)) ActorProcesses.Construct(actor);
        var state = ActorProcesses.Read(actor);
        try
        {
            var election = ReadProcessElection(actor, state.Epoch);
            var selected = ActorProcesses.Reevaluate(election);
            if (selected == state.Level) ActorProcesses.AdmitCurrentEpoch(election);
            else if (selected == FalloutDetectionProcessLevel.High) ActorProcesses.EnsureHigh(actor, state.Epoch, election.Owner);
            else Hold("original-current-non-high-process-replacement-copy-retirement-and-initialization-owner-unbound");
        }
        catch (Exception error)
        {
            // The native body publication already happened. Retain its real
            // lease and the source factory's consumed prefix; do not roll it
            // back merely because an independent tier producer is unowned.
            Hold("actual-source-3d-process-admission-failed:" +
                (string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message));
        }
        void Hold(string boundary)
        {
            ActorProcesses.RetainBoundary(actor, boundary);
            ActorPerception.RequestProcess(actor, ActorProcesses.Read(actor).Epoch, null, boundary);
        }
    }
    internal FalloutActorProcessesSnapshot CaptureActorProcesses() => ActorProcesses.Capture();
    private FalloutDetectionProcessLevel? ReadAdmittedSourceActorProcessLevel(FalloutFormKey actor)
    {
        if (!SelectedPerceptionActor(actor)) return null;
        EnsurePerceptionActor(actor);
        if (!ActorProcesses.HasActor(actor)) ActorProcesses.Construct(actor);
        var state = ActorProcesses.Read(actor);
        if (state.Boundary is { } boundary) throw new NotSupportedException(boundary);
        return state.Level;
    }
    private void RetireActorProcessGraph()
    {
        // The old/new factory transaction consumes perception as a provider.
        // A refused consumer retirement must retain that still-owned provider.
        RetireActorProcesses();
        RetireActualProcessRuntime();
        RetireActorPerception();
    }
    private void RetireActorProcesses()
    {
        // Failure retains the still-owned old/new factory graph. Other world
        // owners retire independently through the common aggregate teardown.
        _actorProcesses?.Dispose(); _actorProcesses = null;
        RetireActualActorConstructorSource();
        _actorProcessDeclaration = null; _actorProcessStack = null; _actorProcessRestore = null;
        _actorProcessInputs = null; _actorProcessInputLease = null;
    }
}
