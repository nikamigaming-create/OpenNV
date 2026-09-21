using System.Numerics;
using OpenNV.Runtime.World.Cells;

internal static class WindForceProbe
{
    internal static void Run()
    {
        var step = 1d / 60;
        Require(FalloutWindForce.Sample(0, 1, step, 1, 1) == Vector3.Zero, "Calm weather applied wind.");
        Require(FalloutWindForce.Sample(1, 1, 0, 1, 1) == Vector3.Zero, "Stopped simulation applied wind.");
        Require(FalloutWindForce.Sample(1, 0, step, 0, .5f) == Vector3.Zero, "Negative gust reversed the wind.");
        var full = FalloutWindForce.Sample(1, 0, step, 1, .5f);
        Require(Vector3.Distance(full, new(0, 250, 0)) < 1e-5, "Wind force cap or source axis changed.");
        var light = FalloutWindForce.Sample(50 / 255f, 0, step, .5f, .5f);
        Require(MathF.Abs(light.Y - 12.254902f) < 1e-5, "Weather normalization or gust bias changed.");
        Require(Vector3.Distance(FalloutWindForce.Sample(1, 0, 1d / 30, 1, .5f), full * 2) < 1e-5,
            "Wind step multiplier changed.");
        var left = FalloutWindForce.Sample(1, 0, step, 1, 0);
        var right = FalloutWindForce.Sample(1, 0, step, 1, 1);
        Require(left.X > 0 && right.X < 0 && MathF.Abs(left.X + right.X) < 1e-5 && left.Z == 0 && right.Z == 0,
            "Wind direction spread left the horizontal plane.");
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f })
            Reject(() => FalloutWindForce.Sample(invalid, 0, step, .5f, .5f));
        Reject(() => FalloutWindForce.Sample(1, float.NaN, step, .5f, .5f));
        Reject(() => FalloutWindForce.Sample(1, 0, double.NaN, .5f, .5f));
        Console.WriteLine("OPENNV_WIND_FORCE_CONTRACT_PASS calm=true cap=true normalizedWeather=true horizontalGusts=true simulationStep=true invalidRefused=true");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid wind force was admitted.");
    }
}
