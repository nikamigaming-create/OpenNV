using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Content;

// Decoded samples are process memory, never a generated launch asset. The
// original resource identity and decoded bytes independently bind cold audio.
internal sealed class NativeOwnedPcmData
{
    internal string MediaSource { get; }
    internal string MediaPath { get; }
    internal string MediaSha256 { get; }
    internal float[] Samples { get; }
    internal int Channels { get; }
    internal int Rate { get; }
    internal int Frames => Samples.Length / Channels;
    internal string Sha256 { get; }

    internal NativeOwnedPcmData(AudioStream source)
    {
        MediaSource = source.GetMeta("opennv_owned_media_source", "").AsString();
        MediaPath = source.GetMeta("opennv_owned_media_path", "").AsString();
        MediaSha256 = source.GetMeta("opennv_owned_media_sha256", "").AsString();
        (Rate, Channels) = source switch
        {
            AudioStreamWav wav => (wav.MixRate, wav.Stereo ? 2 : 1),
            AudioStreamOggVorbis ogg => (ogg.GetMeta("opennv_owned_media_rate", 0).AsInt32(),
                ogg.GetMeta("opennv_owned_media_channels", 0).AsInt32()),
            AudioStreamMP3 mp3 => NativeOwnedMediaFormat.Mp3Format(mp3.Data),
            _ => throw new NotSupportedException("Owned PCM decoding requires WAV, Vorbis or MPEG audio.")
        };
        if (Rate <= 0 || Channels is not (1 or 2)) throw new InvalidDataException("Owned PCM has an invalid source rate or channel count.");
        if (source is AudioStreamWav { Format: AudioStreamWav.FormatEnum.Format16Bits } pcm)
        {
            var bytes = pcm.Data;
            if (bytes.Length == 0 || bytes.Length % (2 * Channels) != 0)
                throw new InvalidDataException("Owned PCM has a truncated sample frame.");
            Samples = new float[bytes.Length / 2];
            for (var index = 0; index < Samples.Length; index++)
                Samples[index] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(index * 2, 2)) / 32768f;
        }
        else Samples = Decode(source, Rate, Channels);
        if (Samples.Length == 0 || Samples.Length % Channels != 0 || Samples.Any(sample => !float.IsFinite(sample)))
            throw new InvalidDataException("Owned decoded PCM has empty or nonfinite sample frames.");
        Sha256 = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(Samples.AsSpan())));
    }

    private static float[] Decode(AudioStream source, int rate, int channels)
    {
        var length = source.GetLength();
        if (!double.IsFinite(length) || length <= 0 ||
            source is AudioStreamOggVorbis { Loop: true } or AudioStreamMP3 { Loop: true })
            throw new InvalidDataException("Owned voice decoding requires a finite nonlooping resource.");
        const int block = 4096;
        var bound = checked((int)Math.Ceiling(length * rate) + block);
        var samples = new List<float>();
        AudioServer.Lock();
        try
        {
            using var playback = source.InstantiatePlayback() ?? throw new InvalidDataException("Owned audio has no decoder playback.");
            var scale = UnitSampleScale(rate, checked((int)AudioServer.GetMixRate()), AudioServer.PlaybackSpeedScale);
            playback.Start();
            try
            {
                while (true)
                {
                    var frames = playback.MixAudio(scale, block);
                    if (frames.Length == 0) break;
                    if (frames.Length > block || (long)samples.Count / channels + frames.Length > bound)
                        throw new InvalidDataException("Owned decoder exceeded its declared finite source extent.");
                    foreach (var frame in frames)
                    {
                        if (channels == 1 && frame.X != frame.Y)
                            throw new InvalidDataException("Owned mono decoder returned unequal channels.");
                        samples.Add(frame.X);
                        if (channels == 2) samples.Add(frame.Y);
                    }
                    if (frames.Length < block) break;
                }
            }
            finally { playback.Stop(); }
        }
        finally { AudioServer.Unlock(); }
        return samples.ToArray();
    }

    // Godot's public decoder mix uses its output rate and speed scale. Require
    // an exact unit source increment rather than baking device resampling into
    // the samples. Adjacent floats account for multiplication rounding.
    internal static float UnitSampleScale(int sourceRate, int outputRate, float speed)
    {
        if (sourceRate <= 0 || outputRate <= 0 || !float.IsFinite(speed) || speed <= 0)
            throw new InvalidDataException("Owned decoder has an invalid mixing rate.");
        var scale = (float)((double)outputRate / sourceRate / speed);
        var lower = scale; var upper = scale;
        for (var index = 0; index < 64; index++)
        {
            if (sourceRate * lower * speed == outputRate) return lower;
            if (sourceRate * upper * speed == outputRate) return upper;
            lower = MathF.BitDecrement(lower); upper = MathF.BitIncrement(upper);
        }
        throw new NotSupportedException("Owned decoder cannot retain a unit source sample increment at this device rate.");
    }
}
