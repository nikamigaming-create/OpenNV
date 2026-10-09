using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutSourceFrameTimerConfiguration? _cachedTimerConfiguration;
    private FalloutSourceFrameTimerState? _cachedSourceTimer;
    private Exception? _cachedTimerConstructionFailure;
    private FalloutSourceFrameTimerSnapshot? _preparedCachedTimerSnapshot;
    private bool _cachedTimerPrepared;
    private object? _cachedTimerNativeOwner;
    private int? _cachedTimerNativeThread;

    // Source/INI and cold validation is preparation. It does not sample OS
    // time or construct the mutable source clock on a background data worker.
    internal void PrepareSourceCachedTimer(FalloutSourceFrameTimerConfiguration configuration,
        FalloutSourceFrameTimerSnapshot? saved = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); configuration.Validate();
        if (_cachedSourceTimer is not null || _cachedTimerConfiguration is not null || _cachedTimerConstructionFailure is not null ||
            configuration.Source.EngineSha256 != _source.ExecutableSha256)
            throw new InvalidOperationException("Source clock preparation changed its selected Main or retried an attempted factory.");
        if (saved is not null)
        {
            FalloutSourceFrameTimerState.Validate(saved);
            if (saved.Source != configuration.Source || saved.CapturedProcess == _process)
                throw new InvalidDataException("Prepared cold timer changed source or reused a captured process.");
        }
        _cachedTimerConfiguration = configuration; _preparedCachedTimerSnapshot = saved; _cachedTimerPrepared = true;
    }
    internal void PublishPreparedSourceCachedTimer(object actualNativeOwner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(actualNativeOwner);
        if (!_cachedTimerPrepared) return; // Unsupported selected source has no timer preparation.
        if (_cachedTimerNativeOwner is not null)
        {
            if (!ReferenceEquals(_cachedTimerNativeOwner, actualNativeOwner) || _cachedTimerNativeThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Source clock publication changed its actual native Main owner.");
            return;
        }
        if (_cachedTimerConstructionFailure is { } original) throw new InvalidOperationException("Source clock factory retains its failed publication.", original);
        var configuration = _cachedTimerConfiguration ?? throw new InvalidOperationException("Prepared source clock omitted its immutable declaration.");
        _cachedTimerNativeOwner = actualNativeOwner; _cachedTimerNativeThread = Environment.CurrentManagedThreadId;
        try { _cachedSourceTimer = new(configuration.Source, new FalloutNativeSourceTickCounter(), _process, _preparedCachedTimerSnapshot); }
        catch (Exception failure) { _cachedTimerConstructionFailure = failure; throw; }
    }

    internal void ConfigureSourceCachedTimer(FalloutSourceFrameTimerConfiguration configuration,
        FalloutSourceFrameTimerSnapshot? saved = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); configuration.Validate();
        if (_cachedSourceTimer is not null || _cachedTimerConfiguration is not null || _cachedTimerConstructionFailure is not null ||
            configuration.Source.EngineSha256 != _source.ExecutableSha256)
            throw new InvalidOperationException("Cached source timer already has a Main lifetime or a foreign executable.");
        _cachedTimerConfiguration = configuration;
        try { _cachedSourceTimer = new(configuration.Source, new FalloutNativeSourceTickCounter(), _process, saved); }
        catch (Exception failure) { _cachedTimerConstructionFailure = failure; throw; }
    }

    // This local writer belongs to the original Main tail after the second
    // sampling segment. The intervening children and the remainder of that
    // timer phase need their own real caller before this private method is used.
    // ClockPrelude is a different, source-proven no-op and cannot call it.
    private void UpdateCachedSourceTimerFromOriginalTail()
    {
        var timer = _cachedSourceTimer ?? throw new NotSupportedException("Actual Main cached timer was not constructed.");
        var configuration = _cachedTimerConfiguration ?? throw new NotSupportedException("Actual Main clock has no selected INI owner.");
        var cached = _mainInterfaceCachedFields ?? throw new NotSupportedException("Actual Main has no constructed cached menu field.");
        timer.UpdateFromMain(configuration, cached.MenuGate);
    }

    internal FalloutSandboxTimerSample ReadSourceCachedMilliseconds()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var timer = _cachedSourceTimer ?? throw new NotSupportedException("Selected original cached UInt32 timer producer is absent.");
        if (!timer.HasReturnedUpdate)
            throw new NotSupportedException("Original post-sampling Main cached timer caller has not returned; a constructor zero is not an advancing clock.");
        return timer.ReadCached();
    }
    internal FalloutSourceFrameTimerSnapshot CaptureSourceCachedTimer() => (_cachedSourceTimer ??
        throw new NotSupportedException("Selected source cached clock has no actual current/cold owner.")).Capture();
    internal string? SourceCachedTimerSaveBlocker => _cachedTimerConstructionFailure is not null ?
        "source-cached-timer-construction-failed" : _cachedSourceTimer?.SaveBlocker;
    internal string? SourceCachedTimerRuntimeBoundary => _cachedSourceTimer is null ?
        _cachedTimerPrepared ? "source-cached-timer-native-factory-publication-pending" : "selected-cached-timer-constructor-unowned" :
        !_cachedSourceTimer.HasReturnedUpdate ? "original-Main-tail-cached-timer-writer-unbound" : null;
    internal object? SourceCachedTimerState => _cachedTimerConfiguration is null ? null : new
    {
        source = _cachedTimerConfiguration,
        timer = _cachedSourceTimer?.State,
        prepared = _cachedTimerPrepared,
        nativeOwnerPublished = _cachedTimerNativeOwner is not null,
        constructionFailure = _cachedTimerConstructionFailure?.ToString(),
        runtimeBoundary = SourceCachedTimerRuntimeBoundary
    };
    internal void RetireSourceCachedTimer()
    {
        RequireMainScriptClosureBoundary(retiring: true);
        _cachedSourceTimer?.Dispose(); _cachedSourceTimer = null; _cachedTimerConfiguration = null;
        _preparedCachedTimerSnapshot = null; _cachedTimerNativeOwner = null; _cachedTimerNativeThread = null; _cachedTimerPrepared = false;
    }
}
