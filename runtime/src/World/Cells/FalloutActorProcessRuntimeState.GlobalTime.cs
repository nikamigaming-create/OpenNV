using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutGlobalTimeSource? _globalTimeSource;
    private FalloutGlobalTimeSnapshot? _preparedGlobalTime;
    private FalloutGlobalTimeState? _globalTime;
    private bool _globalTimePrepared;
    private Exception? _globalTimeConstructionFailure;

    internal void PrepareSourceGlobalTime(FalloutGlobalTimeSource source, FalloutGlobalTimeSnapshot? saved = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); source.Validate();
        if (_globalTimePrepared || _globalTime is not null || _globalTimeConstructionFailure is not null ||
            _cachedTimerConfiguration is null || source.Timer != _cachedTimerConfiguration.Source)
            throw new InvalidOperationException("Global time preparation changed its actual selected Main timer.");
        if (saved is not null)
        {
            FalloutGlobalTimeState.Validate(saved);
            if (saved.Source != source || saved.CapturedProcess == _process || _preparedCachedTimerSnapshot is not { } timer ||
                saved.TimerEpoch != timer.Epoch || saved.TimerSequence != timer.Sequence ||
                !FalloutSourceFrameTimer.SameFloat(saved.CurrentRate, timer.Rate) || !FalloutSourceFrameTimer.SameFloat(saved.TargetRate, timer.TargetRate))
                throw new InvalidDataException("Prepared global time differs from the same cold Main timer.");
        }
        else if (_preparedCachedTimerSnapshot is not null)
            throw new InvalidDataException("Cold Main timer omitted its required global audio-policy authority.");
        _globalTimeSource = source; _preparedGlobalTime = saved; _globalTimePrepared = true;
    }

    internal void PublishPreparedSourceGlobalTime()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_globalTimePrepared) return;
        if (_globalTime is not null) return;
        if (_globalTimeConstructionFailure is { } original)
            throw new InvalidOperationException("Global time retains its failed source factory.", original);
        try
        {
            _globalTime = new(_globalTimeSource ?? throw new InvalidOperationException("Prepared global time omitted its source."),
                _cachedSourceTimer ?? throw new NotSupportedException("Global time requires the actual native-published Main timer."),
                _preparedGlobalTime);
        }
        catch (Exception error) { _globalTimeConstructionFailure = error; throw; }
    }

    internal FalloutGlobalTimeState? ScriptGlobalTime => _globalTimePrepared ? RequireSourceGlobalTime() : null;

    internal FalloutGlobalTimeState RequireSourceGlobalTime()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _globalTime ?? throw new NotSupportedException("Global time has no actual selected/current Main timer owner.");
    }
    internal FalloutGlobalTimeSnapshot? CaptureSourceGlobalTime(FalloutSourceFrameTimerSnapshot? timer)
    {
        if (!_globalTimePrepared && _globalTime is null) return null;
        return RequireSourceGlobalTime().Capture(timer ??
            throw new InvalidDataException("Global time capture omitted the same actual timer snapshot."));
    }
    internal string? SourceGlobalTimeSaveBlocker => _globalTimeConstructionFailure is not null ?
        "source-global-time-construction-failed" : _globalTimePrepared && _globalTime is null ?
        "source-global-time-native-publication-pending" : _globalTime?.SaveBlocker;
    internal object? SourceGlobalTimeState => _globalTimeSource is null ? null : new
    {
        source = _globalTimeSource,
        prepared = _globalTimePrepared,
        state = _globalTime?.State,
        constructionFailure = _globalTimeConstructionFailure?.ToString(),
    };
    internal static void ValidateGlobalTimeContinuation(FalloutActorProcessRuntimeSnapshot saved)
    {
        if (saved.CachedTimer is not { } timer)
        {
            if (saved.GlobalTime is not null)
                throw new InvalidDataException("Global audio policy has no same captured source timer.");
            return;
        }
        var global = saved.GlobalTime ??
            throw new InvalidDataException("Current source timer omitted its global simulation/audio authority.");
        FalloutGlobalTimeState.Validate(global);
        if (global.CapturedProcess != saved.CapturedProcess || global.CapturedProcess != timer.CapturedProcess ||
            global.Source.Timer != timer.Source || global.TimerEpoch != timer.Epoch || global.TimerSequence != timer.Sequence ||
            !FalloutSourceFrameTimer.SameFloat(global.CurrentRate, timer.Rate) || !FalloutSourceFrameTimer.SameFloat(global.TargetRate, timer.TargetRate) || global.NativePublications < 1)
            throw new InvalidDataException("Global time lost its same source timer, process, rate cells or returned publication history.");
    }

    internal static void RequireCurrentGlobalTime(FalloutActorProcessRuntimeSnapshot saved)
    {
        if (saved.CachedTimer is null || saved.GlobalTime is null)
            throw new InvalidDataException("Current selected Main omitted its mandatory timer/global audio-policy continuation.");
        if (saved.CachedTimer.Source.EngineSha256 != saved.Fistp.ExecutableSha256)
            throw new InvalidDataException("Current Main timer belongs to a different selected executable.");
        ValidateGlobalTimeContinuation(saved);
    }

    internal void RetireSourceGlobalTime()
    {
        RequireMainScriptClosureBoundary(retiring: true);
        _globalTime?.Dispose(); _globalTime = null; _globalTimeSource = null;
        _preparedGlobalTime = null; _globalTimePrepared = false;
    }
}
