using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal static class FalloutActorMeleeBounds
{
    internal static FalloutNifBound Read(FalloutNifFile source) => source.Roots.Select(source.ReadNode)
        .SelectMany(node => node.ExtraData).Where(index => index >= 0).Select(source.ReadObject)
        .OfType<FalloutNifBound>().SingleOrDefault(value => value.Name == "BBX") ??
        throw new NotSupportedException("Actor source bounds require the actual source BBX.");

    internal static float NaturalReach(float sourceGameUnits, float actorScale, float unitsToMetres)
    {
        if (!float.IsFinite(sourceGameUnits) || sourceGameUnits <= 0 ||
            !float.IsFinite(actorScale) || actorScale <= 0 || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Creature natural reach or source scale is invalid.");
        var value = sourceGameUnits * actorScale * unitsToMetres;
        if (!float.IsFinite(value)) throw new InvalidDataException("Creature natural reach overflowed its source scale.");
        return value;
    }

    // Sight candidates use the source vertical bound span, independently of melee extent.
    internal static float VerticalSpan(FalloutNifVector3 center, FalloutNifVector3 dimensions,
        float actorScale, float unitsToMetres)
    {
        if (!float.IsFinite(center.Z) || !float.IsFinite(dimensions.Z) || dimensions.Z < 0 ||
            !float.IsFinite(actorScale) || actorScale <= 0 || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Actor sight bounds or source scale are invalid.");
        var maximum = center.Z + dimensions.Z;
        var minimum = center.Z - dimensions.Z;
        var scaledSpan = (float)(((double)maximum - minimum) * actorScale);
        var height = scaledSpan * unitsToMetres;
        if (!float.IsFinite(scaledSpan) || scaledSpan < 0 || !float.IsFinite(height) || height < 0)
            throw new InvalidDataException("Actor sight height overflowed its actual vertical span.");
        return height;
    }

    // This is the native cached maximum local Y bound, not the smallest
    // locomotion capsule radius or a pose-dependent world bounding sphere.
    internal static float MaximumY(FalloutNifVector3 center, FalloutNifVector3 dimensions,
        float actorScale, float unitsToMetres)
    {
        if (!float.IsFinite(center.Y) || !float.IsFinite(dimensions.Y) || dimensions.Y <= 0 ||
            !float.IsFinite(actorScale) || actorScale <= 0 || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("Actor melee source bounds or scale are invalid.");
        var value = (center.Y + dimensions.Y) * actorScale * unitsToMetres;
        if (!float.IsFinite(value)) throw new InvalidDataException("Actor melee extent overflowed its source scale.");
        return value;
    }
}
