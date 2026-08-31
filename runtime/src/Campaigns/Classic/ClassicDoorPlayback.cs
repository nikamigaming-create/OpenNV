using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed partial class ClassicDoorPlayback : Node3D
{
    private readonly ClassicDoorSession _session;
    private readonly Sprite3D _sprite;
    private readonly IReadOnlyDictionary<int, Texture2D> _textures;
    private readonly IReadOnlyDictionary<string, AudioStream> _sounds;
    private readonly Action<ClassicDoorState> _stateChanged;
    private readonly AudioStreamPlayer3D _audio;
    private readonly float _placementOffsetX;
    private readonly float _placementOffsetY;
    private int _appliedFrame = -1;

    internal ClassicDoorPlayback(
        ClassicDoorSession session,
        Sprite3D sprite,
        Action<ClassicDoorState> stateChanged)
    {
        _session = session;
        _sprite = sprite;
        _stateChanged = stateChanged;
        Name = $"ClassicDoorPlayback_{sprite.Name}";
        _textures = session.Source.Frames.ToDictionary(
            row => row.Frame,
            LoadFrame);
        _sounds = new[] { session.Source.OpenSound, session.Source.CloseSound }
            .ToDictionary(row => row.LogicalPath, LoadSound, StringComparer.Ordinal);
        var closed = session.Source.Frames[session.Source.ClosedFrame];
        _placementOffsetX = sprite.Offset.X - closed.OffsetX;
        _placementOffsetY = sprite.Offset.Y + closed.OffsetY - closed.Height / 2.0f;
        _audio = new AudioStreamPlayer3D { Name = "OwnedClassicDoorAudio" };
        AddChild(_audio);
        Apply(session.State);
    }

    internal ClassicDoorState BeginOpening()
    {
        var state = _session.BeginOpening();
        PlaySound(state);
        Apply(state);
        _stateChanged(state);
        return state;
    }

    internal ClassicDoorState BeginClosing()
    {
        var state = _session.BeginClosing();
        PlaySound(state);
        Apply(state);
        _stateChanged(state);
        return state;
    }

    internal void CompleteForHeadless()
    {
        while (_session.State.Phase is "opening" or "closing")
        {
            _ = _session.Advance(1.0 / _session.Source.StoredFramesPerSecond);
            Apply(_session.State);
            _stateChanged(_session.State);
        }
    }

    public override void _Ready()
    {
        if (_session.State.Phase is "opening" or "closing")
            PlaySound(_session.State);
    }

    public override void _Process(double delta)
    {
        if (!_session.Advance(delta))
            return;
        Apply(_session.State);
        _stateChanged(_session.State);
    }

    private void Apply(ClassicDoorState state)
    {
        if (_appliedFrame == state.Frame)
            return;
        var frame = _session.Source.Frames[state.Frame];
        _sprite.Texture = _textures[state.Frame];
        _sprite.Offset = new Vector2(
            _placementOffsetX + frame.OffsetX,
            _placementOffsetY - frame.OffsetY + frame.Height / 2.0f);
        _sprite.SetMeta("source_door_frame", state.Frame);
        _sprite.SetMeta("source_door_phase", state.Phase);
        _appliedFrame = state.Frame;
    }

    private void PlaySound(ClassicDoorState state)
    {
        if (state.LastSoundLogicalPath is not { } logicalPath)
            return;
        _audio.Stream = _sounds[logicalPath];
        _audio.Play((float)state.PhaseElapsedSeconds);
    }

    private static Texture2D LoadFrame(ClassicDoorFrameAsset frame)
    {
        VerifiedGltfLoader.VerifyHash(frame.Path, frame.Sha256);
        var image = Image.LoadFromFile(frame.Path);
        if (image is null || image.IsEmpty() || image.GetWidth() != frame.Width ||
            image.GetHeight() != frame.Height)
            throw new InvalidOperationException(
                $"Classic door frame could not be loaded: {frame.Path}");
        return ImageTexture.CreateFromImage(image);
    }

    private static AudioStream LoadSound(ClassicDoorSound sound)
    {
        VerifiedGltfLoader.VerifyHash(sound.WavPath, sound.WavSha256);
        return AudioStreamWav.LoadFromFile(sound.WavPath) ??
            throw new InvalidOperationException(
                $"Classic door WAV could not be loaded: {sound.WavPath}");
    }
}
