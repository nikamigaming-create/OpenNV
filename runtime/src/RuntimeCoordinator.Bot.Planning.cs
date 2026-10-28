using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.Bots;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using NumericVector = System.Numerics.Vector3;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private sealed class NativeBotRouteRequest(RuntimeNativePlayer player, FalloutCellScene scene,
        RuntimeNativeReferenceEvents events, NumericVector start, NumericVector end, NumericVector target,
        float projection, float distance, string identity)
    {
        internal RuntimeNativePlayer Player { get; } = player;
        internal FalloutCellScene Scene { get; } = scene;
        internal RuntimeNativeReferenceEvents Events { get; } = events;
        internal Basis BodyBasis { get; } = player.GlobalBasis;
        internal NumericVector Start { get; } = start;
        internal NumericVector End { get; } = end;
        internal NumericVector Target { get; } = target;
        internal float Projection { get; } = projection;
        internal float Distance { get; } = distance;
        internal string Identity { get; } = identity;
        internal IEnumerator<IReadOnlyList<Vector3>?>? Search;
        internal IReadOnlyList<Vector3> SourcePath = [];
        internal Vector3[] WorldPath = [];
        internal NativeNavigationProbe? Probe;
        internal NativeNavigationIntent? Intent;
        internal Func<Vector3, bool>? Arrival;
        internal Func<Vector3, int, Func<Vector3, bool>>? PrefixArrival;
        internal Func<Vector3, bool>? ProjectionFloor;
        internal Vector3 LocalTarget;
        internal float ArrivalRadius, Spacing, StandardSpacing, RefinedSpacing;
        internal bool WideGridAttempt;
        internal string? WideGridError;
        internal int Resume;
        internal string? CoarseError;
        internal NativeNavigationWorkSnapshot? Work;
        internal double SourceMilliseconds, MaximumSliceMilliseconds;
        internal BotNavigationProjection? PriorProjection;
        internal float PrefixLength = 8f;
        internal bool ExtendedPrefix;
        internal bool PartialRoute;
        internal bool RetainedPrefix;
        internal Vector3? PartialFloorStart;
    }

    private sealed record NativeBotProjectionArrival(RuntimeNativePlayer Player, FalloutCellScene Scene,
        RuntimeNativeReferenceEvents Events, NumericVector Target, Vector3 Endpoint,
        BotNavigationProjection Projection, string SourceSha256, Func<Vector3, bool> Contains);

    private sealed record NativeBotPartialContinuation(NativeBotRouteRequest Request, IReadOnlyList<Vector3> Path, string SourceSha256);

    private sealed record NativeBotRoutePreference(RuntimeNativePlayer Player, FalloutFormKey? Worldspace, FalloutFormKey? Interior,
        NumericVector Target, string SourceSha256, List<Vector3> PenalizedPortals);

    private NativeBotRoutePreference? _botRoutePreference;
    private NativeBotRouteRequest? _botRouteRequest;
    private NativeBotProjectionArrival? _botProjectionArrival;
    private NativeBotPartialContinuation? _botPartialContinuation;
    private object? _botLastRoutePlanning;
    private BotPlanningProgress? NativeBotPlanningProgress()
    {
        var work = NativeCapsuleNavigation.Work(_botRouteRequest?.Search);
        return work is null ? null : new(work.Search, work.IteratorSteps);
    }
    private object? NativeBotPlanningState => _botRouteRequest is { } current ?
        NativeBotPlanningObservation(current, current.Search is null ? current.Work?.State ?? "source-pending" : "pending") : _botLastRoutePlanning;
    private object NativeBotPlanningObservation(NativeBotRouteRequest request, string status) => new
    {
        status,
        nativeOwner = GodotObject.IsInstanceValid(request.Player) ? request.Player.GetInstanceId() : (ulong?)null,
        ownerBuild = typeof(NativeCapsuleNavigation).Module.ModuleVersionId,
        source = request.Identity,
        sourceSha256 = _botNavigation?.SourceSha256,
        sourceDoorAssociationErrors = _botNavigation?.DoorAssociationErrors,
        requestedStart = new[] { request.Start.X, request.Start.Y, request.Start.Z },
        requestedEnd = new[] { request.End.X, request.End.Y, request.End.Z },
        referenceTarget = new[] { request.Target.X, request.Target.Y, request.Target.Z },
        localTarget = new[] { request.LocalTarget.X, request.LocalTarget.Y, request.LocalTarget.Z },
        sourcePoints = request.WorldPath.Length,
        request.Resume,
        request.RetainedPrefix,
        partialFloorStart = request.PartialFloorStart is { } anchor ? new[] { anchor.X, anchor.Y, anchor.Z } : null,
        request.Spacing,
        request.WideGridAttempt,
        request.WideGridError,
        request.ArrivalRadius,
        request.SourceMilliseconds,
        request.MaximumSliceMilliseconds,
        request.CoarseError,
        work = NativeCapsuleNavigation.Work(request.Search) ?? request.Work
    };

    private void CancelNativeBotRoute() => CancelNativeBotRoute("cancelled");

    private void CancelNativeBotRoute(string reason)
    {
        if (_botRouteRequest is { } request)
        {
            request.Search?.Dispose();
            request.Work = NativeCapsuleNavigation.Work(request.Search) ?? request.Work;
            _botLastRoutePlanning = NativeBotPlanningObservation(request, reason);
        }
        _botRouteRequest = null;
    }

    // A null response means planning is pending. The steering owner releases
    // input and polls the same request; it never runs physics on a worker.
    private BotNavigationRoute? AdvanceNativeReferenceApproachRoute(NumericVector start, NumericVector end,
        NumericVector target, float projectionRadius, float distance)
    {
        var player = _nativePlayer ?? throw new InvalidOperationException("No active navigation player.");
        var scene = _nativeActiveCell ?? throw new InvalidOperationException("No active navigation scene.");
        var events = _nativeReferenceEvents ?? throw new InvalidOperationException("No reference event owner for navigation.");
        if (_botRouteRequest is { } old && (old.Player != player || old.Scene != scene || old.Events != events ||
            !old.BodyBasis.IsEqualApprox(player.GlobalBasis) || old.Start != start || old.End != end || old.Target != target ||
            old.Projection != projectionRadius || old.Distance != distance)) CancelNativeBotRoute("request-or-native-owner-changed");
        if (_botRouteRequest is null)
        {
            var started = Stopwatch.GetTimestamp();
            _botLastRoutePlanning = null;
            var request = _botRouteRequest = new(player, scene, events, start, end, target, projectionRadius, distance, EnsureNativeBotNavigation());
            BeginNativeBotRouteSearch(request);
            request.SourceMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return null;
        }
        var current = _botRouteRequest;
        var slice = Stopwatch.GetTimestamp();
        try
        {
            if (!NativeCapsuleNavigation.Advance(current.Search!, out var path)) return null;
            current.Search!.Dispose(); current.Work = NativeCapsuleNavigation.Work(current.Search); current.Search = null;
            GD.Print($"OPENNV_BOT_CAPSULE_ROUTE source={current.Identity} requested={current.End} projected={current.WorldPath[^1]} projectionRadius={current.Projection} accepted={path![^1]} " +
                $"scope={Scope(current)} target={current.LocalTarget} sourceSha256={_botNavigation!.SourceSha256} " +
                $"spacing={current.Spacing} arrivalRadius={current.ArrivalRadius} sourceExclusions=0 " +
                $"partial={current.PartialRoute} coarseError={current.CoarseError ?? "none"} sourceMs={current.SourceMilliseconds:F3} maxSliceMs={current.MaximumSliceMilliseconds:F3}");
            var route = BotRoute(current, path!);
            if (!current.PartialRoute) return route;
            _botPartialContinuation = new(current, path!, _botNavigation!.SourceSha256);
            return route with { ReachesProjectedEndpoint = false, ObserveArrival = null };
        }
        catch (InvalidOperationException error)
        {
            current.Search?.Dispose(); current.Work = NativeCapsuleNavigation.Work(current.Search) ?? current.Work; current.Search = null;
            GD.Print($"OPENNV_BOT_CAPSULE_REJECT ownerBuild={typeof(NativeCapsuleNavigation).Module.ModuleVersionId} " +
                $"source={current.Identity} sourceSha256={_botNavigation!.SourceSha256} start={current.Start} " +
                $"requested={current.End} projected={current.WorldPath[^1]} projectionRadius={current.Projection} referenceTarget={current.Target} " +
                $"scope={Scope(current)} localTarget={current.LocalTarget} arrivalRadius={current.ArrivalRadius} " +
                $"basis={current.BodyBasis} stepHeight={_configuration.Player.StepHeightMeters} spacing={current.Spacing} " +
                $"resume={current.Resume} sourceExclusions=0 nativeError={error.Message}");
            if (current.WideGridAttempt)
            {
                current.WideGridAttempt = false;
                current.WideGridError = error.Message;
                current.Spacing = current.StandardSpacing;
                current.Search = NativeCapsuleNavigation.Search(player, Native(start), current.LocalTarget,
                    _configuration.Player.StepHeightMeters, current.Spacing, NativeCollisionResident,
                    probe: current.Probe, targetRadius: current.ArrivalRadius, corridor: current.Intent!.Corridor,
                    arrival: NativeBotSearchArrival(current), minimumSearchRadius: current.PrefixLength).GetEnumerator();
                return null;
            }
            if (current.CoarseError is null && current.RefinedSpacing < current.Spacing)
            {
                // A retained partial endpoint has already consumed a search
                // on this source corridor. If its next coarse search cannot
                // advance, prefer a distinct authored corridor before another
                // long refinement. Native collision admission is unchanged;
                // an absent alternate still takes the original fallback.
                if (current.RetainedPrefix && TryAlternateNativeBotCorridor(current, Native(start))) return null;
                current.CoarseError = error.Message; current.Spacing = current.RefinedSpacing;
                var corridorRadius = current.ArrivalRadius > 0 ? current.ArrivalRadius :
                    (!current.Intent!.ReferenceApproach && current.Intent.Projection is null ? 0.45f : 0f);
                var refinedArrival = current.Arrival;
                if (refinedArrival is null && corridorRadius > 0 && current.ArrivalRadius == 0 &&
                    NativeBotSearchArrival(current) is { } prefixArrival)
                {
                    refinedArrival = prefixArrival;
                    corridorRadius = 0;
                }
                // A source corridor may yield a physically supported frontier
                // after its refined search, not only after the longest retry.
                // It remains a partial path to the retained source target and
                // must not be reported as source or reference arrival.
                var allowPartial = !current.Intent!.ReferenceApproach && current.Intent.Projection is null;
                if (allowPartial) current.PartialFloorStart ??= Native(start);
                current.Search = NativeCapsuleNavigation.Search(player, Native(start), current.LocalTarget,
                    _configuration.Player.StepHeightMeters, current.Spacing, NativeCollisionResident, maximumNodes: 3600, probe: current.Probe,
                    targetRadius: corridorRadius, corridor: current.Intent!.Corridor, arrival: refinedArrival,
                    partialResult: allowPartial ? () => current.PartialRoute = true : null,
                    minimumSearchRadius: current.ExtendedPrefix ? current.PrefixLength : 0,
                    partialCandidate: point => NativeBotPartialFloorEligible(current.PartialFloorStart ?? Native(start), current.LocalTarget, point)).GetEnumerator();
                return null;
            }
            // A retained prefix starts in the middle of the original source
            // corridor. Keep its target while expanding the native search;
            // re-slicing the full corridor can select traversed portals.
            if (current.RetainedPrefix && current.Intent is not null && !current.Intent.ReferenceApproach &&
                current.Intent.Projection is null && current.PrefixLength < 32)
            {
                current.PrefixLength = current.PrefixLength < 16 ? 16 : 32;
                current.ExtendedPrefix = true;
                current.WideGridAttempt = true;
                current.WideGridError = null;
                current.Spacing = Math.Max(.9f, current.StandardSpacing);
                current.CoarseError = null;
                current.Search = NativeCapsuleNavigation.Search(player, Native(start), current.LocalTarget,
                    _configuration.Player.StepHeightMeters, current.Spacing, NativeCollisionResident,
                    probe: current.Probe, targetRadius: current.ArrivalRadius, corridor: current.Intent.Corridor,
                    arrival: NativeBotSearchArrival(current), minimumSearchRadius: current.PrefixLength).GetEnumerator();
                return null;
            }
            // Non-retained requests can still choose shorter source prefixes.
            // Alternate source corridors are considered after these retries.
            if (!current.RetainedPrefix && current.Intent is not null && !current.Intent.ReferenceApproach && current.Intent.Projection is null)
            {
                var origin = Native(start);
                while (NextBotPrefixLength(current.PrefixLength, current.ExtendedPrefix) is { } retry)
                {
                    var nextPrefix = retry.Length;
                    current.PrefixLength = nextPrefix;
                    current.ExtendedPrefix = retry.Extended;
                    var fallbackIntent = NativeCapsuleNavigation.Intent(origin, current.WorldPath, Native(current.Target), current.Distance,
                        length: nextPrefix,
                        projection: current.Projection > 0 ? new(Native(current.End), current.WorldPath[^1], current.Projection) : null);
                    if ((!retry.Extended || fallbackIntent.Projection is null && fallbackIntent.Resume < current.WorldPath.Length) &&
                        origin.DistanceTo(fallbackIntent.Target) > 0.2f && fallbackIntent.Target != current.LocalTarget)
                    {
                        current.Intent = fallbackIntent;
                        current.LocalTarget = fallbackIntent.Target;
                        current.PartialFloorStart = null;
                        current.Resume = fallbackIntent.Resume;
                        current.ArrivalRadius = fallbackIntent.ArrivalRadius;
                        IReadOnlyList<Vector3> corridor = fallbackIntent.ReferenceApproach ? [fallbackIntent.Target] : fallbackIntent.Corridor;
                        current.Probe = new(NativeCapsuleNavigation.FirstCorridorContact(current.Player, origin, corridor), current.Events.CollisionReference);
                        current.StandardSpacing = Math.Max(.3f, _configuration.Player.CapsuleRadiusMeters);
                        current.RefinedSpacing = NativeCapsuleNavigation.RefinementSpacing(current.StandardSpacing,
                            _configuration.Player.CapsuleRadiusMeters, current.Player.GlobalBasis.X.Length());
                        current.WideGridAttempt = current.ExtendedPrefix;
                        current.WideGridError = null;
                        current.Spacing = current.WideGridAttempt ? Math.Max(.9f, current.StandardSpacing) : current.StandardSpacing;
                        current.CoarseError = null;
                        current.Search = NativeCapsuleNavigation.Search(player, origin, current.LocalTarget,
                            _configuration.Player.StepHeightMeters, current.Spacing, NativeCollisionResident,
                            probe: current.Probe, targetRadius: current.ArrivalRadius, corridor: fallbackIntent.Corridor, arrival: NativeBotSearchArrival(current),
                            minimumSearchRadius: current.ExtendedPrefix ? current.PrefixLength : 0).GetEnumerator();
                        return null;
                    }
                }
            }
            if (current.Probe is { RejectedContact: { Reference: { } reference } } probe &&
                events.PlayerRouteDoor(reference) is { Reference: not null } door)
            {
                if (door.Error is not null) throw new InvalidOperationException($"Route door: {door.Error}. Native query: {error.Message}", error);
                // Door identity includes stationary frames and sibling NIF
                // bodies. A settled open door cannot need another activation;
                // its contact stays in the native failure evidence below.
                if (door.RequiresInteraction)
                {
                    if (probe.Approach.Length == 0) throw new InvalidOperationException($"Source route door has no supported approach. {error.Message}", error);
                    return BotRoute(current, probe.Approach, retainProjection: false) with { RequiredDoor = reference.ToString() };
                }
            }
            if (TryAlternateNativeBotCorridor(current, Native(start))) return null;
            // A failed point/lattice/approach query does not attest the full
            // traversable width of any source portal. Keep NAVM unchanged and
            // retain the actual native error, including support-only failures.
            throw new InvalidOperationException($"No capsule-supported {Scope(current)}; source portals were not excluded. " +
                $"Coarse: {current.CoarseError ?? error.Message} Refined: {error.Message}", error);
        }
        finally
        {
            current.MaximumSliceMilliseconds = Math.Max(current.MaximumSliceMilliseconds, Stopwatch.GetElapsedTime(slice).TotalMilliseconds);
            current.Work = NativeCapsuleNavigation.Work(current.Search) ?? current.Work;
            _botLastRoutePlanning = NativeBotPlanningObservation(current, current.Search is null ? "settled" : "pending");
        }
    }

    internal static (float Length, bool Extended)? NextBotPrefixLength(float length, bool extended)
    {
        if (!float.IsFinite(length) || length < 2) throw new ArgumentOutOfRangeException(nameof(length));
        if (!extended && length > 2) return (Math.Max(2, length * .5f), false);
        if (!extended) return (16, true);
        return length < 32 ? (32, true) : null;
    }

    private void BeginNativeBotRouteSearch(NativeBotRouteRequest request)
    {
        var units = _configuration.World.GameUnitsToMeters;
        Vector3 Source(NumericVector point) => new Vector3(point.X, -point.Z, point.Y) / units;
        Vector3 World(Vector3 point) => new Vector3(point.X, point.Z, -point.Y) * units;
        var origin = Native(request.Start);
        NativeNavigationIntent? intent = null;
        var partial = _botPartialContinuation;
        if (partial is not null && partial.Request.Player == request.Player &&
            (partial.Request.Scene == request.Scene && partial.Request.Events == request.Events ||
             partial.Request.Scene.Cell.Worldspace is { } world && request.Scene.Cell.Worldspace == world) &&
            partial.Request.Target == request.Target &&
            partial.SourceSha256 == _botNavigation!.SourceSha256 &&
            NativeBotOnPartialPath(origin, Native(partial.Request.Start), partial.Path,
                allowStart: partial.Request.Scene.Cell.FormKey != request.Scene.Cell.FormKey))
        {
            var retained = partial.Request;
            request.SourcePath = retained.SourcePath;
            request.WorldPath = retained.WorldPath;
            request.PrefixArrival = retained.PrefixArrival;
            request.PrefixLength = retained.PrefixLength;
            request.ExtendedPrefix = retained.ExtendedPrefix;
            request.PartialFloorStart = retained.PartialFloorStart;
            request.RetainedPrefix = true;
            // Continue toward the same authored prefix from the observed
            // endpoint. Every new edge is still checked against live native
            // collision and support, and no source arrival is inferred.
            intent = new(retained.LocalTarget, retained.Resume, 0, false, [retained.LocalTarget]);
            GD.Print($"OPENNV_BOT_RETAINED_PREFIX start={origin} partialEndpoint={partial.Path[^1]} " +
                $"target={retained.LocalTarget} resume={retained.Resume} sourceSha256={partial.SourceSha256}");
        }
        else _botPartialContinuation = null;
        if (intent is null && _botProjectionArrival is { } prior && prior.Player == request.Player && prior.Scene == request.Scene &&
            prior.Events == request.Events && prior.Target == request.Target &&
            prior.SourceSha256 == _botNavigation!.SourceSha256)
        {
            intent = NativeCapsuleNavigation.ApproachAfterProjection(origin, prior.Endpoint, Native(request.Target), request.Distance, prior.Contains);
            if (intent is not null)
            {
                request.WorldPath = [Native(prior.Projection.Selected)];
                request.PriorProjection = prior.Projection;
            }
        }
        if (intent is null)
        {
            _botProjectionArrival = null;
            var preference = NativeBotPreference(request);
            var (path, prefix) = NativeBotSourcePath(request, preference?.PenalizedPortals);
            request.SourcePath = path;
            request.PrefixArrival = (target, resume) =>
            {
                var source = prefix(Source(new(target.X, target.Y, target.Z)), resume,
                    .01f / units, NativeCapsuleNavigation.SourceHeightTolerance / units);
                return point => source(Source(new(point.X, point.Y, point.Z)));
            };
            request.WorldPath = request.SourcePath.Select(World).ToArray();
            intent = NativeCapsuleNavigation.Intent(origin, request.WorldPath, Native(request.Target), request.Distance,
                length: request.PrefixLength,
                projection: request.Projection > 0 ? new(Native(request.End), request.WorldPath[^1], request.Projection) : null);
        }
        request.Intent = intent;
        request.LocalTarget = intent.Target; request.Resume = intent.Resume; request.ArrivalRadius = intent.ArrivalRadius;
        IReadOnlyList<Vector3> corridor = intent.ReferenceApproach ? [intent.Target] : intent.Corridor;
        request.Probe = new(NativeCapsuleNavigation.FirstCorridorContact(request.Player, origin, corridor), request.Events.CollisionReference);
        request.StandardSpacing = Math.Max(.3f, _configuration.Player.CapsuleRadiusMeters);
        request.RefinedSpacing = NativeCapsuleNavigation.RefinementSpacing(request.StandardSpacing,
            _configuration.Player.CapsuleRadiusMeters, request.Player.GlobalBasis.X.Length());
        request.WideGridAttempt = request.ExtendedPrefix;
        request.Spacing = request.WideGridAttempt ? Math.Max(.9f, request.StandardSpacing) : request.StandardSpacing;
        if (intent.Projection is not null)
        {
            var region = _botNavigation!.ArrivalRegion(Source(request.Start), Source(request.End), request.SourcePath[^1],
                request.Projection / units, request.Player.SafeMargin * 8 / units, NativeCapsuleNavigation.SourceHeightTolerance / units,
                out var sourceFloor);
            request.Arrival = point => region(new Vector3(point.X, -point.Z, point.Y) / units);
            request.ProjectionFloor = point => sourceFloor(new Vector3(point.X, -point.Z, point.Y) / units);
        }
        request.CoarseError = null;
        request.Search = NativeCapsuleNavigation.Search(request.Player, origin, request.LocalTarget,
            _configuration.Player.StepHeightMeters, request.Spacing, NativeCollisionResident, probe: request.Probe,
            targetRadius: request.ArrivalRadius, corridor: intent.Corridor, arrival: NativeBotSearchArrival(request),
            minimumSearchRadius: request.ExtendedPrefix ? request.PrefixLength : 0).GetEnumerator();
    }

    // An authored portal is a crossing region, not its midpoint. Use the same
    // source triangle predicate already used by refinement on the first search
    // too; native sweeps, root support and floor tolerance still gate every path.
    private static Func<Vector3, bool>? NativeBotSearchArrival(NativeBotRouteRequest request)
    {
        if (request.Arrival is not null) return request.Arrival;
        if (request.Intent is null || request.Intent.ReferenceApproach || request.Intent.Projection is not null ||
            request.ArrivalRadius != 0 || request.PrefixArrival is null ||
            !NativeCapsuleNavigation.CanUsePrefixRegion(request.LocalTarget, request.Resume, request.WorldPath)) return null;
        var sourceRegion = request.PrefixArrival(request.LocalTarget, request.Resume);
        var target = request.LocalTarget;
        var tolerance = request.Player.SafeMargin * 8;
        // The original point acceptance remains valid even when the source
        // triangle predicate rejects a boundary point through rounding. Both
        // paths still require the complete native edge and floor checks.
        return point => NativeBotPrefixArrivalContains(point, target, tolerance, sourceRegion);
    }

    internal static bool NativeBotPrefixArrivalContains(Vector3 point, Vector3 target, float tolerance,
        Func<Vector3, bool> sourceRegion) => sourceRegion(point) ||
        new Vector2(point.X - target.X, point.Z - target.Z).Length() <= tolerance &&
        Math.Abs(point.Y - target.Y) < NativeCapsuleNavigation.SourceHeightTolerance;

    private NativeBotRoutePreference? NativeBotPreference(NativeBotRouteRequest request)
    {
        var current = _botRoutePreference;
        if (current is null || current.Player != request.Player || current.Worldspace != request.Scene.Cell.Worldspace ||
            current.Interior != (request.Scene.Cell.Worldspace is null ? request.Scene.Cell.FormKey : null) ||
            current.Target != request.Target || current.SourceSha256 != _botNavigation!.SourceSha256)
            return _botRoutePreference = null;
        return current;
    }

    private (IReadOnlyList<Vector3> Path, Func<Vector3, int, float, float, Func<Vector3, bool>> Prefix)
        NativeBotSourcePath(NativeBotRouteRequest request, IReadOnlyList<Vector3>? penalties)
    {
        var units = _configuration.World.GameUnitsToMeters;
        Vector3 Source(NumericVector p) => new Vector3(p.X, -p.Z, p.Y) / units;
        float Penalty(Vector3 point) => penalties!.Any(portal => portal.DistanceSquaredTo(point) < .0001f)
            ? 500f / units : 0;
        Func<Vector3, float>? cost = penalties is { Count: > 0 } ? Penalty : null;
        try
        {
            var path = _botNavigation!.FindPath(Source(request.Start), Source(request.End), out var prefix,
                destinationRadiusGameUnits: request.Projection / units, portalPenalty: cost);
            return (path, prefix);
        }
        catch (InvalidOperationException) when (request.Distance > 0 || request.Projection > 0)
        {
            var path = _botNavigation!.FindPath(Source(request.Start), Source(request.End), out var prefix,
                portalPenalty: cost);
            return (path, prefix);
        }
    }

    private bool TryAlternateNativeBotCorridor(NativeBotRouteRequest request, Vector3 origin)
    {
        if (request.Intent is null || request.Intent.ReferenceApproach || request.SourcePath.Count < 2)
            return false;
        var preference = NativeBotPreference(request) ?? new NativeBotRoutePreference(request.Player,
            request.Scene.Cell.Worldspace, request.Scene.Cell.Worldspace is null ? request.Scene.Cell.FormKey : null,
            request.Target, _botNavigation!.SourceSha256, []);
        if (preference.PenalizedPortals.Count >= 4) return false;
        var units = _configuration.World.GameUnitsToMeters;
        var failed = new Vector3(request.LocalTarget.X, -request.LocalTarget.Z, request.LocalTarget.Y) / units;
        var choices = request.SourcePath.Take(request.SourcePath.Count - 1)
            .Where(point => !preference.PenalizedPortals.Any(old => old.DistanceSquaredTo(point) < .0001f))
            .OrderBy(point => point.DistanceSquaredTo(failed)).Take(8).ToArray();
        foreach (var portal in choices)
        {
            var penalties = preference.PenalizedPortals.Append(portal).ToArray();
            var (path, prefix) = NativeBotSourcePath(request, penalties);
            if (path.SequenceEqual(request.SourcePath)) continue;
            preference.PenalizedPortals.Add(portal);
            _botRoutePreference = preference;
            request.SourcePath = path;
            request.WorldPath = path.Select(point => new Vector3(point.X, point.Z, -point.Y) * units).ToArray();
            request.PrefixArrival = (target, resume) =>
            {
                var source = prefix(new Vector3(target.X, -target.Z, target.Y) / units, resume,
                    .01f / units, NativeCapsuleNavigation.SourceHeightTolerance / units);
                return point => source(new Vector3(point.X, -point.Z, point.Y) / units);
            };
            request.PrefixLength = 8; request.ExtendedPrefix = false; request.PartialFloorStart = null;
            request.PartialRoute = false; request.RetainedPrefix = false; _botPartialContinuation = null;
            var intent = NativeCapsuleNavigation.Intent(origin, request.WorldPath, Native(request.Target), request.Distance,
                length: request.PrefixLength,
                projection: request.Projection > 0 ? new(Native(request.End), request.WorldPath[^1], request.Projection) : null);
            request.Intent = intent; request.LocalTarget = intent.Target; request.Resume = intent.Resume;
            request.ArrivalRadius = intent.ArrivalRadius; request.Arrival = null; request.ProjectionFloor = null;
            if (intent.Projection is not null)
            {
                Vector3 Source(NumericVector p) => new Vector3(p.X, -p.Z, p.Y) / units;
                var region = _botNavigation!.ArrivalRegion(Source(request.Start), Source(request.End), path[^1],
                    request.Projection / units, request.Player.SafeMargin * 8 / units,
                    NativeCapsuleNavigation.SourceHeightTolerance / units, out var floor);
                request.Arrival = point => region(new Vector3(point.X, -point.Z, point.Y) / units);
                request.ProjectionFloor = point => floor(new Vector3(point.X, -point.Z, point.Y) / units);
            }
            IReadOnlyList<Vector3> corridor = intent.ReferenceApproach ? [intent.Target] : intent.Corridor;
            request.Probe = new(NativeCapsuleNavigation.FirstCorridorContact(request.Player, origin, corridor), request.Events.CollisionReference);
            request.StandardSpacing = Math.Max(.3f, _configuration.Player.CapsuleRadiusMeters);
            request.RefinedSpacing = NativeCapsuleNavigation.RefinementSpacing(request.StandardSpacing,
                _configuration.Player.CapsuleRadiusMeters, request.Player.GlobalBasis.X.Length());
            request.Spacing = request.StandardSpacing; request.WideGridAttempt = false;
            request.WideGridError = null; request.CoarseError = null;
            request.Search = NativeCapsuleNavigation.Search(request.Player, origin, request.LocalTarget,
                _configuration.Player.StepHeightMeters, request.Spacing, NativeCollisionResident, probe: request.Probe,
                targetRadius: request.ArrivalRadius, corridor: intent.Corridor, arrival: NativeBotSearchArrival(request)).GetEnumerator();
            GD.Print($"OPENNV_BOT_ALTERNATE_CORRIDOR preferences={preference.PenalizedPortals.Count} sourceSha256={preference.SourceSha256} sourceExclusions=0 target={request.LocalTarget}");
            return true;
        }
        return false;
    }

    // Partial routes are intermediate, not source arrival. Do not select a
    // frontier that has moved onto a substantially more distant floor than
    // the one from which this source segment started. A complete native path
    // may still descend and ascend when its destination actually requires it.
    internal static bool NativeBotPartialFloorEligible(Vector3 start, Vector3 target, Vector3 candidate)
    {
        if (!start.IsFinite() || !target.IsFinite() || !candidate.IsFinite())
            throw new InvalidDataException("Partial route floor comparison requires finite positions.");
        return Math.Abs(candidate.Y - target.Y) <= Math.Abs(start.Y - target.Y) + NativeCapsuleNavigation.SourceHeightTolerance;
    }

    internal static bool NativeBotOnPartialPath(Vector3 position, Vector3 start, IReadOnlyList<Vector3> path,
        bool allowStart = false)
    {
        if (!position.IsFinite() || !start.IsFinite() || path.Count == 0 || (!allowStart && position.DistanceTo(start) < .3f) ||
            path.Any(point => !point.IsFinite()))
            return false;
        var previous = start;
        foreach (var point in path)
        {
            var a = new Vector2(previous.X, previous.Z);
            var delta = new Vector2(point.X - previous.X, point.Z - previous.Z);
            var observed = new Vector2(position.X, position.Z);
            var t = delta.LengthSquared() == 0 ? 0 : Math.Clamp((observed - a).Dot(delta) / delta.LengthSquared(), 0, 1);
            var nearest = a + delta * t;
            var height = Mathf.Lerp(previous.Y, point.Y, t);
            if (nearest.DistanceTo(observed) <= .3f && Math.Abs(position.Y - height) < NativeCapsuleNavigation.SourceHeightTolerance)
                return true;
            previous = point;
        }
        return false;
    }

    private static Vector3 Native(NumericVector point) => new(point.X, point.Y, point.Z);
    private static string Scope(NativeBotRouteRequest request) => request.Intent!.ReferenceApproach ? "reference-approach" :
        request.Intent.Projection is not null ? "source-projection-region" : "source-corridor";
    private BotNavigationRoute BotRoute(NativeBotRouteRequest request, IReadOnlyList<Vector3> path, bool retainProjection = true)
    {
        var projected = request.WorldPath[^1];
        BotNavigationProjection? projection = request.PriorProjection;
        Action<NumericVector>? observeArrival = null;
        if (request.Intent!.Projection is { } source && request.Arrival!(path[^1]))
        {
            var units = _configuration.World.GameUnitsToMeters;
            var accepted = _botNavigation!.FindNearestPoint(new Vector3(path[^1].X, -path[^1].Z, path[^1].Y) / units);
            projected = new Vector3(accepted.X, accepted.Z, -accepted.Y) * units;
            projection = new(new(source.Requested.X, source.Requested.Y, source.Requested.Z),
                new(source.Selected.X, source.Selected.Y, source.Selected.Z), source.Radius,
                new(projected.X, projected.Y, projected.Z));
            if (retainProjection)
            {
                var completed = new NativeBotProjectionArrival(request.Player, request.Scene, request.Events,
                    request.Target, path[^1], projection, _botNavigation.SourceSha256, request.ProjectionFloor!);
                observeArrival = _ => _botProjectionArrival = completed;
            }
        }
        return new(path.Select(point => new NumericVector(point.X, point.Y, point.Z)).ToArray(), request.End,
            new(projected.X, projected.Y, projected.Z),
            request.Resume == request.WorldPath.Length, request.Identity, request.Projection,
            Refinement: new(Scope(request), new(request.LocalTarget.X, request.LocalTarget.Y, request.LocalTarget.Z),
                request.ArrivalRadius, _botNavigation!.SourceSha256, projection), ObserveArrival: observeArrival);
    }
}
