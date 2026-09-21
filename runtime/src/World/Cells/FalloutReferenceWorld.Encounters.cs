using System.Buffers.Binary;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private readonly Dictionary<FalloutFormKey, FalloutEncounterZoneSnapshot> _encounterZones = [];
    private readonly Dictionary<FalloutFormKey, FalloutFormKey?> _cellEncounterZones = [];
    private FalloutExteriorGrid? _encounterGrid;

    internal int EnterEncounterCell(FalloutFormKey cell, int playerLevel) =>
        CellEncounterZone(cell) is { } zone ? EncounterLevel(zone, playerLevel) : playerLevel;

    private FalloutFormKey? CellEncounterZone(FalloutFormKey cell)
    {
        if (_cellEncounterZones.TryGetValue(cell, out var retained)) return retained;
        var record = records.GetEffective(cell);
        if (record.Signature != "CELL") throw new InvalidDataException("Encounter residency requires a CELL.");
        var zone = FalloutEncounterZone.Assignment(record);
        if (zone is null && FalloutCellSceneReader.ParentWorldspace(record) is { } world)
            zone = FalloutEncounterZone.Assignment(records.GetEffective(world));
        _cellEncounterZones.Add(cell, zone);
        return zone;
    }

    private int EncounterLevel(FalloutFormKey key, int playerLevel)
    {
        if (_encounterZones.TryGetValue(key, out var retained)) return retained.Level;
        var zone = FalloutEncounterZone.Read(records.GetEffective(key));
        var scaling = FalloutGameSettingFloats.Read(records, "fLevelScalingMult");
        var level = zone.InitialLevel(playerLevel, scaling);
        _encounterZones.Add(key, new(key, zone.SourceSha256, playerLevel, scaling, level));
        return level;
    }

    private FalloutFormKey? ReferenceEncounterZone(FalloutReferenceInstance instance)
    {
        var assigned = FalloutEncounterZone.Assignment(records.GetEffective(instance.Reference));
        if (assigned is not null) return assigned;
        var cell = instance.Placement?.Cell ?? instance.Cell;
        var record = records.GetEffective(cell);
        // Persistent exterior references belong to a world container in the
        // file, but use the zone of their actual spatial CELL at first admission.
        if ((record.Flags & 0x400) != 0 && FalloutCellSceneReader.ParentWorldspace(record) is { } world)
        {
            var position = Placement(instance.Reference).Position;
            cell = (_encounterGrid ??= new(records)).SpatialCell(world, position[0], position[1]);
        }
        return CellEncounterZone(cell);
    }

    private (int Level, int ListLevel, bool? AllLevels) ActorEncounter(FalloutReferenceInstance instance, int playerLevel)
    {
        var reference = records.GetEffective(instance.Reference);
        var zone = ReferenceEncounterZone(instance);
        var level = zone is { } key ? EncounterLevel(key, playerLevel) : playerLevel;
        var fields = reference.ReadSubrecords().Where(field => field.Signature == "XLCM").ToArray();
        if (fields.Length == 0) return (level, level, null);
        if (fields.Length != 1 || fields[0].Data.Length != 4)
            throw new InvalidDataException("Actor level modifier has invalid extent.");
        var modifier = BinaryPrimitives.ReadInt32LittleEndian(fields[0].Data.Span);
        var setting = modifier switch
        {
            0 => "fLeveledActorMultEasy",
            1 => "fLeveledActorMultMedium",
            2 => "fLeveledActorMultHard",
            3 => "fLeveledActorMultBoss",
            4 => null,
            _ => throw new NotSupportedException($"Actor level modifier {modifier} is unbound.")
        };
        if (setting is null) return (level, level, null);
        var multiplier = FalloutGameSettingFloats.Read(records, setting);
        var listLevel = level * (double)multiplier;
        if (!double.IsFinite(listLevel) || multiplier <= 0 || listLevel > ushort.MaxValue)
            throw new InvalidDataException("Actor list level multiplier is invalid.");
        return (level, Math.Max(1, (int)listLevel), modifier == 0);
    }

    internal IReadOnlyList<FalloutEncounterZoneSnapshot> CaptureEncounterZones() =>
        _encounterZones.Values.OrderBy(zone => zone.Zone.ToString(), StringComparer.Ordinal).ToArray();

    internal void RestoreEncounterZones(IReadOnlyList<FalloutEncounterZoneSnapshot>? snapshots)
    {
        if (_encounterZones.Count != 0) throw new InvalidOperationException("Encounter restoration requires a fresh owner.");
        var validated = new Dictionary<FalloutFormKey, FalloutEncounterZoneSnapshot>();
        foreach (var snapshot in snapshots ?? [])
        {
            var source = FalloutEncounterZone.Read(records.GetEffective(snapshot.Zone));
            if (snapshot.SourceSha256 != source.SourceSha256 ||
                source.InitialLevel(snapshot.FirstPlayerLevel, snapshot.ScalingMultiplier) != snapshot.Level ||
                !validated.TryAdd(snapshot.Zone, snapshot))
                throw new InvalidDataException("Saved encounter zone is invalid, duplicated or differs from its source.");
        }
        foreach (var (key, value) in validated) _encounterZones.Add(key, value);
    }
}
