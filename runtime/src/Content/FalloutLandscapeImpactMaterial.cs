namespace OpenNV.Runtime.Content;

// LAND impact selection is a point query, independent of a physics face's
// tessellation. Painted vertices can have different physical materials.
internal sealed class FalloutLandscapeImpactMaterial
{
    private readonly (int X, int Y) _cell;
    private readonly int[][] _materials;

    internal FalloutLandscapeImpactMaterial(FalloutLandscapeTransport source)
    {
        _cell = source.ActiveCoordinates;
        _materials = new int[4][];
        for (byte quadrant = 0; quadrant < 4; quadrant++)
        {
            var basis = source.BaseLayers.Single(layer => layer.Quadrant == quadrant);
            var layers = source.AlphaLayers.Where(layer => layer.Quadrant == quadrant)
                .OrderBy(layer => layer.LayerIndex).ToArray();
            if (layers.Any(layer => layer.LayerIndex >= 6))
                throw new InvalidDataException("LAND alpha index exceeds its six source slots.");
            var weights = FalloutLandscapeMaterialInputs.Weights(layers);
            var materials = new[] { basis }.Concat(layers)
                .Select(layer => (int?)source.Textures[layer.Texture].Physics?.Material ?? -1).ToArray();
            _materials[quadrant] = VertexMaterials(weights, materials);
        }
    }

    internal static int[] VertexMaterials(float[][] weights, int[] materials)
    {
        if (weights.Length == 0 || weights.Length != materials.Length ||
            weights.Any(row => row.Length != 289))
            throw new InvalidDataException("LAND impact weight and material tables disagree.");
        var result = new int[289];
        for (var vertex = 0; vertex < result.Length; vertex++)
        {
            var selected = 0;
            // This build scans six runtime slots (base plus five alphas).
            // All six authored alpha slots still participate in normalization.
            for (var layer = 1; layer < Math.Min(6, weights.Length); layer++)
                if (weights[layer][vertex] > weights[selected][vertex]) selected = layer;
            result[vertex] = materials[selected];
        }
        return result;
    }

    internal uint At(float worldX, float worldY)
    {
        var (quadrant, vertex) = Vertex(_cell, worldX, worldY);
        var material = _materials[quadrant][vertex];
        return material >= 0 ? (uint)material :
            throw new NotSupportedException($"LAND impact quadrant {quadrant} vertex {vertex} has no selected LTEX physics.");
    }

    internal static (int Quadrant, int Vertex) Vertex((int X, int Y) cell, float worldX, float worldY)
    {
        var cellX = cell.X * 4096f; var cellY = cell.Y * 4096f;
        var x = worldX - cellX; var y = worldY - cellY;
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || x > 4096 || y < 0 || y > 4096)
            throw new InvalidDataException("LAND impact point lies outside its source cell.");
        var qx = LowerInterval(x, 2048); var qy = LowerInterval(y, 2048);
        var originX = cellX + qx * 2048; var originY = cellY + qy * 2048;
        var localX = worldX - originX; var localY = worldY - originY;
        int selected;
        if ((int)worldX % 128 == 0 && (int)worldY % 128 == 0)
            selected = (int)localY / 128 * 17 + (int)localX / 128;
        else
        {
            var gx = LowerInterval(localX, 128); var gy = LowerInterval(localY, 128);
            var dx = worldX - (originX + gx * 128); var dy = worldY - (originY + gy * 128);
            var lowerLeft = gy * 17 + gx;
            Span<int> corners = stackalloc int[3];
            if ((gx + gy) % 2 == 0)
            {
                corners[0] = dy < dx ? lowerLeft + 1 : lowerLeft + 17;
                corners[1] = lowerLeft; corners[2] = lowerLeft + 18;
            }
            else
            {
                corners[0] = dx + dy > 128 ? lowerLeft + 18 : lowerLeft;
                corners[1] = lowerLeft + 1; corners[2] = lowerLeft + 17;
            }
            selected = corners[0];
            var best = float.PositiveInfinity;
            foreach (var corner in corners)
            {
                var vx = originX + corner % 17 * 128 - worldX;
                var vy = originY + corner / 17 * 128 - worldY;
                var distance = MathF.Sqrt((float)((double)vx * vx + (double)vy * vy));
                if (distance < best) { best = distance; selected = corner; }
            }
        }
        // The snapped point is queried again in source space. A shared edge
        // belongs to the preceding quadrant, regardless of the hit triangle.
        var snappedX = qx * 2048 + selected % 17 * 128;
        var snappedY = qy * 2048 + selected / 17 * 128;
        var finalX = LowerInterval(snappedX, 2048); var finalY = LowerInterval(snappedY, 2048);
        return (finalX + finalY * 2,
            (snappedY - finalY * 2048) / 128 * 17 + (snappedX - finalX * 2048) / 128);
    }

    private static int LowerInterval(float coordinate, int size)
    {
        var interval = (int)(coordinate / size);
        if ((int)coordinate != 0 && (int)coordinate % size == 0) interval--;
        return interval;
    }
}
