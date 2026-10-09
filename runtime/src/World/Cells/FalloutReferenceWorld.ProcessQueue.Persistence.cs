using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutProcessQueueSnapshots(FalloutQueuedReferencesSnapshot Loader,
    FalloutActorLoaderFieldsSnapshot ActorFields, FalloutProcessReevaluationSnapshot Reevaluation,
    FalloutCellExtraProcessSnapshot Cells, FalloutCurrentCellProcessListsSnapshot CurrentLists,
    FalloutSourceFrameDispatchSnapshot FrameDispatch);

internal sealed partial class FalloutReferenceWorld
{
    internal FalloutProcessQueueSnapshots CaptureSourceProcessQueues()
    {
        if (SourceProcessQueueSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        ActorLoaderFields.RequireActors(ActorPerception.ConstructedSourceActors);
        ProcessReevaluation.RequireActors(ActorProcesses.Capture().Actors);
        foreach (var cell in CellProcesses.ConstructedSourceCells) CellExtraProcess.Construct(cell);
        return new(QueuedReferences.Capture(), ActorLoaderFields.Capture(), ProcessReevaluation.Capture(),
            CellExtraProcess.Capture(), CaptureSourceCurrentCellLists(), CaptureSourceFrameDispatch());
    }
    internal static void ValidateSourceProcessQueueShape(FalloutProcessQueueSnapshots snapshot)
    {
        if (snapshot is null || snapshot.Loader is null || snapshot.ActorFields is null || snapshot.Reevaluation is null ||
            snapshot.Cells is null || snapshot.CurrentLists is null || snapshot.FrameDispatch is null)
            throw new InvalidDataException("Saved process queues omitted an actual current authoritative owner.");
        FalloutQueuedReferences.Validate(snapshot.Loader); FalloutActorLoaderFields.Validate(snapshot.ActorFields);
        FalloutProcessReevaluationState.Validate(snapshot.Reevaluation); FalloutCellExtraProcessState.Validate(snapshot.Cells);
        ValidateSourceCurrentCellLists(snapshot.CurrentLists);
        ValidateSourceFrameDispatch(snapshot.FrameDispatch);
        var executable = FalloutMainFrameDeclaration.Executables.FirstOrDefault(image =>
            FalloutActorProcessQueueDeclaration.ForExecutable(image).Contract == snapshot.Loader.Contract) ??
            throw new InvalidDataException("Source queue snapshot lost its selected original declaration.");
        if (snapshot.FrameDispatch.Priority.Contract != FalloutMainFrameDeclaration.ForExecutable(executable).Contract ||
            snapshot.FrameDispatch.Priority.Stack != snapshot.Loader.Stack || snapshot.CurrentLists.Links.Source.EngineSha256 != executable)
            throw new InvalidDataException("Main cache and loader changed their selected source/order binding.");
        if (snapshot.Loader.Stack != snapshot.ActorFields.Stack || snapshot.Loader.Stack != snapshot.Reevaluation.Stack ||
            snapshot.Loader.Stack != snapshot.Cells.Stack || snapshot.Loader.Stack != snapshot.CurrentLists.Stack ||
            snapshot.Loader.Contract != snapshot.ActorFields.Contract || snapshot.Loader.Contract != snapshot.Reevaluation.Contract ||
            snapshot.Loader.Contract != snapshot.Cells.Contract || snapshot.Loader.Contract != snapshot.CurrentLists.Contract ||
            !snapshot.ActorFields.Actors.Select(value => value.Source).ToHashSet()
                .SetEquals(snapshot.Reevaluation.Actors.Select(value => value.Source)))
            throw new InvalidDataException("Saved loader/CELL/Actor/pending queue owners disagree on their selected source graph.");
    }
}
