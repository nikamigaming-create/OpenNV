using System.Numerics;

namespace OpenNV.Runtime.Content;

// Reference-FOV geometry and the source sampled camera transition. Neither
// changes hit chance, fragment visibility or an authoritative target point.
internal sealed record FalloutVatsSelectionCamera(float NearMultiplier, float FarMultiplier, float FarDistance,
    float MinimumFov, float DialogueFocus, float DefaultFov, float ZoomSeconds, float LimbSeconds, float LimbMargin)
{
    internal static FalloutVatsSelectionCamera Read(FalloutPluginStack records) => new(
        FalloutGameSettingFloats.Read(records, "fVATSTargetFOVMultNear"),
        FalloutGameSettingFloats.Read(records, "fVATSTargetFOVMultFar"),
        FalloutGameSettingFloats.Read(records, "fVATSTargetFOVMultFarDist"),
        FalloutGameSettingFloats.Read(records, "fVATSTargetFOVMinFOV"),
        records.IniSettings.Float("fDlgFocus:Interface"), records.IniSettings.Float("fDefaultFOV:Display"),
        FalloutGameSettingFloats.Read(records, "fVATSCamZoomInTime"),
        FalloutGameSettingFloats.Read(records, "fVATSLimbSelectCamPanTime"),
        FalloutGameSettingFloats.Read(records, "fVATSMoveCameraLimbPercent"));

    internal (float World, float FirstPerson) Fov(float worldRadius, float distance, float unitsToMetres)
    {
        foreach (var value in new[] { NearMultiplier, FarMultiplier, FarDistance, MinimumFov, DialogueFocus, DefaultFov })
            if (!float.IsFinite(value)) throw new InvalidDataException("VATS source camera setting is invalid.");
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0 || !float.IsFinite(worldRadius) || worldRadius < 0 || !float.IsFinite(distance) || distance <= 0)
            throw new InvalidDataException("VATS target world bound/camera distance is invalid.");
        var radiusGameUnits = worldRadius / unitsToMetres;
        var distanceGameUnits = distance / unitsToMetres;
        var ratio = (float)((double)radiusGameUnits * DialogueFocus / distanceGameUnits);
        var blend = Math.Min(distanceGameUnits / FarDistance, 1);
        var multiplier = (FarMultiplier - NearMultiplier) * blend + NearMultiplier;
        var world = Math.Max(MinimumFov, (float)Math.Atan(ratio) * multiplier);
        if (!float.IsFinite(world) || world <= 0 || world >= 180)
            throw new InvalidDataException("VATS source camera FOV is outside perspective projection.");
        return (world, Math.Min(world, DefaultFov));
    }

    internal static Vector3 Focus(Vector3 playerRoot, Vector3? head, IReadOnlyList<Vector3> sourcePartPoints,
        float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0 || !Finite(playerRoot) ||
            head is { } h && !Finite(h) || sourcePartPoints.Any(point => !Finite(point)))
            throw new InvalidDataException("VATS source focus points are nonfinite.");
        if (head is { } point && Vector3.Distance(point, playerRoot) < 300 * unitsToMetres) return point;
        if (sourcePartPoints.Count == 0)
            throw new NotSupportedException("VATS target has no resolved source BPNT camera focus nodes.");
        var sum = Vector3.Zero;
        foreach (var source in sourcePartPoints) sum += source;
        return sum / sourcePartPoints.Count;
    }

    internal static (float Pitch, float Yaw) Angles(Vector3 sourceDirection, float unitsToMetres)
    {
        if (!Finite(sourceDirection) || !float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new InvalidDataException("VATS source camera direction is invalid.");
        var delta = sourceDirection / unitsToMetres;
        var length = delta.Length();
        if (!float.IsFinite(length) || length <= 1e-6f)
            throw new InvalidDataException("VATS camera has no nondegenerate source target direction.");
        var normal = delta / length;
        var yaw = normal.Y == 0 ? normal.X > 0 ? MathF.PI / 2 : 3 * MathF.PI / 2 : (float)Math.Atan(normal.X / normal.Y);
        if (normal.Y < 0) yaw += MathF.PI;
        if (yaw > MathF.PI) yaw -= 2 * MathF.PI;
        var pitch = -(float)Math.Asin(Math.Clamp(delta.Z / length, -1, 1));
        return (pitch, yaw);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

internal sealed class FalloutVatsCameraTransition
{
    private readonly float[] _samples = new float[100];
    private readonly float _duration, _target;
    private float _remaining;
    internal bool Complete => _remaining <= 0;

    internal FalloutVatsCameraTransition(float start, float target, float duration)
    {
        if (!float.IsFinite(start) || !float.IsFinite(target) || !float.IsFinite(duration))
            throw new InvalidDataException("VATS camera transition inputs are invalid.");
        _duration = _remaining = duration; _target = target;
        float accumulated = 0, increment = .02f;
        for (var i = 0; i < 50; i++)
        {
            _samples[i] = start + accumulated / 99 * (target - start);
            _samples[99 - i] = start + (float)((99d - accumulated) / 99) * (target - start);
            accumulated += increment; increment += .04f;
        }
    }

    internal float Advance(float delta)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new InvalidDataException("VATS camera clock delta is invalid.");
        _remaining -= delta;
        return Current;
    }

    internal float Current
    {
        get
        {
            if (_remaining <= 0) return _target;
            var elapsed = _duration - _remaining;
            var progress = elapsed / _duration;
            var index = (int)MathF.Floor((float)((double)progress * 99));
            if (index >= 99) return _samples[99];
            if (index < 0) throw new InvalidDataException("VATS camera transition moved before its admission.");
            // The source interpolates the adjacent samples using global
            // elapsed/duration; this is not a fractional-bin interpolation.
            return _samples[index] + progress * (_samples[index + 1] - _samples[index]);
        }
    }
}
