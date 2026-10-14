using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferenceFormFileDependency(string Plugin, string Sha256);
internal sealed record FalloutReferenceFormFileRead(string Source, string Stack, Guid Process, Guid Factory,
    Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence, int Count, string? File);
internal sealed record FalloutReferenceFormFileCall(FalloutReferencePositionInvocation Invocation, long Entered,
    long Changed, string? Writer, FalloutReferenceFormFileRead? Read, bool Returned, string? Failure);
internal sealed record FalloutReferenceFormFileSnapshot(string Schema, FalloutReferenceFormFileSource Source,
    string Stack, Guid Process, Guid Factory, Guid Constructor, FalloutCombatActorIdentity Actor, long Sequence,
    IReadOnlyList<FalloutReferenceFormFileDependency> Files, IReadOnlyList<FalloutReferencePositionPublication> Publications,
    IReadOnlyList<FalloutReferenceFormFileCall> Calls, string? Writer, bool Retired);

// The original association list is a real reference field. It does not borrow
// the winning record's plugin or synthesize an active file from load order.
internal sealed class FalloutReferenceFormFileAssociationState
{
    internal const string Schema = "opennv-reference-form-files/v1";
    private readonly Action<FalloutReferencePositionInvocation> _requireActual;
    private readonly List<FalloutReferencePositionPublication> _publications = [];
    private readonly List<FalloutReferenceFormFileCall> _calls = [];
    private readonly IReadOnlyList<FalloutReferenceFormFileDependency> _files;
    private readonly LinkedList<string> _associations = new();
    private long _sequence, _fault;
    private int? _thread;
    private bool _busy, _retired;
    private string? _writer;
    internal FalloutReferenceFormFileSource Source { get; }
    internal string Stack { get; }
    internal Guid Process { get; }
    internal Guid Factory { get; } = Guid.NewGuid();
    internal Guid Constructor { get; }
    internal FalloutCombatActorIdentity Actor { get; }
    internal string? SaveBlocker => _busy ? "source-reference-form-files-in-flight" : _retired ?
        "source-reference-form-files-retired" : _writer ?? _calls.FirstOrDefault(value => !value.Returned)?.Failure;

    internal FalloutReferenceFormFileAssociationState(FalloutReferenceFormFileSource source, FalloutPluginStack records,
        string stack, Guid process, Guid constructor, FalloutCombatActorIdentity actor,
        Action<FalloutReferencePositionInvocation> requireActual, FalloutReferenceFormFileSnapshot? saved = null)
    {
        source.Validate(); actor.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack); ArgumentNullException.ThrowIfNull(requireActual);
        if (!actor.EnginePlayer || process == Guid.Empty || constructor == Guid.Empty || records.TryGetWinner(actor.Reference, out _))
            throw new NotSupportedException("source-reference-form-files-record-or-non-Player-constructor-writer-unbound");
        Source = source; Stack = stack; Process = process; Constructor = constructor; Actor = actor; _requireActual = requireActual;
        _files = Array.AsReadOnly(records.Plugins.Select(value => new FalloutReferenceFormFileDependency(value.Plugin.Name, value.Sha256)).ToArray());
        if (saved is not null)
        {
            Validate(saved, source, stack, actor);
            if (saved.Process == process || saved.Factory == Factory || saved.Constructor == constructor || saved.Retired || !saved.Files.SequenceEqual(_files))
                throw new InvalidDataException("Cold reference form files changed original files or reused their constructor.");
            _sequence = saved.Sequence; _writer = saved.Writer; _publications.AddRange(saved.Publications); _calls.AddRange(saved.Calls);
        }
        _publications.Add(new(Process, Factory, Constructor, saved?.Process, saved?.Factory, _sequence));
    }

    internal FalloutReferenceFormFileRead ReadCurrentFile(FalloutReferencePositionInvocation invocation)
    {
        var call = Run(invocation, null);
        return call.Read!;
    }
    internal void ObserveUnownedWriter(FalloutReferencePositionInvocation invocation, string boundary)
    { ArgumentException.ThrowIfNullOrWhiteSpace(boundary); _ = Run(invocation, boundary); }
    private FalloutReferenceFormFileCall Run(FalloutReferencePositionInvocation invocation, string? writer)
    {
        if (_busy) { _fault++; throw new InvalidOperationException("Reference form-file getter reentered."); }
        ObjectDisposedException.ThrowIf(_retired, this);
        var entered = checked(++_sequence); var fault = _fault; _busy = true;
        FalloutReferenceFormFileRead? read = null; string? failure = null;
        try
        {
            Guard();
            if (writer is not null) _writer = writer;
            else
            {
                if (_writer is not null) throw new NotSupportedException(_writer);
                // Both actual constructor list cells are null. No actual
                // association/copy/load writer has been entered on this lane.
                read = new(Source.Identity, Stack, Process, Factory, Constructor, Actor, entered, _associations.Count, _associations.Last?.Value);
            }
            Guard();
        }
        catch (Exception error) { failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message; throw; }
        finally
        {
            _calls.Add(new(invocation, entered, checked(++_sequence), writer, read, failure is null, failure)); _busy = false;
        }
        return _calls[^1];

        void Guard()
        {
            invocation.ValidatePackageCaller();
            if (_retired || invocation.Actor != Actor || invocation.ReferenceProcess != Process || invocation.ReferenceConstructor != Constructor ||
                invocation.ThreadId != Environment.CurrentManagedThreadId || invocation.Call == Guid.Empty || invocation.CommonProcess == Guid.Empty ||
                invocation.Epoch < 1 || _thread is { } thread && thread != invocation.ThreadId || _fault != fault)
                throw new InvalidOperationException("Reference form-file read left its actual caller or field constructor.");
            _requireActual(invocation);
            if (_retired || _fault != fault) throw new InvalidOperationException("Reference form-file guard swallowed reentry.");
            _thread ??= invocation.ThreadId;
        }
    }
    internal FalloutReferenceFormFileSnapshot Capture()
    {
        if (_busy) { _fault++; throw new InvalidOperationException("Entered reference form files cannot be captured."); }
        var saved = new FalloutReferenceFormFileSnapshot(Schema, Source, Stack, Process, Factory, Constructor, Actor, _sequence,
            _files, Array.AsReadOnly(_publications.ToArray()), Array.AsReadOnly(_calls.ToArray()), _writer, _retired);
        Validate(saved, Source, Stack, Actor); return saved;
    }
    internal void Retire()
    { if (_busy) { _fault++; throw new InvalidOperationException("Entered form files cannot retire."); } _retired = true; }

    internal static void Validate(FalloutReferenceFormFileSnapshot saved, FalloutReferenceFormFileSource source,
        string stack, FalloutCombatActorIdentity actor)
    {
        source.Validate(); actor.Validate();
        if (saved.Schema != Schema || saved.Source != source || saved.Stack != stack || saved.Actor != actor || !actor.EnginePlayer ||
            saved.Files is not { Count: > 0 } || saved.Files.Any(value => string.IsNullOrWhiteSpace(value.Plugin) || value.Sha256.Length != 64) ||
            saved.Files.Select(value => value.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Files.Count ||
            saved.Publications is not { Count: > 0 } || saved.Calls is null || saved.Writer is { Length: 0 })
            throw new InvalidDataException("Reference form files changed their exact original source or cold shape.");
        var owners = new Dictionary<Guid, FalloutReferencePositionPublication>(); var factories = new HashSet<Guid>(); var constructors = new HashSet<Guid>();
        FalloutReferencePositionPublication? previous = null;
        foreach (var owner in saved.Publications)
        {
            if (owner.Process == Guid.Empty || owner.Factory == Guid.Empty || owner.Constructor == Guid.Empty || !owners.TryAdd(owner.Process, owner) ||
                !factories.Add(owner.Factory) || !constructors.Add(owner.Constructor) || (previous is null ? owner.PreviousProcess is not null || owner.PreviousFactory is not null :
                    owner.PreviousProcess != previous.Process || owner.PreviousFactory != previous.Factory))
                throw new InvalidDataException("Form-file field reused its actual constructor lineage.");
            previous = owner;
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory || previous.Constructor != saved.Constructor)
            throw new InvalidDataException("Form-file snapshot omitted its current field owner.");
        long sequence = 0; string? writer = null; var threads = new Dictionary<Guid, int>(); var ordinal = -1;
        var order = saved.Publications.Select((value, index) => (value.Process, index)).ToDictionary(value => value.Process, value => value.index);
        foreach (var call in saved.Calls)
        {
            var invocation = call.Invocation; invocation.ValidatePackageCaller();
            if (!owners.TryGetValue(invocation.ReferenceProcess, out var owner) || invocation.ReferenceConstructor != owner.Constructor ||
                invocation.Actor != actor || invocation.Call == Guid.Empty || invocation.CommonProcess == Guid.Empty || invocation.Epoch < 1 ||
                invocation.ThreadId < 1 || call.Entered != ++sequence || call.Changed != ++sequence || call.Returned != (call.Failure is null) ||
                call.Failure is { Length: 0 } || order[owner.Process] < ordinal || threads.TryGetValue(owner.Process, out var thread) && thread != invocation.ThreadId)
                throw new InvalidDataException("Form-file history lost actual getter order or caller lineage.");
            if (call.Writer is { } observed) { if (string.IsNullOrWhiteSpace(observed) || call.Read is not null) throw new InvalidDataException("Form-file invalidation invented a getter."); writer = observed; }
            if (call.Read is { } read && (writer is not null || read.Source != source.Identity || read.Stack != stack || read.Actor != actor ||
                read.Process != owner.Process || read.Factory != owner.Factory || read.Constructor != owner.Constructor || read.Sequence != call.Entered || read.Count != 0 || read.File is not null) ||
                call.Returned && call.Writer is null && call.Read is null)
                throw new InvalidDataException("Form-file getter substituted an owning plugin or an unclosed current association.");
            threads.TryAdd(owner.Process, invocation.ThreadId); ordinal = order[owner.Process];
        }
        if (sequence != saved.Sequence || writer != saved.Writer) throw new InvalidDataException("Form-file current history differs from its snapshot.");
        FalloutReferencePublicationSequence.Validate(saved.Publications.Select(value => (value.Process, value.PublishedSequence)),
            saved.Calls.Select(value => (value.Invocation.ReferenceProcess, value.Entered, value.Changed)), saved.Sequence);
    }
}
