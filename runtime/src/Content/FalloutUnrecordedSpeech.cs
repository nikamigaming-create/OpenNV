using System.Text;

namespace OpenNV.Runtime.Content;

// A response with neither audio nor LIP retains authored text and ordinary
// speech effects. The no-LIP duration is the source byte count times the live
// setting sampled at submission. No synthetic sound or subtitle estimate.
internal sealed class FalloutUnrecordedSpeech
{
    internal int TextBytes { get; }
    internal float SecondsPerByte { get; }
    internal float DurationSeconds { get; }
    internal float RemainingSeconds { get; private set; }

    internal FalloutUnrecordedSpeech(string text, float secondsPerByte)
    {
        if (!float.IsFinite(secondsPerByte) || secondsPerByte < 0)
            throw new InvalidDataException("Unrecorded speech duration setting must be finite and nonnegative.");
        TextBytes = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetByteCount(text);
        SecondsPerByte = secondsPerByte;
        DurationSeconds = TextBytes * secondsPerByte;
        if (!float.IsFinite(DurationSeconds)) throw new InvalidDataException("Unrecorded speech duration exceeds its finite clock.");
        RemainingSeconds = DurationSeconds;
    }

    internal bool Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new InvalidDataException("Unrecorded speech tick must be finite and nonnegative.");
        // The native process tests its timer before subtracting frame time.
        // Crossing zero becomes eligible on the following ordinary tick.
        if (RemainingSeconds <= 0) return true;
        var next = (float)(RemainingSeconds - delta);
        if (!float.IsFinite(next)) throw new InvalidDataException("Unrecorded speech countdown exceeds finite storage.");
        RemainingSeconds = next;
        return false;
    }

    internal object State => new
    {
        disposition = "source-audio-and-lip-absent",
        textBytes = TextBytes,
        setting = "fAIDefaultSpeechMult",
        secondsPerByte = SecondsPerByte,
        durationSeconds = DurationSeconds,
        remainingSeconds = RemainingSeconds,
        completionEligibleNextTick = RemainingSeconds <= 0,
        owner = "source-text-byte-count-and-process-timer",
        parity = "matched-timing-unverified"
    };
}
