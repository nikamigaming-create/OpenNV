using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutFormKey? _nativePendingFastTravel;
    private double _nativeMapDiscoverySeconds;
    private IReadOnlyDictionary<FalloutFormKey, FalloutPluginRecord[]>? _nativeMapDoors;

    private (FalloutFormKey? World, Vector3 Position) NativePipBoyMapContext(RuntimeNativePlayer player)
    {
        if (_nativeActiveCell!.Cell.Worldspace is { } world) return (world, NativePipBoyMapPosition(player));
        var records = _nativePluginStack!;
        _nativeMapDoors ??= records.EffectiveRecords("REFR")
            .Where(record => record.ReadSubrecords().Any(field => field.Signature == "XTEL"))
            .Where(record => FalloutCellSceneReader.ParentCell(record) is not null)
            .GroupBy(record => FalloutCellSceneReader.ParentCell(record)!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var pending = new Queue<FalloutFormKey>();
        var visited = new HashSet<FalloutFormKey>();
        pending.Enqueue(_nativeActiveCell.Cell.FormKey);
        while (pending.TryDequeue(out var cell))
        {
            if (!visited.Add(cell)) continue;
            foreach (var door in _nativeMapDoors.GetValueOrDefault(cell) ?? [])
            {
                var target = FalloutCellSceneReader.ReadTeleport(door)!.Door;
                var destination = records.GetEffective(target);
                var destinationCell = FalloutCellSceneReader.ParentCell(destination)!.Value;
                if (FalloutCellSceneReader.ParentWorldspace(destination) is { } exterior)
                {
                    var placement = _nativeReferences!.Placement(target);
                    return (exterior, new(placement.Position[0], placement.Position[1], placement.Position[2]));
                }
                pending.Enqueue(destinationCell);
            }
        }
        return (null, NativePipBoyMapPosition(player));
    }

    private string? RequestNativeFastTravel(FalloutFormKey reference)
    {
        var records = _nativePluginStack!;
        var player = _nativePlayer!;
        var driver = _nativeOpeningStageDriver!;
        var marker = FalloutMapMarker.Read(records.GetEffective(reference));
        string? reason = !_nativeReferences!.MapMarkerState(marker).CanTravel ? "sNoFastTravelUndiscovered" :
            _nativeActiveCell!.Cell.Worldspace is null ? "sNoFastTravelCell" :
            !player.SourceControls.Movement || player.FurnitureActive ? "sNoFastTravelScriptBlock" :
            driver.IsInCombat(records.RuntimeFormKey(0x14)) ? "sNoFastTravelCombat" :
            !player.IsOnFloor() ? "sNoFastTravelInAir" : null;
        var weight = _nativeInventory.Items.Sum(item => (item.Weight ?? 0) * item.Count);
        var capacity = FalloutGameSettingFloats.Read(records, "fAVDCarryWeightsBase") +
            driver.PlayerSkillValue("Strength") * FalloutGameSettingFloats.Read(records, "fAVDCarryWeightMult");
        if (weight > capacity) reason = "sNoFastTravelOverencumbered";
        if (reason is not null) return FalloutGameSettingStrings.Read(records, reason);
        if (driver.Vitals.HitPoints <= 0 || _nativeDoorLoading || _nativePendingFastTravel is not null || _nativeReferences.PlayerMoves.Pending)
            return "Travel is already in progress or unavailable.";
        _ = NativeFastTravelPlacement(reference);
        _nativePendingFastTravel = reference;
        _nativePipBoy!.RequestClose();
        return null;
    }

    private void AdvanceNativePipBoyWorld(double delta)
    {
        if (_nativePendingFastTravel is { } target && _nativeOpeningStageDriver?.PipBoy.Open == false && !_nativeDoorLoading)
        {
            _nativePendingFastTravel = null;
            _nativePlayerMoveRead = ConsumeNativeFastTravel(target);
            return;
        }
        if (_nativeDoorLoading || _nativeSessionTransitioning || _retiringNativeSession ||
            _nativePlayer is not { } player || _nativeActiveCell?.Cell.Worldspace is not { } world) return;
        _nativeMapDiscoverySeconds += delta;
        if (_nativeMapDiscoverySeconds < 0.5) return;
        _nativeMapDiscoverySeconds = 0;
        var position = NativePipBoyMapPosition(player);
        var records = _nativePluginStack!;
        var visible = records.NumericSettings.IntegerBits("iMapMarkerVisibleDistance");
        var discovered = records.NumericSettings.IntegerBits("iMapMarkerRevealDistance");
        foreach (var marker in FalloutWorldMap.SourceMarkers(records).Where(marker => marker.World == world))
        {
            var distance = new Vector2(marker.X - position.X, marker.Y - position.Y).Length();
            if (distance > visible) continue;
            if (distance <= discovered && !_nativeReferences!.MapMarkerState(marker.Marker).CanTravel)
                _nativeOpeningStageDriver!.RewardExperience(records.NumericSettings.IntegerBits("iXPRewardDiscoverMapMarker"));
            _nativeReferences!.ShowMap(marker.Marker.Reference, distance <= discovered);
        }
    }

    private async Task ConsumeNativeFastTravel(FalloutFormKey target)
    {
        var player = _nativePlayer!;
        var active = _nativeActiveCell!;
        _nativeDoorLoading = true; player.SetModalInput(true);
        try
        {
            var placement = NativeFastTravelPlacement(target);
            var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), 1),
                GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * player.UnitsToMeters);
            await ShowNativeLoadingScreens(placement.Cell);
            if (placement.Cell == active.Cell.FormKey) player.Teleport(transform);
            else await StreamNativePlayerMove(placement, transform);
            RequestNativeInteractionSave(target);
            GD.Print($"OPENNV_FAST_TRAVEL destination={target} cell={_nativeActiveCell!.Cell.FormKey}");
        }
        catch (Exception error)
        {
            SetMeta("opennv_fast_travel_error", error.Message);
            GD.PushError($"OPENNV_FAST_TRAVEL_FAIL {error}");
        }
        finally { CloseNativeLoadingScreens(); _nativeDoorLoading = false; player.SetModalInput(false); }
    }

    private FalloutReferencePlacement NativeFastTravelPlacement(FalloutFormKey marker) =>
        _nativeReferences!.Placement(_nativeReferences.GetLinkedRef(marker) ?? marker);

    private string NativePipBoyDateTime()
    {
        if (_nativeGameTime is not { } time) return "";
        var date = time.ScheduleTime();
        return $"{date.Month + 1:00}.{date.Date:00}.{time.Year % 100:00}, {(int)date.Hour:00}:{(int)((date.Hour % 1) * 60):00}";
    }
}
