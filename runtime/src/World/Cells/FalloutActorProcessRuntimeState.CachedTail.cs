using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutMainCachedTailState? _mainCachedTail;
    private FalloutPluginStack? _mainCachedTailRecords;
    private int _registeredClockChannel;
    internal object? SourceMainCachedTailState => _mainCachedTail?.State;
    internal string? SourceMainCachedTailSaveBlocker => _mainCachedTail?.SaveBlocker ??
        (MainScriptCallerConstructed && _mainCachedTail is null ?
            "source-Main-post-sampling-tail-construction-unbound" : null);
    internal void ConfigureSourceMainCachedTail(FalloutPluginStack records, FalloutMainCachedTailSnapshot? saved)
    {
        RequireNotBusy(); var source = MainScriptCallerSource();
        if (_mainCachedTail is not null || _cachedSourceTimer is null ||
            records.OwnedSource?.StackId != _stack || saved is not null && saved.Source.Main != source)
            throw new InvalidDataException("Main tail must join the actual selected records/cached timer/current process once.");
        _mainCachedTailRecords = records;
        _mainCachedTail = new(FalloutMainCachedTailSource.Read(source), _stack, _process, new FalloutNativeSourceTickCounter(), saved,
            source.HasNewVegasChildren ? null : FalloutRendererClockConfiguration.Read(records, source));
    }
    internal void PublishSourceRendererClock(FalloutMainScriptInvocation invocation, float scaledSeconds, FalloutScriptSession session)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.CachedTailAfter); ArgumentNullException.ThrowIfNull(session);
        var timer = _cachedSourceTimer ?? throw new NotSupportedException("Renderer clock lost its actual Main cached timer.");
        if (!timer.HasReturnedUpdate || BitConverter.SingleToUInt32Bits(timer.ScaledSeconds) != BitConverter.SingleToUInt32Bits(scaledSeconds))
            throw new InvalidOperationException("Renderer clock did not consume the actual returned source timer value.");
        (_mainCachedTail ?? throw new NotSupportedException("Renderer clock lost its actual Main tail."))
            .PublishRendererClock(invocation, scaledSeconds, timer.ReadRates().Current,
                (_mainCachedTailRecords ?? throw new InvalidOperationException("Renderer clock lost its selected records.")).IniSettings.Float("fMaxTime:HAVOK"),
                session.SceneIsComplex);
        _registeredClockChannel = 0;
    }
    internal float ReadSourceRegisteredClock(FalloutMainScriptInvocation invocation, int index)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.CachedTailAfter);
        if (index != _registeredClockChannel || index is < 0 or > 3)
            throw new InvalidOperationException("Registered source clock changed the actual four-channel Main store order.");
        var source = (_mainCachedTail ?? throw new NotSupportedException("Registered clock lost its actual Main tail."))
            .ReadReturnedRendererClock(invocation);
        var timer = _cachedSourceTimer ?? throw new NotSupportedException("Registered clock lost its actual cached timer.");
        var value = index switch
        {
            0 => (_sourceMainImages ?? throw new NotSupportedException("Source channel zero lost its actual shared image/calendar cache."))
                .SampleSourceMainRegisteredHour(_process, source.HourFactor),
            1 => StandaloneInterface.Read(OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.MenuGate) != 0 ? 0 : timer.ScaledSeconds,
            2 => StandaloneInterface.Read(OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.MenuGate) != 0 ? 0 :
                (float)timer.ReadCached().Milliseconds / source.MillisecondsPerSecond,
            3 => timer.ScaledSeconds,
            _ => throw new InvalidDataException("Source Main clock channel is undefined."),
        };
        ++_registeredClockChannel; return value;
    }
    private void ExecuteSourceMainCachedTailBefore(FalloutMainScriptInvocation invocation)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.CachedTailBefore);
        var tail = _mainCachedTail ?? throw new NotSupportedException("Source Main cached tail constructor is absent.");
        tail.BeforeTimer(invocation, invocation.ReadCachedInterfaceFields().MenuGate, SourceMainCachedTailChildren());
    }
    private void ExecuteSourceMainCachedTimer(FalloutMainScriptInvocation invocation)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.CachedTimer);
        var tail = _mainCachedTail ?? throw new NotSupportedException("Source Main timer has no entered tail owner.");
        try { UpdateCachedSourceTimerFromOriginalTail(); tail.TimerReturned(invocation); }
        catch (Exception failure) { tail.RetainTimerFailure(invocation, failure); throw; }
    }
    private void ExecuteSourceMainCachedTailAfter(FalloutMainScriptInvocation invocation)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.CachedTailAfter);
        var tail = _mainCachedTail ?? throw new NotSupportedException("Source Main cached tail owner is absent.");
        tail.AfterTimer(invocation, invocation.ReadCachedInterfaceFields().MenuGate,
            (_cachedSourceTimer ?? throw new NotSupportedException("Actual Main cached timer is absent.")).ScaledSeconds,
            _mainCachedTailRecords ?? throw new NotSupportedException("Source Main tail lost its actual source records."), SourceMainCachedTailChildren());
    }
    private IFalloutMainCachedTailChildren SourceMainCachedTailChildren() => _scriptCallerConsumers as IFalloutMainCachedTailChildren ??
        throw new NotSupportedException("Actual source Main tail has no genuine native/request/render consumers.");
    internal void RetireSourceMainCachedTail()
    {
        RequireMainScriptClosureBoundary(retiring: true); _mainCachedTail?.Dispose(); _mainCachedTail = null; _mainCachedTailRecords = null;
    }
}
