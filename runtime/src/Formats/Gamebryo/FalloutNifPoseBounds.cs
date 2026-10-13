namespace OpenNV.Runtime.Formats.Gamebryo;

// Bounds use the selected source's bone spheres and the current pose. Static
// mesh bounds cannot stand in for the animated skin, including on cold entry.
internal static class FalloutNifPoseBounds
{
    internal static FalloutNifSphereBound? Read(FalloutNifFile source,
        Func<string, FalloutNifTransform> readBonePose)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(readBonePose);
        var visited = new HashSet<int>();
        FalloutNifSphereBound? Visit(int index)
        {
            if (!visited.Add(index)) throw new InvalidDataException("Posed bounds require a singly owned source tree.");
            switch (source.ReadObject(index))
            {
                case FalloutNifGeometry geometry when geometry.SkinInstance >= 0:
                    if (source.ReadObject(geometry.SkinInstance) is not FalloutNifSkinInstance instance ||
                        source.ReadObject(instance.Data) is not FalloutNifSkinData skin ||
                        instance.Bones.Length == 0 || instance.Bones.Length != skin.Bones.Length)
                        throw new InvalidDataException("Posed source bounds have an incomplete skin palette.");
                    var bones = instance.Bones.Select(bone => source.ReadNode(bone).Name).ToArray();
                    return Skin(skin.Bones, bones, readBonePose);
                case FalloutNifGeometry geometry:
                    var mesh = source.ReadMeshData(geometry.Data);
                    return Checked(new(new(mesh.Center.X, mesh.Center.Y, mesh.Center.Z), mesh.Radius)).Transform(geometry.Transform);
                case FalloutNifNode node:
                    FalloutNifSphereBound? bound = null;
                    foreach (var child in node.Children.Where(child => child >= 0))
                        if (Visit(child) is { } next) bound = bound?.Merge(next) ?? next;
                    // Skin spheres already occupy the current skeleton's
                    // model space. Rigid-only nodes keep their source transform.
                    if (bound is not null && !IsIdentity(node.Transform) &&
                        ContainsSkin(index, new HashSet<int>()))
                        throw new NotSupportedException("Mixed posed skin bounds require an independent transformed-root owner.");
                    return bound?.Transform(node.Transform);
                case FalloutNifAmbientLight or FalloutNifPointLight:
                    return null;
                default:
                    throw new NotSupportedException($"Posed bound owner is unbound for {source.Blocks[index].TypeName}.");
            }
        }
        bool ContainsSkin(int index, HashSet<int> seen)
        {
            if (!seen.Add(index)) throw new InvalidDataException("Posed bounds contain a cyclic source tree.");
            return source.ReadObject(index) switch
            {
                FalloutNifGeometry geometry => geometry.SkinInstance >= 0,
                FalloutNifNode node => node.Children.Where(child => child >= 0).Any(child => ContainsSkin(child, seen)),
                _ => false
            };
        }
        FalloutNifSphereBound? result = null;
        foreach (var root in source.Roots)
            if (Visit(root) is { } next) result = result?.Merge(next) ?? next;
        return result;
    }
    private static bool IsIdentity(FalloutNifTransform pose) => pose.Scale == 1 &&
        pose.Translation == new FalloutNifVector3(0, 0, 0) &&
        pose.RotationRowMajor.SequenceEqual(new float[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 });

    internal static FalloutNifSphereBound Skin(IReadOnlyList<FalloutNifSkinBoneData> data,
        IReadOnlyList<string> names, Func<string, FalloutNifTransform> readBonePose)
    {
        if (data.Count == 0 || names.Count != data.Count || names.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Posed skin bounds lost their complete source bone palette.");
        FalloutNifSphereBound? result = null;
        for (var index = 0; index < data.Count; ++index)
        {
            var bone = data[index];
            var pose = readBonePose(names[index]);
            if (pose is null || !float.IsFinite(pose.Scale) || pose.RotationRowMajor.Length != 9 ||
                pose.RotationRowMajor.Any(value => !float.IsFinite(value)))
                throw new InvalidDataException("Posed skin bound has no finite current bone transform.");
            var bound = Checked(new(new(bone.BoundingSphereCenter.X, bone.BoundingSphereCenter.Y, bone.BoundingSphereCenter.Z),
                bone.BoundingSphereRadius));
            var next = Checked(bound.Transform(pose));
            result = result?.Merge(next) ?? next;
        }
        return Checked(result!.Value);
    }
    private static FalloutNifSphereBound Checked(FalloutNifSphereBound bound)
    {
        if (!float.IsFinite(bound.Radius) || bound.Radius < 0 ||
            !float.IsFinite(bound.Center.X) || !float.IsFinite(bound.Center.Y) || !float.IsFinite(bound.Center.Z))
            throw new InvalidDataException("Posed source bound contains a malformed center or radius.");
        return bound;
    }
}
