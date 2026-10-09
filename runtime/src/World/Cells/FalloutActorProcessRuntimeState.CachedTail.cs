using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutMainCachedTailState? _mainCachedTail;
    private FalloutPluginStack? _mainCachedTailRecords;
    internal object? SourceMainCachedTailState => _mainCachedTail?.State;
    internal string? SourceMainCachedTailSaveBlocker => _mainCachedTail?.SaveBlocker ??
        (MainScriptCallerConstructed && MainScriptCallerSource().HasNewVegasChildren && _mainCachedTail is null ?
            "source-Main-post-sampling-tail-construction-unbound" : null);
    internal void ConfigureSourceMainCachedTail(FalloutPluginStack records, FalloutMainCachedTailSnapshot? saved)
    {
        RequireNotBusy(); var source = MainScriptCallerSource();
        if (!source.HasNewVegasChildren || _mainCachedTail is not null || _cachedSourceTimer is null ||
            records.OwnedSource?.StackId != _stack || saved is not null && saved.Source.Main != source)
            throw new InvalidDataException("Main tail must join the actual selected records/cached timer/current process once.");
        _mainCachedTailRecords = records;
        _mainCachedTail = new(FalloutMainCachedTailSource.Read(source), _stack, _process, new FalloutNativeSourceTickCounter(), saved);
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
