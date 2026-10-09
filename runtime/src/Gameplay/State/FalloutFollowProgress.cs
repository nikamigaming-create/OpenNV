using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

// Only returned native path work is representable here. An iterator or an
// entered door callback cannot be encoded as an already observed route.
internal sealed record FalloutFollowProgress(FalloutFormKey Target, string NavigationSha256,
    float[] RouteTarget, float[][] RouteWaypoints, int RouteCursor, double RetrySeconds, double StallSeconds,
    float? WaypointDistance, string? RouteError, int RouteFailures, FalloutFollowElection? Election = null)
{
    internal void Validate()
    {
        Election?.Validate();
        if (string.IsNullOrWhiteSpace(Target.OwnerPlugin) || Target.ObjectId == 0 ||
            NavigationSha256 is not { Length: 64 } || !NavigationSha256.All(Uri.IsHexDigit) ||
            RouteTarget is not { Length: 3 } || RouteTarget.Any(value => !float.IsFinite(value)) ||
            RouteWaypoints is null || RouteCursor < 0 || RouteCursor > RouteWaypoints.Length ||
            RouteWaypoints.Any(point => point is not { Length: 3 } || point.Any(value => !float.IsFinite(value))) ||
            !double.IsFinite(RetrySeconds) || !double.IsFinite(StallSeconds) || StallSeconds < 0 ||
            WaypointDistance is { } distance && (!float.IsFinite(distance) || distance < 0) ||
            RouteFailures is < 0 or > 4 || (RouteError is null) != (RouteFailures == 0) ||
            RouteError is not null && string.IsNullOrWhiteSpace(RouteError))
            throw new InvalidDataException("Saved Follow target, returned route or exact retry clock is invalid.");
    }

    internal FalloutFollowProgress Copy() => this with
    {
        RouteTarget = (float[])RouteTarget.Clone(),
        RouteWaypoints = RouteWaypoints.Select(point => (float[])point.Clone()).ToArray()
    };
}

internal sealed record FalloutFollowElection(double PollRemaining, FalloutScheduleTime? ScheduleTime,
    long QuestRevision, long? ActivityRevision, bool EvaluateRequested, FalloutActorActivitySnapshot Activity,
    long EventRevision, string? LastEvent, FalloutFormKey? LastPackage)
{
    internal void Validate()
    {
        Activity.Validate();
        if (!double.IsFinite(PollRemaining) || QuestRevision < -1 || QuestRevision == long.MaxValue ||
            ActivityRevision is < -1 or long.MaxValue ||
            EventRevision < 0 || EventRevision == long.MaxValue ||
            (EventRevision == 0 ? LastEvent is not null || LastPackage is not null :
                LastEvent is not ("POBA" or "POCA" or "POEA") || LastPackage is not { ObjectId: > 0 }) ||
            ScheduleTime is { } time && (!float.IsFinite(time.Hour) || time.Month < 0 || time.Date <= 0 || time.Weekday is < 0 or > 6))
            throw new InvalidDataException("Saved Follow election clock or source observation is invalid.");
    }

    internal void RestoreHistory(FalloutPackageEvents lifecycle)
    {
        Validate();
        if (lifecycle.Revision == 0 && lifecycle.LastEvent is null && lifecycle.LastPackage is null)
            lifecycle.RestoreHistory(EventRevision, LastEvent, LastPackage);
        else if (lifecycle.Revision != EventRevision || lifecycle.LastEvent != LastEvent || lifecycle.LastPackage != LastPackage)
            throw new InvalidDataException("Cold Follow differs from the already bound original event history.");
    }
}
