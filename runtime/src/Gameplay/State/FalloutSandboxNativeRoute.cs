namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSandboxNativeRoute(string NavigationSha256, float[] Target, float[][] Waypoints,
    int Cursor, double RetrySeconds, double StallSeconds, float? WaypointDistance, string? Error, int Failures)
{
    internal void Validate()
    {
        if (NavigationSha256 is not { Length: 64 } || !NavigationSha256.All(Uri.IsHexDigit) ||
            Target is not { Length: 3 } || Target.Any(value => !float.IsFinite(value)) ||
            Waypoints is null || Waypoints.Any(p => p is not { Length: 3 } || p.Any(value => !float.IsFinite(value))) ||
            Cursor < 0 || Cursor > Waypoints.Length || !double.IsFinite(RetrySeconds) ||
            !double.IsFinite(StallSeconds) || StallSeconds < 0 ||
            WaypointDistance is { } distance && (!float.IsFinite(distance) || distance < 0) ||
            Failures is < 0 or > 4 || (Error is null) != (Failures == 0) || Error is not null && string.IsNullOrWhiteSpace(Error))
            throw new InvalidDataException("Sandbox saved native route or fractional retry clock is invalid.");
    }
    internal FalloutSandboxNativeRoute Copy() => this with
    { Target = (float[])Target.Clone(), Waypoints = Waypoints.Select(point => (float[])point.Clone()).ToArray() };
}
