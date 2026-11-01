using System.Buffers.Binary;
using System.Numerics;

namespace OpenNV.Runtime.Content;

// The shared CELL cache supplies LANDs; this class creates
// no independent residency ring and never substitutes a height for missing LAND.
internal sealed class FalloutGrassLandscape(Func<int, int, FalloutLandscapeTransport?> cachedLand)
{
    private readonly record struct Sample(FalloutLandscapeTransport Land, int X, int Y,
        float U, float V, bool Odd, bool Upper, int First, int Second, int Third);

    internal float Height(float sourceX, float sourceY)
    {
        var s = At(World(sourceX, sourceY), sourceX, sourceY);
        var ll = s.Land.Heights[s.Y * 33 + s.X];
        var lr = s.Land.Heights[s.Y * 33 + s.X + 1];
        var ul = s.Land.Heights[(s.Y + 1) * 33 + s.X];
        var ur = s.Land.Heights[(s.Y + 1) * 33 + s.X + 1];
        var u = s.U / 128f;
        var v = s.V / 128f;
        if (!s.Odd) return s.Upper ? ll + (ul - ll) * v + (ur - ul) * u : ll + (lr - ll) * u + (ur - lr) * v;
        return s.Upper ? ur - (ur - ul) * (1f - u) - (ur - lr) * (1f - v) : ll + (lr - ll) * u + (ul - ll) * v;
    }

    internal Vector3 Normal(FalloutLandscapeTransport sourceLand, float sourceX, float sourceY, byte flags)
    {
        var land = Contains(sourceLand, sourceX, sourceY) ? sourceLand : World(sourceX, sourceY);
        var s = At(land, sourceX, sourceY);
        int vertex;
        if (sourceX % 128f == 0 && sourceY % 128f == 0)
            vertex = (int)((sourceY - land.ActiveCoordinates.Y * 4096f) / 128f) * 33 +
                (int)((sourceX - land.ActiveCoordinates.X * 4096f) / 128f);
        else
        {
            vertex = s.First;
            var distance = Distance(vertex);
            foreach (var candidate in new[] { s.Second, s.Third })
            {
                var next = Distance(candidate);
                if (next < distance) { vertex = candidate; distance = next; }
            }
        }
        var offset = vertex * 3;
        var normal = new Vector3(land.Normals[offset], land.Normals[offset + 1], land.Normals[offset + 2]);
        // Ordinary undeformed LAND returns the selected vertex normal in both lanes.
        // FitSlope without VertexLighting normalizes their sum; other ordinary cases
        // retain the decoded vertex value. Alternate/displaced terrain is not admitted.
        if ((flags & 4) != 0 && (flags & 1) == 0) normal = Vector3.Normalize(normal + normal);
        return normal;

        float Distance(int index)
        {
            var dx = land.ActiveCoordinates.X * 4096f + (index % 33) * 128f - sourceX;
            var dy = land.ActiveCoordinates.Y * 4096f + (index / 33) * 128f - sourceY;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }

    internal Vector3 Color(float packedX, float packedY)
    {
        // Native color is queried after normal fractions have been packed into XY.
        var s = At(World(packedX, packedY), packedX, packedY);
        var c0 = VertexColor(s.First);
        var c1 = VertexColor(s.Second);
        var c2 = VertexColor(s.Third);
        var u = packedX % 128f / 128f;
        var v = packedY % 128f / 128f;
        // Source uses signed FPREM and these three-color expressions, not a generic
        // positive-fraction bilinear or planar barycentric sampler.
        return (s.Odd, s.Upper) switch
        {
            (false, false) => (c1 * (1f - u) + c0 * u) * (1f - v) + c2 * v,
            (false, true) => (c0 * (1f - u) + c2 * u) * v + c1 * (1f - v),
            (true, false) => (c0 * (1f - u) + c1 * u) * (1f - v) + c2 * v,
            _ => (c2 * (1f - u) + c0 * u) * v + c1 * (1f - v),
        };

        Vector3 VertexColor(int index)
        {
            var offset = index * 3;
            return new(s.Land.Colors[offset] / 255f, s.Land.Colors[offset + 1] / 255f, s.Land.Colors[offset + 2] / 255f);
        }
    }

    private FalloutLandscapeTransport World(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new InvalidDataException("Grass surface coordinate is non-finite.");
        var cellX = checked((int)MathF.Floor(x / 4096f));
        var cellY = checked((int)MathF.Floor(y / 4096f));
        return cachedLand(cellX, cellY) ?? throw new NotSupportedException($"Grass requires missing cached LAND at ({cellX},{cellY}).");
    }

    private static bool Contains(FalloutLandscapeTransport land, float x, float y)
    {
        x -= land.ActiveCoordinates.X * 4096f;
        y -= land.ActiveCoordinates.Y * 4096f;
        return x >= 0 && x <= 4096 && y >= 0 && y <= 4096;
    }

    private static Sample At(FalloutLandscapeTransport land, float x, float y)
    {
        if (!Contains(land, x, y)) throw new InvalidDataException("Grass surface query is outside its source LAND.");
        var lx = x - land.ActiveCoordinates.X * 4096f;
        var ly = y - land.ActiveCoordinates.Y * 4096f;
        static int LowerBin(float position, int width)
        {
            var bin = (int)(position / width);
            // Source tests truncated local coordinate, including packed fractions
            // within the first source unit above a nonzero lattice boundary.
            var integer = (int)position;
            return integer != 0 && integer % width == 0 ? bin - 1 : bin;
        }
        var qx = LowerBin(lx, 2048);
        var qy = LowerBin(ly, 2048);
        var localX = lx - qx * 2048f;
        var localY = ly - qy * 2048f;
        var sx = LowerBin(localX, 128);
        var sy = LowerBin(localY, 128);
        if (qx is < 0 or > 1 || qy is < 0 or > 1 || sx is < 0 or > 15 || sy is < 0 or > 15)
            throw new NotSupportedException("Grass LAND query reached an unqualified source quadrant/square index.");
        var u = localX - sx * 128f;
        var v = localY - sy * 128f;
        var gx = qx * 16 + sx;
        var gy = qy * 16 + sy;
        var ll = gy * 33 + gx;
        var lr = ll + 1;
        var ul = ll + 33;
        var ur = ul + 1;
        var odd = ((sx + sy) & 1) != 0;
        var upper = odd ? u + v > 128f : v >= u;
        return new(land, gx, gy, u, v, odd, upper,
            odd ? (upper ? ur : ll) : (upper ? ul : lr), odd ? lr : ll, odd ? ul : ur);
    }
}

internal static class FalloutGrassWater
{
    internal static float Read(FalloutPluginStack stack, FalloutCellDefinition cell)
    {
        if ((cell.Flags & 2) == 0 || (cell.Flags & 1) != 0) return -float.MaxValue;
        var record = stack.GetEffective(cell.FormKey);
        var water = record.ReadSubrecords().Where(row => row.Signature == "XCLW").ToArray();
        if (water.Length != 1 || water[0].Data.Length != 4)
            throw new NotSupportedException($"Water-bearing CELL {cell.FormKey} has no qualified explicit XCLW initialization.");
        var height = BinaryPrimitives.ReadSingleLittleEndian(water[0].Data.Span);
        if (!float.IsFinite(height)) throw new InvalidDataException("CELL grass-water height is non-finite.");
        if (height != float.MaxValue) return height;
        if (cell.Worldspace is not { } worldspace) return 0f;
        var world = stack.GetEffective(worldspace);
        var fields = world.ReadSubrecords().ToArray();
        var parents = fields.Where(row => row.Signature == "WNAM").ToArray();
        if (parents.Length != 0)
        {
            if (parents.Length != 1 || parents[0].Data.Length != 4)
                throw new InvalidDataException("WRLD water parent requires one FormID.");
            var parent = world.Plugin.AdjustFormId(BinaryPrimitives.ReadUInt32LittleEndian(parents[0].Data.Span));
            if (stack.GetEffective(parent).Signature != "WRLD")
                throw new InvalidDataException("WRLD water parent is not WRLD.");
            var flags = fields.Where(row => row.Signature == "PNAM").ToArray();
            if (flags.Length != 1 || flags[0].Data.Length != 2)
                throw new NotSupportedException($"WRLD {worldspace} has no qualified parent flags.");
            // A parent used for map/LOD/etc. does not replace this world's water.
            // Actual UseWaterData inheritance still requires its native consumer.
            if ((BinaryPrimitives.ReadUInt16LittleEndian(flags[0].Data.Span) & 8) != 0)
                throw new NotSupportedException($"WRLD {worldspace} grass-water inheritance is not yet qualified.");
        }
        var defaults = fields.Where(row => row.Signature == "DNAM").ToArray();
        if (defaults.Length != 1 || defaults[0].Data.Length != 8)
            throw new NotSupportedException($"WRLD {worldspace} has no qualified explicit default water height.");
        height = BinaryPrimitives.ReadSingleLittleEndian(defaults[0].Data.Span[4..]);
        if (!float.IsFinite(height)) throw new InvalidDataException("WRLD grass-water height is non-finite.");
        return height;
    }
}
