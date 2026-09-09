using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseReferenceAngles()
    {
        var quarter = Mathf.Pi / 2;
        var fixtures = new (Vector3 Angles, Vector3 Source, Vector3 Expected)[]
        {
            (new(quarter, 0, 0), Vector3.Up, Vector3.Forward),
            (new(0, quarter, 0), Vector3.Right, Vector3.Back),
            (new(0, 0, quarter), Vector3.Right, Vector3.Down),
            (new(quarter, quarter, 0), Vector3.Right, Vector3.Up),
            (new(quarter, 0, quarter), Vector3.Right, Vector3.Back),
        };
        foreach (var (angles, source, expected) in fixtures)
        {
            var basis = GamebryoCoordinate.ConvertReferenceEuler(angles, 1);
            var actual = basis * GamebryoCoordinate.ConvertVector(source);
            if (actual.DistanceTo(GamebryoCoordinate.ConvertVector(expected)) > 1e-5f ||
                Mathf.Abs(basis.Determinant() - 1) > 1e-5f)
                throw new InvalidDataException("Placed reference angle signs or composition order differ from the coordinate contract.");
        }
        var scaled = GamebryoCoordinate.ConvertReferenceEuler(new(.4f, -.8f, 1.1f), 1.7f);
        if (new[] { scaled.X.Length(), scaled.Y.Length(), scaled.Z.Length() }.Any(length => Mathf.Abs(length - 1.7f) > 1e-5f))
            throw new InvalidDataException("Reference orientation changed its source scale.");
        GD.Print("OPENNV_REFERENCE_ANGLE_PASS clockwise=true composition=XYZ mixedAxes=true sourceScale=true");
    }
}
