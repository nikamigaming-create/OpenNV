using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class RecordedInputPlayback
{
    private readonly RecordedInputTape _tape;
    private readonly long _maximumLatenessMicroseconds;
    private readonly bool _unjoinedDiagnostic;
    private long _lastMicroseconds;
    internal int Cursor { get; private set; }
    internal string? Error { get; private set; }
    internal long MaximumLatenessMicroseconds { get; private set; }
    internal bool Complete { get; private set; }
    internal bool Active => !Complete && Error is null;
    internal long? AttemptedOrdinal { get; private set; }
    internal bool DeliveryEntered { get; private set; }
    internal bool DeliveryReturned { get; private set; }
    internal long? DeliveryReturnedOrdinal { get; private set; }
    internal string? ReleaseError { get; private set; }
    internal object State => new
    {
        active = Active,
        complete = Complete,
        cursor = Cursor,
        inputs = _tape.Inputs.Count,
        recordedEngine = _tape.Header.Engine,
        error = Error,
        maximumLatenessMicroseconds = MaximumLatenessMicroseconds,
        alignment = _unjoinedDiagnostic ? "unjoined-diagnostic" : "checkpoint-bound; retail-state-alignment-unverified",
        timing = _tape.Header.Timing,
        attemptedOrdinal = AttemptedOrdinal,
        deliveryEntered = DeliveryEntered,
        deliveryReturned = DeliveryReturned,
        deliveryReturnedOrdinal = DeliveryReturnedOrdinal,
        releaseError = ReleaseError,
        retailStateAuthority = false,
        next = Cursor < _tape.Inputs.Count ? _tape.Inputs[Cursor] : null,
        gameplayParity = "unverified"
    };

    internal RecordedInputPlayback(RecordedInputTape tape, RecordedInputBinding binding, long maximumLatenessMicroseconds)
        : this(tape, maximumLatenessMicroseconds, false)
    {
        RecordedInputTape.RequireBinding(tape.Header.Binding ?? throw new InvalidDataException("Unjoined input cannot enter bound playback."), binding);
    }

    private RecordedInputPlayback(RecordedInputTape tape, long maximumLatenessMicroseconds, bool unjoinedDiagnostic)
    {
        RecordedInputTape.ValidateHeader(tape.Header);
        if (unjoinedDiagnostic != tape.Unjoined)
            throw new InvalidDataException("Diagnostic replay and checkpoint-bound input require distinct explicit admission.");
        if (maximumLatenessMicroseconds < 0) throw new ArgumentOutOfRangeException(nameof(maximumLatenessMicroseconds));
        (_tape, _maximumLatenessMicroseconds, _unjoinedDiagnostic) = (tape, maximumLatenessMicroseconds, unjoinedDiagnostic);
    }

    internal static RecordedInputPlayback UnjoinedDiagnostic(RecordedInputTape tape, long maximumLatenessMicroseconds) =>
        new(tape, maximumLatenessMicroseconds, true);

    internal void Advance(long microseconds, Func<string> stateKey, Action<JsonElement> deliver, Action release)
    {
        if (!Active) return;
        try
        {
            if (microseconds < _lastMicroseconds) throw new InvalidDataException("Input replay clock moved backwards.");
            _lastMicroseconds = microseconds;
            while (Cursor < _tape.Inputs.Count && _tape.Inputs[Cursor].Microseconds <= microseconds)
            {
                var step = _tape.Inputs[Cursor];
                var late = microseconds - step.Microseconds;
                MaximumLatenessMicroseconds = Math.Max(MaximumLatenessMicroseconds, late);
                if (late > _maximumLatenessMicroseconds)
                    throw new InvalidDataException($"Recorded input {step.Ordinal} is {late} microseconds late.");
                var actual = stateKey();
                if (!_unjoinedDiagnostic && step.StateKey != actual)
                    throw new InvalidDataException($"Recorded input {step.Ordinal} expected scene {step.StateKey}; observed {actual}.");
                AttemptedOrdinal = step.Ordinal; DeliveryEntered = true; DeliveryReturned = false;
                deliver(step.Input); DeliveryReturned = true; DeliveryReturnedOrdinal = step.Ordinal; ++Cursor;
            }
            if (Cursor == _tape.Inputs.Count && microseconds >= _tape.Footer.Microseconds)
            { release(); Complete = true; }
        }
        catch (Exception error)
        { Stop(error.Message, release); }
    }

    internal void Stop(string error, Action release)
    {
        if (!Active) return;
        Error = error;
        try { release(); }
        catch (Exception failure) { ReleaseError = failure.GetType().Name + ": " + failure.Message; }
    }

    internal void RetainEvidenceFailure(string error)
    {
        // Input already returned; evidence retirement cannot turn that prefix
        // into a replayable cursor or a successful complete receipt.
        Error = error; Complete = false;
    }
}
