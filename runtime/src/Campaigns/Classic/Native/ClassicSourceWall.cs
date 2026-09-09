using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Extrudes a source wall's ground line, rather than its blocking hex.</summary>
internal static class ClassicSourceWall
{
    internal static Node3D? Build(Fallout1NativeFrmFrame frame, Vector3 anchor,
        ClassicBlockoutPolicy policy, Material shellMaterial, Material faceMaterial,
        List<Aabb>? joinedShell = null, bool door = false, int firstColumn = 0, int lastColumn = int.MaxValue)
    {
        var columns = new List<(int X, int Top, int Bottom)>();
        for (var x = firstColumn; x < Math.Min(frame.Width, lastColumn); x++)
        {
            var top = -1; var bottom = -1;
            for (var y = 0; y < frame.Height; y++)
                if (frame.PaletteIndexes[y * frame.Width + x] != 0)
                { if (top < 0) top = y; bottom = y; }
            if (top >= 0) columns.Add((x, top, bottom));
        }
        if (columns.Count < 4) return null;

        // The original orthogonal wall axes project at -1/4 and +3/4.
        // Fit the opaque bottom contour, retaining direction/frame offsets.
        // More complex silhouettes keep their source art until they have a
        // geometry owner; a corner is never replaced with a solid hex column.
        var fits = new[] { -0.25f, 0.75f }.Select(slope =>
        {
            var values = columns.Select(column => column.Bottom - slope * column.X).Order().ToArray();
            var intercept = door ? values[values.Length / 2] : values.Average();
            var errors = values.Select(value => Math.Abs(value - intercept)).Order().ToArray();
            var error = errors[door ? errors.Length * 4 / 5 : errors.Length - 1];
            return (Slope: slope, Intercept: intercept, Error: error);
        }).OrderBy(fit => fit.Error).ToArray();
        var fit = fits[0];
        if (fit.Error > policy.WallBaselineTolerancePixels)
        {
            if (firstColumn != 0 || lastColumn != int.MaxValue) return null;
            // A convex source corner contains two perpendicular baselines.
            // Both halves keep the full FRM projection, meeting at the turn.
            for (var split = 4; split <= frame.Width - 4; split++)
            {
                var temporaryBounds = new List<Aabb>();
                var firstPart = Build(frame, Vector3.Zero, policy, shellMaterial, faceMaterial, temporaryBounds, door, 0, split);
                if (firstPart is null) continue;
                var secondPart = Build(frame, Vector3.Zero, policy, shellMaterial, faceMaterial, temporaryBounds, door, split, frame.Width);
                if (secondPart is null) { firstPart.Free(); continue; }
                var corner = new Node3D { Position = anchor }; corner.AddChild(firstPart); corner.AddChild(secondPart);
                if (joinedShell is not null)
                    joinedShell.AddRange(temporaryBounds.Select(box => new Aabb(box.Position + anchor, box.Size)));
                else corner.AddChild(ClassicWallJoiner.Build(temporaryBounds, shellMaterial, policy.WallJoinTolerance));
                return corner;
            }
            return null;
        }
        var left = -frame.Width / 2f + frame.DirectionX + frame.FrameX;
        var topOffset = -frame.Height + frame.DirectionY + frame.FrameY;
        Vector3 Ground(float x)
        {
            var world = ClassicMapProjection.World(4816 + left + x,
                11 + topOffset + fit.Intercept + fit.Slope * x);
            return new((float)world.X, 0, (float)world.Z);
        }
        var first = Ground(columns[0].X - 0.5f);
        var last = Ground(columns[^1].X + 0.5f);
        var along = (last - first).Normalized();
        var across = new Vector3(-along.Z, 0, along.X);
        var height = door ? columns.Max(column => fit.Intercept + fit.Slope * column.X - column.Top + 1) /
            policy.PixelsPerMeter : policy.VaultWallHeight;
        var center = (first + last) / 2;
        var root = new Node3D { Position = anchor };
        var shell = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new(first.DistanceTo(last), height, policy.VaultWallThickness) },
            MaterialOverride = shellMaterial,
            Transform = new Transform3D(new Basis(along, Vector3.Up, across), center + Vector3.Up * (height / 2)),
        };
        if (joinedShell is null) root.AddChild(shell);
        else
        {
            var bounds = shell.Transform * shell.GetAabb();
            joinedShell.Add(new Aabb(bounds.Position + anchor, bounds.Size)); shell.Free();
        }

        // Project the original panel onto its real vertical plane. The same
        // UVs remain attached during orbit; this is not a camera-facing card.
        var vertices = new List<Vector3>(); var normals = new List<Vector3>();
        var uv = new List<Vector2>(); var indices = new List<int>();
        foreach (var sign in new[] { -1, 1 })
        {
            var normal = across * sign;
            var offset = normal * (policy.VaultWallThickness / 2 + 0.001f);
            var a = first + offset; var b = last + offset;
            var start = vertices.Count;
            Vector3[] corners = [a, b, b + Vector3.Up * height, a + Vector3.Up * height];
            foreach (var point in corners)
            {
                vertices.Add(point); normals.Add(normal);
                var screenX = -16 * MathF.Sqrt(3) * point.X + 16 * point.Z;
                var screenY = 4 * MathF.Sqrt(3) * point.X + 12 * point.Z - policy.PixelsPerMeter * point.Y;
                uv.Add(new((screenX - left + 0.5f) / frame.Width, (screenY - topOffset + 0.5f) / frame.Height));
            }
            if ((corners[2] - corners[0]).Cross(corners[1] - corners[0]).Dot(normal) > 0)
                indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
            else indices.AddRange([start, start + 2, start + 1, start, start + 3, start + 2]);
        }
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = faceMaterial });
        root.SetMeta("presentation", "source-ground-line-extrusion");
        return root;
    }
}
