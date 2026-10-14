namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutSourceFrameDeltaReader { SceneClock, ActorTime }
internal sealed record FalloutSourceFrameDeltaReadOwner(FalloutSourceFrameDeltaReader Reader,
    Guid Consumer, Guid Invocation, Guid Main, long Ordinal, Guid Process);
internal sealed record FalloutSourceFrameTimerPublication(Guid Process, long Sequence, Guid? PreviousProcess);
internal sealed record FalloutSourceFrameTimerReadInterval(Guid WriterProcess, string? Operation,
    long Entered, long Returned, long? Closed, uint BeforeBits, uint Bits,
    long BeforeUpdated, long Updated);
internal sealed record FalloutSourceFrameTimerRetainedRead(FalloutSourceFrameDeltaRead Read,
    FalloutSourceFrameTimerReadInterval Interval);
internal sealed record FalloutSourceFrameTimerReadProvenance(int Version,
    IReadOnlyList<FalloutSourceFrameTimerPublication> Publications,
    FalloutSourceFrameTimerReadInterval Current,
    IReadOnlyList<FalloutSourceFrameTimerRetainedRead> Reads);

internal sealed partial class FalloutSourceFrameTimerState
{
    private const int ReadProvenanceVersion = 1;
    private const string UpdateOperation = "cached-timer-update";
    private const string RateOperation = "source-rate-target-store";
    private readonly List<FalloutSourceFrameTimerPublication> _readPublications = [];
    private readonly Dictionary<FalloutSourceFrameDeltaReader, FalloutSourceFrameTimerRetainedRead> _retainedReads = [];
    private FalloutSourceFrameTimerReadInterval _readInterval = null!;
    private uint _writerBeforeBits;
    private long _writerBeforeUpdated;

    // Reads retain proof metadata only. They never advance the source counter,
    // timer sequence, scalar, update ordinal or Main invocation.
    internal FalloutSourceFrameDeltaRead ReadScaledDelta(FalloutSourceFrameDeltaReadOwner owner)
    {
        RequireHealthy(); ValidateReadOwner(owner);
        if (owner.Process != _process)
            throw new InvalidOperationException("Typed cached timer read changed its actual current consumer process.");
        var read = ReadScaledDelta() with { Owner = owner };
        _retainedReads[owner.Reader] = new(read, _readInterval);
        return read;
    }

    private void InitializeReadProvenance(FalloutSourceFrameTimerSnapshot? restored)
    {
        if (restored is null)
        {
            _readPublications.Add(new(_process, 0, null));
            _readInterval = new(_process, null, 0, 0, null, 0, 0, 0, 0);
            return;
        }
        var provenance = restored.ReadProvenance ??
            throw new InvalidDataException("Cold timer omitted its actual scalar/read provenance.");
        _readPublications.AddRange(provenance.Publications);
        _readPublications.Add(new(_process, _sequence, restored.CapturedProcess));
        _readInterval = provenance.Current;
        foreach (var read in provenance.Reads) _retainedReads.Add(read.Read.Owner!.Reader, read);
    }

    private void EnterReadWriter()
    {
        _writerBeforeBits = BitConverter.SingleToUInt32Bits(_scaled);
        _writerBeforeUpdated = _updated;
        var closing = _readInterval with { Closed = _enteredAt };
        foreach (var reader in _retainedReads.Keys.ToArray())
        {
            var retained = _retainedReads[reader];
            if (retained.Interval == _readInterval)
                _retainedReads[reader] = retained with { Interval = closing };
        }
        _readInterval = closing;
    }

    private void ReturnReadWriter(string operation) => _readInterval = new(_process, operation,
        _enteredAt, _returnedThrough, null, _writerBeforeBits,
        BitConverter.SingleToUInt32Bits(_scaled), _writerBeforeUpdated, _updated);

    private FalloutSourceFrameTimerReadProvenance CaptureReadProvenance() => new(ReadProvenanceVersion,
        _readPublications.ToArray(), _readInterval,
        _retainedReads.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray());

    // Parent callers provide the exact actual consumer/call identity. Historical
    // reads join to their own returned interval, rather than the newest scalar.
    internal static void ValidateRetainedRead(FalloutSourceFrameTimerSnapshot timer,
        FalloutSourceFrameDeltaRead read, FalloutSourceFrameDeltaReadOwner expectedOwner)
    {
        Validate(timer); ValidateReadOwner(expectedOwner);
        if (read is null || read.Process != expectedOwner.Process || read.Owner != expectedOwner || timer.ReadProvenance!.Reads
            .SingleOrDefault(retained => retained.Read.Owner!.Reader == expectedOwner.Reader)?.Read != read)
            throw new InvalidDataException("Cached delta has no identical actual typed timer read/consumer receipt.");
    }

    private static void ValidateReadOwner(FalloutSourceFrameDeltaReadOwner owner)
    {
        if (owner is null || !Enum.IsDefined(owner.Reader) || owner.Consumer == Guid.Empty ||
            owner.Invocation == Guid.Empty || owner.Main == Guid.Empty || owner.Ordinal < 1 || owner.Process == Guid.Empty ||
            owner.Reader == FalloutSourceFrameDeltaReader.SceneClock && owner.Invocation != owner.Main)
            throw new InvalidDataException("Cached timer read omitted its actual typed consumer and Main call.");
    }

    private static bool KnownReadWriter(string operation) => operation is
        "fixed-millisecond-store" or "source-INI-rate-mode-store" or RateOperation or
        "source-rate-adjustment" or "nested-source-pause" or "nested-source-resume" or UpdateOperation;

    private static void ValidateReadProvenance(FalloutSourceFrameTimerSnapshot saved)
    {
        var provenance = saved.ReadProvenance;
        if (provenance is null || provenance.Version != ReadProvenanceVersion ||
            provenance.Publications is not { Count: > 0 } || provenance.Reads is null ||
            provenance.Reads.Count > Enum.GetValues<FalloutSourceFrameDeltaReader>().Length || provenance.Current is null)
            throw new InvalidDataException("Cached timer omitted its bounded real scalar/read owner history.");
        var publications = new Dictionary<Guid, (long Start, long End)>();
        for (var index = 0; index < provenance.Publications.Count; index++)
        {
            var publication = provenance.Publications[index];
            if (publication is null || publication.Process == Guid.Empty || publication.Sequence < 0 ||
                publication.Sequence > saved.Sequence ||
                (index == 0 ? publication.Sequence != 0 || publication.PreviousProcess is not null :
                    publication.Sequence < provenance.Publications[index - 1].Sequence ||
                    publication.PreviousProcess != provenance.Publications[index - 1].Process) ||
                !publications.TryAdd(publication.Process, (publication.Sequence,
                    index + 1 < provenance.Publications.Count ? provenance.Publications[index + 1].Sequence : saved.Sequence)))
                throw new InvalidDataException("Cached timer substituted a process or replayed its cold publication.");
        }
        var initial = provenance.Publications[0].Process;
        var initialEpoch = BitConverter.ToInt64(initial.ToByteArray()) & long.MaxValue;
        if (initialEpoch == 0) initialEpoch = long.MaxValue;
        if (initialEpoch != saved.Epoch || provenance.Publications[^1].Process != saved.CapturedProcess)
            throw new InvalidDataException("Cached timer relabelled its actual constructor epoch/current process.");
        ValidateInterval(provenance.Current);
        var current = provenance.Current;
        if (current.Bits != BitConverter.SingleToUInt32Bits(saved.ScaledSeconds) || current.Updated != saved.Updated ||
            current.Returned != saved.ReturnedThrough ||
            (saved.Failure is null ? current.Closed is not null || current.Entered != saved.EnteredAt :
                current.Closed != saved.EnteredAt || saved.EnteredOperation is not { } failed || !KnownReadWriter(failed)))
            throw new InvalidDataException("Cached timer current fields differ from their real last returned writer interval.");
        var readers = new HashSet<FalloutSourceFrameDeltaReader>();
        foreach (var retained in provenance.Reads)
        {
            if (retained is null || retained.Read is not { Owner: { } owner } read || retained.Interval is null)
                throw new InvalidDataException("Cached timer retained a read without its actual owner/interval.");
            ValidateReadOwner(owner); ValidateInterval(retained.Interval);
            var interval = retained.Interval;
            if (!readers.Add(owner.Reader) || read.Source != saved.Source.EngineSha256 || read.Epoch != saved.Epoch || read.Process != owner.Process ||
                !publications.TryGetValue(read.Process, out var publication) ||
                read.Sequence < publication.Start || read.Sequence > publication.End ||
                read.Sequence < interval.Returned || interval.Closed is { } end && read.Sequence >= end ||
                read.ScaledSecondsBits != interval.Bits || read.HasReturnedUpdate != (interval.Updated > 0) ||
                interval.Closed is null && interval != current ||
                interval.Closed is { } closed && closed > current.Entered)
                throw new InvalidDataException("Cached timer read escaped its actual process/returned scalar value interval.");
        }

        void ValidateInterval(FalloutSourceFrameTimerReadInterval interval)
        {
            if (!publications.TryGetValue(interval.WriterProcess, out var publication) ||
                interval.Entered < publication.Start || interval.Returned > publication.End ||
                interval.Returned > saved.Sequence || !float.IsFinite(BitConverter.UInt32BitsToSingle(interval.Bits)) ||
                // Update overwrites the scalar in every source arm before its
                // returned value is read. A genuine zero-rate division may
                // leave a nonfinite prior intermediate; other writers cannot
                // erase that provenance by relabelling themselves as updates.
                !float.IsFinite(BitConverter.UInt32BitsToSingle(interval.BeforeBits)) && interval.Operation != UpdateOperation ||
                interval.Closed is { } closed && (closed <= interval.Returned || closed > saved.Sequence))
                throw new InvalidDataException("Cached scalar interval escaped its actual source writer/process lifetime.");
            if (interval.Operation is null)
            {
                if (interval.WriterProcess != initial || interval.Entered != 0 || interval.Returned != 0 ||
                    interval.BeforeBits != 0 || interval.Bits != 0 || interval.BeforeUpdated != 0 || interval.Updated != 0)
                    throw new InvalidDataException("Cached timer substituted a value for its genuine constructor-zero interval.");
                return;
            }
            if (!KnownReadWriter(interval.Operation) || interval.Entered <= publication.Start ||
                interval.BeforeUpdated < 0 || interval.BeforeUpdated >= interval.Entered || interval.Updated > saved.Updated ||
                (interval.Operation == UpdateOperation ? interval.Updated != interval.Entered + 1 ||
                    interval.Returned != interval.Entered + 2 :
                    interval.Updated != interval.BeforeUpdated || interval.Returned != interval.Entered + 1) ||
                interval.Operation is not (UpdateOperation or RateOperation) && interval.BeforeBits != interval.Bits)
                throw new InvalidDataException("Cached timer interval fabricated a returned writer or scalar/update store.");
        }
    }
}
