using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutImageSpaceState? _sourceMainImages;
    internal string? SourceMainImageSaveBlocker => _sourceMainImages?.SourceMainImageSaveBlocker;
    internal IDisposable BindSourceMainImages(FalloutImageSpaceState state)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(state);
        if (_sourceMainImages is not null || _scriptCallerInvocation is not null || state.SourceMainImageProcess != _process ||
            MainScriptCallerSource().HasNewVegasChildren)
            throw new InvalidOperationException("Main image binding changed its actual source process, invocation or owner.");
        _sourceMainImages = state; return new ImageLease(this, state);
    }
    private sealed class ImageLease(FalloutActorProcessRuntimeState owner, FalloutImageSpaceState state) : IDisposable
    {
        private bool _retired;
        public void Dispose()
        {
            if (_retired) return;
            if (owner._scriptCallerInvocation is not null || !ReferenceEquals(owner._sourceMainImages, state))
                throw new InvalidOperationException("Main image retirement changed its current or entered owner.");
            owner._sourceMainImages = null; _retired = true;
        }
    }
    internal void ExecuteSourceMainImages(FalloutMainScriptInvocation invocation, bool advance)
    {
        invocation.Require(FalloutMainScriptCallerStep.TimedContexts);
        var timer = _cachedSourceTimer ?? throw new NotSupportedException("Source image Main has no actual source timer constructor.");
        var state = _sourceMainImages ?? throw new NotSupportedException("source-Main-timed-image-manager-current-binding-unowned");
        state.ExecuteSourceImageMain(invocation, advance, timer.ScaledSeconds, _contextTimeBits, () => _contextTimeBits);
    }
    internal uint ReadSourceImageContextClock(FalloutMainScriptInvocation invocation)
    { invocation.Require(FalloutMainScriptCallerStep.TimedContexts); return _contextTimeBits; }

    internal uint ReadSourceImageMenuClock(FalloutImageSpaceState state)
    {
        if (!ReferenceEquals(_sourceMainImages, state) || state.SourceMainImageProcess != _process)
            throw new InvalidOperationException("Menu image clock is not the actual current shared process binding.");
        return _contextTimeBits;
    }
}

internal sealed partial class FalloutReferenceWorld
{
    internal IDisposable BindCampaignSourceMainImages(FalloutImageSpaceState state) => ProcessRuntime.BindSourceMainImages(state);
    internal uint ReadCampaignSourceImageMenuClock(FalloutImageSpaceState state) => ProcessRuntime.ReadSourceImageMenuClock(state);
}
