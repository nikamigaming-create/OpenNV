using System.Globalization;
using System.Numerics;

namespace OpenNV.Runtime.Content;

// All positions remain in source Z-up game units.
internal sealed record FalloutGrassSettings(int EvaluationSize, int MinimumSize,
    int MaximumTypesPerTexture, float TextureThreshold)
{
    internal static FalloutGrassSettings Read(FalloutInstallationSettings source)
    {
        int Integer(string key) => int.Parse(source.Require("Grass", key), CultureInfo.InvariantCulture);
        var size = Integer("iGrassDensityEvalSize");
        if (size is not (1 or 2 or 4 or 8)) size = 2;
        var minimum = Integer("iMinGrassSize");
        if (minimum <= 0) throw new NotSupportedException("Nonpositive source minimum grass size has no valid population divisor.");
        var threshold = source.Number("Grass", "fTexturePctThreshold");
        if (!float.IsFinite(threshold)) throw new InvalidDataException("Grass texture threshold is non-finite.");
        if (threshold < 0) threshold = 0;
        else if (threshold > 1) threshold = .9f;
        return new(size, minimum, Integer("iMaxGrassTypesPerTexure"), threshold);
    }
}

internal sealed record FalloutGrassAreaSlot(FalloutGrassDefinition Grass, float[] Density);
internal sealed record FalloutGrassArea(FalloutFormKey Cell, byte Quadrant, Vector2 Origin,
    float Side, uint Key, IReadOnlyList<FalloutGrassAreaSlot> Slots);

internal sealed class FalloutGrassAreas(FalloutPluginStack stack)
{
    private readonly Dictionary<FalloutFormKey, IReadOnlyList<FalloutGrassDefinition>> _associations = [];
    private static readonly int[] SampleOffsets = [-18, -17, -16, -1, 0, 1, 16, 17, 18];

    internal IReadOnlyList<FalloutGrassArea> Build(FalloutLandscapeTransport land, FalloutGrassSettings settings)
    {
        var areas = new List<FalloutGrassArea>();
        for (byte quadrant = 0; quadrant < 4; quadrant++)
        {
            var basis = land.BaseLayers.Single(layer => layer.Quadrant == quadrant);
            var alpha = land.AlphaLayers.Where(layer => layer.Quadrant == quadrant)
                .OrderBy(layer => layer.LayerIndex).ToArray();
            if (alpha.Length > 6 || alpha.Any(layer => layer.LayerIndex >= 6) ||
                alpha.Select(layer => layer.LayerIndex).Distinct().Count() != alpha.Length)
                throw new NotSupportedException($"LAND {land.Landscape} has an unqualified grass alpha-slot layout.");
            var alphaWeights = new float[6][];
            for (var slot = 0; slot < 6; slot++) alphaWeights[slot] = new float[17 * 17];
            foreach (var layer in alpha)
                foreach (var opacity in layer.Opacities)
                    alphaWeights[layer.LayerIndex][opacity.VertexIndex] = opacity.Opacity;
            var baseWeights = new float[17 * 17];
            for (var vertex = 0; vertex < baseWeights.Length; vertex++)
            {
                var sum = 0f;
                for (var slot = 0; slot < 6; slot++) sum += alphaWeights[slot][vertex];
                baseWeights[vertex] = 1f - sum; // Native grass does not normalize rendering weights.
            }

            // Native population visits X outside Y. Preserve that RNG consumption order.
            for (var x = settings.EvaluationSize; x < 16; x += settings.EvaluationSize * 2)
                for (var y = settings.EvaluationSize; y < 16; y += settings.EvaluationSize * 2)
                {
                    var center = y * 17 + x;
                    var slots = new List<FalloutGrassAreaSlot>(16);
                    Append(basis.Texture, baseWeights, alphaLayer: false);
                    foreach (var layer in alpha) Append(layer.Texture, alphaWeights[layer.LayerIndex], alphaLayer: true);
                    var origin = new Vector2(land.ActiveCoordinates.X * 4096f + (quadrant % 2) * 2048f + x * 128f - 256f,
                        land.ActiveCoordinates.Y * 4096f + (quadrant / 2) * 2048f + y * 128f - 256f);
                    var key = unchecked((uint)((int)origin.X << 16)) | (unchecked((uint)(int)origin.Y) & 0xffffu);
                    areas.Add(new(land.ActiveCell, quadrant, origin, settings.EvaluationSize * 256f, key, slots));

                    void Append(FalloutFormKey texture, float[] weights, bool alphaLayer)
                    {
                        if (slots.Count == 16 || settings.MaximumTypesPerTexture < 0) return;
                        if (alphaLayer && !SampleOffsets.Any(offset => weights[center + offset] > .1f)) return;
                        var source = land.Textures[texture];
                        if (source.LandscapeTexture is not { } identity) return; // Initialized default LTEX has no authored GNAM.
                        if (!_associations.TryGetValue(identity, out var grasses))
                            _associations.Add(identity, grasses = FalloutGrassSource.ReadAssociations(stack, stack.GetEffective(identity)));
                        var textureCount = 0;
                        foreach (var grass in grasses)
                        {
                            if (textureCount > settings.MaximumTypesPerTexture || slots.Count == 16) break;
                            var density = new float[9];
                            var sum = 0f;
                            for (var sample = 0; sample < density.Length; sample++)
                            {
                                density[sample] = weights[center + SampleOffsets[sample]] > settings.TextureThreshold
                                    ? grass.Density / 100f : 0f;
                                sum += density[sample];
                            }
                            if (sum / 9f < settings.TextureThreshold) Array.Clear(density);
                            slots.Add(new(grass, density)); // Zero density and duplicate identities consume list slots.
                            textureCount++;
                        }
                    }
                }
        }
        return areas;
    }
}

internal static class FalloutGrassDensity
{
    internal static int CandidateCount(FalloutGrassArea area, FalloutGrassDefinition grass, FalloutGrassSettings settings)
    {
        var sourceCount = MathF.Truncate(area.Side / grass.PositionRange);
        var limitedCount = (int)area.Side / settings.MinimumSize;
        if (sourceCount > ushort.MaxValue || limitedCount > ushort.MaxValue)
            throw new NotSupportedException("Grass source count wraps its native u16 lane; that profile is unqualified.");
        return Math.Min((int)sourceCount, limitedCount);
    }

    internal static float Interpolate(ReadOnlySpan<float> density, int x, int y, int count)
    {
        if (density.Length != 9 || count <= 0 || x < 0 || y < 0 || x >= count || y >= count)
            throw new ArgumentOutOfRangeException(nameof(count));
        var coordinateX = x * (3f / count);
        var coordinateY = y * (3f / count);
        var ix = checked((int)MathF.Round(coordinateX, MidpointRounding.ToEven));
        var iy = checked((int)MathF.Round(coordinateY, MidpointRounding.ToEven));
        if (ix is < 0 or > 2 || iy is < 0 or > 2)
            throw new NotSupportedException("Source grass density profile indexes beyond its nine qualified samples; no clamp is substituted.");
        var fx = coordinateX - ix;
        var fy = coordinateY - iy;
        var d00 = density[iy * 3 + ix];
        var d10 = 0f;
        var d01 = 0f;
        var d11 = 0f;
        if (fx > .01) // Native comparison uses the double constant, not a rounded float threshold.
        {
            d10 = density[iy * 3 + (ix >= 2 ? ix : ix + 1)];
            if (fy > .01) d11 = density[(iy >= 2 ? iy : iy + 1) * 3 + (ix >= 2 ? ix : ix + 1)];
        }
        if (fy > .01) d01 = density[(iy >= 2 ? iy : iy + 1) * 3 + ix];
        return ((1f - fx) * d00 + d10 * fx) * (1f - fy) + ((1f - fx) * d01 + d11 * fx) * fy;
    }
}
