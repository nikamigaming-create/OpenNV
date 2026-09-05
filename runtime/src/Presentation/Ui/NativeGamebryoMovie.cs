using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGamebryoMovie : CanvasLayer
{
    private readonly OwnedMovieDecoder _decoder = new();
    private readonly List<(Node Node, ProcessModeEnum Mode, bool Paused)> _audioOwners = [];
    private FalloutMovieCommand _command = null!;
    private Action<bool> _completed = null!;
    private TextureRect _picture = null!;
    private ImageTexture? _texture;
    private AudioStreamPlayer? _audio;
    private AudioStreamGeneratorPlayback? _playback;
    private OwnedMovieVideoFrame? _nextFrame;
    private float[]? _nextAudio;
    private long _startTimestamp;
    private long _lastFrame = -1;
    private long _queuedAudioFrames;
    private double _seconds;
    private bool _ready;
    private bool _started;
    private bool _finished;
    private bool _pausedBeforeMovie;
    private bool _pauseOwned;
    private bool _failed;

    internal void Configure(FalloutMovieCommand command, Action<bool> completed)
    {
        _command = command;
        _completed = completed;
        Name = "NativeMovie";
        Layer = 110;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override async void _Ready()
    {
        var background = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);
        _picture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = _command.Letterboxed ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _picture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        background.AddChild(_picture);
        _pausedBeforeMovie = GetTree().Paused;
        foreach (var node in GetTree().Root.FindChildren("*", "", true, false)
            .Where(node => node is AudioStreamPlayer or AudioStreamPlayer3D or AudioStreamPlayer2D))
        {
            _audioOwners.Add((node, node.ProcessMode, node.Get("stream_paused").AsBool()));
            var pause = node.IsInGroup("opennv_music") ? _command.PauseMusic : _command.MuteWorldAudio;
            if (pause)
                node.Set("stream_paused", true);
            else
                node.ProcessMode = ProcessModeEnum.Always;
        }
        GetTree().Paused = true;
        _pauseOwned = true;
        try
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned movie source is absent.");
            // Retail PlayBink resolves relative to Data/Video and reads loose
            // files. It does not load Bink movies from BSA members.
            var logicalPath = "video/" + _command.FileName;
            if (!source.TryResolve(logicalPath, null, out var moviePath) || !File.Exists(moviePath))
                throw new FileNotFoundException($"Owned Bink movie is missing: {logicalPath}");
            await _decoder.OpenAsync(moviePath);
            if (_finished || !IsInsideTree())
                return;
            if (_decoder.Info.AudioChannels != 0)
            {
                _audio = new AudioStreamPlayer
                {
                    Name = "MovieSoundtrack",
                    ProcessMode = ProcessModeEnum.Always,
                    Stream = new AudioStreamGenerator { MixRate = _decoder.Info.AudioRate, BufferLength = 0.25f },
                };
                AddChild(_audio);
            }
            _ready = true;
            SetMeta("opennv_movie_source", logicalPath);
            SetMeta("opennv_movie_frame_count", _decoder.Info.FrameCount);
            SetMeta("opennv_movie_rate_numerator", _decoder.Info.RateNumerator);
            SetMeta("opennv_movie_rate_denominator", _decoder.Info.RateDenominator);
            SetMeta("opennv_movie_decoder", "ffmpeg-bink; retail decoded-byte correspondence unmeasured");
            GD.Print($"OPENNV_MOVIE_READY source={logicalPath} frames={_decoder.Info.FrameCount} " +
                $"rate={_decoder.Info.RateNumerator}/{_decoder.Info.RateDenominator} interruptible={_command.Interruptible}");
        }
        catch (Exception error)
        {
            if (!_finished && IsInsideTree())
                Fail(error);
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (!_ready || _finished || _failed)
            return;
        try { ProcessMovie(); }
        catch (Exception error) { Fail(error); }
    }

    private void ProcessMovie()
    {
        if (_decoder.Failure is { } error)
        {
            Fail(error);
            return;
        }
        _nextFrame ??= _decoder.Video.TryRead(out var nextFrame) ? nextFrame : null;
        _nextAudio ??= _decoder.Audio.TryRead(out var nextAudio) ? nextAudio : null;
        if (!_started)
        {
            if (_nextFrame is null || (_audio is not null && _nextAudio is null))
                return;
            _audio?.Play();
            _playback = _audio?.GetStreamPlayback() as AudioStreamGeneratorPlayback;
            if (_audio is not null && _playback is null)
                throw new InvalidOperationException("Movie audio playback could not start.");
            _startTimestamp = Stopwatch.GetTimestamp();
            _started = true;
        }
        if (_playback is not null)
        {
            while (_nextAudio is not null)
            {
                var count = _nextAudio.Length / _decoder.Info.AudioChannels;
                if (!_playback.CanPushBuffer(count))
                    break;
                var samples = new Vector2[count];
                for (var index = 0; index < count; ++index)
                {
                    var left = _nextAudio[index * _decoder.Info.AudioChannels];
                    var right = _decoder.Info.AudioChannels == 1 ? left : _nextAudio[index * 2 + 1];
                    samples[index] = new Vector2(left, right);
                }
                if (!_playback.PushBuffer(samples))
                    throw new InvalidOperationException("Movie PCM buffer rejected available space.");
                _queuedAudioFrames = checked(_queuedAudioFrames + count);
                _nextAudio = _decoder.Audio.TryRead(out nextAudio) ? nextAudio : null;
            }
        }
        var clockSeconds = _audio is null
            ? Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds
            : Math.Max(0.0, _audio.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency());
        // AudioServer publishes mix positions in blocks; do not move the movie
        // backwards when a newly published block changes the interpolation.
        var seconds = _seconds = Math.Max(_seconds, clockSeconds);
        while (_nextFrame is not null && _nextFrame.Index * _decoder.Info.FrameSeconds <= seconds)
        {
            using var frame = Image.CreateFromData(_decoder.Info.Width, _decoder.Info.Height, false, Image.Format.Rgba8, _nextFrame.Rgba);
            if (_texture is null)
            {
                _texture = ImageTexture.CreateFromImage(frame);
                _picture.Texture = _texture;
            }
            else
                _texture.Update(frame);
            _lastFrame = _nextFrame.Index;
            _nextFrame = _decoder.Video.TryRead(out nextFrame) ? nextFrame : null;
        }
        SetMeta("opennv_movie_frame", _lastFrame);
        SetMeta("opennv_movie_seconds", seconds);
        SetMeta("opennv_movie_audio_underruns", _playback?.GetSkips() ?? 0);
        var soundtrackSeconds = _decoder.Info.AudioRate == 0 ? 0 : (double)_queuedAudioFrames / _decoder.Info.AudioRate;
        if (_decoder.DecodingComplete && _decoder.Audio.Completion.IsCompletedSuccessfully && _nextAudio is null &&
            _lastFrame == _decoder.Info.FrameCount - 1 && seconds >= Math.Max(_decoder.Info.DurationSeconds, soundtrackSeconds))
            Finish(interrupted: false);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (_finished || _failed || _decoder.Failure is not null || !_command.Interruptible ||
            inputEvent is not InputEventKey { Pressed: true, Echo: false } key ||
            (key.PhysicalKeycode != Key.Escape && key.Keycode != Key.Escape))
            return;
        GetViewport().SetInputAsHandled();
        Finish(interrupted: true);
    }

    private void Finish(bool interrupted)
    {
        if (_finished)
            return;
        _finished = true;
        _decoder.Dispose();
        _audio?.Stop();
        RestoreWorld();
        GD.Print($"OPENNV_MOVIE_COMPLETE source={_command.FileName} frame={_lastFrame} interrupted={interrupted}");
        QueueFree();
        _completed(interrupted);
    }

    private void Fail(Exception error)
    {
        if (_failed || _finished)
            return;
        _failed = true;
        _decoder.Dispose();
        _audio?.Stop();
        SetMeta("opennv_movie_error", error.Message);
        GD.PushError($"OPENNV_MOVIE_FAILED {error}");
        var label = new Label { Text = $"Unable to play {_command.FileName}: {error.Message}", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(label);
    }

    private void RestoreWorld()
    {
        if (!_pauseOwned)
            return;
        _pauseOwned = false;
        GetTree().Paused = _pausedBeforeMovie;
        foreach (var owner in _audioOwners.Where(owner => IsInstanceValid(owner.Node)))
        {
            owner.Node.ProcessMode = owner.Mode;
            owner.Node.Set("stream_paused", owner.Paused);
        }
    }

    public override void _ExitTree()
    {
        _finished = true;
        _decoder.Dispose();
        RestoreWorld();
    }
}
