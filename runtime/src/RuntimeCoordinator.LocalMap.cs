using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutReferenceWorld? _localMapViewOwner;
    private readonly Dictionary<FalloutFormKey, NativeLocalMapView> _localMapViews = [];

    private NativeOwnedLocalMap CreateNativeLocalMap(RuntimeNativePlayer player, Color color)
    {
        var records = _nativePluginStack!; var references = _nativeReferences!;
        var cell = _nativeActiveCell!.Cell;
        if (!ReferenceEquals(_localMapViewOwner, references)) { _localMapViews.Clear(); _localMapViewOwner = references; }
        var scope = cell.Worldspace ?? cell.FormKey;
        if (!_localMapViews.TryGetValue(scope, out var view)) _localMapViews.Add(scope, view = new());
        var source = ReadNativeLocalMapSource(records, references, cell.FormKey, NativePipBoyMapPosition(player), player.UnitsToMeters);
        return new(player.GetWorld3D(), player.GlobalPosition, color,
            GamebryoCoordinate.ReferenceEuler(player.GlobalBasis).Z, source, view);
    }

    internal static NativeLocalMapSource ReadNativeLocalMapSource(FalloutPluginStack records, FalloutReferenceWorld references,
        FalloutFormKey cellKey, Vector3 player, float unitsToMetres)
    {
        var cell = FalloutCellSceneReader.ReadDefinition(records, cellKey);
        var interior = (cell.Flags & FalloutCellSceneReader.InteriorCellFlag) != 0;
        var north = 0f;
        if (interior)
        {
            var markers = records.EffectiveCellChildren(cellKey, new HashSet<string> { "REFR" })
                .Where(record => FalloutDialogueTopic.RequiredForm(record, "NAME") == records.RuntimeFormKey(3)).ToArray();
            if (markers.Length > 1) throw new NotSupportedException("Local map multiple NorthMarker ordering is unbound.");
            if (markers.Length == 1) north = -references.Placement(markers[0].FormKey).RotationRadians[2];
        }
        var mapPlayer = new Vector2(player.X, player.Y).Rotated(-north);
        var gridValue = records.IniSettings.Read("uGridsToLoad:General");
        if (!double.IsFinite(gridValue) || gridValue < 1 || gridValue > int.MaxValue / 4096 || gridValue != Math.Truncate(gridValue))
            throw new InvalidDataException("Local map source grid count is invalid.");
        var grids = (int)gridValue;
        var offset = interior ? 2048 : 0;
        float Minimum(float coordinate)
        {
            if (!float.IsFinite(coordinate) || coordinate <= int.MinValue + 2048f || coordinate >= int.MaxValue - 2048f)
                throw new NotSupportedException("Local map position exceeds source grid arithmetic.");
            var start = (((int)MathF.Round(coordinate, MidpointRounding.ToEven) - offset) >> 12) - (grids >> 1);
            return checked(start * 4096 + offset);
        }
        var bounds = new Rect2(new(Minimum(mapPlayer.X), Minimum(mapPlayer.Y)), new(grids * 4096, grids * 4096));
        var doors = new List<NativeLocalMapDoor>();
        var placement = new FalloutReferencePlacement(cellKey, [player.X, player.Y, player.Z], [0, 0, 0]);
        foreach (var instance in references.ResidentSnapshot())
        {
            if (records.GetEffective(instance.Base).Signature != "DOOR" || instance.Deleted || instance.DeletePending ||
                instance.Destroyed || !references.IsEnabled(instance.Reference)) continue;
            var current = references.SpatialPlacement(instance.Reference, placement, unitsToMetres);
            if (current.Cell != cellKey && (interior || FalloutCellSceneReader.ReadDefinition(records, current.Cell).Worldspace != cell.Worldspace)) continue;
            var flags = records.GetEffective(instance.Base).ReadSubrecords().SingleOrDefault(field => field.Signature == "FNAM").Data;
            if (flags.Length > 1) throw new NotSupportedException("Local door flags extent is unbound.");
            if (!flags.IsEmpty && (flags.Span[0] & 4) != 0) continue;
            doors.Add(new(instance.Reference, new Vector2(current.Position[0], current.Position[1]).Rotated(-north)));
        }
        float Setting(string name) => FalloutGameSettingFloats.Read(records, name);
        return new(bounds, mapPlayer, north, unitsToMetres, interior, grids,
            Setting("fLocalMapMinZoom"), Setting("fLocalMapMaxZoom"), Setting("fLocalMapMarkerMinSize"), Setting("fLocalMapMarkerMaxSize"),
            Setting("fLocalfPlayerCursorMinSize"), Setting("fLocalPlayerCursorMaxSize"), doors);
    }
}
