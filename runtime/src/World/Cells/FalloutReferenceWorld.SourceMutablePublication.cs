using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private object? _sourceMutableNativeOwner;
    private int? _sourceMutableNativeThread;
    private Exception? _sourceMutableNativeFailure;
    private bool _sourceMutableRetirementOnly, _sourceMainTailPrepared;
    private FalloutMainCachedTailSnapshot? _preparedSourceMainTail;

    internal object SourceMutableNativePublicationState => new
    {
        published = _sourceMutableNativeOwner is not null,
        retirementOnly = _sourceMutableRetirementOnly,
        failure = _sourceMutableNativeFailure?.ToString(),
        timer = _processRuntime?.SourceCachedTimerState
    };
    internal string? SourceMutableNativePublicationSaveBlocker => _sourceMutableNativeFailure is not null ?
        "source-mutable-native-publication-failed" : _sourceMutableRetirementOnly ?
        "source-mutable-prepared-retirement-entered" : _sourceMutableNativeOwner is null &&
        (_sourceCellLinks is not null || _sourceMainTailPrepared) ? "source-mutable-native-publication-pending" : null;

    // Called during immutable campaign preparation. No native clock is
    // sampled and no new CELL insertion is entered on that worker.
    private void PrepareCampaignSourceMainTail(FalloutMainCachedTailSnapshot? saved)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CampaignMainScriptSource.HasNewVegasChildren) return;
        if (_sourceMainTailPrepared || _sourceMutableNativeOwner is not null)
            throw new InvalidOperationException("Source Main tail preparation replaced its current/native lifetime.");
        if ((_processRuntimeRestore is null) != (saved is null))
            throw new InvalidDataException("Cold Main omitted its source tail; constructor state cannot replace it.");
        if (saved is not null)
        {
            FalloutMainCachedTailState.Validate(saved);
            if (saved.Source.Main != CampaignMainScriptSource || saved.Stack != _processRuntimeStack)
                throw new InvalidDataException("Prepared Main tail changed the exact current source/stack.");
        }
        _preparedSourceMainTail = saved; _sourceMainTailPrepared = true;
    }

    // The caller is the living native world root after its background await.
    // Publication precedes the first new source writer. A successful handoff
    // cannot move again, even to another thread using the same root object.
    internal void PublishSourceMutableNativeOwners(object actualNativeRoot)
    {
        if (!BindPreparedSourceMutableThread(actualNativeRoot, retirementOnly: false)) return;
        try
        {
            _processRuntime?.PublishPreparedSourceCachedTimer(actualNativeRoot);
            if (_sourceMainTailPrepared)
                ProcessRuntime.ConfigureSourceMainCachedTail(records, _preparedSourceMainTail);
        }
        catch (Exception failure) { _sourceMutableNativeFailure = failure; throw; }
    }

    // A failed preparation may have no native presentation to consume it.
    // Its actual main-thread cleanup can adopt only unentered preparation;
    // it never constructs/samples a clock merely to destroy that clock.
    internal void BindPreparedSourceMutableRetirement(object actualNativeRoot)
    {
        _ = BindPreparedSourceMutableThread(actualNativeRoot, retirementOnly: true);
    }
    private bool BindPreparedSourceMutableThread(object actualNativeRoot, bool retirementOnly)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(actualNativeRoot);
        if (_sourceMutableNativeOwner is not null)
        {
            if (!ReferenceEquals(_sourceMutableNativeOwner, actualNativeRoot) ||
                _sourceMutableNativeThread != Environment.CurrentManagedThreadId || _sourceMutableRetirementOnly && !retirementOnly)
                throw new InvalidOperationException("Mutable source publication changed its actual root/thread or replayed preparation retired for failure.");
            if (_sourceMutableNativeFailure is { } original && !retirementOnly)
                throw new InvalidOperationException("Mutable source publication retains its failed factory prefix.", original);
            return false;
        }
        _sourceMutableNativeOwner = actualNativeRoot; _sourceMutableNativeThread = Environment.CurrentManagedThreadId;
        _sourceMutableRetirementOnly = retirementOnly;
        try
        {
            _sourceCellLinks?.PublishNativeOwner(actualNativeRoot);
            _sourceCellIngestion?.PublishNativeOwner(actualNativeRoot);
        }
        catch (Exception failure) { _sourceMutableNativeFailure = failure; throw; }
        return true;
    }
}
