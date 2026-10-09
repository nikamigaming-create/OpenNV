using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutProcessQueueSaveContract
{
    internal static void ValidateShape(FalloutProcessQueueSnapshots? queues, FalloutActorProcessesSnapshot? processes,
        FalloutCellProcessesSnapshot? cells)
    {
        if (queues is null || processes is null || cells is null)
            throw new InvalidDataException("Current save omitted its actual loader/CELL/pending process continuation.");
        FalloutReferenceWorld.ValidateSourceProcessQueueShape(queues);
        FalloutActorProcessManager.ValidateShape(processes); FalloutCellProcesses.RequireCommittedSnapshot(cells);
        FalloutSourceFrameDispatchSaveContract.ValidateShape(queues, cells);
        if (queues.Loader.Stack != processes.Stack || queues.Loader.Stack != cells.Stack ||
            queues.CurrentLists.Unowned.Count != 0 || queues.Cells.Cells.Any(cell => cell.Failure is not null) ||
            queues.Reevaluation.Invocations.Any(call => call.Failure is not null || call.Phase is not
                (FalloutProcessReevaluationPhase.Complete or FalloutProcessReevaluationPhase.AlreadyPending or FalloutProcessReevaluationPhase.ProcessAbsent)) ||
            queues.Cells.Invocations.Any(call => call.Phase != FalloutCellExtraProcessPhase.Complete || call.Failure is not null) ||
            queues.ActorFields.Actors.Any(actor => actor.Failure is not null))
            throw new NotSupportedException("Current queue continuation retains an actual unowned or entered source consumer.");
        var actors = processes.Actors.ToDictionary(actor => actor.Source.Reference, FalloutFormKeyComparer.Instance);
        if (!actors.Keys.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(queues.ActorFields.Actors.Select(actor => actor.Source.Reference)))
            throw new InvalidDataException("Current queue actor constructors differ from the actual process manager.");
        foreach (var field in queues.ActorFields.Actors)
            if (field.Source != actors[field.Source.Reference].Source || field.Retired != actors[field.Source.Reference].Retired)
                throw new InvalidDataException("Actor loader field lost its exact canonical process source/lifetime.");
        foreach (var field in queues.Reevaluation.Actors)
        {
            var process = actors[field.Source.Reference];
            if (field.Source != process.Source || field.Epoch != process.Epoch || field.Retired != process.Retired)
                throw new InvalidDataException("Pending process state has a foreign current actor epoch/source/lifetime.");
        }
        var cellSources = cells.Cells.ToDictionary(cell => cell.Source.Cell, cell => cell.Source, FalloutFormKeyComparer.Instance);
        if (!cellSources.Keys.ToHashSet(FalloutFormKeyComparer.Instance).SetEquals(queues.Cells.Cells.Select(cell => cell.Source.Cell)))
            throw new InvalidDataException("CELL extra state omitted or invented an actually constructed source CELL.");
        foreach (var cell in queues.Cells.Cells)
            if (cell.Source != cellSources[cell.Source.Cell])
                throw new InvalidDataException("CELL extra state changed its winning CELL/world/master relationship.");
        foreach (var list in queues.CurrentLists.Cells)
            if (!cellSources.TryGetValue(list.Source.Cell, out var actual) || actual != list.Source)
                throw new InvalidDataException("Current ordered CELL references have no matching actual source CELL constructor.");
        // The pending list is retained as typed metadata. Its next original
        // factory consumer is deliberately a separate runtime boundary; a
        // restored list never reports that the replacement already occurred.
    }

    internal static void ValidateSource(FalloutPluginStack records, FalloutProcessQueueSnapshots? queues,
        FalloutActorProcessesSnapshot? processes, FalloutCellProcessesSnapshot? cells)
    {
        ValidateShape(queues, processes, cells);
        var source = records.OwnedSource ?? throw new InvalidDataException("Process queue save has no selected original source.");
        var declaration = FalloutActorProcessQueueDeclaration.ForExecutable(
            FalloutActorProcessDeclaration.Read(source.FalloutExecutablePath).ExecutableSha256);
        var saved = queues!;
        FalloutSourceFrameDispatchSaveContract.ValidateSource(records, saved);
        if (saved.Loader.Contract != declaration.Contract || saved.Loader.Stack != source.StackId)
            throw new InvalidDataException("Process queue continuation changed its selected executable/configuration/source stack.");
        foreach (var queued in saved.Loader.Objects)
            if (FalloutQueuedReferenceSourceReader.Read(records, declaration, queued.Source.Reference) != queued.Source || queued.Source.BaseSignature == "SCPT")
                throw new InvalidDataException("Queued reference history changed its exact original reference/master/base/factory source.");
        var actorSource = FalloutCombatGroupDeclaration.ForExecutable(declaration.ExecutableSha256);
        foreach (var actor in saved.ActorFields.Actors)
            if (FalloutCombatActorSource.Read(records, actorSource, actor.Source.Reference) != actor.Source)
                throw new InvalidDataException("Actor queue fields changed the winning source/master/base relationship.");
        var reader = new FalloutCellProcessSource(records);
        foreach (var cell in saved.Cells.Cells)
            if (reader.ReadIdentity(cell.Source.Cell) != cell.Source)
                throw new InvalidDataException("CELL extra history changed the winning source/world/master.");
        foreach (var list in saved.CurrentLists.Cells)
        {
            var actual = reader.Read(list.Source.Cell);
            if (actual.Source != list.Source || actual.GraphSha256 != list.GraphSha256 ||
                !actual.References.SequenceEqual(list.Members.Select(member => member.Source)))
                throw new InvalidDataException("Current CELL list changed the complete source reference graph/order.");
        }
        foreach (var call in saved.Cells.Invocations.Where(call => call.CurrentReferences is not null))
        {
            var actual = reader.Read(call.Cell);
            if (!actual.References.SequenceEqual(call.CurrentReferences!.References.Select(child => child.Source)))
                throw new InvalidDataException("Counted CELL consumer history changed its exact source child order.");
            foreach (var child in call.CurrentReferences.References.Where(child => child.Actor is not null))
                if (FalloutCombatActorSource.Read(records, actorSource, child.Actor!.Reference) != child.Actor)
                    throw new InvalidDataException("Counted CELL history contains a foreign Actor process source.");
        }
    }
}
