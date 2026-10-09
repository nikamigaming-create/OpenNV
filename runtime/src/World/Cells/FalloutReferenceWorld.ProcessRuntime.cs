using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutActorProcessRuntimeState? _processRuntime;
    private FalloutActorProcessCommonState? _processCommon;
    private FalloutActorProcessRuntimeDeclaration? _processRuntimeDeclaration;
    private string? _processRuntimeStack;
    private FalloutActorProcessRuntimeSnapshot? _processRuntimeRestore;
    private FalloutProcessCommonSnapshot? _processCommonRestore;
    private sealed record ProcessBodyOwner(object Lease, FalloutActorProcessBodyBinding Source, Func<bool> Living);
    private readonly Dictionary<FalloutFormKey, ProcessBodyOwner> _processBodies = new(FalloutFormKeyComparer.Instance);
    internal bool ActualProcessRuntimeConfigured => _processRuntime is not null && _processCommon is not null;
    internal object? ActualProcessRuntimeState => _processRuntime?.State;
    internal object? ActualProcessCommonState => _processCommon?.State;
    internal string? ActualProcessRuntimeSaveBlocker => _processRuntime?.SaveBlocker ??
        (_processRuntime is null ? "actual-Main-Player-Actor-process-runtime-owner-absent" : null);
    internal string? ActualProcessCommonSaveBlocker => _processCommon?.SaveBlocker ??
        (_processCommon is null ? "actual-actor-common-process-owner-absent" : null);
    private FalloutActorProcessRuntimeState ProcessRuntime => _processRuntime ??
        throw new NotSupportedException("Actual source Main/Player/Actor runtime process inputs are absent.");
    private FalloutActorProcessCommonState ProcessCommon => _processCommon ??
        throw new NotSupportedException("Actual source common process transfer owner is absent.");

    internal void ConfigureActualProcessRuntime(FalloutActorProcessRuntimeDeclaration declaration, string stack,
        FalloutActorProcessRuntimeSnapshot? runtime = null, FalloutProcessCommonSnapshot? common = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); declaration.Validate();
        if (ActualProcessRuntimeConfigured)
        {
            RequireActualProcessRuntimeBinding(declaration, stack, runtime, common); return;
        }
        if (_processRuntime is not null || _processCommon is not null || (runtime is null) != (common is null) ||
            _perceptionDeclaration?.ExecutableSha256 != declaration.ExecutableSha256 || _perceptionStack != stack ||
            _combatGroupDeclaration?.ExecutableSha256 != declaration.ExecutableSha256 || _combatGroupStackIdentity != stack)
            throw new InvalidDataException("Actual runtime/common process inputs lost their complete source/cold/reference factory.");
        var inputs = new FalloutActorProcessRuntimeState(declaration, stack, _enginePlayer, ReadCombatActorIdentity, runtime);
        FalloutActorProcessCommonState? fields = null;
        try
        {
            fields = new(declaration, stack, ReadCombatActorIdentity, ReadActualProcessGameplay, ReadActualProcessBody, common);
            fields.BindSource3DInitializer(ReadActualSourceProcess3D);
            var actors = ActorPerception.ConstructedSourceActors;
            if (runtime is null)
                foreach (var actor in actors)
                {
                    inputs.Construct(actor);
                    fields.Construct(actor, ActorPerception.ProcessEpoch(actor), ActorPerception.Process(actor)?.Level ??
                        throw new InvalidDataException("Actual source common constructor has no current process class."));
                }
            else inputs.RequireActors(actors);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try { fields?.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { inputs.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 1)
            {
                _processRuntime = inputs; _processCommon = fields; _processRuntimeDeclaration = declaration;
                _processRuntimeStack = stack; _processRuntimeRestore = runtime; _processCommonRestore = common;
                throw new AggregateException("Process runtime setup retains its original failure and still-owned child retirement.", failures);
            }
            throw;
        }
        _processRuntime = inputs; _processCommon = fields; _processRuntimeDeclaration = declaration;
        _processRuntimeStack = stack; _processRuntimeRestore = runtime; _processCommonRestore = common;
    }
    internal void RequireActualProcessRuntimeBinding(FalloutActorProcessRuntimeDeclaration declaration, string stack,
        FalloutActorProcessRuntimeSnapshot? runtime, FalloutProcessCommonSnapshot? common)
    {
        if (!ActualProcessRuntimeConfigured || _processRuntimeDeclaration != declaration || _processRuntimeStack != stack ||
            !ReferenceEquals(_processRuntimeRestore, runtime) || !ReferenceEquals(_processCommonRestore, common))
            throw new InvalidDataException("Attached actual process runtime differs from the selected current/cold source owner.");
    }
    private void ConstructActualProcessRuntimeActor(FalloutReferenceInstance instance)
    {
        if (_processRuntime is null || records.GetEffective(instance.Reference).Signature is not ("ACHR" or "ACRE")) return;
        ProcessRuntime.Construct(instance.Reference);
        if (_processCommon is not null && !_processCommon.HasActor(instance.Reference))
        {
            var tier = _actorProcesses?.HasActor(instance.Reference) == true ? ActorProcesses.Read(instance.Reference) : null;
            ProcessCommon.Construct(instance.Reference, tier?.Epoch ?? 1, tier?.Level ?? FalloutDetectionProcessLevel.Low);
        }
    }
    private FalloutActorProcessElection JoinActualProcessRuntime(FalloutActorProcessElection value) =>
        value with { PlayerTransitionCount = ProcessRuntime.PlayerTravelCounter, ForcedProcessing = ProcessRuntime.MainForcedProcessing };
    private FalloutActorProcessScheduleObservation JoinActualNeutralActorLife(FalloutActorProcessScheduleObservation value)
    {
        if (value.Actor != _enginePlayer)
        {
            var actor = Actor(value.Actor);
            if (actor.Injury?.Dead == true || actor.Unconscious || actor.KnockedDown || actor.HitReaction is not null || actor.Ragdoll is not null)
                ProcessRuntime.RetainUnownedLifeTransition(value.Actor, "actual-current-physical-life-transition-has-no-original-neutral-code-producer");
        }
        return value with { LifeState = ProcessRuntime.Life(value.Actor) };
    }
    private FalloutProcessGameplayState ReadActualProcessGameplay(FalloutFormKey reference)
    {
        if (reference == _enginePlayer)
            throw new NotSupportedException("Player package and value pools are distinct driver owners; Player never enters the reached Low-to-High common copy.");
        var actor = Actor(reference);
        // The real procedure owner captures its current package state. Other
        // fields remain on this exact retained actor. No save/audio consumer,
        // package election or event execution is entered by a process copy.
        return new(reference, actor.CapturePackageAssignment?.Invoke() ?? actor.PackageAssignment,
            actor.PackageMotion, actor.ScriptPackage, actor.PendingPackageChoice, actor.DeferredPackageContinuation,
            actor.CaptureFurniture?.Invoke() ?? actor.FurnitureContinuation,
            actor.CaptureDialogue?.Invoke() ?? actor.DialogueContinuation,
            actor.SelectionFailure, actor.PackageBindingFailure, actor.PendingPackageSelection, actor.ProcedureCaptureBlocker,
            new Dictionary<string, OpenNV.Runtime.World.Actors.FalloutActorValue>(actor.ActorValues));
    }
    internal IDisposable BindActualNativeProcessBody(FalloutFormKey actor, string skeletonPath, FalloutNifFile skeleton,
        FalloutBodyPartData parts, string owner, Func<bool> living)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(living);
        if (!ActualProcessRuntimeConfigured) throw new NotSupportedException("Actor body has no actual source runtime/common process owner.");
        EnsurePerceptionActor(actor);
        var record = records.GetEffective(parts.Form);
        if (record.Signature != "BPTD" || !parts.Parts.SequenceEqual(FalloutBodyPartData.Read(record).Parts))
            throw new InvalidDataException("Actual process body differs from its exact winning BPTD fields.");
        var source = records.OwnedSource ?? throw new NotSupportedException("Actual source process body has no selected original resource owner.");
        if (source.StackId != _processRuntimeStack || !source.TryRead(skeletonPath, null, out var bytes, out _) ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(skeleton.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Actual process body NIF differs from its winning source resource.");
        var body = FalloutActorProcessBodySource.Read(_processRuntimeDeclaration!, actor, skeletonPath, skeleton, parts,
            Convert.ToHexString(SHA256.HashData(record.ReadData())).ToLowerInvariant(), owner);
        FalloutActorProcessCommonState.RequireBody(body, actor);
        _processBodies.TryGetValue(actor, out var previous);
        if (previous is not null && !FalloutActorProcessCommonState.BodyEquivalent(previous.Source, body))
            throw new NotSupportedException("Actual actor body replacement has no admitted source BPTD/LOD rebind transaction.");
        if (!living()) throw new NotSupportedException("Actor process source body is not actually published.");
        var lease = new object(); var candidate = new ProcessBodyOwner(lease, body, living);
        _processBodies[actor] = candidate;
        try
        {
            if (ActorProcesses.Read(actor).Level == FalloutDetectionProcessLevel.High)
                ProcessCommon.ObserveExistingHighBody(actor, ActorProcesses.Read(actor).Epoch);
        }
        catch
        {
            if (previous is null) _processBodies.Remove(actor); else _processBodies[actor] = previous;
            throw;
        }
        return new ProcessBodyLease(this, actor, candidate);
    }
    private sealed class ProcessBodyLease(FalloutReferenceWorld world, FalloutFormKey actor, ProcessBodyOwner owner) : IDisposable
    {
        private bool _retired;
        public void Dispose()
        {
            if (_retired) return;
            if (world._processBodies.TryGetValue(actor, out var current) && ReferenceEquals(current, owner)) world._processBodies.Remove(actor);
            _retired = true;
        }
    }
    private FalloutActorProcessBodyBinding? ReadActualProcessBody(FalloutFormKey actor)
    {
        if (!_processBodies.TryGetValue(actor, out var body)) return null;
        if (!body.Living()) throw new NotSupportedException("Actor common initializer retains a retired/unpublished native body lease.");
        return body.Source;
    }
    internal Guid EnterActualMainProcess(FalloutMainProcessOperation operation, string owner) => ProcessRuntime.BeginMain(operation, owner);
    internal void CompleteActualMainProcess(Guid invocation, string owner) => ProcessRuntime.CompleteMain(invocation, owner);
    internal void FailActualMainProcess(Guid invocation, string owner, Exception error) => ProcessRuntime.FailMain(invocation, owner, error);
    internal void RetainActualUnownedLifeTransition(FalloutFormKey actor, string owner)
    {
        if (_processRuntime is not null) ProcessRuntime.RetainUnownedLifeTransition(actor, owner);
    }
    private void RetireActualProcessRuntimeActor(FalloutFormKey actor, string owner)
    {
        if (_processBodies.ContainsKey(actor)) throw new NotSupportedException("Actor source process retirement still owns a living native body.");
        var process = ActorProcesses.Read(actor);
        if (!process.Retired) throw new InvalidDataException("Actual common retirement preceded its source process invalidation.");
        ProcessCommon.RetireActor(actor, process.Epoch, owner); ProcessRuntime.RetireActor(actor, owner);
    }
    internal FalloutActorProcessRuntimeSnapshot CaptureActualProcessRuntime()
    {
        ProcessRuntime.RequireActors(ActorPerception.ConstructedSourceActors); return ProcessRuntime.Capture();
    }
    internal FalloutProcessCommonSnapshot CaptureActualProcessCommon()
    {
        ProcessCommon.RequireActors(ActorProcesses.Capture().Actors);
        foreach (var actor in ActorPerception.ConstructedSourceActors)
            if (ActorPerception.RequiresNativePublication(actor) && ReadActualProcessBody(actor) is null)
                throw new NotSupportedException("Current common process lost its actual source body publication: " + actor);
        return ProcessCommon.Capture();
    }
    private void RetireActualProcessRuntime()
    {
        if (_processBodies.Count != 0) throw new NotSupportedException("Actual common process source still owns native body consumers.");
        // The manager retires first. Failed common/runtime retirement retains
        // its real providers and cannot be reported as a complete world close.
        _processCommon?.Dispose(); _processCommon = null;
        _processRuntime?.Dispose(); _processRuntime = null;
        _processRuntimeDeclaration = null; _processRuntimeStack = null;
        _processRuntimeRestore = null; _processCommonRestore = null;
    }
}
