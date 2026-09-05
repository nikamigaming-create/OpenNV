using System.Numerics;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct FalloutNifSphereBound(Vector3 Center, float Radius)
{
    internal FalloutNifSphereBound Merge(FalloutNifSphereBound other)
    {
        var delta = other.Center - Center;
        var distance = delta.Length();
        if (Radius >= distance + other.Radius) return this;
        if (other.Radius >= distance + Radius) return other;
        var radius = (Radius + distance + other.Radius) * 0.5f;
        return new(Center + delta * ((radius - Radius) / distance), radius);
    }

    internal FalloutNifSphereBound Transform(FalloutNifTransform transform)
    {
        var r = transform.RotationRowMajor; var p = Center * transform.Scale; var t = transform.Translation;
        return new(new Vector3(r[0] * p.X + r[1] * p.Y + r[2] * p.Z + t.X,
            r[3] * p.X + r[4] * p.Y + r[5] * p.Z + t.Y,
            r[6] * p.X + r[7] * p.Y + r[8] * p.Z + t.Z), Radius * Math.Abs(transform.Scale));
    }
}

internal static class FalloutNifBounds
{
    internal static FalloutNifSphereBound ReadStatic(FalloutNifFile source)
    {
        var seen = new HashSet<int>();
        FalloutNifSphereBound? Visit(int index)
        {
            if (!seen.Add(index)) throw new InvalidDataException("NIF bounds require an acyclic, singly owned visual tree.");
            switch (source.ReadObject(index))
            {
                case FalloutNifGeometry geometry:
                    if (geometry.SkinInstance >= 0) throw new NotSupportedException("Skinned NIF bounds require the current pose owner.");
                    var mesh = source.ReadMeshData(geometry.Data);
                    return new FalloutNifSphereBound(new(mesh.Center.X, mesh.Center.Y, mesh.Center.Z), mesh.Radius).Transform(geometry.Transform);
                case FalloutNifNode node:
                    FalloutNifSphereBound? bound = null;
                    foreach (var child in node.Children.Where(child => child >= 0))
                        if (Visit(child) is { } next) bound = bound?.Merge(next) ?? next;
                    return bound?.Transform(node.Transform);
                case FalloutNifAmbientLight or FalloutNifPointLight:
                    return null;
                default:
                    throw new NotSupportedException($"NIF bound owner is unbound for {source.Blocks[index].TypeName}.");
            }
        }
        FalloutNifSphereBound? result = null;
        foreach (var root in source.Roots)
            if (Visit(root) is { } next) result = result?.Merge(next) ?? next;
        return result ?? throw new InvalidDataException("NIF has no source geometry bounds.");
    }
}
