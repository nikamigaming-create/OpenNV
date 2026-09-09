namespace OpenNV.Runtime.Content;

internal enum FalloutSoundLoopMode { None, Loop, EnvelopeFast, EnvelopeSlow }

internal sealed record FalloutSoundLoop(FalloutSoundLoopMode Mode, uint Start, uint End)
{
    internal static FalloutSoundLoop Read(FalloutSoundRecord source)
    {
        var modes = source.Flags & (FalloutSoundFlags.Loop | FalloutSoundFlags.EnvelopeFast | FalloutSoundFlags.EnvelopeSlow);
        var mode = modes switch
        {
            0 => FalloutSoundLoopMode.None,
            FalloutSoundFlags.Loop => FalloutSoundLoopMode.Loop,
            FalloutSoundFlags.EnvelopeFast => FalloutSoundLoopMode.EnvelopeFast,
            FalloutSoundFlags.EnvelopeSlow => FalloutSoundLoopMode.EnvelopeSlow,
            _ => throw FalloutSoundPlaybackContract.Unsupported(source, "conflicting loop/envelope modes")
        };
        if (mode == FalloutSoundLoopMode.None && (source.LoopStartSample != 0 || source.LoopEndSample != 0))
            throw FalloutSoundPlaybackContract.Unsupported(source, "loop bounds without a loop/envelope mode");
        if ((mode is FalloutSoundLoopMode.EnvelopeFast or FalloutSoundLoopMode.EnvelopeSlow || source.LoopEndSample != 0) &&
            (source.LoopEndSample <= source.LoopStartSample || source.LoopEndSample > int.MaxValue))
            throw FalloutSoundPlaybackContract.Unsupported(source, "empty, reversed or oversized source loop region");
        return new(mode, source.LoopStartSample, source.LoopEndSample);
    }

    internal double? ReleasePosition(uint mixRate)
    {
        if (mixRate == 0) throw new InvalidDataException("A sound envelope has no source sample clock.");
        return Mode == FalloutSoundLoopMode.EnvelopeFast ? (double)End / mixRate : null;
    }
}
