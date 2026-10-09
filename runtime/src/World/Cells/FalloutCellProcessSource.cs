using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// Exact source metadata is reused in memory. This is not transformed geometry,
// native publication, or a claim that all CELL fields have a gameplay owner.
internal sealed class FalloutCellProcessSource(FalloutPluginStack records,
    Func<FalloutPlacedReference, FalloutReferencePlacement>? currentPlacement = null)
{
    private readonly Dictionary<FalloutFormKey, FalloutCellProcessData> _decoded = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, FalloutCellScene> _scenes = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, Dictionary<FalloutFormKey, FalloutPlacedReference>> _placements = new(FalloutFormKeyComparer.Instance);
    private readonly Dictionary<FalloutFormKey, Dictionary<FalloutFormKey, FalloutCellProcessReference>> _references = new(FalloutFormKeyComparer.Instance);
    internal FalloutCellProcessIdentity ReadIdentity(FalloutFormKey cell)
    {
        var source = records.GetEffective(cell);
        if (source.Signature != "CELL" || source.IsDeleted) throw new InvalidDataException("CELL process has no winning live CELL source.");
        var world = FalloutCellSceneReader.ParentWorldspace(source);
        var worldRecord = world is { } parent ? records.GetEffective(parent) : null;
        if (worldRecord is not null && (worldRecord.Signature != "WRLD" || worldRecord.IsDeleted))
            throw new InvalidDataException("CELL process source world/master is not a winning WRLD.");
        return new(cell, source.Flags, Digest(source), world, worldRecord is null ? null : Digest(worldRecord));
    }
    internal FalloutCellProcessData Read(FalloutFormKey cell)
    {
        var identity = ReadIdentity(cell);
        if (_decoded.TryGetValue(cell, out var cached))
        {
            if (cached.Source != identity) throw new InvalidDataException("CELL process immutable source changed during its lifetime.");
            return cached;
        }
        var scene = FalloutCellSceneReader.Read(records, cell);
        var references = scene.References.Select(ReadReference).ToArray();
        if (references.Any(reference => reference.SourceCell != cell) ||
            references.Select(reference => reference.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != references.Length)
            throw new InvalidDataException("CELL process data lost exact source child ancestry/order.");
        var graph = Digest(JsonSerializer.SerializeToUtf8Bytes(new { identity, references }));
        var result = new FalloutCellProcessData(identity, references, graph);
        _decoded.Add(cell, result); _scenes.Add(cell, scene);
        _placements.Add(cell, scene.References.ToDictionary(reference => reference.FormKey, FalloutFormKeyComparer.Instance));
        _references.Add(cell, references.ToDictionary(reference => reference.Reference, FalloutFormKeyComparer.Instance));
        return result;
    }
    internal FalloutCellProcessReference ReadReference(FalloutPlacedReference reference)
    {
        var record = records.GetEffective(reference.FormKey);
        if (record.IsDeleted || record.Signature is not ("REFR" or "ACHR" or "ACRE" or "PGRE" or "PMIS") ||
            FalloutCellSceneReader.ParentCell(record) != reference.Cell ||
            FalloutDialogueTopic.RequiredForm(record, "NAME") != reference.Base || record.Flags != reference.Flags)
            throw new InvalidDataException("CELL child work has a foreign winning reference/master/base.");
        var basis = records.TryGetEffective(reference.Base, out var winning) ? winning : null;
        if (basis is not null && basis.IsDeleted || basis is null && !record.ReadSubrecords().Any(field => field.Signature == "XPRM"))
            throw new InvalidDataException("CELL child work has no actual winning or source primitive base.");
        return new(record.FormKey, reference.Cell, record.Signature, record.Flags, Digest(record), reference.Base,
            basis is null ? null : Digest(basis));
    }
    internal IReadOnlyList<FalloutCellProcessPlacedChild> ValidateCurrentScene(FalloutCellScene scene,
        IReadOnlyList<FalloutFormKey> cells)
    {
        var selected = cells.ToHashSet(FalloutFormKeyComparer.Instance);
        if (selected.Count != cells.Count || !selected.Contains(scene.Cell.FormKey))
            throw new InvalidDataException("Native CELL attachment has an ambiguous or missing current source CELL.");
        var source = Read(scene.Cell.FormKey).Source;
        var actualDefinition = _scenes[scene.Cell.FormKey].Cell;
        if (JsonSerializer.Serialize(actualDefinition) != JsonSerializer.Serialize(scene.Cell))
            throw new InvalidDataException("Native CELL definition differs from its winning source/inheritance.");
        foreach (var cell in cells)
            if (ReadIdentity(cell).Worldspace != source.Worldspace)
                throw new InvalidDataException("Native CELL grid crossed its actual world/master ownership.");
        var rows = scene.References.Select(ReadReference).ToArray();
        if (rows.Select(row => row.Reference).Distinct(FalloutFormKeyComparer.Instance).Count() != rows.Length)
            throw new InvalidDataException("Native CELL attachment omitted its real source ancestry or repeats child work.");
        var supplied = scene.References.ToDictionary(reference => reference.FormKey, FalloutFormKeyComparer.Instance);
        var placed = new List<FalloutCellProcessPlacedChild>(rows.Length);
        foreach (var row in rows)
        {
            _ = Read(row.SourceCell);
            if (!_references[row.SourceCell].TryGetValue(row.Reference, out var exact) || exact != row)
                throw new InvalidDataException("Native CELL reference graph differs from its exact winning reader result.");
            var original = _placements[row.SourceCell][row.Reference];
            var current = CurrentPlacement(original);
            if (!selected.Contains(current.Cell))
                throw new InvalidDataException("Native CELL child has no actual current CELL in this attachment.");
            var expectedPlacement = original with { Position = current.Position.ToArray(), RotationRadians = current.RotationRadians.ToArray() };
            if (JsonSerializer.Serialize(expectedPlacement) != JsonSerializer.Serialize(supplied[row.Reference]) ||
                !scene.BaseObjects.TryGetValue(row.Base, out var suppliedBase) ||
                JsonSerializer.Serialize(_scenes[row.SourceCell].BaseObjects[row.Base]) != JsonSerializer.Serialize(suppliedBase))
                throw new InvalidDataException("Native CELL child placement/base fields differ from their actual source parser.");
            placed.Add(new(row, current.Copy()));
        }
        var present = rows.Select(row => row.Reference).ToHashSet(FalloutFormKeyComparer.Instance);
        foreach (var cell in cells)
        {
            _ = Read(cell);
            // The existing exterior source owner selects a spatial subset of
            // persistent references. Temporary/interior omissions must be an
            // actual retained move outside this attachment, never a draw skip.
            if ((ReadIdentity(cell).Flags & 0x400) != 0) continue;
            foreach (var reference in _placements[cell].Values)
                if (selected.Contains(CurrentPlacement(reference).Cell) && !present.Contains(reference.FormKey))
                    throw new InvalidDataException("Native CELL attachment omitted a winning temporary/interior child.");
        }
        return placed;
    }
    internal void ValidateRetained(FalloutCellProcessData saved)
    {
        var actual = Read(saved.Source.Cell);
        if (saved.Source != actual.Source || saved.GraphSha256 != actual.GraphSha256 ||
            !saved.References.SequenceEqual(actual.References))
            throw new InvalidDataException("Retained CELL data differs from its complete winning source metadata graph.");
    }
    internal void ValidateChildSource(FalloutCellProcessChild child)
    {
        _ = Read(child.Source.SourceCell);
        if (!_references[child.Source.SourceCell].TryGetValue(child.Source.Reference, out var actual) || actual != child.Source)
            throw new InvalidDataException("Retained native child has a foreign winning source.");
        child.Placement.Validate();
        _ = ReadIdentity(child.Placement.Cell);
    }
    internal FalloutBaseObjectDefinition ReadChildBase(FalloutCellProcessReference child)
    {
        _ = Read(child.SourceCell);
        if (!_references[child.SourceCell].TryGetValue(child.Reference, out var actual) || actual != child)
            throw new InvalidDataException("CELL child base query has a foreign source reference.");
        return _scenes[child.SourceCell].BaseObjects[child.Base];
    }
    private FalloutReferencePlacement CurrentPlacement(FalloutPlacedReference original)
    {
        var current = currentPlacement?.Invoke(original) ??
            new(original.Cell, original.Position.ToArray(), original.RotationRadians.ToArray());
        current.Validate(); _ = ReadIdentity(current.Cell); return current;
    }
    internal void ReleaseMetadata(FalloutFormKey cell)
    { _decoded.Remove(cell); _scenes.Remove(cell); _placements.Remove(cell); _references.Remove(cell); }
    internal void ReleaseAllMetadata()
    { _decoded.Clear(); _scenes.Clear(); _placements.Clear(); _references.Clear(); }
    private static string Digest(FalloutPluginRecord record) => Digest(record.ReadData());
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
