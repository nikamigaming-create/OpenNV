using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal static class FalloutSourceFrameDispatchSaveContract
{
    internal static void ValidateShape(FalloutProcessQueueSnapshots queues, FalloutCellProcessesSnapshot cells)
    {
        FalloutReferenceWorld.ValidateSourceFrameDispatch(queues.FrameDispatch);
        if (queues.FrameDispatch.Priority.Stack != queues.Loader.Stack)
            throw new InvalidDataException("Main priority/cache continuation lost the actual selected stack.");
        var cache = queues.FrameDispatch.Priority.Cache;
        if (cache is not null && !cells.Cells.Any(cell => cell.Source == cache.Cell.Source))
            throw new InvalidDataException("Main priority cache names an unconstructed or foreign source CELL.");
    }
    internal static void ValidateSource(FalloutPluginStack records, FalloutProcessQueueSnapshots queues)
    {
        var source = records.OwnedSource ?? throw new InvalidDataException("Main priority continuation has no selected source installation.");
        var frame = queues.FrameDispatch;
        var selected = FalloutMainFrameDeclaration.ForExecutable(FalloutActorProcessDeclaration.Read(source.FalloutExecutablePath).ExecutableSha256);
        if (frame.Priority.Contract != selected.Contract || frame.Priority.Stack != source.StackId)
            throw new InvalidDataException("Main priority/cache continuation changed its executable/settings/order binding.");
        if (frame.Priority.Cache is not { } cache) return;
        var current = new FalloutCellProcessSource(records).ReadPriorityCell(cache.Cell.Source.Cell);
        // The old actual instance GUID remains diagnostic. Cold construction
        // never reinstalls it as the new process's live CELL pointer cache.
        if (current.Source != cache.Cell.Source || current.Interior != cache.Cell.Interior ||
            current.X != cache.Cell.X || current.Y != cache.Cell.Y)
            throw new InvalidDataException("Main priority cache changed its original CELL flags/coordinates/world/master.");
    }
}
