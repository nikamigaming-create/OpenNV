using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedScriptSoundPlayer(FalloutScriptSounds owner,
    RuntimeLiveContentSource source, FalloutScriptMenus menus) : Node
{
    private IDisposable? _binding;
    private readonly Dictionary<string, AudioStream> _streams = new(StringComparer.OrdinalIgnoreCase);

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ProcessPriority = int.MinValue + 2;
        _binding = owner.Bind(descriptor => FalloutAnimationSound.Variants(descriptor,
            descriptor.HasExactFile ? [] : source.ResourcePathsUnder(descriptor.LogicalPath)), Prepare,
            AudioServer.GetSpeakerMode() == AudioServer.SpeakerMode.ModeStereo);
    }

    private FalloutScriptSoundPlayback Prepare(FalloutScriptSoundRequest request)
    {
        var selected = request.Selection;
        if (!_streams.TryGetValue(selected.Path!, out var stream))
        {
            stream = NativeOwnedMediaLoader.LoadAudio(selected.Path!);
            if (stream is AudioStreamWav cachedWav) cachedWav.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
            _streams.Add(selected.Path!, stream);
        }
        if (stream is not AudioStreamWav) throw new InvalidDataException("PlaySound did not resolve an owned WAV stream.");
        var media = new FalloutScriptSoundMedia(selected.Path!, stream.GetMeta("opennv_owned_media_source").AsString(),
            stream.GetMeta("opennv_owned_media_sha256").AsString(), stream.GetLength());
        var loop = FalloutSoundLoop.Read(request.Source);
        AudioStream? ownedStream = null;
        if (loop.Mode != FalloutSoundLoopMode.None) stream = ownedStream = NativeOwnedSoundPlayback.CreateLoopStream(stream, loop);
        var voice = new AudioStreamPlayer
        {
            Name = $"ScriptSound_{request.Id}",
            ProcessMode = ProcessModeEnum.Always,
            Stream = stream,
            VolumeDb = selected.GainDb,
            PitchScale = selected.PitchScale,
        };
        voice.Finished += () => owner.Complete(request.Id);
        try { AddChild(voice); }
        catch { voice.Free(); ownedStream?.Dispose(); throw; }
        var released = false;
        return new(media, () =>
        {
            voice.Play();
            GD.Print($"OPENNV_SCRIPT_SOUND_PLAY request={request.Id} caller={request.Caller} sound={request.Source.FormKey} " +
                $"asset={media.Source} sha256={media.Sha256} duration={media.Duration:R} systemSound={request.SystemSound} " +
                $"unbound={string.Join(',', selected.Unbound)} parity=unverified");
        }, paused => voice.StreamPaused = paused, () =>
        {
            if (released) return;
            released = true;
            if (GodotObject.IsInstanceValid(voice)) { voice.Stop(); voice.Stream = null; voice.QueueFree(); }
            ownedStream?.Dispose();
        });
    }

    public override void _Process(double delta)
    {
        try { owner.Update(!GetTree().Paused && menus.Query() == 0); }
        catch (Exception error)
        {
            GD.PushError($"OPENNV_SCRIPT_SOUND_FAILURE {error}");
        }
    }

    public override void _ExitTree()
    {
        _binding?.Dispose(); _binding = null;
        foreach (var stream in _streams.Values) stream.Dispose();
        _streams.Clear();
    }
}
