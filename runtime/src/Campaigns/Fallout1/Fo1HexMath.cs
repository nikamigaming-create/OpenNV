using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1HexMathNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float GeometryFloat0Point5f = 0.5f;
    internal const int GeometryInt5 = 5;
}

internal static class Fo1HexMath
{
    internal const int Width = 200;
    internal const int Height = 200;
    internal const int FloorWidth = Width / 2;
    internal const int FloorHeight = Height / 2;
    internal const int DirectionCount = 6;
    internal const float FlatToFlatMeters = 1.0f;
    internal const float ColumnSpacingMeters = 0.8660254037844386f;
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
            coordinate.X * ColumnSpacingMeters,
            0.0f,
            coordinate.Y - Fo1HexMathNumericContracts.GeometryFloat0Point5f * (coordinate.X & 1));
    }

    internal static int FloorIndex(int tile)
    {
        var coordinate = Coordinate(tile);
        return (coordinate.Y / 2) * FloorWidth +
            (FloorWidth - 1 - coordinate.X / 2);
    }

    internal static Vector3 FloorPatchCenter(int index)
    {
        if (index is < 0 or >= FloorWidth * FloorHeight)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "Fallout floor tile must be in the 100x100 grid.");
        var floorX = FloorWidth - 1 - index % FloorWidth;
        var floorY = index / FloorWidth;
        var center = Vector3.Zero;
        for (var offsetY = 0; offsetY < 2; offsetY++)
            for (var offsetX = 0; offsetX < 2; offsetX++)
                center += Center(
                    (floorY * 2 + offsetY) * Width +
                    floorX * 2 + offsetX);
        return center / 4.0f;
    }

    internal static int NearestTile(Vector3 world)
    {
        var fractionalQ = world.X / ColumnSpacingMeters;
        var fractionalR = world.Z - fractionalQ / 2.0f;
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
        var column = (int)roundedX;
        var axialRow = (int)roundedZ;
        var row = axialRow + (column + (column & 1)) / 2;
        return Tile(new Vector2I(column, row));
    }

    internal static int[] Neighbors(int tile)
    {
        return Enumerable.Range(0, DirectionCount)
            .Select(edge => NeighborAcrossEdge(tile, edge))
            .Where(neighbor => neighbor >= 0)
            .ToArray();
    }

    internal static int NeighborAcrossEdge(int tile, int edge)
    {
        if (edge is < 0 or >= DirectionCount)
            throw new ArgumentOutOfRangeException(nameof(edge), edge, "Fallout hex edge is invalid.");
        var rotation = edge switch
        {
            0 => 3,
            1 => 2,
            2 => 1,
            3 => 0,
            4 => Fo1HexMathNumericContracts.GeometryInt5,
            Fo1HexMathNumericContracts.GeometryInt5 => 4,
            _ => throw new InvalidOperationException("Fallout hex edge dispatch failed."),
        };
        return TileInDirection(tile, rotation);
    }

    internal static int TileInDirection(int tile, int rotation)
    {
        var coordinate = Coordinate(tile);
        if (rotation is < 0 or >= DirectionCount)
            throw new ArgumentOutOfRangeException(
                nameof(rotation),
                rotation,
                "Fallout rotation is invalid.");
        var odd = (coordinate.X & 1) != 0;
        var offset = rotation switch
        {
            0 => new Vector2I(-1, odd ? -1 : 0),
            1 => new Vector2I(-1, odd ? 0 : 1),
            2 => new Vector2I(0, 1),
            3 => new Vector2I(1, odd ? 0 : 1),
            4 => new Vector2I(1, odd ? -1 : 0),
            Fo1HexMathNumericContracts.GeometryInt5 => new Vector2I(0, -1),
            _ => throw new InvalidOperationException("Fallout rotation dispatch failed."),
        };
        return Tile(coordinate + offset);
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
        return Enumerable.Range(0, DirectionCount)
            .Select(index => center + CornerOffset(index, radiusScale))
            .ToArray();
    }

    internal static Vector3 CornerOffset(int index, float radiusScale = 1.0f)
    {
        if (index is < 0 or >= DirectionCount)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "Fallout hex corner is invalid.");
        var angle = Mathf.Tau * index / DirectionCount;
        var radius = CircumradiusMeters * radiusScale;
        return new Vector3(
            MathF.Cos(angle) * radius,
            0.0f,
            MathF.Sin(angle) * radius);
    }

    private static Vector3I Cube(int tile)
    {
        var coordinate = Coordinate(tile);
        var q = coordinate.X;
        var r = coordinate.Y - (coordinate.X + (coordinate.X & 1)) / 2;
        return new Vector3I(q, -q - r, r);
    }
}
