using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutExteriorGridScene ResolveExteriorFromSourceCell(FalloutFormKey world, FalloutFormKey cell)
    {
        var grid = _nativeExteriorGrid ??= new(_nativePluginStack!);
        var settings = FalloutInstallationSettings.Read(RuntimeLiveContentSource.Current!);
        var diameter = checked((int)settings.Unsigned("General", "uGridsToLoad"));
        diameter = Math.Max(diameter, _configuration.World.MinimumExteriorGridDiameter);
        var selected = grid.ResolveSourceCell(world, grid.PersistentCell(world), cell, diameter);
        if (selected.Scene.Cell.FormKey != cell)
            throw new InvalidDataException("Raw Player target changed between original source CELL lookup and native grid preparation.");
        return selected;
    }
}
