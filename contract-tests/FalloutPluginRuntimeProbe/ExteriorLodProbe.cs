using OpenNV.Runtime.Content;

internal static class ExteriorLodProbe
{
    internal static void Run()
    {
        var payloads = new RuntimeLiveContentSource.SourcePayloadCache(8);
        var reads = 0;
        byte[] Read(string key) { ++reads; return new byte[4]; }
        var first = payloads.GetOrAdd("first", Read);
        _ = payloads.GetOrAdd("second", Read);
        if (!ReferenceEquals(first, payloads.GetOrAdd("FIRST", Read))) throw new InvalidOperationException("Owned byte cache lost source case folding or reuse.");
        _ = payloads.GetOrAdd("third", Read);
        _ = payloads.GetOrAdd("first", Read);
        if (reads != 3 || payloads.Bytes > 8) throw new InvalidOperationException("Owned byte cache evicted the recently used payload or exceeded its budget.");
        _ = payloads.GetOrAdd("second", Read);
        if (reads != 4 || payloads.Bytes != 8 || first.Length != 4) throw new InvalidOperationException("Owned cache eviction invalidated a live reader or did not reread an evicted source.");
        _ = payloads.GetOrAdd("large", _ => new byte[16]);
        if (payloads.Bytes > 8) throw new InvalidOperationException("Oversized source bypass did not respect the cache budget.");
        const string world = "OffsetWorld";
        static string Path(int level, int x, int y, bool objects = false) =>
            $"meshes/landscape/lod/{world}/{(objects ? "blocks/" : "")}{world}.level{level}.x{x}.y{y}.nif";
        string[] paths = [Path(8, -2, -1), Path(4, -2, -1), Path(4, 2, -1), Path(4, -2, 3), Path(4, 2, 3), Path(4, -2, -1, true)];
        var lod = new FalloutExteriorLod(world, paths, 125000, .75f, .7f);
        var near = lod.Select(0, 0);
        if (near.Count != 4 || near.Any(block => block.Level != 4) || near.Single(block => block.Objects is not null).X != -2)
            throw new InvalidOperationException("LOD lost a nonzero origin or its object companion.");
        var coarse = lod.Blocks.Single(block => block.Level == 8);
        var preparation = lod.PreparationOrder(near, new HashSet<FalloutLodBlock>(), 0, 0);
        if (preparation[0] != coarse || preparation.Count != 5)
            throw new InvalidOperationException("Cold LOD did not prepare complete coarse coverage before its finer demand.");
        if (lod.PreparationOrder(near, new HashSet<FalloutLodBlock> { coarse }, 0, 0).Any(block => block == coarse))
            throw new InvalidOperationException("Resident coarse coverage was read again during refinement.");
        var available = new HashSet<FalloutLodBlock>();
        foreach (var block in preparation)
        {
            available.Add(block);
            var cover = lod.ResidentCover(near, available);
            if (cover.Count == 0 || near.Any(child => !cover.Any(parent => parent.Contains(child))))
                throw new InvalidOperationException("An incremental LOD upload removed a still-needed quadrant.");
        }
        var failedChild = near[0];
        if (lod.ResidentCover(near, lod.Blocks.Where(block => block != failedChild).ToHashSet()) is not [{ Level: 8 }] ||
            lod.ResidentCover([coarse], near.ToHashSet()).Count != 4)
            throw new InvalidOperationException("LOD discarded resident coverage during a failed split or pending coarsening.");
        if (lod.Select(60000, 0) is not [{ Level: 8 }] || lod.Select(1000000, 0).Count != 0)
            throw new InvalidOperationException("LOD coarsening or source load distance is invalid.");
        var partial = new FalloutExteriorLod(world, paths.Where(path => path != Path(4, 2, 3)).ToArray(), 125000, .75f, .7f);
        if (partial.Select(0, 0) is not [{ Level: 8 }]) throw new InvalidOperationException("Incomplete finer coverage erased the source parent.");
        for (var x = -100000; x <= 100000; x += 1000)
        {
            var selected = lod.Select(x, 0);
            if (selected.Any(block => selected.Any(other => !ReferenceEquals(block, other) && block.Contains(other))))
                throw new InvalidOperationException("LOD selection overlaps its ancestor.");
        }
        Console.WriteLine("OPENNV_EXTERIOR_LOD_CONTRACT_PASS offsetOrigin=true completeCover=true distance=true noAncestorOverlap=true pixels=unverified");
    }
}
