using Godot;

namespace OpenNV.Runtime;

internal static class Fo1HexMath
{
    internal const int Width = 200;
    internal const int Height = 200;
    internal const float FlatToFlatMeters = 1.0f;
    internal const float RowSpacingMeters = 0.8660254037844386f;
    internal const float CircumradiusMeters = 0.5773502691896258f;

    internal static Vector2I Coordinate(int tile)
    {
        if (tile is < 0 or >= Width * Height)
            throw new ArgumentOutOfRangeException(nameof(tile), tile, "Fallout tile must be in the 200x200 grid.");
        return new Vector2I(tile % Width, tile / Width);
    }

    internal static int Tile(Vector2I coordinate)
    {
        if (coordinate.X is < 0 or >= Width || coordinate.Y is < 0 or >= Height)
            return -1;
        return coordinate.Y * Width + coordinate.X;
    }

    internal static Vector3 Center(int tile)
    {
        var coordinate = Coordinate(tile);
        return new Vector3(
            coordinate.X + 0.5f * (coordinate.Y & 1),
            0.0f,
            coordinate.Y * RowSpacingMeters);
    }

    internal static int FloorIndex(int tile)
    {
        var coordinate = Coordinate(tile);
        return (coordinate.Y / 2) * 100 + coordinate.X / 2;
    }

    internal static int NearestTile(Vector3 world)
    {
        var fractionalR = world.Z / RowSpacingMeters;
        var fractionalQ = world.X - fractionalR / 2.0f;
        var cubeX = fractionalQ;
        var cubeZ = fractionalR;
        var cubeY = -cubeX - cubeZ;
        var roundedX = MathF.Round(cubeX);
        var roundedY = MathF.Round(cubeY);
        var roundedZ = MathF.Round(cubeZ);
        var deltaX = MathF.Abs(roundedX - cubeX);
        var deltaY = MathF.Abs(roundedY - cubeY);
        var deltaZ = MathF.Abs(roundedZ - cubeZ);
        if (deltaX > deltaY && deltaX > deltaZ)
            roundedX = -roundedY - roundedZ;
        else if (deltaY > deltaZ)
            roundedY = -roundedX - roundedZ;
        else
            roundedZ = -roundedX - roundedY;
        var row = (int)roundedZ;
        var column = (int)roundedX + (row - (row & 1)) / 2;
        return Tile(new Vector2I(column, row));
    }

    internal static int[] Neighbors(int tile)
    {
        var coordinate = Coordinate(tile);
        var odd = (coordinate.Y & 1) != 0;
        var offsets = odd
            ? new[]
            {
                new Vector2I(0, -1), new Vector2I(1, -1),
                new Vector2I(-1, 0), new Vector2I(1, 0),
                new Vector2I(0, 1), new Vector2I(1, 1),
            }
            : new[]
            {
                new Vector2I(-1, -1), new Vector2I(0, -1),
                new Vector2I(-1, 0), new Vector2I(1, 0),
                new Vector2I(-1, 1), new Vector2I(0, 1),
            };
        return offsets
            .Select(offset => Tile(coordinate + offset))
            .Where(candidate => candidate >= 0)
            .ToArray();
    }

    internal static int Distance(int firstTile, int secondTile)
    {
        var first = Cube(firstTile);
        var second = Cube(secondTile);
        return Math.Max(
            Math.Abs(first.X - second.X),
            Math.Max(Math.Abs(first.Y - second.Y), Math.Abs(first.Z - second.Z)));
    }

    internal static Vector3[] Corners(int tile, float radiusScale = 1.0f)
    {
        var center = Center(tile);
        var radius = CircumradiusMeters * radiusScale;
        return Enumerable.Range(0, 6)
            .Select(index =>
            {
                var angle = Mathf.DegToRad(60.0f * index - 30.0f);
                return center + new Vector3(MathF.Cos(angle) * radius, 0.0f, MathF.Sin(angle) * radius);
            })
            .ToArray();
    }

    private static Vector3I Cube(int tile)
    {
        var coordinate = Coordinate(tile);
        var q = coordinate.X - (coordinate.Y - (coordinate.Y & 1)) / 2;
        var r = coordinate.Y;
        return new Vector3I(q, -q - r, r);
    }
}
