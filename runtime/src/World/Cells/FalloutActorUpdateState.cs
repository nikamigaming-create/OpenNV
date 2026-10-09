using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutActorUpdateMutation { SetActorsAI, ToggleActorsAI, ActorDataLoadReset, SourceRetirement }
internal sealed record FalloutActorUpdateReceipt(long Sequence, FalloutFormKey Actor,
    FalloutActorUpdateMutation Operation, byte Before, byte After, int? Argument, FalloutFormKey? CallingOwner, string Owner);
internal sealed record FalloutActorUpdateEntry(FalloutCombatActorIdentity Source, byte Value,
    long Created, long LastChanged, bool Retired, string? Failure);
internal sealed record FalloutActorUpdateHandoff(Guid PreviousProcess, Guid CurrentProcess, long Sequence);
internal sealed record FalloutActorUpdateSnapshot(string Schema, string Stack, string Contract,
    Guid CapturedProcess, long Sequence, IReadOnlyList<FalloutActorUpdateEntry> Actors,
    IReadOnlyList<FalloutActorUpdateReceipt> Receipts, FalloutActorUpdateHandoff? ColdHandoff);

// The actual source actor factory creates this state. Native rendering and the
// existence of a metadata row do not construct or enable an actor here.
internal sealed partial class FalloutActorUpdateState : IDisposable
{
    internal const string Schema = "opennv-source-actor-update/v1";
    private readonly FalloutActorUpdateDeclaration _source;
    private readonly string _stack;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutActorUpdateEntry> _actors = new(FalloutFormKeyComparer.Instance);
    private readonly List<FalloutActorUpdateReceipt> _receipts = [];
    private FalloutActorUpdateHandoff? _cold;
    private long _sequence;
    private bool _busy, _disposed;

    internal FalloutActorUpdateState(FalloutActorUpdateDeclaration source, string stack,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity, FalloutActorUpdateSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(identity);
        _source = source; _stack = stack; _identity = identity;
        if (restore is not null) Restore(restore);
    }
    internal bool HasActor(FalloutFormKey actor) => _actors.ContainsKey(actor);
    internal IReadOnlyList<FalloutFormKey> Actors => _actors.Keys.ToArray();
    internal string? SaveBlocker => _busy ? "source-actor-update-mutation-in-flight" :
        _actors.Values.FirstOrDefault(actor => actor.Failure is not null) is { } failed ?
            "source-actor-update:" + failed.Source.Reference + ":" + failed.Failure : null;
    internal object State => new
    {
        source = _source.Contract,
        process = _process,
        _sequence,
        actors = _actors.Values.ToArray(),
        receipts = _receipts.ToArray(),
        cold = _cold,
        saveBlocker = SaveBlocker
    };

    internal void Construct(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Actor update constructor reentered a live mutation.");
        if (_actors.TryGetValue(actor, out var existing))
        {
            if (existing.Retired) throw new InvalidOperationException("A retired source actor cannot be reconstructed by native body publication.");
            return;
        }
        var source = ReadIdentity(actor);
        var sequence = Next();
        _actors.Add(actor, new(source, _source.Initial, sequence, sequence, false, null));
    }
    internal FalloutActorProcessFact<bool> Read(FalloutFormKey actor)
    {
        var state = Require(actor);
        return new(state.Value != 0, "original-Actor-update-byte:" + actor + "/" + state.LastChanged,
            state.Retired ? "actual-source-actor-lifetime-retired" : state.Failure);
    }
    internal int IsOff(FalloutFormKey actor)
    {
        var state = RequireHealthy(actor);
        return FalloutActorUpdateDeclaration.Off(state.Value);
    }
    internal void Set(FalloutFormKey actor, int signed, FalloutFormKey? caller, string owner) =>
        Mutate(actor, FalloutActorUpdateMutation.SetActorsAI, FalloutActorUpdateDeclaration.Normalize(signed), caller, owner, signed);
    internal void Toggle(FalloutFormKey actor, FalloutFormKey? caller, string owner) =>
        Mutate(actor, FalloutActorUpdateMutation.ToggleActorsAI, FalloutActorUpdateDeclaration.Toggle(RequireHealthy(actor).Value), caller, owner);

    // The original actor data-load reset writes one amid other actor-owned
    // resets. A caller must own that real operation; ResetAI is another API.
    internal void ActorDataLoadReset(FalloutFormKey actor, string owner) =>
        Mutate(actor, FalloutActorUpdateMutation.ActorDataLoadReset, _source.Initial, null, owner);
    internal void Retire(FalloutFormKey actor, string owner)
    {
        var state = Require(actor); if (state.Retired) return;
        Mutate(actor, FalloutActorUpdateMutation.SourceRetirement, state.Value, null, owner);
        _actors[actor] = _actors[actor] with { Retired = true };
    }
    internal void RetainUnownedTransition(FalloutFormKey actor, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); var state = Require(actor);
        _actors[actor] = state with { LastChanged = Next(), Failure = state.Failure ?? owner };
    }
    private void Mutate(FalloutFormKey actor, FalloutActorUpdateMutation operation,
        byte value, FalloutFormKey? caller, string owner, int? argument = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var state = RequireHealthy(actor);
        if (_busy)
        {
            RetainUnownedTransition(actor, "actual-source-actor-update-mutation-reentry");
            throw new InvalidOperationException("Source actor update mutation reentered.");
        }
        _busy = true;
        try
        {
            if (ReadIdentity(actor) != state.Source) throw new InvalidDataException("Actor update source winner/master changed.");
            var sequence = Next();
            _actors[actor] = state with { Value = value, LastChanged = sequence };
            _receipts.Add(new(sequence, actor, operation, state.Value, value, argument, caller, owner));
        }
        catch (Exception error)
        {
            _actors[actor] = _actors[actor] with { Failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message };
            throw;
        }
        finally { _busy = false; }
    }
    private FalloutCombatActorIdentity ReadIdentity(FalloutFormKey actor)
    {
        var source = _identity(actor); source.Validate();
        if (source.Reference != actor) throw new InvalidDataException("Actor update has a foreign source identity.");
        return source;
    }
    private FalloutActorUpdateEntry Require(FalloutFormKey actor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _actors.TryGetValue(actor, out var state) ? state :
            throw new NotSupportedException("Actual actor update constructor is absent: " + actor);
    }
    private FalloutActorUpdateEntry RequireHealthy(FalloutFormKey actor)
    {
        var state = Require(actor);
        if (state.Retired || state.Failure is not null) throw new NotSupportedException(state.Failure ?? "Source actor update lifetime retired.");
        return state;
    }
    private long Next() => _sequence = checked(_sequence + 1);
    public void Dispose()
    {
        if (_disposed) return;
        if (_busy) throw new InvalidOperationException("Source actor update owner cannot retire inside a mutation.");
        _disposed = true; _actors.Clear();
    }
}
