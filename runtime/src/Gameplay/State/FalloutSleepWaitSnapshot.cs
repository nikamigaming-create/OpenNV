using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutRestPhase { Idle, Choosing, Running, Completed, Cancelled }
internal enum FalloutRestStep { None, MenuBegin, WritePlayerHours, HourPrelude, WorldSeconds, Calendar, HourEffects, HourFinish, Completion, Cancellation, Publication, Retirement, MenuAfterPlayerHours, MenuBeforeCancellation }
internal sealed record FalloutRestFailure(long Attempt, FalloutRestStep Step, string Error);
internal sealed record FalloutRestNativeFailure(long Sequence, FalloutRestStep Step, string Error);
internal sealed record FalloutRestHourReceipt(long Ordinal, float SimulationSeconds, FalloutGameTimeStamp Before,
    FalloutGameTimeStamp? After, bool PreludeCommitted, bool WorldSecondsCommitted, bool CalendarCommitted, bool EffectsCommitted, bool Finished);
internal sealed record FalloutSleepWaitSnapshot(FalloutSleepWaitSource Source, long RequestOrdinal,
    long Attempt, FalloutRestRequest? Request, FalloutRestPhase Phase, int SelectedHours, int RemainingHours,
    long CommittedHours, bool Sleeping, float Countdown, bool MenuPending, bool CompletionEffectsCommitted,
    FalloutRestHourReceipt? LastHour, FalloutRestFailure? Failure, IReadOnlyList<FalloutRestNativeFailure> NativeFailures,
    FalloutRestMenuControlSnapshot? MenuControls)
{
    internal void Validate()
    {
        Source.Validate();
        if (RequestOrdinal < 0 || Attempt < 0 || !Enum.IsDefined(Phase) || SelectedHours < 1 ||
            SelectedHours > Source.MaximumMenuHours || CommittedHours < 0 || !float.IsFinite(Countdown) || Countdown is < 0 or >= 1 ||
            Phase == FalloutRestPhase.Idle && (Request is not null || RequestOrdinal != 0 || RemainingHours != 0 || Sleeping || CommittedHours != 0 || MenuPending) ||
            Phase != FalloutRestPhase.Idle && (Request is null || RequestOrdinal == 0) ||
            Phase == FalloutRestPhase.Cancelled && (RemainingHours != 0 || Sleeping) && Failure is null ||
            Phase == FalloutRestPhase.Completed && Sleeping && Failure is null ||
            Phase == FalloutRestPhase.Choosing && RemainingHours != 0 && !Sleeping && Failure is null)
            throw new InvalidDataException("Saved sleep/wait current state is invalid.");
        Request?.Validate();
        MenuControls?.Validate(Source);
        if ((Request is not null && Request.Origin != FalloutRestOrigin.ScriptHours) != (MenuControls is not null) ||
            MenuControls is { } controls && (controls.Request != RequestOrdinal ||
                controls.Failure is not null && Failure is null || Failure is null &&
                Phase is (FalloutRestPhase.Running or FalloutRestPhase.Completed) && !controls.Counting ||
                Failure is null && Phase == FalloutRestPhase.Choosing && controls.Counting))
            throw new InvalidDataException("Saved rest controls lost their genuine native request/start/failure identity.");
        if (Request?.Origin == FalloutRestOrigin.ScriptHours && MenuPending || CompletionEffectsCommitted && CommittedHours == 0 ||
            Failure is null && Phase == FalloutRestPhase.Cancelled && MenuPending ||
            Failure is null && Phase is (FalloutRestPhase.Choosing or FalloutRestPhase.Running) &&
                Request?.Origin != FalloutRestOrigin.ScriptHours && !MenuPending ||
            Phase == FalloutRestPhase.Idle && (Countdown != 0 || LastHour is not null || CompletionEffectsCommitted || Failure is not null) ||
            LastHour is { CalendarCommitted: false, After: not null })
            throw new InvalidDataException("Saved rest menu/effect receipt is inconsistent with its actual source origin.");
        if (LastHour is { } hour)
        {
            hour.Before.Validate(); hour.After?.Validate();
            if (hour.Ordinal < 1 || !float.IsFinite(hour.SimulationSeconds) || hour.SimulationSeconds <= 0 ||
                hour.WorldSecondsCommitted && !hour.PreludeCommitted ||
                hour.CalendarCommitted && (!hour.WorldSecondsCommitted || hour.After is null) ||
                hour.EffectsCommitted && !hour.CalendarCommitted || hour.Finished && !hour.EffectsCommitted ||
                hour.Ordinal != CommittedHours + (hour.Finished ? 0 : 1))
                throw new InvalidDataException("Saved rest hour has an invalid genuine commit prefix.");
        }
        if (CommittedHours > 0 && LastHour is null || Failure is { } fault &&
            (fault.Attempt < 1 || fault.Attempt != Attempt || !Enum.IsDefined(fault.Step) || fault.Step == FalloutRestStep.None || string.IsNullOrWhiteSpace(fault.Error)))
            throw new InvalidDataException("Saved rest failure/history has no exact attempted operation.");
        if (NativeFailures is null || NativeFailures.Select((failure, index) => failure.Sequence != index + 1L ||
            !Enum.IsDefined(failure.Step) || failure.Step == FalloutRestStep.None || string.IsNullOrWhiteSpace(failure.Error)).Any(invalid => invalid) ||
            NativeFailures.Count != 0 && Failure is null)
            throw new InvalidDataException("Saved rest native failures have no actual ordered retained lifetime.");
    }
    internal void RequireSource(FalloutPluginStack records, FalloutAdvancementRuntimeReceipt runtime)
    {
        Validate(); Source.RequireCurrent(FalloutSleepWaitSource.Read(records, runtime));
        if (Request?.BedSource is { } bed) bed.RequireCurrent(FalloutSleepWaitBed.ReadCurrent(records, bed.Reference));
    }
    internal void RequirePhysical(FalloutPlayerPhysicalSnapshot physical)
    {
        Validate(); physical.Validate();
        if (physical.Source.RuntimeSha256 != Source.RuntimeSha256 || physical.Sleeping != Sleeping ||
            Sleeping && (Request is null || Phase is not (FalloutRestPhase.Choosing or FalloutRestPhase.Running) && !MenuPending))
            throw new InvalidDataException("Saved rest state has no exact current physical-player continuation.");
    }
}
