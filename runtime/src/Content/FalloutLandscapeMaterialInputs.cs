namespace OpenNV.Runtime.Content;

internal static class FalloutLandscapeMaterialInputs
{
    internal static float QuadrantTiling(float installationMultiplier)
    {
        if (!float.IsFinite(installationMultiplier) || installationMultiplier <= 0)
            throw new InvalidDataException("Landscape texture tiling must be positive.");
        return installationMultiplier * (FalloutLandscapeTransportResolver.QuadrantVertexSide - 1) / 4;
    }

    // Resolve weights at vertices before raster interpolation. Sequential alpha
    // compositing attenuates earlier painted textures and changes the source mix.
    internal static float[][] Weights(IReadOnlyList<FalloutLandscapeLayer> layers)
    {
        const int vertices = FalloutLandscapeTransportResolver.QuadrantVertexSide * FalloutLandscapeTransportResolver.QuadrantVertexSide;
        var result = Enumerable.Range(0, layers.Count + 1).Select(_ => new float[vertices]).ToArray();
        for (var layer = 0; layer < layers.Count; layer++)
        {
            var seen = new HashSet<ushort>();
            foreach (var row in layers[layer].Opacities)
            {
                if (row.VertexIndex >= vertices || !seen.Add(row.VertexIndex) ||
                    !float.IsFinite(row.Opacity) || row.Opacity < 0 || row.Opacity > 1)
                    throw new InvalidDataException("Landscape weight has an invalid vertex, duplicate or opacity.");
                result[layer + 1][row.VertexIndex] = row.Opacity;
            }
        }
        for (var vertex = 0; vertex < vertices; vertex++)
        {
            var sum = 0f;
            for (var layer = result.Length - 1; layer > 0; layer--) sum += result[layer][vertex];
            result[0][vertex] = Math.Clamp(1 - sum, 0, 1);
            if (sum > 1)
                for (var layer = 1; layer < result.Length; layer++) result[layer][vertex] /= sum;
        }
        return result;
    }

    internal static int[] CollisionMaterials(float[][] weights, int[] materials, int[] indices)
    {
        if (weights.Length == 0 || weights.Length != materials.Length || indices.Length % 3 != 0 ||
            weights.Any(row => row.Length != weights[0].Length)) throw new InvalidDataException("LAND material tables disagree.");
        var vertices = Enumerable.Repeat(-1, weights[0].Length).ToArray();
        for (var vertex = 0; vertex < vertices.Length; vertex++)
        {
            var active = Enumerable.Range(0, weights.Length).Where(layer => weights[layer][vertex] > 0)
                .Select(layer => materials[layer]).Distinct().ToArray();
            if (active.Length == 1) vertices[vertex] = active[0];
        }
        var faces = new int[indices.Length / 3];
        for (var face = 0; face < faces.Length; face++)
        {
            var a = vertices[indices[face * 3]]; var b = vertices[indices[face * 3 + 1]]; var c = vertices[indices[face * 3 + 2]];
            faces[face] = a == b && a == c ? a : -1;
        }
        // A texture blend may contain several collision materials. Keep that
        // selection unresolved until its retail rule is measured; never guess.
        return faces;
    }
}
