using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

// A bound from the actual selected 3D root. Native actor construction retains
// this named object in its admitted process; the source fallback reads the
// same root. It is independent of current skin/mesh spheres and root transform.
internal sealed record FalloutDetectionBounds(string SourceSha256, int RootBlock, int BoundBlock,
    float[] Center, float[] Extents)
{
    internal static FalloutDetectionBounds Read(FalloutNifFile source, int actualSourceRoot)
    {
        ArgumentNullException.ThrowIfNull(source);
        var root = source.ReadNode(actualSourceRoot);
        var named = root.ExtraData.Where(index => index >= 0).Select(source.ReadObject)
            .Where(value => Name(value) == "BBX").ToArray();
        if (named.Length > 1 || named is [not FalloutNifBound])
            throw new InvalidDataException("Detection bounds have no unique typed source BBX object.");
        if (named.Length == 0) return new(source.Sha256, actualSourceRoot, -1, [0, 0, 0], [0, 0, 0]);
        var bound = (FalloutNifBound)named[0];
        var result = new FalloutDetectionBounds(source.Sha256, actualSourceRoot, bound.Block.Index,
            [bound.Center.X, bound.Center.Y, bound.Center.Z],
            [bound.Dimensions.X, bound.Dimensions.Y, bound.Dimensions.Z]);
        result.Validate(); return result;
    }

    internal float[] Minimum => Expand(-1);
    internal float[] Maximum => Expand(1);

    internal void ValidateAgainst(FalloutNifFile source, int actualSourceRoot)
    {
        Validate();
        var declared = Read(source, actualSourceRoot);
        if (SourceSha256 != declared.SourceSha256 || RootBlock != declared.RootBlock || BoundBlock != declared.BoundBlock ||
            !Center.SequenceEqual(declared.Center) || !Extents.SequenceEqual(declared.Extents))
            throw new InvalidDataException("Saved detection BBX differs from its actual source root/bound.");
    }

    private float[] Expand(int direction)
    {
        Validate();
        return Center.Zip(Extents, (center, extent) => Store((double)center + direction * (double)extent)).ToArray();
    }
    private void Validate()
    {
        if (SourceSha256 is not { Length: 64 } || SourceSha256.Any(value => !Uri.IsHexDigit(value)) ||
            RootBlock < 0 || BoundBlock < -1 || Center is not { Length: 3 } || Extents is not { Length: 3 } ||
            Center.Any(value => !float.IsFinite(value)) || Extents.Any(value => !float.IsFinite(value) || value < 0) ||
            BoundBlock == -1 && (Center.Any(value => value != 0) || Extents.Any(value => value != 0)))
            throw new InvalidDataException("Detection BBX has invalid source identity or finite extents.");
    }
    private static float Store(double value)
    {
        var result = (float)value;
        return float.IsFinite(result) ? result : throw new InvalidDataException("Detection BBX exceeds finite Float32 bounds.");
    }
    private static string Name(FalloutNifObject value) => value switch
    {
        FalloutNifBound bound => bound.Name,
        FalloutNifIntegerExtraData extra => extra.Name,
        FalloutNifFloatExtraData extra => extra.Name,
        FalloutNifStringExtraData extra => extra.Name,
        FalloutNifTextKeyExtraData extra => extra.Name,
        FalloutNifFurnitureMarker extra => extra.Name,
        FalloutNifDecalPlacementExtraData extra => extra.Name,
        _ => throw new NotSupportedException("Detection BBX lookup contains an unowned extra-data name/type.")
    };
}
