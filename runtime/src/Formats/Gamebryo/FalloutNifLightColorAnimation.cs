namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed class FalloutNifLightColorAnimation
{
    private readonly FalloutNifPoint3Animation _sampler;
    internal IReadOnlyList<float> Boundaries { get; }

    internal FalloutNifLightColorAnimation(FalloutNifFile source, FalloutNifControllerLink link, int point)
    {
        if (link.ControllerType != "NiLightColorController" || link.PropertyType.Length != 0 ||
            link.Variable1 != "Diffuse" || link.Variable2.Length != 0 ||
            source.ReadObject(link.Controller) is not FalloutNifLightColorController controller ||
            controller.TargetColor != 0 || controller.Time.Target != point || controller.Time.UnknownInteger != 0 ||
            controller.Time.NextController != -1 || controller.Time.Flags != 0x006c ||
            controller.Time.Frequency != 1 || controller.Time.Phase != 0 ||
            controller.Time.StopTime < controller.Time.StartTime ||
            source.ReadObject(controller.Interpolator) is not FalloutNifBlendPoint3Interpolator blend ||
            blend.Flags != 1 || blend.ArraySize != 2 || blend.WeightThreshold != 0 ||
            blend.Value.X != float.MinValue || blend.Value.Y != float.MinValue || blend.Value.Z != float.MinValue ||
            source.ReadObject(link.Interpolator) is not FalloutNifPoint3Interpolator interpolator)
            throw new NotSupportedException("Managed point diffuse has an incomplete source target/blend binding.");
        FalloutNifVectorKey[] keys = interpolator.Data < 0 ? [] :
            (source.ReadObject(interpolator.Data) as FalloutNifPositionData ??
                throw new InvalidDataException("Point diffuse has non-vector data.")).Keys;
        for (var index = 0; index < keys.Length; index++)
        {
            var key = keys[index];
            if (!float.IsFinite(key.Time) || index > 0 && key.Time <= keys[index - 1].Time ||
                key.Interpolation is not (1 or 2) ||
                key.Interpolation == 2 && (key.Forward is null || key.Backward is null))
                throw new NotSupportedException("Point diffuse has an unsupported vector curve.");
            RequireValue(key.Value);
            if (key.Forward is { } forward) RequireValue(forward);
            if (key.Backward is { } backward) RequireValue(backward);
        }
        // A managed controller can be reused by sequences with independent
        // ranges. The controlled interpolator uses its selected sequence clock.
        _sampler = new(source, link.Interpolator);
        if (keys.Length == 0) _ = _sampler.Sample(0);
        Boundaries = keys.Select(key => key.Time).ToArray();
    }

    private static void RequireValue(FalloutNifVector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
            value.X == float.MinValue || value.Y == float.MinValue || value.Z == float.MinValue)
            throw new InvalidDataException("Point diffuse key or tangent has no finite source value.");
    }

    internal FalloutNifColor3 Sample(float time)
    {
        var value = _sampler.Sample(time);
        return new(value.X, value.Y, value.Z);
    }
}
