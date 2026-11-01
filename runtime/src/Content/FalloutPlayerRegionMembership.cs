using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

// Snapshot at accepted player CELL entry, shared by flat/XR. It is not the
// highest-priority weather-region choice and has no independent timer.
internal sealed record FalloutPlayerRegionMembership(FalloutFormKey Cell,
    IReadOnlyList<FalloutFormKey> CellRegions, IReadOnlyList<FalloutFormKey> Current,
    IReadOnlyList<FalloutFormKey?>? DroppedSourceRegions = null, IReadOnlyList<float>? EntryPosition = null)
{
    internal static FalloutPlayerRegionMembership Restore(FalloutPluginStack records,
        FalloutPlayerRegionMembership snapshot, FalloutFormKey activeCell)
    {
        if (snapshot.Cell != activeCell || snapshot.CellRegions is null || snapshot.Current is null)
            throw new InvalidDataException("Saved player region cache has no accepted active CELL.");
        var restored = Enter(records, FalloutCellSceneReader.ReadDefinition(records, snapshot.Cell), snapshot.EntryPosition);
        if (!restored.CellRegions.SequenceEqual(snapshot.CellRegions) || !restored.Current.SequenceEqual(snapshot.Current))
            throw new InvalidDataException("Saved player region cache disagrees with its owned CELL-entry source inputs.");
        return restored;
    }

    internal static FalloutPlayerRegionMembership Enter(FalloutPluginStack records,
        FalloutCellDefinition cell, IReadOnlyList<float>? position)
    {
        if (cell.Worldspace is null) return new(cell.FormKey, [], []);
        if (position is not { Count: 3 } || position.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Exterior region entry requires the authoritative finite source player position.");
        var source = records.GetEffective(cell.FormKey);
        var rows = source.ReadSubrecords().Where(field => field.Signature == "XCLR").ToArray();
        if (rows.Length > 1 || rows.Length == 1 && rows[0].Data.Length % 4 != 0)
            throw new InvalidDataException("CELL region list has invalid extent/count.");
        var regions = new List<FalloutFormKey>();
        var dropped = new List<FalloutFormKey?>();
        if (rows.Length != 0)
            for (var offset = 0; offset < rows[0].Data.Length; offset += 4)
            {
                var key = source.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(rows[0].Data.Span[offset..]));
                // The native XCLR loader resolves/casts each optional link
                // before insertion. Null/missing/wrong-class entries are not
                // region objects, while their declarations remain observable.
                if (key is null || !records.TryGetEffective(key.Value, out var region) || region.Signature != "REGN")
                {
                    dropped.Add(key);
                    continue;
                }
                if (!region.IsDeleted && !regions.Contains(key.Value)) regions.Insert(0, key.Value);
            }
        var current = new List<FalloutFormKey>();
        foreach (var region in regions)
        {
            var definition = records.GetEffective(region);
            var fields = definition.ReadSubrecords().ToArray();
            var worlds = fields.Where(field => field.Signature == "WNAM").ToArray();
            if (worlds.Length > 1 || worlds.Length == 1 && worlds[0].Data.Length != 4)
                throw new InvalidDataException("REGN world binding has invalid extent/count.");
            var world = worlds.Length == 0 ? null : definition.Plugin.AdjustOptionalFormId(
                BinaryPrimitives.ReadUInt32LittleEndian(worlds[0].Data.Span));
            if (world is not null && world != cell.Worldspace) continue;
            if (fields.Where(field => field.Signature == "RPLD").Any(field =>
                FalloutExteriorClimate.ContainsPolygon(field.Data.Span, position[0], position[1])))
                current.Insert(0, region);
        }
        return new(cell.FormKey, regions.AsReadOnly(), current.AsReadOnly(), dropped.AsReadOnly(), position.ToArray());
    }
}
