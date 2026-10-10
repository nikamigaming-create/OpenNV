using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal uint ReadStandalonePendingPostNullMainWord(FalloutMainPlayerCellInvocation invocation)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.PendingFlagQueries);
        if (!FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256) ||
            _mainPlayerCellSource is null || _mainPlayerCellSource.Main.HasNewVegasChildren ||
            _mainPlayerCellSource.Main != MainScriptCallerSource() || _standaloneMainRetired ||
            _standaloneMainSource != _mainPlayerCellSource.Main)
            throw new NotSupportedException("Post-null Main word has no actual selected standalone constructor/caller.");
        _mainFrameDeclaration.Validate();
        if (_mainFrameDeclaration.ExecutableSha256 != _mainPlayerCellSource.Main.EngineSha256)
            throw new InvalidDataException("Post-null word changed the actual Main field source.");
        var call = _mainPlayerCellLast!;
        if (call.Pending is null || call.Disposition != FalloutMainPlayerCellDisposition.Entered ||
            _mainPlayerPending is null || _mainPlayerPending.Pending || _mainPlayerPending.Error is not null ||
            !Returned(FalloutMainPlayerCellStep.PendingDestination, true) ||
            !Returned(FalloutMainPlayerCellStep.PendingDeferredDestruction, null) ||
            !Returned(FalloutMainPlayerCellStep.PendingNullStore, null))
            throw new InvalidOperationException("Standalone post-null word cannot precede the actual destination/deferred/payload return.");
        if (_mainFrameBoundary is { } boundary)
            throw new NotSupportedException("Post-null Main word retains its actual source writer failure: " + boundary);
        // The selected source reads the same living Main word used by its
        // real forced-processing writers and raw deferred-release gate. The
        // other engine's manager getter and second Player-bit query differ.
        // Capture/cold already owns this whole word; there is no shadow flag.
        return _mainWord;

        bool Returned(FalloutMainPlayerCellStep step, bool? value) => call.Children.Any(child =>
            child.Step == step && child.Returned is not null && child.Error is null &&
            (value is null || child.Boolean == value));
    }
}
