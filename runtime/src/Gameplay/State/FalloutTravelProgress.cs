using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutTravelProgress(FalloutFormKey Cell, float[] Location, bool Complete,
    string? NavigationSha256 = null, float[]? RouteTarget = null, float[][]? RouteWaypoints = null,
    int RouteCursor = 0, FalloutTravelRouteFailure? RouteFailure = null)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Cell.OwnerPlugin) || Cell.ObjectId == 0 ||
            Location is not { Length: 3 } || Location.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("Saved Travel location is invalid.");
        if (NavigationSha256 is null)
        {
            if (RouteTarget is not null || RouteWaypoints is not null || RouteCursor != 0 || RouteFailure is not null)
                throw new InvalidDataException("Saved Travel route has no navigation source identity.");
            return;
        }
        if (NavigationSha256 is not { Length: 64 } || !NavigationSha256.All(Uri.IsHexDigit) ||
            RouteTarget is not { Length: 3 } || RouteTarget.Any(value => !float.IsFinite(value)) ||
            RouteWaypoints is null || RouteCursor < 0 || RouteCursor > RouteWaypoints.Length ||
            RouteWaypoints.Any(point => point is not { Length: 3 } || point.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException("Saved Travel route, cursor or navigation source identity is invalid.");
        if (RouteFailure is { } failure)
        {
            failure.Validate();
            if (Complete || RouteWaypoints.Length != 0 || RouteCursor != 0)
                throw new InvalidDataException("A failed Travel search cannot retain a successful route or arrival.");
        }
    }
}

internal sealed record FalloutTravelRouteFailure(string Error, int Failures, double RetrySeconds)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Error) || Failures is < 1 or > 4 ||
            !double.IsFinite(RetrySeconds) || RetrySeconds is < 0 or > 2)
            throw new InvalidDataException("Saved Travel search failure or retry countdown is invalid.");
    }
}
