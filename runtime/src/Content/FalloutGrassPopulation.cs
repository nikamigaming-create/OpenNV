using System.Numerics;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutGrassBatch(FalloutGrassArea Area, FalloutGrassDefinition Grass, Vector4[] Instances);

// Creates actual source-model instance payloads; serial
// execution preserves the process random owner's draw order. No per-cell seed.
internal sealed class FalloutGrassPopulation(FalloutGrassSettings settings, FalloutGrassLandscape terrain)
{
    private static readonly float[] SlopeCosines = BuildSlopeCosines();

    internal FalloutGrassBatch Prepare(FalloutLandscapeTransport land, FalloutGrassArea area,
        FalloutGrassAreaSlot slot, float sourceCellWater)
    {
        if (land.ActiveCell != area.Cell) throw new InvalidDataException("Grass area lost its source CELL.");
        var grass = slot.Grass;
        if ((grass.Flags & ~7) != 0)
            throw new NotSupportedException($"GRAS {grass.Form} contains unqualified flag bits.");
        var count = FalloutGrassDensity.CandidateCount(area, grass, settings);
        if (count == 0) return new(area, grass, []);
        // Reject the actual unqualified sample-index condition before consuming RNG.
        _ = FalloutGrassDensity.Interpolate(slot.Density, count - 1, count - 1, count);
        var maximumZ = SlopeCosine(grass.MinimumSlope);
        var minimumZ = SlopeCosine(grass.MaximumSlope);
        var step = area.Side / count;
        var instances = new List<Vector4>();
        var random = FalloutSharedUnsignedRandom.Process;
        for (var x = 0; x < count; x++)
            for (var y = 0; y < count; y++)
            {
                var density = FalloutGrassDensity.Interpolate(slot.Density, x, y, count);
                var threshold = checked((int)(density * 32768f));
                if (threshold == 0) continue;
                if (random.NextBounded(32767) >= threshold) continue;
                var px = area.Origin.X + x * step;
                var py = area.Origin.Y + y * step;
                px += FalloutGrassInstanceMath.SignedPlacementDraw(random) * grass.PositionRange;
                py += FalloutGrassInstanceMath.SignedPlacementDraw(random) * grass.PositionRange;
                px = MathF.Floor(px);
                py = MathF.Floor(py);
                var height = terrain.Height(px, py);
                if (!FalloutGrassSource.WaterAdmits(grass.WaterMode, height, sourceCellWater, grass.WaterDistance)) continue;
                var normal = terrain.Normal(land, px, py, grass.Flags);
                if (normal.Z < minimumZ || normal.Z > maximumZ) continue;
                var point = new Vector3(px, py, MathF.Floor(height));
                var packedPosition = FalloutGrassInstanceMath.PackPosition(point, normal);
                var color = terrain.Color(packedPosition.X, packedPosition.Y);
                var appearance = FalloutGrassInstanceMath.Pack(point, normal, grass.HeightRange, grass.ColorRange,
                    FalloutGrassInstanceMath.TerrainLuminance(color));
                instances.Add(appearance.Packed);
            }
        return new(area, grass, instances.ToArray());
    }

    private static float SlopeCosine(byte degrees)
    {
        var radians = (float)(degrees * 0.01745329238474369);
        var index = (int)((512f / 6.2831854820251465f) * radians);
        return SlopeCosines[index & 511];
    }

    private static float[] BuildSlopeCosines()
    {
        var result = new float[512];
        var angle = 0f;
        var step = 6.2831854820251465f / 512f;
        for (var index = 0; index < result.Length; index++)
        {
            // Native table also accumulates/stores each angle as float. Its CRT
            // cosine implementation versus host Math.Cos is not yet bit-compared.
            result[index] = (float)Math.Cos(angle);
            angle += step;
        }
        return result;
    }
}
