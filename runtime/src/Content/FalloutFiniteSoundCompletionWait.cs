namespace OpenNV.Runtime.Content;

// Mixer inactivity precedes the native node's Finished dispatch. This phase
// retains the original playback binding; it grants no ledger completion.
internal sealed class FalloutFiniteSoundCompletionWait
{
    internal const ulong MaximumNativePhases = 2;
    internal const ulong MaximumMilliseconds = 1000;
    private readonly FalloutFiniteSoundVoice _receipt;
    private readonly ulong _voice, _playback, _stream;
    private bool _observedPlaying;
    private ulong? _sincePhase, _sinceMilliseconds;
    internal string? Error { get; private set; }
    internal string Phase => Error is not null ? "refused" : _sincePhase is not null
        ? "awaiting-native-finished" : _observedPlaying ? "native-playing" : "unobserved-native-playback";
    internal object State => new
    {
        phase = Phase,
        source = _receipt,
        nativeVoice = _voice,
        nativePlayback = _playback,
        nativeStream = _stream,
        sinceNativePhase = _sincePhase,
        sinceMilliseconds = _sinceMilliseconds,
        maximumNativePhases = MaximumNativePhases,
        maximumMilliseconds = MaximumMilliseconds,
        error = Error
    };

    internal FalloutFiniteSoundCompletionWait(FalloutFiniteSoundVoice receipt, ulong voice, ulong playback, ulong stream)
    {
        receipt.Validate();
        if (voice == 0 || playback == 0 || stream == 0)
            throw new InvalidDataException("Finite completion wait lacks its actual native voice, playback or stream.");
        _receipt = receipt; _voice = voice; _playback = playback; _stream = stream;
    }

    internal FalloutFiniteSoundVoice? Observe(FalloutFiniteSoundVoice receipt, ulong voice, ulong playback, ulong stream,
        bool playing, ulong nativePhase, ulong milliseconds)
    {
        if (Error is not null) return null;
        receipt.Validate();
        if (receipt != _receipt || voice != _voice || playback != _playback || stream != _stream)
            return Refuse("Finite native completion wait changed its original source or playback binding.");
        if (playing)
        {
            if (_sincePhase is not null)
                return Refuse("Finite native playback resumed without its matching Finished receipt.");
            _observedPlaying = true;
            return _receipt;
        }
        if (!_observedPlaying) return null;
        _sincePhase ??= nativePhase; _sinceMilliseconds ??= milliseconds;
        if (nativePhase < _sincePhase.Value || milliseconds < _sinceMilliseconds.Value)
            return Refuse("Finite native completion wait clock regressed.");
        if (nativePhase - _sincePhase.Value > MaximumNativePhases ||
            milliseconds - _sinceMilliseconds.Value > MaximumMilliseconds)
            return Refuse("Finite native Finished was not delivered within its bounded completion wait.");
        return _receipt;
    }

    internal FalloutFiniteSoundVoice? ObserveSavePreparation(FalloutFiniteSoundVoice receipt, ulong voice, ulong playback,
        ulong stream, bool playing, bool paused, ulong nativePhase, ulong milliseconds)
    {
        if (!_observedPlaying || Error is not null) return null;
        receipt.Validate();
        if (receipt != _receipt || voice != _voice || playback != _playback || stream != _stream)
            return Refuse("Finite save preparation changed its original source or playback binding.");
        // A pause notification is not mixer EOS. Retain an already observed EOS
        // deadline, but start no completion window from paused playback.
        return paused && _sincePhase is null ? _receipt :
            Observe(receipt, voice, playback, stream, playing, nativePhase, milliseconds);
    }

    private FalloutFiniteSoundVoice? Refuse(string error) { Error = error; return null; }
}
