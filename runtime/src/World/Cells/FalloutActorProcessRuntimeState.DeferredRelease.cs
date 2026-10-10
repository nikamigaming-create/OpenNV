using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private void ConstructCurrentDeferredRelease(FalloutMainPlayerCellSource player, FalloutMainPlayerPendingSnapshot? saved)
    {
        if (player.Main.HasNewVegasChildren)
        {
            var newVegas = new FalloutNewVegasDeferredRelease(player, _stack, _process, saved?.NewVegasDeferredRelease);
            MainPlayerPendingConsumers.BindNewVegasDeferredRelease(newVegas, RequireMainNativeDeliveryClosed);
            return;
        }
        if (!FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256) || player != MainPlayerCellSource)
            throw new InvalidDataException("Release manager construction changed the actual selected Main/Player source.");
        var manager = new FalloutSourceDeferredRelease(player, _stack, _process, ReadActualDeferredMainWord,
            RequireMainNativeDeliveryClosed, saved?.DeferredRelease);
        MainPlayerPendingConsumers.BindCurrentDeferredRelease(manager);
    }

    private FalloutActorProcessFact<uint> ReadActualDeferredMainWord()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!MainPlayerCellConstructed || !FalloutSourceMainFamily.IsFallout3(_source.ExecutableSha256))
            throw new NotSupportedException("Deferred source writer has no actual constructed current Main word.");
        _mainFrameDeclaration.Validate();
        return new(_mainWord, "actual-source-Main-forced-processing-word/" + _process + "/" + _sequence, _mainFrameBoundary);
    }
}
