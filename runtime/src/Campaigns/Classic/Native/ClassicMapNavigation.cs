using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Current source floors and MAP collision flags. Scripted changes require their world owner.</summary>
internal sealed class ClassicMapNavigation
{
    internal IReadOnlySet<int> Walkable { get; }
    internal IReadOnlySet<int> Blocked { get; }
    internal IReadOnlySet<int> FloorBacked { get; }

    internal ClassicMapNavigation(Fallout1NativeMap map, Fallout1NativeObjectGraph graph, int elevation,
        Func<Fallout1NativeMapObject, bool?>? dynamicBlock = null)
    {
        var tiles = map.Elevations[elevation]; var blocked = new HashSet<int>();
        foreach (var placed in graph.TopLevelObjects.Where(row => row.Elevation == elevation && row.Tile >= 0))
        {
            // OBJECT_HIDDEN / OBJECT_NO_BLOCK do not collide. Item and misc art
            // do not become solid merely because their FRM has opaque pixels.
            if ((placed.Flags & 1) != 0 || placed.Prototype.ObjectType is not (1 or 2 or 3)) continue;
            if (!(dynamicBlock?.Invoke(placed) ?? ((placed.Flags & 0x10) == 0))) continue;
            blocked.Add(placed.Tile);
            if ((placed.Flags & 0x800) != 0) blocked.UnionWith(ClassicHexGrid.Neighbors(placed.Tile));
        }
        Blocked = blocked;
        FloorBacked = Enumerable.Range(0, ClassicHexGrid.Count)
            .Where(tile => (tiles[ClassicHexGrid.FloorIndex(tile)] & 0xfff) != 1).ToHashSet();
        Walkable = FloorBacked.Where(tile => !blocked.Contains(tile)).ToHashSet();
    }
}
