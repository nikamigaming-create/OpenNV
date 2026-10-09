using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutCellProcessSource
{
    internal void ValidateRetainedScene(FalloutCellScene scene, FalloutCellProcessAttachment attachment)
    {
        _ = Read(scene.Cell.FormKey);
        if (!attachment.CellEpochs.ContainsKey(scene.Cell.FormKey) ||
            _scenes[scene.Cell.FormKey].Cell.Coordinates != scene.Cell.Coordinates ||
            JsonSerializer.Serialize(_scenes[scene.Cell.FormKey].Cell) != JsonSerializer.Serialize(scene.Cell) ||
            scene.References.Count != attachment.Children.Count)
            throw new InvalidDataException("Actual pre-change native graph differs from its retained winning CELL/child source.");
        for (var index = 0; index < scene.References.Count; index++)
        {
            var supplied = scene.References[index]; var child = attachment.Children[index];
            ValidateChildSource(child);
            var original = _placements[child.Source.SourceCell][child.Source.Reference];
            var expected = original with { Position = child.Placement.Position.ToArray(), RotationRadians = child.Placement.RotationRadians.ToArray() };
            if (ReadReference(supplied) != child.Source || !attachment.CellEpochs.ContainsKey(child.Placement.Cell) ||
                JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(supplied) ||
                !scene.BaseObjects.TryGetValue(child.Source.Base, out var basis) ||
                JsonSerializer.Serialize(basis) != JsonSerializer.Serialize(_scenes[child.Source.SourceCell].BaseObjects[child.Source.Base]))
                throw new InvalidDataException("Actual pre-change native reference lost its retained source/placement/base work.");
        }
    }
    internal void RequireCurrentChildPlacement(FalloutCellProcessPlacedChild child)
    {
        _ = Read(child.Source.SourceCell);
        var current = CurrentPlacement(_placements[child.Source.SourceCell][child.Source.Reference]);
        if (current.Cell != child.Placement.Cell || !current.Position.SequenceEqual(child.Placement.Position) ||
            !current.RotationRadians.SequenceEqual(child.Placement.RotationRadians))
            throw new InvalidDataException("Shared publication crossed a reentrant authoritative placement change.");
    }
}
