using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    // Two resident declarations cover flat first/third person and XR. A live
    // settings or ammunition/range change replaces a shape, never its geometry
    // while a query is using it. No per-shot convex cooking is needed on hits.
    private readonly Dictionary<(float Range, float Degrees), ConvexPolygonShape3D> _autoAimShapes = [];
    private readonly Queue<(float Range, float Degrees)> _autoAimShapeOrder = [];
    private int _autoAimShapeCreations;

    internal ConvexPolygonShape3D AutoAimShape(float range, float degrees)
    {
        if (!float.IsFinite(range) || range <= 0 || !float.IsFinite(degrees) || degrees <= 0 || degrees >= 90)
            throw new InvalidDataException("Auto-aim volume requires finite positive range and an acute half-angle.");
        var key = (range, degrees);
        if (_autoAimShapes.TryGetValue(key, out var shape)) return shape;
        const int sides = 16;
        const float startRadius = .2f;
        var endRadius = startRadius + range * MathF.Tan(Mathf.DegToRad(degrees)) / MathF.Cos(MathF.PI / sides);
        var points = new Vector3[sides * 2];
        for (var index = 0; index < sides; index++)
        {
            var angle = index * MathF.Tau / sides;
            points[index] = new(startRadius * MathF.Cos(angle), startRadius * MathF.Sin(angle), 0);
            points[index + sides] = new(endRadius * MathF.Cos(angle), endRadius * MathF.Sin(angle), -range);
        }
        shape = new() { Points = points };
        if (_autoAimShapes.Count == 2)
        {
            var oldest = _autoAimShapeOrder.Dequeue();
            _autoAimShapes.Remove(oldest, out var retired); retired!.Dispose();
        }
        _autoAimShapes.Add(key, shape); _autoAimShapeOrder.Enqueue(key); _autoAimShapeCreations++;
        return shape;
    }

    private void PrepareAutoAimShapes(FalloutWeaponShot shot)
    {
        var records = _presentationRecords!;
        var maximum = FalloutGameSettingFloats.Read(records, "fAutoAimMaxDistance") * UnitsToMeters;
        if (!float.IsFinite(maximum) || maximum < 0) throw new InvalidDataException("Auto-aim distance is invalid.");
        var range = MathF.Min(shot.Projectile.Range * UnitsToMeters, maximum);
        foreach (var name in new[] { "fAutoAimMaxDegrees", "fAutoAimMaxDegrees3rdPerson" })
        {
            var degrees = FalloutGameSettingFloats.Read(records, name);
            if (!float.IsFinite(degrees) || degrees < 0 || degrees >= 90) throw new InvalidDataException("Auto-aim angle is invalid.");
            if (range > 0 && degrees > 0) _ = AutoAimShape(range, degrees);
        }
    }

    private void ReleaseAutoAimShapes()
    {
        foreach (var shape in _autoAimShapes.Values) shape.Dispose();
        _autoAimShapes.Clear();
        _autoAimShapeOrder.Clear();
    }
}
