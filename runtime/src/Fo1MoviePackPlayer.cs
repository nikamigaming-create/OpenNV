using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1MoviePackPlayer : TextureRect
{
    private static readonly byte[] Magic = "ONVFO1M1"u8.ToArray();
    private byte[] _pack = [];
    private int[] _offsets = [];
    private int[] _sizes = [];
    private int _framesPerSecond;
    private double _durationSeconds;
    private double _elapsed;
    private int _currentFrame = -1;
    private bool _playing;
    private ImageTexture? _texture;
    private AudioStreamPlayer _audio = null!;

    internal int RenderedFrames { get; private set; }
    internal bool IsMoviePlaying => _playing;
    internal int CurrentFrameIndex => _currentFrame;
    internal string CurrentFrameSha256 => _currentFrame < 0
        ? string.Empty
        : Convert.ToHexString(SHA256.HashData(
            _pack.AsSpan(_offsets[_currentFrame], _sizes[_currentFrame]))).ToLowerInvariant();

    internal void Configure(Fo1CharacterStartContract contract)
    {
        Name = "OwnedFalloutOverseerFramePlayback";
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepAspectCentered;
        MouseFilter = MouseFilterEnum.Ignore;
        _pack = File.ReadAllBytes(contract.OpeningFramesPath);
        if (_pack.Length < 28 || !_pack.AsSpan(0, 8).SequenceEqual(Magic))
            throw new InvalidOperationException("Fallout Overseer frame pack has an invalid header.");
        var width = ReadInt(8);
        var height = ReadInt(12);
        _framesPerSecond = ReadInt(16);
        var frameRateDenominator = ReadInt(20);
        var frameCount = ReadInt(24);
        if (width != contract.OpeningWidth || height != contract.OpeningHeight ||
            _framesPerSecond != contract.OpeningFramesPerSecond || frameRateDenominator != 1 ||
            frameCount != contract.OpeningFrameCount)
            throw new InvalidOperationException("Fallout Overseer frame-pack metadata drifted.");
        _durationSeconds = contract.OpeningDurationSeconds;
        _offsets = new int[frameCount];
        _sizes = new int[frameCount];
        var cursor = 28;
        for (var index = 0; index < frameCount; index++)
        {
            if (cursor + 4 > _pack.Length)
                throw new InvalidOperationException("Fallout Overseer frame table is truncated.");
            var size = ReadInt(cursor);
            cursor += 4;
            if (size < 256 || size > _pack.Length - cursor)
                throw new InvalidOperationException("Fallout Overseer JPEG frame escapes its pack.");
            _offsets[index] = cursor;
            _sizes[index] = size;
            cursor += size;
        }
        if (cursor != _pack.Length)
            throw new InvalidOperationException("Fallout Overseer frame pack has trailing bytes.");
        _audio = new AudioStreamPlayer
        {
            Name = "OwnedFalloutOverseerAudio",
            Stream = AudioStreamOggVorbis.LoadFromFile(contract.OpeningAudioPath)
                ?? throw new InvalidOperationException("Fallout Overseer audio could not be loaded."),
            VolumeDb = 0.0f,
        };
        AddChild(_audio);
        ShowFrame(0);
    }

    internal void PlayMovie()
    {
        _elapsed = 0.0;
        _currentFrame = -1;
        RenderedFrames = 0;
        _playing = true;
        ShowFrame(0);
        _audio.Play();
    }

    internal void AdvanceMovie(double delta)
    {
        if (!_playing)
            return;
        _elapsed += delta;
        var frame = Math.Min(
            _offsets.Length - 1,
            Math.Max(0, (int)Math.Floor(_elapsed * _framesPerSecond)));
        if (frame != _currentFrame)
            ShowFrame(frame);
        if (_elapsed < _durationSeconds)
            return;
        _playing = false;
        _audio.Stop();
    }

    internal void SkipMovie()
    {
        if (!_playing)
            return;
        _playing = false;
        _audio.Stop();
    }

    private void ShowFrame(int index)
    {
        var image = new Image();
        var error = image.LoadJpgFromBuffer(
            _pack.AsSpan(_offsets[index], _sizes[index]).ToArray());
        if (error != Error.Ok || image.IsEmpty())
            throw new InvalidOperationException($"Fallout Overseer JPEG frame {index} failed to decode.");
        if (_texture is null)
        {
            _texture = ImageTexture.CreateFromImage(image);
            Texture = _texture;
        }
        else
            _texture.Update(image);
        _currentFrame = index;
        RenderedFrames++;
    }

    private int ReadInt(int offset) =>
        checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_pack.AsSpan(offset, 4)));
}
