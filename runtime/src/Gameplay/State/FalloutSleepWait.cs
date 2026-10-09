using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutRestHour(long Ordinal, FalloutRestKind Kind, float SimulationSeconds,
    FalloutGameTimeStamp Before, FalloutGameTimeStamp After, bool Sleeping);
internal sealed record FalloutSleepWaitHost(
    Func<FalloutRestRequest, FalloutRestFact, FalloutRestObservation> Observe,
    Action<FalloutRestRequest> BeginMenuCountdown,
    Action<bool, Action> WritePlayerSleepFlag,
    Action<FalloutRestHour> ApplyHourPrelude,
    Action<float> AdvanceWorldSeconds,
    Action<FalloutRestHour> ApplyHourEffects,
    Action<FalloutRestRequest> CompleteSleep,
    Action<FalloutRestRequest, bool> CloseMenu,
    Func<bool> ReadPlayerSleeping)
{
    internal required Action<FalloutRestRequest> AfterMenuPlayerHours { get; init; }
    internal required Action<FalloutRestRequest> BeforeCancelPlayerHours { get; init; }
}

// One current campaign owner for player Rest, source menu requests and original
// signed hour writes. Godot consumes this state; it does not invent time/effects.
internal sealed class FalloutSleepWait
{
    internal FalloutSleepWaitSource Source { get; }
    private readonly FalloutGameTime _clock;
    private readonly FalloutSleepWaitHost _host;
    internal long RequestOrdinal { get; private set; }
    internal long Attempt { get; private set; }
    internal FalloutRestRequest? Request { get; private set; }
    internal FalloutRestPhase Phase { get; private set; }
    internal int SelectedHours { get; private set; } = 1;
    internal int RemainingHours { get; private set; }
    internal long CommittedHours { get; private set; }
    internal bool Sleeping { get; private set; }
    internal float Countdown { get; private set; }
    internal FalloutRestHourReceipt? LastHour { get; private set; }
    internal FalloutRestFailure? Failure { get; private set; }
    internal bool Published { get; private set; }
    internal bool MenuPending { get; private set; }
    internal bool CompletionEffectsCommitted { get; private set; }
    private bool _busy;
    private ulong? _lastPlayerFrame;
    private string? _prefixReadFailure;
    private readonly List<FalloutRestNativeFailure> _nativeFailures = [];
    internal bool Active => Phase is FalloutRestPhase.Choosing or FalloutRestPhase.Running;
    internal bool NeedsMenuPublication => MenuPending && Request?.Origin != FalloutRestOrigin.ScriptHours;
    internal bool OwnsClock => Phase == FalloutRestPhase.Running && Request?.Origin != FalloutRestOrigin.ScriptHours;
    internal bool OwnsPhysicalSleepContinuation => Sleeping && Request is not null && (Active || MenuPending);
    internal object State => new { Source, RequestOrdinal, Attempt, Request, Phase, SelectedHours, RemainingHours,
        CommittedHours, Sleeping, Countdown, MenuPending, CompletionEffectsCommitted, LastHour, Failure, Published,
        nativeFailures = _nativeFailures.ToArray(), prefixReadFailure = _prefixReadFailure, saveBlocker = SaveBlocker };
    internal string? SaveBlocker => _busy ? "sleep-wait-operation-prefix" :
        _prefixReadFailure is not null ? "sleep-wait-physical-prefix-unread" : null;

    internal FalloutSleepWait(FalloutSleepWaitSource source, FalloutGameTime clock, FalloutSleepWaitHost host,
        FalloutSleepWaitSnapshot? restore = null)
    {
        source.Validate(); Source = source; _clock = clock; _host = host;
        ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(host);
        if (clock.CarryAtDayBoundary != source.CarryAtDayBoundary)
            throw new InvalidDataException("Rest and ordinary clock use different selected calendar boundary rules.");
        if (restore is null) return;
        restore.Validate(); restore.Source.RequireCurrent(source);
        RequestOrdinal = restore.RequestOrdinal; Attempt = restore.Attempt; Request = restore.Request; Phase = restore.Phase;
        SelectedHours = restore.SelectedHours; RemainingHours = restore.RemainingHours; CommittedHours = restore.CommittedHours;
        Sleeping = restore.Sleeping; Countdown = restore.Countdown; LastHour = restore.LastHour; Failure = restore.Failure;
        MenuPending = restore.MenuPending; CompletionEffectsCommitted = restore.CompletionEffectsCommitted;
        _nativeFailures.AddRange(restore.NativeFailures);
        if ((OwnsClock || NeedsMenuPublication) && LastHour is { CalendarCommitted: true, After: { } after } && !_clock.Stamp().HasSameBits(after))
            throw new InvalidDataException("Cold rest hour differs from the actual restored global/calendar prefix.");
        // No process-local input/view lease survives cold, and no failed prefix
        // is retried. A new publication must match the restored physical flag.
    }
    internal FalloutRestAdmission Inspect(FalloutRestRequest request) => FalloutSleepWaitAdmission.Inspect(request, _host.Observe);
    internal void Open(FalloutRestRequest request)
    {
        RequireHealthy(); if (Active || MenuPending) throw new InvalidOperationException("Another source rest request is active.");
        Inspect(request).Require();
        RequestOrdinal = checked(RequestOrdinal + 1); Request = request; Phase = FalloutRestPhase.Choosing;
        SelectedHours = 1; RemainingHours = 0; CommittedHours = 0; Countdown = 0; LastHour = null; Published = false;
        MenuPending = true; CompletionEffectsCommitted = false;
    }
    internal void Publish(long requestOrdinal)
    {
        RequireHealthy();
        if (!NeedsMenuPublication || requestOrdinal != RequestOrdinal || Published || _host.ReadPlayerSleeping() != Sleeping)
            throw new InvalidOperationException("Native rest publication differs from the actual current source/physical request.");
        Published = true;
    }
    internal void RetirePublication() => Published = false;
    internal void Select(int hours)
    {
        RequirePublished();
        if (Phase != FalloutRestPhase.Choosing || hours < 1 || hours > Source.MaximumMenuHours)
            throw new InvalidOperationException("Rest selection is outside the current source slider.");
        SelectedHours = hours;
    }
    internal void Begin()
    {
        RequirePublished(); if (Phase != FalloutRestPhase.Choosing) throw new InvalidOperationException("Rest countdown has already begun.");
        Inspect(Request!).Require();
        Execute(FalloutRestStep.MenuBegin, () => _host.BeginMenuCountdown(Request!));
        WriteHours(SelectedHours, Request!.Kind == FalloutRestKind.Sleep);
        Execute(FalloutRestStep.MenuAfterPlayerHours, () => _host.AfterMenuPlayerHours(Request!));
        Phase = FalloutRestPhase.Running;
    }
    internal void SetScriptHours(int hours)
    {
        RequireHealthy();
        if (Request is null || !Active && !MenuPending)
        {
            var request = new FalloutRestRequest(FalloutRestKind.Sleep, FalloutRestOrigin.ScriptHours);
            Inspect(request).Require(); RequestOrdinal = checked(RequestOrdinal + 1); Request = request;
            CommittedHours = 0; Countdown = 0; LastHour = null; Published = false;
            CompletionEffectsCommitted = false; MenuPending = false; SelectedHours = 1;
            Phase = FalloutRestPhase.Running;
        }
        // Original signed storage is independent of the menu slider. Zero and
        // negative counts still set IsPCSleeping. The actual later player
        // update consumes that flag; this write does not start a menu countdown.
        WriteHours(hours, sleeping: true);
        // A genuine source write can replace the counter before a completed
        // menu reaches its next close check. Preserve earlier effect receipts
        // while its actual counting owner resumes with the new signed value.
        if (Phase == FalloutRestPhase.Completed && MenuPending) Phase = FalloutRestPhase.Running;
    }
    private void WriteHours(int hours, bool sleeping) => Execute(FalloutRestStep.WritePlayerHours, () =>
    {
        _host.WritePlayerSleepFlag(sleeping, () => RemainingHours = hours);
        Sleeping = _host.ReadPlayerSleeping();
        if (Sleeping != sleeping) throw new InvalidOperationException("Source hour write has no exact independent sleeping-flag receipt.");
    });
    internal bool AdvanceCountdown(float frameSeconds)
    {
        RequireHealthy();
        if (!float.IsFinite(frameSeconds) || frameSeconds < 0) throw new ArgumentOutOfRangeException(nameof(frameSeconds));
        if (Phase != FalloutRestPhase.Running || Request?.Origin == FalloutRestOrigin.ScriptHours) return false;
        RequirePublished();
        if (RemainingHours <= 0) { Cancel(); return false; }
        var next = Countdown + frameSeconds;
        if (!float.IsFinite(next)) throw new InvalidDataException("Source rest frame clock overflowed.");
        if (next < 1) { Countdown = next; return false; }
        Countdown = 0; AdvanceHour(); return true;
    }
    internal bool AdvancePlayerUpdate(ulong nativeFrame)
    {
        RequireHealthy();
        if (_lastPlayerFrame is { } last && nativeFrame <= last)
            throw new InvalidOperationException("Source sleep cannot consume the same or an older actual player update.");
        _lastPlayerFrame = nativeFrame;
        if (!Sleeping || !Active || OwnsClock) return false;
        AdvanceHour(); return true;
    }
    private void AdvanceHour()
    {
        var seconds = _clock.RestHourSimulationSeconds();
        var before = _clock.Stamp(); var ordinal = checked(CommittedHours + 1);
        LastHour = new(ordinal, seconds, before, null, false, false, false, false, false);
        Execute(FalloutRestStep.HourPrelude, () =>
        {
            _host.ApplyHourPrelude(new(ordinal, Request!.Kind, seconds, before, before, Sleeping));
            LastHour = LastHour! with { PreludeCommitted = true };
        });
        Execute(FalloutRestStep.WorldSeconds, () =>
        {
            _host.AdvanceWorldSeconds(seconds); LastHour = LastHour! with { WorldSecondsCommitted = true };
        });
        Execute(FalloutRestStep.Calendar, () =>
        {
            _clock.AdvanceSimulation(seconds);
            LastHour = LastHour! with { CalendarCommitted = true, After = _clock.Stamp() };
        });
        var hour = new FalloutRestHour(ordinal, Request!.Kind, seconds, before, LastHour!.After!, Sleeping);
        Execute(FalloutRestStep.HourEffects, () =>
        {
            _host.ApplyHourEffects(hour); LastHour = LastHour! with { EffectsCommitted = true };
        });
        Execute(FalloutRestStep.HourFinish, () =>
        {
            RemainingHours = unchecked(RemainingHours - 1); CommittedHours = checked(CommittedHours + 1);
            LastHour = LastHour! with { Finished = true };
        });
        if (RemainingHours <= 0) Complete();
    }
    private void Complete()
    {
        Execute(FalloutRestStep.Completion, () =>
        {
            if (Sleeping) { _host.CompleteSleep(Request!); CompletionEffectsCommitted = true; }
            _host.WritePlayerSleepFlag(false, () => { });
            Sleeping = _host.ReadPlayerSleeping();
            if (Sleeping) throw new InvalidOperationException("Sleep completion did not retire its independent player flag.");
            Phase = FalloutRestPhase.Completed;
        });
    }
    internal void RetireCompletedMenu()
    {
        RequirePublished();
        if (Phase != FalloutRestPhase.Completed || !MenuPending)
            throw new InvalidOperationException("Rest menu has no new completed counter receipt.");
        Execute(FalloutRestStep.Retirement, () =>
        { _host.CloseMenu(Request!, true); MenuPending = false; });
    }
    internal void Cancel()
    {
        RequireHealthy(); if (!Active && !MenuPending) throw new InvalidOperationException("No living rest request can cancel.");
        if (Request!.Origin != FalloutRestOrigin.ScriptHours)
            Execute(FalloutRestStep.MenuBeforeCancellation, () => _host.BeforeCancelPlayerHours(Request));
        Execute(FalloutRestStep.Cancellation, () =>
        {
            _host.WritePlayerSleepFlag(false, () => RemainingHours = 0);
            Sleeping = _host.ReadPlayerSleeping();
            if (Sleeping) throw new InvalidOperationException("Rest cancellation did not retire its independent player flag.");
            Countdown = 0; Phase = FalloutRestPhase.Cancelled;
            if (Request!.Origin != FalloutRestOrigin.ScriptHours) _host.CloseMenu(Request, false);
            MenuPending = false;
        });
    }
    internal void ReportNativeFailure(FalloutRestStep step, Exception error)
    {
        if (!Enum.IsDefined(step) || step == FalloutRestStep.None) throw new ArgumentOutOfRangeException(nameof(step));
        _nativeFailures.Add(new(checked(_nativeFailures.Count + 1L), step,
            error.GetType().Name + ": " + error.Message));
        if (Failure is not null) return;
        Attempt = checked(Attempt + 1); Retain(step, error);
    }
    private void Execute(FalloutRestStep step, Action action)
    {
        RequireHealthy(); if (_busy) throw new InvalidOperationException("Rest operation reentry has no source ordering owner.");
        Attempt = checked(Attempt + 1); _busy = true;
        try { action(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            // The native physical owner may retain a flag mutation before a
            // callback failed. Capture that genuine prefix, not the old copy.
            // ReadPlayerSleeping is a pure committed-prefix read of the same
            // physical owner. It deliberately does not retry a failed action.
            Retain(step, error);
            try { Sleeping = _host.ReadPlayerSleeping(); }
            catch (Exception query) when (FalloutPlayerPhysicalActivity.Ordinary(query))
            { _prefixReadFailure = query.GetType().Name + ": " + query.Message; }
            throw;
        }
        finally { _busy = false; }
    }
    private void Retain(FalloutRestStep step, Exception error) => Failure ??= new(Attempt, step,
        string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message);
    internal void RequireHealthy()
    {
        if (Failure is { } fault) throw new InvalidOperationException($"Rest operation {fault.Attempt}:{fault.Step} failed: {fault.Error}");
    }
    internal int ReadPlayerHours() { RequireHealthy(); return RemainingHours; }
    private void RequirePublished()
    {
        RequireHealthy(); if (!Published) throw new NotSupportedException("Rest input/countdown has no actual current native publication.");
    }
    internal FalloutSleepWaitSnapshot Capture()
    {
        if (SaveBlocker is { } blocker) throw new NotSupportedException("Capture requires " + blocker);
        var result = new FalloutSleepWaitSnapshot(Source, RequestOrdinal, Attempt, Request, Phase, SelectedHours,
            RemainingHours, CommittedHours, Sleeping, Countdown, MenuPending, CompletionEffectsCommitted, LastHour, Failure, _nativeFailures.ToArray());
        result.Validate(); return result;
    }
}
