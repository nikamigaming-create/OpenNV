using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

// A process owns a lease on the actual retained actor gameplay provider. Tier
// replacement moves/copies process ownership; it never creates a second actor,
// reselects a package, replays an event or recreates modifier pools.
internal sealed partial class FalloutActorProcessCommonState : IDisposable
{
    internal const string Schema = "opennv-actor-process-common/v1";
    private readonly FalloutActorProcessRuntimeDeclaration _source;
    private readonly string _stack;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Func<FalloutFormKey, FalloutProcessGameplayState> _gameplay;
    private readonly Func<FalloutFormKey, FalloutActorProcessBodyBinding?> _body;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutProcessCommonEntry> _current = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutProcessCommonEntry> _retired = [];
    private FalloutProcessCommonEntry? _pending;
    private FalloutProcessCommonTransfer? _transfer;
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _busy, _disposed;
    internal FalloutActorProcessCommonState(FalloutActorProcessRuntimeDeclaration source, string stack,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity,
        Func<FalloutFormKey, FalloutProcessGameplayState> gameplay,
        Func<FalloutFormKey, FalloutActorProcessBodyBinding?> body, FalloutProcessCommonSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(identity); ArgumentNullException.ThrowIfNull(gameplay); ArgumentNullException.ThrowIfNull(body);
        _source = source; _stack = stack; _identity = identity; _gameplay = gameplay; _body = body;
        if (restore is not null) Restore(restore);
    }
    internal bool HasActor(FalloutFormKey actor) => _current.ContainsKey(actor);
    internal string? SaveBlocker => _busy ? "actual-process-common-owner-consumer-in-flight" :
        _transfer is { Failure: not null } failedTransfer ? "actual-process-common-transfer:" + failedTransfer.Owner + ":" + failedTransfer.Failure :
        _transfer is { Initialized: false } transfer ? "actual-process-common-transfer:" + transfer.Owner + ":" + (transfer.Failure ?? "source-prefix-incomplete") :
        _current.Values.FirstOrDefault(entry => entry.Boundary is not null) is { } failed ?
            "actual-process-common:" + failed.Source.Reference + ":" + failed.Boundary : null;
    internal object State => new { source = _source.Contract, process = _process, _sequence,
        current = _current.Values.ToArray(), retired = _retired.ToArray(), pending = _pending,
        transfer = _transfer, cold = _cold, saveBlocker = SaveBlocker };

    internal void Construct(FalloutFormKey actor, long epoch, FalloutDetectionProcessLevel level)
    {
        RequireNotBusy();
        if (_current.TryGetValue(actor, out var existing))
        {
            if (existing.Epoch != epoch || existing.Level != level || existing.Phase == FalloutProcessCommonPhase.Retired)
                throw new InvalidDataException("Source common constructor differs from the actual existing process epoch/class.");
            return;
        }
        var source = Identity(actor);
        if (epoch != 1 || level != (source.EnginePlayer ? FalloutDetectionProcessLevel.High : FalloutDetectionProcessLevel.Low))
            throw new InvalidDataException("Actual source common constructor lost the original Player/nonplayer factory class.");
        var sequence = Next();
        _current.Add(actor, new(source, epoch, level, FalloutProcessCommonScalars.Constructed,
            new(source, Guid.NewGuid(), epoch, sequence, false), FalloutProcessCommonPhase.Constructed, null, null, sequence));
    }
    internal void Copy(FalloutActorProcessFactorySnapshot factory) => Operation(factory, () =>
    {
        var old = Require(factory.Actor);
        RequireFactory(factory, FalloutActorProcessFactoryPhase.ConstructedNew, old);
        if (factory.Before != FalloutDetectionProcessLevel.Low)
            throw new NotSupportedException("Highest shared Middle/High process common fields have no current complete typed copy owner.");
        if (_pending is not null || _transfer is { Initialized: false })
            throw new InvalidOperationException("An earlier process common transfer still owns its consumed prefix.");
        var gameplay = ReadGameplay(old.Source.Reference);
        var ownership = Guid.NewGuid(); var sequence = Next();
        _pending = new(old.Source, factory.NewEpoch, factory.After, old.Scalars,
            new(old.Source, ownership, factory.NewEpoch, sequence, false), FalloutProcessCommonPhase.Copied, null, null, sequence);
        // The selected Low constructors own null in both move-and-clear child
        // slots. Any reached creation of such a child must first bind its own
        // typed owner, or retain a common boundary. A nonnull opaque pointer is
        // never admitted as a successfully transferred owner.
        _transfer = new(factory.Actor, factory.BeforeEpoch, factory.NewEpoch, factory.Before, factory.After,
            factory.Owner, ownership, gameplay, GameplayHash(gameplay), true, false, false, false, null, sequence);
    });
    internal void RetireOld(FalloutActorProcessFactorySnapshot factory) => Operation(factory, () =>
    {
        var old = Require(factory.Actor);
        RequireFactory(factory, FalloutActorProcessFactoryPhase.CopiedPerception, old); RequireTransfer(factory);
        if (_pending is null || _transfer!.OldRetired || !GameplayEquivalent(ReadGameplay(factory.Actor), _transfer.ObservedBeforeCopy))
            throw new InvalidDataException("Old source process cannot retire a lost/mutated common transfer or a duplicated gameplay owner.");
        // Providers remain in the real reference world; release only the old
        // process lease. Package events and live native continuations survive.
        var sequence = Next();
        var retired = old with { Gameplay = null, Body = null, Phase = FalloutProcessCommonPhase.OldRetired, Changed = sequence };
        _current[factory.Actor] = retired; _retired.Add(retired);
        _pending = _pending with { Phase = FalloutProcessCommonPhase.OldRetired, Changed = sequence };
        _transfer = _transfer with { OldRetired = true, Changed = sequence };
    });
    internal void InitializeNew(FalloutActorProcessFactorySnapshot factory) => Operation(factory, () =>
    {
        RequireTransfer(factory);
        if (factory.Phase != FalloutActorProcessFactoryPhase.RegisteredNew || !_transfer!.OldRetired || _pending is null ||
            _pending.Epoch != factory.NewEpoch || _pending.Level != factory.After || _pending.Gameplay?.Ownership != _transfer.GameplayOwnership)
            throw new InvalidDataException("New source process cannot initialize before actual retirement/publication/registration.");
        if (!GameplayEquivalent(ReadGameplay(factory.Actor), _transfer.ObservedBeforeCopy))
            throw new InvalidDataException("Actual package/modifier state changed inside the original common transfer.");
        _current[factory.Actor] = _pending with { Phase = FalloutProcessCommonPhase.Published, Changed = Next() };
        _pending = null;
        _transfer = _transfer with { Published = true, Changed = Next() };
        // A real current native body supplies the source NIF/BPTD binding.
        // Missing nodes/controllers are legal nullable lookup results. An
        // unavailable body or source relationship is a genuine failed owner.
        var body = Callback(() => _body(factory.Actor)) ?? throw new NotSupportedException("Actual new High process source skeleton publication is absent.");
        RequireBody(body, factory.Actor);
        _current[factory.Actor] = _current[factory.Actor] with
        { Body = body, Phase = FalloutProcessCommonPhase.Initialized, Changed = Next() };
        _transfer = _transfer with { Initialized = true, Changed = Next() };
    });
    internal void ObserveExistingHighBody(FalloutFormKey actor, long epoch)
    {
        RequireNotBusy(); var state = Require(actor);
        if (state.Epoch != epoch || state.Level != FalloutDetectionProcessLevel.High || state.Gameplay is null || state.Boundary is not null)
            throw new InvalidDataException("Current High body belongs to a retired/unadmitted process epoch.");
        var body = Callback(() => _body(actor)) ?? throw new NotSupportedException("Existing High process has no actual source body publication.");
        RequireBody(body, actor);
        if (state.Body is { } previous && !BodyEquivalent(previous, body))
            throw new NotSupportedException("Source High body replacement has no admitted body-part/LOD rebinding transaction.");
        _current[actor] = state with { Body = body, Phase = FalloutProcessCommonPhase.Initialized, Changed = Next() };
    }
    internal void RetainUnownedMutation(FalloutFormKey actor, string owner)
    {
        RequireNotBusy(); HoldMutation(actor, owner);
    }
    private void HoldMutation(FalloutFormKey actor, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); var state = Require(actor);
        _current[actor] = state with { Boundary = state.Boundary ?? owner, Changed = Next() };
    }
    internal void RetireActor(FalloutFormKey actor, long retirementEpoch, string owner)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(owner); var state = Require(actor);
        if (state.Source.EnginePlayer) throw new InvalidOperationException("Player common process belongs to the actual world lifetime.");
        if (state.Phase == FalloutProcessCommonPhase.Retired)
        {
            if (state.Epoch != retirementEpoch) throw new InvalidDataException("Common retirement has a foreign actual source epoch.");
            return;
        }
        if (retirementEpoch != checked(state.Epoch + 1))
            throw new InvalidDataException("Common retirement lost the actual source process invalidation epoch.");
        if (_transfer is { Initialized: false }) throw new NotSupportedException("Actor retirement cannot discard an incomplete common transfer.");
        var retired = state with { Gameplay = null, Body = null, Phase = FalloutProcessCommonPhase.Retired, Changed = Next() };
        _retired.Add(retired);
        _current[actor] = retired with { Epoch = retirementEpoch };
    }
    private void RequireFactory(FalloutActorProcessFactorySnapshot factory, FalloutActorProcessFactoryPhase phase, FalloutProcessCommonEntry old)
    {
        if (factory.Operation != FalloutActorProcessSourceOperation.EnsureHigh || factory.Phase != phase ||
            factory.Actor != old.Source.Reference || factory.BeforeEpoch != old.Epoch || factory.Before != old.Level ||
            factory.NewEpoch != checked(old.Epoch + 1) || factory.After != FalloutDetectionProcessLevel.High ||
            factory.BeforePerception.Source != old.Source || old.Gameplay is null || old.Boundary is not null ||
            old.Gameplay.Retired || old.Gameplay.ProcessEpoch != old.Epoch || Identity(factory.Actor) != old.Source)
            throw new InvalidDataException("Actual common copy lost its source actor/class/process/lease or consumed factory phase.");
    }
    private void RequireTransfer(FalloutActorProcessFactorySnapshot factory)
    {
        if (_transfer is null || _transfer.Actor != factory.Actor || _transfer.BeforeEpoch != factory.BeforeEpoch ||
            _transfer.NewEpoch != factory.NewEpoch || _transfer.Owner != factory.Owner || _transfer.Failure is not null)
            throw new InvalidDataException("Common continuation has no exact healthy requesting factory invocation.");
    }
    private FalloutProcessGameplayState ReadGameplay(FalloutFormKey actor)
    {
        var state = Callback(() => _gameplay(actor));
        if (state is null || state.Actor != actor || state.ActorValues is null || state.ActorValues.Any(item =>
            string.IsNullOrWhiteSpace(item.Key) || item.Value is null || !item.Value.IsFinite))
            throw new InvalidDataException("Common copy lost the actual retained package/actor-value provider.");
        state.Package?.Validate(); state.Motion?.Validate(); state.PendingChoice?.Validate();
        return state;
    }
    internal static string GameplayHash(FalloutProcessGameplayState state) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(state))).ToLowerInvariant();
    private static bool GameplayEquivalent(FalloutProcessGameplayState first, FalloutProcessGameplayState second) =>
        GameplayHash(first) == GameplayHash(second);
    internal static bool BodyEquivalent(FalloutActorProcessBodyBinding first, FalloutActorProcessBodyBinding second) =>
        first.Actor == second.Actor && first.SkeletonPath == second.SkeletonPath && first.SkeletonSha256 == second.SkeletonSha256 &&
        first.BodyPartSource == second.BodyPartSource && first.BodyPartSha256 == second.BodyPartSha256 &&
        first.HeadTarget == second.HeadTarget && first.HeadBlock == second.HeadBlock &&
        first.TorsoTarget == second.TorsoTarget && first.TorsoBlock == second.TorsoBlock &&
        first.Bip01Block == second.Bip01Block && first.BoneLodController == second.BoneLodController && first.Parts.SequenceEqual(second.Parts);
    private FalloutCombatActorIdentity Identity(FalloutFormKey actor)
    {
        var identity = Callback(() => _identity(actor)); identity.Validate();
        return identity.Reference == actor ? identity : throw new InvalidDataException("Common process has a foreign source actor identity.");
    }
    private FalloutProcessCommonEntry Require(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _current.TryGetValue(actor, out var state) ? state : throw new NotSupportedException("Actual common source process constructor is absent: " + actor);
    }
    private void RequireNotBusy()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy)
        {
            _callbackFault = checked(_callbackFault + 1);
            throw new InvalidOperationException("Common process source owner reentered its transaction.");
        }
    }
    private T Callback<T>(Func<T> callback)
    {
        var previous = _busy; _busy = true; var faults = _callbackFault;
        try
        {
            var value = callback();
            if (_callbackFault != faults) throw new InvalidOperationException("Common process callback caught a real reentry failure.");
            return value;
        }
        finally { _busy = previous; }
    }
    private void Operation(FalloutActorProcessFactorySnapshot factory, Action action)
    {
        RequireNotBusy(); _busy = true; var faults = _callbackFault;
        try
        {
            action();
            if (_callbackFault != faults) throw new InvalidOperationException("Common process callback caught a real reentry failure.");
        }
        catch (Exception error)
        {
            var message = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            if (_transfer is not null && _transfer.Actor == factory.Actor && _transfer.BeforeEpoch == factory.BeforeEpoch &&
                _transfer.NewEpoch == factory.NewEpoch && _transfer.Owner == factory.Owner)
                _transfer = _transfer with { Failure = _transfer.Failure ?? message, Changed = Next() };
            HoldMutation(factory.Actor, message); throw;
        }
        finally { _busy = false; }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    public void Dispose()
    {
        if (_disposed) return; RequireNotBusy();
        if (_transfer is { Initialized: false }) throw new NotSupportedException("Actual common process retirement retains an unfinished old/new ownership transfer.");
        _disposed = true; _current.Clear(); _retired.Clear(); _pending = null;
    }
}
