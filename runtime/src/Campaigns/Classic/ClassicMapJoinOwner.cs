using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Classic;

internal sealed record ClassicMapEndpoint(
    int MapIndex,
    string? MapName,
    string MapSha256,
    int Tile,
    int? Elevation,
    int? Rotation);

internal sealed record ClassicMapJoin(
    int SourceSerial,
    ClassicMapEndpoint Source,
    ClassicMapEndpoint Destination);

internal sealed record ClassicMapJoinState(
    ClassicMapJoin Join,
    bool Committed);

internal static class ClassicMapJoinOwner
{
    private const int Sha256HexCharacters = 64;

    internal static ClassicMapJoinState Commit(
        ClassicMapJoin join,
        int activeMapIndex,
        string activeMapSha256,
        int activeTile,
        int? activeElevation)
    {
        Validate(join);
        if (activeMapIndex != join.Source.MapIndex ||
            !activeMapSha256.Equals(join.Source.MapSha256, StringComparison.OrdinalIgnoreCase) ||
            activeTile != join.Source.Tile || activeElevation != join.Source.Elevation)
            throw new InvalidOperationException(
                "Classic MAP join does not match authoritative active-map state.");
        return new ClassicMapJoinState(join, true);
    }

    internal static void ValidateReciprocal(
        ClassicMapJoin forward,
        ClassicMapJoin reverse)
    {
        Validate(forward);
        Validate(reverse);
        if (!SameMap(forward.Source, reverse.Destination) ||
            !SameMap(forward.Destination, reverse.Source))
            throw new InvalidOperationException("Classic MAP joins are not reciprocal.");
    }

    private static void Validate(ClassicMapJoin join)
    {
        if (join.SourceSerial < 0 || !Endpoint(join.Source) || !Endpoint(join.Destination) ||
            join.Source.MapIndex == join.Destination.MapIndex)
            throw new InvalidOperationException("Classic MAP join contract is invalid.");
    }

    private static bool Endpoint(ClassicMapEndpoint value) =>
        value.MapIndex >= 0 &&
        value.MapSha256.Length == Sha256HexCharacters && value.MapSha256.All(Uri.IsHexDigit) &&
        value.Tile is >= 0 and < Fo1HexMath.Width * Fo1HexMath.Height &&
        (value.Elevation is null or >= 0) &&
        (value.Rotation is null or >= 0 and < Fo1HexMath.DirectionCount);

    private static bool SameMap(ClassicMapEndpoint left, ClassicMapEndpoint right) =>
        left.MapIndex == right.MapIndex &&
        (left.MapName is null || right.MapName is null ||
         Path.GetFileNameWithoutExtension(left.MapName).Equals(
             Path.GetFileNameWithoutExtension(right.MapName), StringComparison.OrdinalIgnoreCase)) &&
        left.MapSha256.Equals(right.MapSha256, StringComparison.OrdinalIgnoreCase);
}
