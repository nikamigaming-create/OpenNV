using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScriptSoundRequest(long Id, FalloutFormKey Caller, FalloutSoundRecord Source,
    FalloutAnimationSoundSelection Selection, bool SystemSound);
internal sealed record FalloutScriptSoundMedia(string Path, string Source, string Sha256, double Duration);
internal sealed record FalloutScriptSoundPlayback(FalloutScriptSoundMedia Media, Action Start,
    Action<bool> Pause, Action Release);

// Script sound requests have world lifetime. A presentation adapter prepares the
// owned stream before the command commits, then reports actual voice completion.
// These transient voices are not campaign state and never replay on restoration.
internal sealed class FalloutScriptSounds(FalloutPluginStack records, FalloutScriptMenus menus, ulong? seed = null)
{
    private sealed class Voice(FalloutScriptSoundRequest request, FalloutScriptSoundPlayback playback)
    {
        internal readonly FalloutScriptSoundRequest Request = request;
        internal readonly FalloutScriptSoundPlayback Playback = playback;
        internal string Phase = "queued-menu";
        internal IDisposable? Registration;
    }
    private sealed record Binding(Func<FalloutSoundRecord, IReadOnlyList<string>> Variants,
        Func<FalloutScriptSoundRequest, FalloutScriptSoundPlayback> Prepare, bool Stereo);
    private readonly FalloutSoundRandomState _random = new(seed ?? BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private readonly Dictionary<FalloutFormKey, (FalloutSoundRecord Source, long Revision)> _sources = [];
    private readonly Dictionary<long, Voice> _voices = [];
    private Binding? _binding;
    private long _requests, _completed, _cancelled;
    internal FalloutScriptSoundRequest? LastRequest { get; private set; }
    internal FalloutScriptSoundMedia? LastMedia { get; private set; }
    internal string? LastDisposition { get; private set; }
    internal string? LastError { get; private set; }
    internal int ActiveVoices => _voices.Count;
    internal bool IsActive(long id) => _voices.ContainsKey(id);
    internal static bool SystemFlag(double value) => double.IsFinite(value) && value == Math.Truncate(value) &&
        value >= int.MinValue && value <= int.MaxValue ? value != 0 :
        throw new InvalidDataException("PlaySound system flag must be a signed integer.");
    internal object State => new
    {
        requests = _requests,
        completed = _completed,
        cancelled = _cancelled,
        bound = _binding is not null,
        active = _voices.Values.Select(voice => new { request = voice.Request, voice.Phase, media = voice.Playback.Media }).ToArray(),
        lastRequest = LastRequest,
        lastMedia = LastMedia,
        lastDisposition = LastDisposition,
        error = LastError,
        persistence = "transient-world-voices;not-save-baked;no-cold-replay",
        timing = "menu-queued-normal;system-sound-unpaused;retail-voice-timing-unmatched"
    };

    internal IDisposable Bind(Func<FalloutSoundRecord, IReadOnlyList<string>> variants,
        Func<FalloutScriptSoundRequest, FalloutScriptSoundPlayback> prepare, bool stereo)
    {
        if (_binding is not null) throw new InvalidOperationException("Script sounds already have a presentation owner.");
        ArgumentNullException.ThrowIfNull(variants); ArgumentNullException.ThrowIfNull(prepare);
        var binding = new Binding(variants, prepare, stereo); _binding = binding;
        return new Scope(() => { if (_binding == binding) Clear(); });
    }

    internal void Play(FalloutFormKey caller, FalloutFormKey sound, bool systemSound = false)
    {
        try
        {
            var binding = _binding ?? throw new NotSupportedException("PlaySound has no owned sound presentation binding.");
            var revision = records.SoundPaths.Revision(sound);
            if (!_sources.TryGetValue(sound, out var entry) || entry.Revision != revision)
            {
                var record = records.GetEffective(sound);
                if (record.Signature != "SOUN") throw new InvalidDataException("PlaySound argument is not a SOUN form.");
                entry = (FalloutSoundRecordReader.Read(records, sound), revision);
                _sources[sound] = entry;
            }
            var source = entry.Source;
            // PlaySound explicitly requests non-locational playback. Keep the
            // original flags separately; the shared selector still exposes
            // environmental, submersion and stereo/LFE presentation gaps.
            var random = new FalloutSoundRandomState(_random.State);
            var selection = FalloutAnimationSound.Select(source with { Flags = source.Flags | FalloutSoundFlags.TwoDimensional },
                binding.Variants(source), random, ownsLoopStop: true, stereoOutput: binding.Stereo);
            if (selection.Play && !Path.GetExtension(selection.Path!).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                throw FalloutSoundPlaybackContract.Unsupported(source, "PlaySound for a non-WAV ambient effect");
            var request = new FalloutScriptSoundRequest(checked(_requests + 1), caller, source, selection, systemSound);
            FalloutScriptSoundPlayback? playback = null;
            if (selection.Play)
            {
                playback = binding.Prepare(request);
                if (!StringComparer.OrdinalIgnoreCase.Equals(playback.Media.Path, selection.Path) ||
                    playback.Media.Sha256.Length != 64 || !playback.Media.Sha256.All(Uri.IsHexDigit) ||
                    string.IsNullOrWhiteSpace(playback.Media.Source) || !double.IsFinite(playback.Media.Duration) || playback.Media.Duration <= 0)
                {
                    playback.Release();
                    throw new InvalidDataException("Prepared script sound has no matching owned media identity or duration.");
                }
            }
            _random.Restore(random.State); _requests = request.Id;
            LastRequest = request; LastMedia = playback?.Media;
            LastDisposition = selection.Play ? "queued-menu" : "source-sound-chance-skipped";
            if (playback is not null)
            {
                var voice = new Voice(request, playback); _voices.Add(request.Id, voice);
                try { Update(voice, menus.Query() == 0); }
                catch { _voices.Remove(request.Id); voice.Registration?.Dispose(); playback.Release(); LastDisposition = "failed-start"; throw; }
            }
        }
        catch (FalloutPluginFormatException error)
        {
            LastError = error.Message;
            throw new InvalidDataException(error.Message, error);
        }
        catch (Exception error)
        {
            LastError = error.Message;
            throw;
        }
    }

    internal void Update(bool gameMode)
    {
        foreach (var voice in _voices.Values.ToArray())
        {
            try { Update(voice, gameMode); }
            catch (Exception error)
            {
                LastError = error.Message; LastDisposition = "failed-playback";
                _voices.Remove(voice.Request.Id); voice.Registration?.Dispose(); voice.Playback.Release(); throw;
            }
        }
    }

    private void Update(Voice voice, bool gameMode)
    {
        var audible = voice.Request.SystemSound || gameMode;
        if (voice.Phase == "queued-menu")
        {
            if (!audible) return;
            voice.Phase = "playing"; LastDisposition = "source-sound-playing";
            voice.Registration = records.SoundVoices.Register(voice.Request.Source.FormKey, null, "PlaySound",
                () => _voices.ContainsKey(voice.Request.Id), () => StopVoice(voice));
            voice.Playback.Start();
        }
        else if (audible == (voice.Phase == "paused-menu"))
        {
            voice.Playback.Pause(!audible); voice.Phase = audible ? "playing" : "paused-menu";
        }
    }

    internal void Complete(long id)
    {
        if (!_voices.Remove(id, out var voice)) return;
        _completed++; LastDisposition = "source-sound-completed"; voice.Registration?.Dispose(); voice.Playback.Release();
    }

    private void StopVoice(Voice voice)
    {
        if (!_voices.Remove(voice.Request.Id)) return;
        ++_cancelled; LastDisposition = "source-sound-stopped";
        voice.Registration?.Dispose(); voice.Playback.Release();
    }

    internal void Clear()
    {
        _binding = null;
        var voices = _voices.Values.ToArray(); _voices.Clear();
        foreach (var voice in voices) { _cancelled++; voice.Registration?.Dispose(); voice.Playback.Release(); }
        if (voices.Length != 0) LastDisposition = "session-retired";
    }
    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
    }
}
