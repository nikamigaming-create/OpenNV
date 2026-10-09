using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutImageSpacePhaseClockSnapshot(string Schema, FalloutDoubleVisionPhase Source,
    Guid CapturedClock, Guid CapturedProcess, long Samples, uint SecondsBits, uint? SampledHourBits,
    FalloutImagePhaseHourSource HourSource, bool Retired, string? Failure);

// The renderer's indexed clock cache is separate from effect elapsed time.
// Sampling reads the actual shared calendar owner; no draw delta is integrated.
internal sealed class FalloutImageSpacePhaseClock
{
    internal const string Schema = "opennv-source-image-phase-clock/v1";
    private readonly FalloutGameTime _time;
    private readonly Guid _process;
    private int? _thread;
    private long _samples;
    private uint _seconds;
    private uint? _hour;
    private bool _retired;
    private string? _failure;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutDoubleVisionPhase Source { get; }
    internal float Seconds { get { Require(); return BitConverter.UInt32BitsToSingle(_seconds); } }
    internal FalloutImageSpacePhaseClock(FalloutDoubleVisionPhase source, FalloutGameTime time, Guid process)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(time);
        if (process == Guid.Empty || source.SourceSha256.Length != 64 || !source.SourceSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Image phase cache omitted its actual source/process construction.");
        Source = source; _time = time; _process = process;
        _seconds = source.LoadedClockBits ?? throw new NotSupportedException("Image phase cache has no actual loaded source clock cell.");
        if (!float.IsFinite(BitConverter.UInt32BitsToSingle(_seconds)))
            throw new InvalidDataException("Source image phase clock has a nonfinite loaded cell.");
        // Loaded source bits are separate from an actual frame sampling return.
    }
    private void Require()
    {
        if (_retired || _failure is not null) throw new InvalidOperationException("Image phase clock is retired or failed: " + _failure);
        if (_thread is { } thread && thread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Image phase cache left its actual presentation thread.");
    }
    internal void BindWriter(FalloutGameTime time, Guid process)
    {
        Require();
        if (!ReferenceEquals(time, _time) || process != _process)
            throw new InvalidDataException("Image phase cache belongs to another actual calendar/process owner.");
        _thread = System.Environment.CurrentManagedThreadId;
    }
    internal void Sample(FalloutGameTime time, Guid process)
    {
        BindWriter(time, process);
        try
        {
            var hour = _time.Hour;
            var seconds = Source.ClockSeconds(hour);
            _hour = BitConverter.SingleToUInt32Bits(hour);
            _seconds = BitConverter.SingleToUInt32Bits(seconds);
            _samples = checked(_samples + 1);
        }
        catch (Exception error)
        {
            _failure = string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
            throw;
        }
    }
    internal FalloutImageSpacePhaseClockSnapshot Capture()
    {
        Require();
        if (_thread is null || _samples == 0)
            throw new NotSupportedException("Image phase cache has no actual product-thread sampling return.");
        var result = new FalloutImageSpacePhaseClockSnapshot(Schema, Source, Identity, _process,
            _samples, _seconds, _hour, _time.ImagePhaseHourSource, _retired, _failure);
        Validate(result, Source, _time); return result;
    }
    internal static void Validate(FalloutImageSpacePhaseClockSnapshot saved, FalloutDoubleVisionPhase source, FalloutGameTime time)
    {
        if (saved.Schema != Schema || saved.Source != source || saved.CapturedClock == Guid.Empty || saved.CapturedProcess == Guid.Empty ||
            saved.Samples < 1 || saved.Retired || saved.Failure is not null || saved.HourSource != time.ImagePhaseHourSource ||
            (saved.SampledHourBits is not { } bits ||
                BitConverter.SingleToUInt32Bits(source.ClockSeconds(BitConverter.UInt32BitsToSingle(bits))) != saved.SecondsBits))
            throw new InvalidDataException("Saved image phase cache omitted or changed its actual source/hour/cache store.");
    }
    internal void Restore(FalloutImageSpacePhaseClockSnapshot saved)
    {
        Require(); Validate(saved, Source, _time);
        if (_samples != 0 || _hour is not null || saved.CapturedClock == Identity || saved.CapturedProcess == _process)
            throw new InvalidOperationException("Image phase cold restore requires an untouched cache in a genuinely new process epoch.");
        _samples = saved.Samples; _seconds = saved.SecondsBits; _hour = saved.SampledHourBits;
        // Writer threads and renderer/native pointers never cross processes.
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_thread is { } thread && thread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Image phase cache retirement left its actual presentation thread.");
        _retired = true;
    }
}
