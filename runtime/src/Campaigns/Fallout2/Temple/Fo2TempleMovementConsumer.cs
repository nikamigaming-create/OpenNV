using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed class Fo2TempleMovementConsumer
{
    internal const string Schema = "opennv-fo2-temple-entry-component-movement/v1";
    private readonly bool[] _walkable;
    private readonly HashSet<int> _entryComponent;
    private readonly Node3D _cursor;

    private Fo2TempleMovementConsumer(
        Fo2TemplePresentationCatalog catalog,
        Fo2TempleTopologyProfile profile,
        bool[] walkable,
        HashSet<int> entryComponent,
        string walkMaskSha256,
        Node3D cursor)
    {
        _walkable = walkable;
        _entryComponent = entryComponent;
        _cursor = cursor;
        EntryTile = catalog.EntryTile;
        CurrentTile = EntryTile;
        CacheManifestSha256 = catalog.ManifestSha256;
        SourceManifestSha256 = catalog.SourceManifestSha256;
        SourceProfileId = catalog.SourceProfileId;
        MapSha256 = catalog.MapSha256;
        TopologyProfileId = profile.Id;
        TopologyProfileSha256 = profile.Sha256;
        WalkMaskSha256 = walkMaskSha256;
    }

    internal int EntryTile { get; }
    internal int CurrentTile { get; private set; }
    internal int CompletedSteps { get; private set; }
    internal int EntryComponentHexes => _entryComponent.Count;
    internal string CacheManifestSha256 { get; }
    internal string SourceManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string MapSha256 { get; }
    internal string TopologyProfileId { get; }
    internal string TopologyProfileSha256 { get; }
    internal string WalkMaskSha256 { get; }
    internal Vector3 WorldPosition => _cursor.Position;
    internal IReadOnlySet<int> ReachableTiles => _entryComponent;

    internal static Fo2TempleMovementConsumer Build(
        Node3D root,
        Fo2TemplePresentationCatalog catalog,
        Fo2TempleTopologyProfile profile,
        bool[] walkable,
        string walkMaskSha256,
        int expectedEntryComponentHexes)
    {
        if (walkable.Length != Fo1HexMath.Width * Fo1HexMath.Height ||
            catalog.EntryElevation != 0 ||
            catalog.EntryTile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            !walkable[catalog.EntryTile] ||
            profile.Id != "fo2-temple-map-126-topology-v1" ||
            profile.Sha256.Length != 64 ||
            catalog.ManifestSha256.Length != 64 ||
            catalog.SourceManifestSha256.Length != 64 ||
            catalog.SourceProfileId.Length != 64 ||
            catalog.MapSha256.Length != 64 ||
            MaskSha256(walkable) != walkMaskSha256)
            throw new InvalidOperationException(
                "Fallout 2 Temple movement identity or walk mask drifted.");
        var component = EntryComponent(catalog.EntryTile, walkable);
        if (component.Count != expectedEntryComponentHexes)
            throw new InvalidOperationException(
                "Fallout 2 Temple entry-component coverage drifted.");

        var cursor = new Node3D
        {
            Name = "MAP_HEADER_ENTRY_MOVEMENT_CURSOR_NO_ACTOR",
            Position = Fo1HexMath.Center(catalog.EntryTile),
        };
        cursor.SetMeta("movement_schema", Schema);
        cursor.SetMeta("cache_manifest_sha256", catalog.ManifestSha256);
        cursor.SetMeta("source_manifest_sha256", catalog.SourceManifestSha256);
        cursor.SetMeta("source_profile_id", catalog.SourceProfileId);
        cursor.SetMeta("map_sha256", catalog.MapSha256);
        cursor.SetMeta("topology_profile_id", profile.Id);
        cursor.SetMeta("topology_profile_sha256", profile.Sha256);
        cursor.SetMeta("walk_mask_sha256", walkMaskSha256);
        cursor.SetMeta("entry_tile", catalog.EntryTile);
        root.AddChild(cursor);
        return new Fo2TempleMovementConsumer(
            catalog,
            profile,
            walkable.ToArray(),
            component,
            walkMaskSha256,
            cursor);
    }

    internal bool TryStep(int destinationTile)
    {
        if (!_entryComponent.Contains(destinationTile) ||
            !_walkable[destinationTile] ||
            !Fo1HexMath.Neighbors(CurrentTile).Contains(destinationTile))
            return false;
        CurrentTile = destinationTile;
        CompletedSteps++;
        _cursor.Position = Fo1HexMath.Center(CurrentTile);
        return true;
    }

    internal bool CanReachFromEntry(int tile) => _entryComponent.Contains(tile);

    internal IReadOnlyList<int> BuildPathTo(int targetTile)
    {
        if (CurrentTile != EntryTile || CompletedSteps != 0 ||
            !_entryComponent.Contains(targetTile))
            throw new InvalidOperationException(
                "Fallout 2 Temple target path must begin at the entry and remain in its component.");
        return BuildShortestPath(EntryTile, targetTile);
    }

    internal IReadOnlyList<int> BuildShortestPath(int startTile, int targetTile)
    {
        if (!_entryComponent.Contains(startTile) || !_entryComponent.Contains(targetTile))
            throw new InvalidOperationException(
                "Fallout 2 Temple path endpoints must remain in the source entry component.");
        var parents = new Dictionary<int, int> { [startTile] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(startTile);
        while (queue.Count > 0 && !parents.ContainsKey(targetTile))
        {
            var tile = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(tile))
                if (_entryComponent.Contains(neighbor) && !parents.ContainsKey(neighbor))
                {
                    parents.Add(neighbor, tile);
                    queue.Enqueue(neighbor);
                }
        }
        if (!parents.ContainsKey(targetTile))
            throw new InvalidOperationException(
                "Fallout 2 Temple target is not reachable from the MAP header entry.");
        var reversed = new List<int>();
        for (var tile = targetTile; tile >= 0; tile = parents[tile])
            reversed.Add(tile);
        reversed.Reverse();
        if (reversed.Count == 0 || reversed[0] != startTile || reversed[^1] != targetTile ||
            reversed.Zip(reversed.Skip(1)).Any(row =>
                !Fo1HexMath.Neighbors(row.First).Contains(row.Second)))
            throw new InvalidOperationException(
                "Fallout 2 Temple source path reconstruction is invalid.");
        return reversed;
    }

    internal IReadOnlyList<int> BuildFarthestProofPath()
    {
        if (CurrentTile != EntryTile || CompletedSteps != 0)
            throw new InvalidOperationException(
                "Fallout 2 Temple movement proof must begin at the MAP header entry.");
        var parents = new Dictionary<int, int> { [EntryTile] = -1 };
        var distances = new Dictionary<int, int> { [EntryTile] = 0 };
        var queue = new Queue<int>();
        queue.Enqueue(EntryTile);
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(tile))
                if (_entryComponent.Contains(neighbor) && !parents.ContainsKey(neighbor))
                {
                    parents.Add(neighbor, tile);
                    distances.Add(neighbor, distances[tile] + 1);
                    queue.Enqueue(neighbor);
                }
        }
        if (parents.Count != _entryComponent.Count)
            throw new InvalidOperationException(
                "Fallout 2 Temple movement traversal does not cover the entry component.");
        var boundaryRows = distances
            .Where(row => Fo1HexMath.Neighbors(row.Key).Any(neighbor =>
                !_entryComponent.Contains(neighbor)))
            .ToArray();
        if (boundaryRows.Length == 0)
            throw new InvalidOperationException(
                "Fallout 2 Temple entry component has no fail-closed boundary.");
        var maximumDistance = boundaryRows.Max(row => row.Value);
        var target = boundaryRows
            .Where(row => row.Value == maximumDistance)
            .Select(row => row.Key)
            .Min();
        var reversed = new List<int>();
        for (var tile = target; tile >= 0; tile = parents[tile])
            reversed.Add(tile);
        reversed.Reverse();
        if (reversed.Count != maximumDistance + 1 || reversed[0] != EntryTile)
            throw new InvalidOperationException(
                "Fallout 2 Temple movement proof path reconstruction drifted.");
        return reversed;
    }

    internal int RejectedAdjacentTile()
    {
        return Fo1HexMath.Neighbors(CurrentTile)
            .Where(tile => !_entryComponent.Contains(tile) || !_walkable[tile])
            .DefaultIfEmpty(-1)
            .Min();
    }

    internal static string PathSha256(IReadOnlyList<int> path)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> value = stackalloc byte[sizeof(int)];
        foreach (var tile in path)
        {
            BinaryPrimitives.WriteInt32BigEndian(value, tile);
            hash.AppendData(value);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    internal static string MaskSha256(IReadOnlyList<bool> mask)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> value = stackalloc byte[sizeof(int)];
        for (var tile = 0; tile < mask.Count; tile++)
            if (mask[tile])
            {
                BinaryPrimitives.WriteInt32BigEndian(value, tile);
                hash.AppendData(value);
            }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static HashSet<int> EntryComponent(int entryTile, IReadOnlyList<bool> walkable)
    {
        var visited = new HashSet<int> { entryTile };
        var queue = new Queue<int>();
        queue.Enqueue(entryTile);
        while (queue.Count > 0)
            foreach (var neighbor in Fo1HexMath.Neighbors(queue.Dequeue()))
                if (walkable[neighbor] && visited.Add(neighbor))
                    queue.Enqueue(neighbor);
        return visited;
    }
}
