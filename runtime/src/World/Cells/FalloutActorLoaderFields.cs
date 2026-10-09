using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutActorLoaderField(FalloutCombatActorIdentity Source, uint? Word,
    long Changed, bool Retired, string? Failure);
internal sealed record FalloutActorLoaderFieldsSnapshot(string Schema, string Stack, string Contract,
    Guid CapturedProcess, long Sequence, IReadOnlyList<FalloutActorLoaderField> Actors,
    FalloutActorProcessRuntimeHandoff? ColdHandoff);

// This is the independently read original Actor word, not common process
// flags, life codes, C# Enabled, a pause state or the actor AI update byte.
internal sealed class FalloutActorLoaderFields : IDisposable
{
    internal const string Schema = "opennv-actor-loader-fields/v1";
    private readonly FalloutActorProcessQueueDeclaration _source;
    private readonly string _stack;
    private readonly Func<FalloutFormKey, FalloutCombatActorIdentity> _identity;
    private readonly Guid _process = Guid.NewGuid();
    private readonly Dictionary<FalloutFormKey, FalloutActorLoaderField> _actors = new(FalloutFormKeyComparer.Instance);
    private FalloutActorProcessRuntimeHandoff? _cold;
    private long _sequence, _callbackFault;
    private bool _callback, _disposed;
    internal string? SaveBlocker => _callback ? "actual-Actor-loader-word-source-callback-in-flight" :
        _actors.Values.FirstOrDefault(field => field.Failure is not null) is { } failed ?
            "actual-Actor-loader-word:" + failed.Source.Reference + ":" + failed.Failure : null;

    internal FalloutActorLoaderFields(FalloutActorProcessQueueDeclaration source, string stack,
        Func<FalloutFormKey, FalloutCombatActorIdentity> identity, FalloutActorLoaderFieldsSnapshot? restore = null)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(identity);
        _source = source; _stack = stack; _identity = identity;
        if (restore is null) return;
        Validate(restore);
        if (restore.Stack != stack || restore.Contract != source.Contract || restore.CapturedProcess == _process)
            throw new InvalidDataException("Actor loader fields lost their selected source/new-process owner.");
        foreach (var field in restore.Actors)
        {
            if (ReadSource(field.Source.Reference) != field.Source)
                throw new InvalidDataException("Saved Actor loader word has a foreign winner/master.");
            _actors.Add(field.Source.Reference, field);
        }
        _sequence = restore.Sequence; _cold = new(restore.CapturedProcess, _process, Next());
    }
    internal void Construct(FalloutFormKey actor)
    {
        RequireCurrent();
        if (_actors.TryGetValue(actor, out var existing))
        {
            if (existing.Retired) throw new InvalidDataException("A loader read cannot recreate a retired source Actor.");
            return;
        }
        // Both inspected original Actor initialization consumers store the
        // full word as positive zero. Only actual Actor construction joins it.
        var source = ReadSource(actor); _actors.Add(actor, new(source, 0, Next(), false, null));
    }
    internal FalloutActorProcessFact<uint> Read(FalloutFormKey actor)
    {
        RequireCurrent();
        var field = _actors.TryGetValue(actor, out var value) ? value :
            throw new NotSupportedException("Actual Actor loader word constructor is absent: " + actor);
        return new(field.Word, "actual-selected-Actor-loader-word/" + field.Changed,
            field.Retired ? "actual-Actor-loader-word-retired" : field.Failure);
    }
    internal void RetainUnownedWriter(FalloutFormKey actor, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner); _ = Read(actor); var field = _actors[actor];
        if (field.Retired) throw new InvalidOperationException("Retired Actor cannot enter a source loader-word writer.");
        _actors[actor] = field with { Word = null, Failure = field.Failure ?? owner, Changed = Next() };
    }
    internal void RetireActor(FalloutFormKey actor)
    {
        _ = Read(actor); var field = _actors[actor];
        if (!field.Retired) _actors[actor] = field with { Retired = true, Changed = Next() };
    }
    internal void RequireActors(IEnumerable<FalloutFormKey> actors)
    {
        RequireCurrent();
        if (!actors.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(_actors.Keys))
            throw new InvalidDataException("Actor loader words omitted or invented an actual source constructor.");
    }
    private FalloutCombatActorIdentity ReadSource(FalloutFormKey actor)
    {
        var faults = _callbackFault; _callback = true;
        try
        {
            var source = _identity(actor); source.Validate();
            if (_callbackFault != faults) throw new InvalidOperationException("Actor loader source callback caught an actual reentry refusal.");
            return source.Reference == actor ? source : throw new InvalidDataException("Actor loader word has a foreign source identity.");
        }
        finally { _callback = false; }
    }
    internal FalloutActorLoaderFieldsSnapshot Capture()
    {
        RequireCurrent();
        return new(Schema, _stack, _source.Contract, _process, _sequence, _actors.Values.ToArray(), _cold);
    }
    internal static void Validate(FalloutActorLoaderFieldsSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Schema != Schema || string.IsNullOrWhiteSpace(snapshot.Stack) ||
            snapshot.Contract is not { Length: 64 } || !snapshot.Contract.All(Uri.IsHexDigit) || snapshot.CapturedProcess == Guid.Empty ||
            snapshot.Sequence < 0 || snapshot.Actors is null || snapshot.Actors.Any(value => value is null || value.Source is null) ||
            snapshot.Actors.Select(value => value.Source.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != snapshot.Actors.Count ||
            snapshot.ColdHandoff is { } cold && (cold.PreviousProcess == Guid.Empty || cold.CurrentProcess != snapshot.CapturedProcess ||
                cold.PreviousProcess == cold.CurrentProcess || cold.Sequence < 1 || cold.Sequence > snapshot.Sequence))
            throw new InvalidDataException("Saved Actor loader fields have an incomplete source/current/cold owner.");
        foreach (var field in snapshot.Actors)
        {
            field.Source.Validate();
            if (field.Changed < 1 || field.Changed > snapshot.Sequence || field.Word is not (null or 0) ||
                (field.Word is null) != (field.Failure is not null) || field.Failure is not null && string.IsNullOrWhiteSpace(field.Failure))
                throw new InvalidDataException("Saved Actor loader word has an unobserved constructor or writer.");
        }
    }
    private void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_callback)
        {
            _callbackFault = checked(_callbackFault + 1);
            throw new InvalidOperationException("Actual Actor loader field source reentered its constructor/current lifetime.");
        }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    public void Dispose() { if (_disposed) return; RequireCurrent(); _disposed = true; _actors.Clear(); }
}
