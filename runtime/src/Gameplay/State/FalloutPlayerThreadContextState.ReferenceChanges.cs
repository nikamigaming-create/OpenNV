using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSourceThreadReferenceFlagsRead(string Source, Guid Process, Guid Factory,
    Guid Invocation, Guid ConsumerFactory, int Thread, long Sequence, byte Initialized, uint Flags);
internal sealed record FalloutSourceThreadReferenceFlagsCall(string Source, Guid Process, Guid Factory,
    Guid Invocation, Guid ConsumerFactory, int Thread, long Entered, long Changed,
    uint Before, uint After, bool? Enabled, bool Returned, string? Failure, string? WriterBoundary = null);
internal sealed record FalloutSourceThreadReferenceFlagsPublication(Guid Process, Guid Factory,
    Guid? PreviousProcess = null, Guid? PreviousFactory = null);
internal sealed record FalloutSourceThreadReferenceFlagsSnapshot(string Source, Guid Process, Guid Factory,
    long Sequence, uint Flags, string? WriterBoundary,
    IReadOnlyList<FalloutSourceThreadReferenceFlagsPublication> Publications,
    IReadOnlyList<FalloutSourceThreadReferenceFlagsCall> Calls);

internal sealed partial class FalloutPlayerThreadContextState
{
    private FalloutReferencePositionSource? _referenceFlagsSource;
    private readonly List<FalloutSourceThreadReferenceFlagsPublication> _referenceFlagPublications = [];
    private readonly List<FalloutSourceThreadReferenceFlagsCall> _referenceFlagCalls = [];
    private readonly HashSet<Guid> _referenceFlagInvocations = [];
    private uint _referenceChangeFlags;
    private long _referenceFlagSequence, _referenceFlagFault;
    private int? _referenceFlagThread;
    private bool _referenceFlagsBusy;
    private string? _referenceFlagWriterBoundary;
    internal string? ReferenceChangeFlagsSaveBlocker => _referenceFlagsBusy ? "source-thread-reference-flags-in-flight" :
        _referenceFlagWriterBoundary ?? _referenceFlagCalls.FirstOrDefault(value => !value.Returned)?.Failure;

    internal void BindReferenceChangeFlags(FalloutReferencePositionSource source,
        FalloutSourceThreadReferenceFlagsSnapshot? saved = null)
    {
        source.Validate();
        if (_retired || _referenceFlagsSource is not null || source.Fields.Controls.Main != _source.Main)
            throw new InvalidOperationException("Reference flags changed the actual source thread owner or repeated its binding.");
        _referenceFlagsSource = source;
        if (saved is not null)
        {
            ValidateReferenceChangeFlags(saved, source);
            if (saved.Process == _process || saved.Factory == _factory)
                throw new InvalidDataException("Cold reference flags reused their native process/thread factory.");
            _referenceChangeFlags = saved.Flags; _referenceFlagSequence = saved.Sequence;
            _referenceFlagWriterBoundary = saved.WriterBoundary;
            _referenceFlagPublications.AddRange(saved.Publications); _referenceFlagCalls.AddRange(saved.Calls);
            foreach (var call in saved.Calls.Where(value => value.Enabled is not null || value.WriterBoundary is not null))
                _referenceFlagInvocations.Add(call.Invocation);
        }
        _referenceFlagPublications.Add(new(_process, _factory, saved?.Process, saved?.Factory));
    }

    internal FalloutSourceThreadReferenceFlagsRead ReadReferenceChangeFlags(Guid invocation, Guid consumerFactory,
        Action requireCurrent)
    {
        var call = RunReferenceFlags(invocation, consumerFactory, null, null, requireCurrent);
        return new(call.Source, _process, _factory, invocation, consumerFactory,
            Environment.CurrentManagedThreadId, call.Changed, 1, call.After);
    }

    // The original setter returns the prior enabled state. Enabled=true clears
    // ONLY bit0; enabled=false sets it. It stores even when the value is unchanged.
    internal FalloutSourceThreadReferenceFlagsCall SetReferenceChangesEnabled(Guid invocation, Guid consumerFactory,
        bool enabled, Action requireCurrent) => RunReferenceFlags(invocation, consumerFactory, enabled, null, requireCurrent);

    internal FalloutSourceThreadReferenceFlagsCall ObserveReferenceFlagsWriter(Guid invocation, Guid consumerFactory,
        string boundary, Action requireCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        return RunReferenceFlags(invocation, consumerFactory, null, boundary, requireCurrent);
    }

    private FalloutSourceThreadReferenceFlagsCall RunReferenceFlags(Guid invocation, Guid consumerFactory,
        bool? enabled, string? boundary, Action requireCurrent)
    {
        ArgumentNullException.ThrowIfNull(requireCurrent);
        if (_referenceFlagsBusy)
        { _referenceFlagFault = checked(_referenceFlagFault + 1); throw new InvalidOperationException("Reference thread flags reentered."); }
        var source = _referenceFlagsSource ?? throw new NotSupportedException("source-thread-reference-flags-constructor-unbound");
        if (_retired || invocation == Guid.Empty || consumerFactory == Guid.Empty ||
            _referenceFlagThread is { } thread && thread != Environment.CurrentManagedThreadId ||
            (enabled is not null || boundary is not null) && !_referenceFlagInvocations.Add(invocation))
            throw new InvalidOperationException("Reference flags lost the actual source thread or repeated its invocation.");
        var before = _referenceChangeFlags; var entered = checked(++_referenceFlagSequence);
        var fault = _referenceFlagFault;
        _referenceFlagsBusy = true; string? failure = null;
        try
        {
            Guard(); RequireCurrentKindOwner();
            // This is the same initialized byte and empty on-demand source
            // initializer used by Script/menu/physics, independent of kind.
            CurrentKindFields().Initialized = 1;
            if (_thread == Environment.CurrentManagedThreadId) _initialized = 1;
            Guard();
            if (boundary is not null) _referenceFlagWriterBoundary = boundary;
            else
            {
                if (_referenceFlagWriterBoundary is not null) throw new NotSupportedException(_referenceFlagWriterBoundary);
                if (enabled is { } request) _referenceChangeFlags = request ? before & ~1u : before | 1;
            }
            Guard();
        }
        catch (Exception error)
        { failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message; throw; }
        finally
        {
            _referenceFlagThread ??= Environment.CurrentManagedThreadId;
            _referenceFlagCalls.Add(new(source.Identity, _process, _factory, invocation, consumerFactory,
                Environment.CurrentManagedThreadId, entered, checked(++_referenceFlagSequence), before,
                _referenceChangeFlags, enabled, failure is null, failure, boundary));
            _referenceFlagsBusy = false;
        }
        return _referenceFlagCalls[^1];

        void Guard()
        {
            requireCurrent();
            if (_retired || _referenceFlagFault != fault ||
                _referenceFlagThread is { } currentThread && currentThread != Environment.CurrentManagedThreadId ||
                _thread is { } ownerThread && ownerThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Reference thread flags changed their current owner or swallowed reentry.");
        }
    }

    internal FalloutSourceThreadReferenceFlagsSnapshot CaptureReferenceChangeFlags()
    {
        if (_referenceFlagsBusy) { _referenceFlagFault++; throw new InvalidOperationException("Entered thread flags cannot be captured."); }
        var source = _referenceFlagsSource ?? throw new NotSupportedException("source-thread-reference-flags-constructor-unbound");
        var saved = new FalloutSourceThreadReferenceFlagsSnapshot(source.Identity, _process, _factory,
            _referenceFlagSequence, _referenceChangeFlags, _referenceFlagWriterBoundary,
            Array.AsReadOnly(_referenceFlagPublications.ToArray()), Array.AsReadOnly(_referenceFlagCalls.ToArray()));
        ValidateReferenceChangeFlags(saved, source); return saved;
    }

    internal static void ValidateReferenceChangeFlags(FalloutSourceThreadReferenceFlagsSnapshot saved,
        FalloutReferencePositionSource source)
    {
        ArgumentNullException.ThrowIfNull(saved); source.Validate();
        if (saved.Source != source.Identity || saved.Process == Guid.Empty || saved.Factory == Guid.Empty ||
            saved.Publications is not { Count: > 0 } || saved.Calls is null || saved.WriterBoundary is { Length: 0 })
            throw new InvalidDataException("Reference flags lost their exact original source/thread history.");
        var publications = new Dictionary<Guid, (Guid Factory, int Ordinal)>();
        var factories = new HashSet<Guid>(); FalloutSourceThreadReferenceFlagsPublication? previous = null;
        foreach (var publication in saved.Publications)
        {
            if (publication.Process == Guid.Empty || publication.Factory == Guid.Empty || !factories.Add(publication.Factory) ||
                !publications.TryAdd(publication.Process, (publication.Factory, publications.Count)) ||
                (previous is null ? publication.PreviousProcess is not null || publication.PreviousFactory is not null :
                    publication.PreviousProcess != previous.Process || publication.PreviousFactory != previous.Factory))
                throw new InvalidDataException("Reference flags reused or replaced their actual process/thread publication.");
            previous = publication;
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory)
            throw new InvalidDataException("Reference flags omitted their current fresh publication.");
        uint flags = 0; long sequence = 0; string? boundary = null; var ids = new HashSet<Guid>();
        var threads = new Dictionary<Guid, int>(); var lastPublication = -1;
        foreach (var call in saved.Calls)
        {
            if (call.Source != source.Identity || call.Invocation == Guid.Empty ||
                (call.Enabled is not null || call.WriterBoundary is not null) && !ids.Add(call.Invocation) ||
                call.ConsumerFactory == Guid.Empty || call.Thread < 1 ||
                !publications.TryGetValue(call.Process, out var publication) || publication.Factory != call.Factory ||
                publication.Ordinal < lastPublication || call.Entered != ++sequence || call.Changed != ++sequence ||
                call.Before != flags || call.Returned != (call.Failure is null) || call.Failure is { Length: 0 } ||
                threads.TryGetValue(call.Process, out var thread) && thread != call.Thread)
                throw new InvalidDataException("Reference flag history lost its ordered stores or actual source thread.");
            if (call.WriterBoundary is { } observed) boundary = observed;
            var next = call.Enabled is { } enabled ? enabled ? flags & ~1u : flags | 1 : flags;
            // A rejected child can fail before or after the original store.
            if (call.Returned ? boundary is null && call.After != next || boundary is not null && call.After != flags :
                call.After != flags && call.After != next)
                throw new InvalidDataException("Reference flag receipt invented an unrelated scalar store.");
            flags = call.After; threads.TryAdd(call.Process, call.Thread); lastPublication = publication.Ordinal;
        }
        if (flags != saved.Flags || sequence != saved.Sequence || boundary != saved.WriterBoundary)
            throw new InvalidDataException("Current reference flags disagree with their original store history.");
    }
}
