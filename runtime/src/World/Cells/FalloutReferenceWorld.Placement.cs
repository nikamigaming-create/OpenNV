using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutReferencePlacement(FalloutFormKey Cell, float[] Position, float[] RotationRadians)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId == 0 ||
            Position is not { Length: 3 } || RotationRadians is not { Length: 3 } ||
            Position.Concat(RotationRadians).Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Reference placement is invalid.");
    }
    internal FalloutReferencePlacement Copy() => new(Cell, (float[])Position.Clone(), (float[])RotationRadians.Clone());
}

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutCellScene> _placementSourceCells = [];
    internal long PlacementRevision { get; private set; }

    private FalloutCellScene PlacementSource(FalloutReferenceInstance instance)
    {
        if (!_placementSourceCells.TryGetValue(instance.Cell, out var scene))
            _placementSourceCells.Add(instance.Cell, scene = FalloutCellSceneReader.Read(records, instance.Cell));
        return scene;
    }

    internal FalloutReferencePlacement Placement(FalloutFormKey reference)
    {
        var instance = Get(reference);
        if (instance.Placement is { } retained) return retained.Copy();
        var source = PlacementSource(instance).References.Single(value => value.FormKey == reference);
        return new(instance.Cell, (float[])source.Position.Clone(), (float[])source.RotationRadians.Clone());
    }

    internal void MoveTo(FalloutFormKey reference, FalloutFormKey destination, float x = 0, float y = 0, float z = 0)
    {
        var instance = Get(reference);
        if (instance.Deleted || instance.DeletePending) throw new InvalidOperationException("Cannot move a deleted reference.");
        var previous = Placement(reference);
        var target = Placement(destination);
        var moved = new FalloutReferencePlacement(target.Cell,
            [target.Position[0] + x, target.Position[1] + y, target.Position[2] + z], previous.RotationRadians);
        moved.Validate();
        moved = ProjectMovedActor(instance, moved);
        SetPlacement(reference, moved);
    }

    internal void SetPlacement(FalloutFormKey reference, FalloutReferencePlacement placement)
    {
        var instance = Get(reference);
        placement.Validate();
        if (instance.Deleted || instance.DeletePending || records.GetEffective(placement.Cell).Signature != "CELL")
            throw new InvalidDataException("Reference placement target is unavailable.");
        instance.Placement = placement.Copy();
        instance.PackageMotion = null;
        instance.Engagement = null;
        instance.PlacementRevision = ++PlacementRevision;
    }

    internal IReadOnlyList<FalloutReferenceInstance> MovedSince(long revision) =>
        _instances.Values.Where(instance => instance.PlacementRevision > revision).ToArray();

    internal FalloutCellScene ComposeResidency(FalloutCellScene scene, IReadOnlyList<FalloutCellDefinition>? exteriorCells = null)
    {
        var coordinates = exteriorCells?.Select(cell => cell.Coordinates!.Value).ToHashSet();
        bool Resident(FalloutReferencePlacement placement)
        {
            var cell = FalloutCellSceneReader.ReadDefinition(records, placement.Cell);
            if (scene.Cell.Worldspace is null) return placement.Cell == scene.Cell.FormKey;
            if (cell.Worldspace != scene.Cell.Worldspace) return false;
            var coordinate = ((int)MathF.Floor(placement.Position[0] / 4096), (int)MathF.Floor(placement.Position[1] / 4096));
            return coordinates?.Contains(coordinate) ?? coordinate == scene.Cell.Coordinates;
        }
        var references = scene.References.ToDictionary(reference => reference.FormKey);
        var bases = scene.BaseObjects.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var instance in _instances.Values.Where(value => value.Placement is not null))
        {
            var placement = instance.Placement!;
            if (!Resident(placement)) { references.Remove(instance.Reference); continue; }
            var source = PlacementSource(instance);
            var reference = source.References.Single(value => value.FormKey == instance.Reference);
            references[instance.Reference] = reference with
            {
                Position = (float[])placement.Position.Clone(),
                RotationRadians = (float[])placement.RotationRadians.Clone()
            };
            bases.TryAdd(instance.Base, source.BaseObjects[instance.Base]);
        }
        return scene with { References = references.Values.ToArray(), BaseObjects = bases };
    }

    internal void ReplaceResidentCell(FalloutCellScene scene)
    {
        if (!_residentCells.TryGetValue(scene.Cell.FormKey, out var previous))
            throw new InvalidOperationException("Cannot update an unloaded CELL.");
        var next = scene.References.Select(reference => Get(reference.FormKey)).ToArray();
        if (next.Select(instance => instance.Reference).Distinct().Count() != next.Length ||
            next.Where((instance, index) => instance.Cell != scene.References[index].Cell).Any())
            throw new InvalidDataException("Replacement residency changed source reference identity.");
        var oldKeys = previous.Select(instance => instance.Reference).ToHashSet();
        var newKeys = next.Select(instance => instance.Reference).ToHashSet();
        foreach (var key in oldKeys.Except(newKeys))
        {
            var remaining = _residentReferences[key] - 1;
            if (remaining > 0) _residentReferences[key] = remaining;
            else
            {
                _residentReferences.Remove(key);
                var instance = _instances[key];
                if (instance.DeletePending) { instance.Deleted = true; instance.DeletePending = false; }
            }
        }
        foreach (var key in newKeys.Except(oldKeys)) _residentReferences[key] = _residentReferences.GetValueOrDefault(key) + 1;
        _residentCells[scene.Cell.FormKey] = next;
    }
}
