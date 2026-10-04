using System.Text.Json;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed class RecordedInputPlayback
{
    private readonly RecordedInputTape _tape;
    private readonly long _maximumLatenessMicroseconds;
    private long _lastMicroseconds;
    internal int Cursor { get; private set; }
    internal string? Error { get; private set; }
    internal long MaximumLatenessMicroseconds { get; private set; }
    internal bool Complete { get; private set; }
    internal bool Active => !Complete && Error is null;
    internal object State => new
    {
        active = Active,
        complete = Complete,
        cursor = Cursor,
        inputs = _tape.Inputs.Count,
        recordedEngine = _tape.Header.Engine,
        error = Error,
        maximumLatenessMicroseconds = MaximumLatenessMicroseconds,
        next = Cursor < _tape.Inputs.Count ? _tape.Inputs[Cursor] : null,
        gameplayParity = "unverified"
    };

    internal RecordedInputPlayback(RecordedInputTape tape, RecordedInputBinding binding, long maximumLatenessMicroseconds)
    {
        RecordedInputTape.RequireBinding(tape.Header.Binding, binding);
        if (maximumLatenessMicroseconds < 0) throw new ArgumentOutOfRangeException(nameof(maximumLatenessMicroseconds));
        (_tape, _maximumLatenessMicroseconds) = (tape, maximumLatenessMicroseconds);
    }

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
                if (step.StateKey != actual)
                    throw new InvalidDataException($"Recorded input {step.Ordinal} expected scene {step.StateKey}; observed {actual}.");
                deliver(step.Input); ++Cursor;
            }
            if (Cursor == _tape.Inputs.Count && microseconds >= _tape.Footer.Microseconds)
            { release(); Complete = true; }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or IOException or KeyNotFoundException or UnauthorizedAccessException or JsonException)
        { Stop(error.Message, release); }
    }

    internal void Stop(string error, Action release)
    {
        if (!Active) return;
        Error = error; release();
    }
}
