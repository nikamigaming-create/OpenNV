using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutFormKey? _nativePendingFastTravel;
    private double _nativeMapDiscoverySeconds;

    private (FalloutFormKey? World, Vector3 Position) NativePipBoyMapContext(RuntimeNativePlayer player)
    {
        var position = NativePipBoyMapPosition(player);
        var source = FalloutPipBoyMapSource.Resolve(_nativePluginStack!, _nativeReferences!, _nativeActiveCell!.Cell.FormKey,
            new(position.X, position.Y, position.Z));
        return (source.World, new(source.Position.X, source.Position.Y, source.Position.Z));
    }

    private string? RequestNativeFastTravel(FalloutFormKey reference)
    {
        var records = _nativePluginStack!;
        var player = _nativePlayer!;
        var driver = _nativeOpeningStageDriver!;
        var marker = FalloutMapMarker.Read(records.GetEffective(reference));
        string? reason = !_nativeReferences!.MapMarkerState(marker).CanTravel ? "sNoFastTravelUndiscovered" :
            _nativeActiveCell!.Cell.Worldspace is null ? "sNoFastTravelCell" :
            !driver.FastTravel.State.CanTravel || !player.SourceControls.Movement || player.FurnitureActive ? "sNoFastTravelScriptBlock" :
            driver.IsInCombat(records.RuntimeFormKey(0x14)) ? "sNoFastTravelCombat" :
            !player.IsOnFloor() ? "sNoFastTravelInAir" : null;
        if (player.Overencumbered) reason = "sNoFastTravelOverencumbered";
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
            var newlyDiscovered = distance <= discovered && !_nativeReferences!.MapMarkerState(marker.Marker).CanTravel;
            var notice = new FalloutHudEvent(FalloutHudEventKind.LocationDiscovered, marker.Marker.Reference, 0);
            if (newlyDiscovered) _nativeInventory!.Notifications.RequirePublish([notice]);
            _nativeReferences!.ShowMap(marker.Marker.Reference, distance <= discovered);
            if (newlyDiscovered)
            {
                _nativeInventory!.Notifications.Publish([notice]);
                _nativeOpeningStageDriver!.RewardExperience(records.NumericSettings.IntegerBits("iXPRewardDiscoverMapMarker"));
            }
        }
    }

    private async Task ConsumeNativeFastTravel(FalloutFormKey target)
    {
        var player = _nativePlayer!;
        _nativeDoorLoading = true; player.SetModalInput(true);
        try
        {
            var placement = NativeFastTravelPlacement(target);
            if (_nativeActiveCell!.Cell.Worldspace is not { } world || FalloutCellSceneReader.ReadDefinition(_nativePluginStack!, placement.Cell).Worldspace != world)
                throw new NotSupportedException("Fast travel between disconnected coordinate spaces requires source portal-route distance.");
            var departure = NativePipBoyMapPosition(player);
            var destination = new Vector3(placement.Position[0], placement.Position[1], placement.Position[2]);
            var clock = _nativeGameTime ?? throw new InvalidOperationException("Fast travel has no game clock owner.");
            var hours = FalloutFastTravel.ElapsedHours(departure.DistanceTo(destination), player.CalculateRunSpeed(), clock.TimeScale);
            clock.ValidateFastTravel(hours);
            var transform = new Transform3D(GamebryoCoordinate.ConvertReferenceEuler(
                new(placement.RotationRadians[0], placement.RotationRadians[1], placement.RotationRadians[2]), 1),
                GamebryoCoordinate.ConvertVector(new(placement.Position[0], placement.Position[1], placement.Position[2])) * player.UnitsToMeters);
            await ShowNativeLoadingScreens(placement.Cell);
            if (placement.Cell == _nativeActiveCell!.Cell.FormKey) await MoveNativePlayerWithFollowers(placement, transform);
            else await StreamNativePlayerMove(placement, transform, transferFollowers: true);
            _nativeOpeningStageDriver!.FastTravel.CompleteRelocation();
            clock.AdvanceFastTravel(hours);
            SaveNativeInteraction();
            GD.Print($"OPENNV_FAST_TRAVEL destination={target} cell={_nativeActiveCell!.Cell.FormKey} elapsedHours={hours:R}");
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
        return $"{date.Month + 1:00}.{date.Date:00}.{time.Year % 100:00}, {(int)date.Hour}:{(int)((date.Hour % 1) * 60):00}";
    }
}
