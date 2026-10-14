using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutGlobalState
{
    private string? _frameCalendarSource, _frameCalendarStack, _frameCalendarWinning;
    private Guid _frameCalendarProcess;
    private readonly Guid _frameCalendarFactory = Guid.NewGuid();
    private FalloutFormKey _frameCalendarForm;
    private long _frameCalendarSequence, _frameCalendarRevision, _frameCalendarFault;
    private bool _frameCalendarBusy;
    private Action? _requireFrameCalendarLifetime;
    private FalloutPlayerFrameCalendarSnapshot? _preparedFrameCalendar;
    private FalloutPlayerLateCalendarDayRead? _lastFrameCalendarRead;
    private readonly List<FalloutPlayerFrameCalendarPublication> _frameCalendarPublications = [];

    internal void BindPlayerFrameCalendar(FalloutPlayerLateActorTimeSource source, string stack,
        Guid process, FalloutFormKey day, Action requireLifetime)
    {
        source.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        ArgumentNullException.ThrowIfNull(requireLifetime); requireLifetime();
        if (_frameCalendarSource is not null || process == Guid.Empty || _frameCalendarBusy)
            throw new InvalidOperationException("Frame calendar requires its fresh actual GLOBAL/Scene owner.");
        var winning = Source(day).SourceSha256;
        if (_preparedFrameCalendar is { } previous)
        {
            if (previous.Source != source.Identity || previous.Stack != stack || previous.Form != day ||
                previous.WinningSha256 != winning || previous.Publications.Any(row => row.Process == process || row.Factory == _frameCalendarFactory))
                throw new InvalidDataException("Cold frame calendar substituted a source/form or reused a retained owner.");
            _frameCalendarSequence = previous.Sequence; _frameCalendarRevision = previous.ValueRevision;
            _lastFrameCalendarRead = previous.LastRead;
            _frameCalendarPublications.AddRange(previous.Publications);
            _frameCalendarPublications.Add(new(process, _frameCalendarFactory, checked(++_frameCalendarSequence),
                previous.Process, previous.Factory));
        }
        else _frameCalendarPublications.Add(new(process, _frameCalendarFactory, 0));
        _frameCalendarSource = source.Identity; _frameCalendarStack = stack; _frameCalendarProcess = process;
        _frameCalendarForm = day; _frameCalendarWinning = winning; _requireFrameCalendarLifetime = requireLifetime;
        _preparedFrameCalendar = null;
    }

    // Keep one actual read, since Actor-time itself retains one latest call.
    // Later Set writes change the value revision; they do not rewrite that read.
    internal FalloutPlayerLateCalendarDayRead ReadPlayerFrameCalendarDay(FalloutFormKey day,
        FalloutPlayerLateFrameInvocation invocation, Action<FalloutPlayerLateFrameInvocation> requireActual)
    {
        ArgumentNullException.ThrowIfNull(invocation); ArgumentNullException.ThrowIfNull(requireActual);
        if (_frameCalendarBusy) { _frameCalendarFault++; throw new InvalidOperationException("Frame calendar read reentered."); }
        RequireBoundFrameCalendar();
        var fault = _frameCalendarFault; var revision = _frameCalendarRevision;
        _frameCalendarBusy = true;
        try
        {
            Check(); var source = Source(day); var bits = BitConverter.SingleToUInt32Bits(Get(day)); Check();
            if (source.SourceSha256 != _frameCalendarWinning)
                throw new InvalidDataException("Frame calendar lost its actual winning GLOBAL source.");
            var owner = new FalloutPlayerFrameCalendarReadOwner(_frameCalendarSource!, _frameCalendarStack!,
                _frameCalendarProcess, _frameCalendarFactory, checked(++_frameCalendarSequence), revision,
                invocation.Caller, invocation.PlayerConstructor, invocation.Common.Process, invocation.Common.Epoch, invocation.ThreadId);
            return _lastFrameCalendarRead = new(day, source.SourceSha256, bits, FalloutGameTime.ActorFrameSignedDay(bits), owner);
        }
        finally { _frameCalendarBusy = false; }

        void Check()
        {
            RequireBoundFrameCalendar(); requireActual(invocation);
            if (day != _frameCalendarForm || invocation.PlayerProcess != _frameCalendarProcess ||
                invocation.Common.Stack != _frameCalendarStack || invocation.Caller.Main == Guid.Empty ||
                invocation.Caller.Invocation == Guid.Empty || invocation.Caller.Ordinal < 1 ||
                invocation.PlayerConstructor == Guid.Empty || invocation.Common.Process == Guid.Empty || invocation.Common.Epoch < 1 ||
                invocation.ThreadId != Environment.CurrentManagedThreadId || revision != _frameCalendarRevision || fault != _frameCalendarFault)
                throw new InvalidOperationException("Frame calendar lost its actual Scene/Common caller, thread or preceding GLOBAL writer.");
        }
    }

    private void ObservePlayerFrameCalendarWrite(FalloutFormKey form)
    {
        if (_preparedFrameCalendar?.Form == form)
            throw new InvalidOperationException("Cold calendar GLOBAL cannot change before its actual read owner is bound.");
        if (_frameCalendarSource is null || form != _frameCalendarForm) return;
        RequireBoundFrameCalendar();
        if (_frameCalendarBusy) { _frameCalendarFault++; throw new InvalidOperationException("Calendar GLOBAL writer reentered an actual frame read."); }
        var sequence = checked(_frameCalendarSequence + 1); var revision = checked(_frameCalendarRevision + 1);
        _frameCalendarSequence = sequence; _frameCalendarRevision = revision;
    }

    private void RequireBoundFrameCalendar()
    {
        if (_frameCalendarSource is null || _requireFrameCalendarLifetime is null)
            throw new NotSupportedException("source-calendar-GLOBAL-current-frame-owner-unbound");
        _requireFrameCalendarLifetime();
    }

    private FalloutPlayerFrameCalendarSnapshot? CaptureRetainedPlayerFrameCalendar() => _frameCalendarSource is null ?
        _preparedFrameCalendar : new(_frameCalendarSource, _frameCalendarStack!, _frameCalendarProcess, _frameCalendarFactory,
            _frameCalendarForm, _frameCalendarWinning!, _frameCalendarSequence, _frameCalendarRevision,
            _lastFrameCalendarRead, Array.AsReadOnly(_frameCalendarPublications.ToArray()));

    private void PrepareColdPlayerFrameCalendar(FalloutGlobalStateSnapshot saved)
    {
        if (_frameCalendarSource is not null || _frameCalendarBusy || _preparedFrameCalendar is not null)
            throw new InvalidOperationException("Bound GLOBAL owner cannot be restored over a live frame-calendar domain.");
        if (saved.PlayerFrameCalendar is not { } calendar) return;
        ValidatePlayerFrameCalendar(calendar, saved.Values);
        if (Source(calendar.Form).SourceSha256 != calendar.WinningSha256)
            throw new InvalidDataException("Cold frame calendar differs from its actual winning GLOBAL record.");
        _preparedFrameCalendar = calendar with { Publications = Array.AsReadOnly(calendar.Publications.ToArray()) };
    }

    internal static void ValidatePlayerFrameCalendar(FalloutPlayerFrameCalendarSnapshot saved,
        IReadOnlyList<FalloutGlobalValueSnapshot> values)
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(saved.Source) || string.IsNullOrWhiteSpace(saved.Stack) ||
            !FalloutAdvancementRuntimeReceipt.Digest(saved.WinningSha256) || saved.Form.ObjectId == 0 ||
            saved.Process == Guid.Empty || saved.Factory == Guid.Empty || saved.Sequence < 0 || saved.ValueRevision < 0 ||
            saved.Publications is not { Count: > 0 } || saved.Publications.Any(row => row is null))
            throw new InvalidDataException("Frame-calendar snapshot lost its actual source, winning GLOBAL or owner.");
        var matching = values.Where(row => row.Form == saved.Form).ToArray();
        if (matching is not [var value] || value.SourceSha256 != saved.WinningSha256)
            throw new InvalidDataException("Frame calendar has no identical winning GLOBAL in its real parent capture.");
        var factories = new HashSet<Guid>(); var processes = new Dictionary<Guid, (Guid Factory, long Start, long End)>();
        FalloutPlayerFrameCalendarPublication? previous = null;
        for (var index = 0; index < saved.Publications.Count; index++)
        {
            var row = saved.Publications[index];
            if (row is null || row.Process == Guid.Empty || row.Factory == Guid.Empty || !factories.Add(row.Factory) ||
                row.Changed > saved.Sequence || (previous is null ? row.Changed != 0 || row.PreviousProcess is not null || row.PreviousFactory is not null :
                    row.Changed <= previous.Changed || row.PreviousProcess != previous.Process || row.PreviousFactory != previous.Factory) ||
                !processes.TryAdd(row.Process, (row.Factory, row.Changed,
                    index + 1 < saved.Publications.Count ? saved.Publications[index + 1].Changed : checked(saved.Sequence + 1))))
                throw new InvalidDataException("Frame calendar reused or disconnected a current/cold publication.");
            previous = row;
        }
        if (previous!.Process != saved.Process || previous.Factory != saved.Factory)
            throw new InvalidDataException("Frame calendar promoted a historical owner as current.");
        if (saved.LastRead is not { } read) return;
        if (read.Form != saved.Form || read.WinningSha256 != saved.WinningSha256 || read.SignedDay != FalloutGameTime.ActorFrameSignedDay(read.Bits) ||
            read.Owner is not { } owner || owner.Source != saved.Source || owner.Stack != saved.Stack || owner.ValueRevision < 0 ||
            owner.ValueRevision > saved.ValueRevision || !processes.TryGetValue(owner.Process, out var interval) || owner.Factory != interval.Factory ||
            owner.Sequence <= interval.Start || owner.Sequence >= interval.End || owner.Caller is null || owner.Caller.Main == Guid.Empty ||
            owner.Caller.Invocation == Guid.Empty || owner.Caller.Ordinal < 1 || owner.Constructor == Guid.Empty || owner.CommonProcess == Guid.Empty ||
            owner.Epoch < 1 || owner.ThreadId < 1 || owner.ValueRevision == saved.ValueRevision &&
                read.Bits != BitConverter.SingleToUInt32Bits(value.Value))
            throw new InvalidDataException("Frame calendar lost its retained actual read or borrowed a newer GLOBAL value.");
    }
}
