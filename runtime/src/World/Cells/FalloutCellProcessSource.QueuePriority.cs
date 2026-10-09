using System.Runtime.CompilerServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcessSource
{
    private sealed class PriorityCellInstance { internal readonly Guid Identity = Guid.NewGuid(); }
    private readonly ConditionalWeakTable<FalloutCellDefinition, PriorityCellInstance> _priorityInstances = new();
    internal FalloutQueuedPriorityCell ReadPriorityCell(FalloutFormKey cell)
    {
        var source = Read(cell).Source; var definition = _scenes[cell].Cell;
        return new(source, _priorityInstances.GetValue(definition, _ => new()).Identity,
            (definition.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0,
            definition.Coordinates?.X, definition.Coordinates?.Y);
    }
}

internal sealed partial class FalloutCellProcesses
{
    internal FalloutQueuedPriorityCell ReadSourcePriorityCell(FalloutFormKey cell)
    {
        _ = RequireHealthy(cell);
        return _source.ReadPriorityCell(cell);
    }
}
