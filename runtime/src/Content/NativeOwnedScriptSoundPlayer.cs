using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedScriptSoundPlayer(FalloutScriptSounds owner,
    RuntimeLiveContentSource source, FalloutScriptMenus menus, Func<FalloutFormKey, Node3D?>? reference = null,
    float unitsToMetres = 0) : Node
{
    private IDisposable? _binding;
    private readonly Dictionary<string, AudioStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AudioStreamPlayer3D, FalloutAnimationSoundSelection> _spatial = [];

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
        if (stream is not AudioStreamWav wav) throw new InvalidDataException("Script sound did not resolve an owned WAV stream.");
        var media = new FalloutScriptSoundMedia(selected.Path!, stream.GetMeta("opennv_owned_media_source").AsString(),
            stream.GetMeta("opennv_owned_media_sha256").AsString(), stream.GetLength(), wav.Stereo);
        if (request.Reference is { } emitter) return PreparePositioned(request, media, stream,
            (reference ?? throw new NotSupportedException("PlaySound3D has no reference presentation binding."))(emitter));
        return PrepareFlat(request, media, stream, "source-2D");
    }

    private FalloutScriptSoundPlayback PrepareFlat(FalloutScriptSoundRequest request,
        FalloutScriptSoundMedia media, AudioStream stream, string routing)
    {
        var selected = request.Selection;
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
        }, routing);
    }

    private FalloutScriptSoundPlayback PreparePositioned(FalloutScriptSoundRequest request,
        FalloutScriptSoundMedia media, AudioStream stream, Node3D? emitter)
    {
        if (emitter is null || !GodotObject.IsInstanceValid(emitter) || !emitter.IsInsideTree())
            throw new NotSupportedException("PlaySound3D reference has no loaded 3D presentation owner.");
        if (request.Selection.Source.IsTwoDimensional || media.Stereo)
            return PrepareFlat(request, media, stream, media.Stereo ? "stereo-media-2D" : "source-2D-flag");
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("PlaySound3D has no owned game-unit scale.");
        var selected = request.Selection;
        AudioStream? ownedStream = null;
        var loop = FalloutSoundLoop.Read(request.Source);
        if (loop.Mode != FalloutSoundLoopMode.None) stream = ownedStream = NativeOwnedSoundPlayback.CreateLoopStream(stream, loop);
        var voice = new AudioStreamPlayer3D
        {
            Name = $"ScriptSound3D_{request.Id}",
            ProcessMode = ProcessModeEnum.Always,
            Stream = stream,
            PitchScale = selected.PitchScale,
            AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled,
            MaxDistance = selected.Source.MaximumDistanceGameUnits * unitsToMetres,
            AreaMask = 0,
        };
        voice.Finished += () => owner.Complete(request.Id);
        voice.TreeExiting += () => owner.Complete(request.Id);
        try { emitter.AddChild(voice); _spatial.Add(voice, selected); ApplyListener(voice, selected); }
        catch { _spatial.Remove(voice); voice.Free(); ownedStream?.Dispose(); throw; }
        var released = false;
        return new(media, () =>
        {
            ApplyListener(voice, selected); voice.Play();
            GD.Print($"OPENNV_SCRIPT_SOUND_3D_PLAY request={request.Id} reference={request.Reference} sound={request.Source.FormKey} " +
                $"asset={media.Source} sha256={media.Sha256} duration={media.Duration:R} routing=source-3D " +
                $"unbound={string.Join(',', selected.Unbound)} parity=unverified");
        }, paused => voice.StreamPaused = paused, () =>
        {
            if (released) return;
            released = true; _spatial.Remove(voice);
            if (GodotObject.IsInstanceValid(voice)) { voice.Stop(); voice.Stream = null; voice.QueueFree(); }
            ownedStream?.Dispose();
        }, "source-3D");
    }

    private void ApplyListener(AudioStreamPlayer3D voice, FalloutAnimationSoundSelection selected)
    {
        var listener = GetViewport().GetCamera3D();
        voice.VolumeDb = listener is null ? float.NegativeInfinity : selected.GainDb +
            selected.Source.AttenuationDbAtDistanceGameUnits(voice.GlobalPosition.DistanceTo(listener.GlobalPosition) / unitsToMetres);
        voice.SetMeta("opennv_sound_listener", listener?.GetPath().ToString() ?? "unbound");
    }

    public override void _Process(double delta)
    {
        try
        {
            owner.Update(!GetTree().Paused && menus.Query() == 0);
            foreach (var (voice, selected) in _spatial.ToArray()) ApplyListener(voice, selected);
        }
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
