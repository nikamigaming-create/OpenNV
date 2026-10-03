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
        internal readonly HashSet<Vector3> BlockedPortals = [];
        internal IEnumerator<IReadOnlyList<Vector3>?>? Search;
        internal IReadOnlyList<Vector3> SourcePath = [];
        internal Vector3[] WorldPath = [];
        internal NativeNavigationProbe? Probe;
        internal Vector3 LocalTarget;
        internal float ArrivalRadius, Spacing, RefinedSpacing;
        internal int Resume, Alternatives;
        internal string? CoarseError;
        internal double SourceMilliseconds, MaximumSliceMilliseconds;
    }

    private NativeBotRouteRequest? _botRouteRequest;

    private void CancelNativeBotRoute()
    {
        _botRouteRequest?.Search?.Dispose();
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
            old.Projection != projectionRadius || old.Distance != distance)) CancelNativeBotRoute();
        if (_botRouteRequest is null)
        {
            var started = Stopwatch.GetTimestamp();
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
            current.Search!.Dispose(); current.Search = null;
            GD.Print($"OPENNV_BOT_CAPSULE_ROUTE source={current.Identity} requested={current.End} projected={current.WorldPath[^1]} " +
                $"spacing={current.Spacing} arrivalRadius={current.ArrivalRadius} alternatives={current.Alternatives} " +
                $"coarseError={current.CoarseError ?? "none"} sourceMs={current.SourceMilliseconds:F3} maxSliceMs={current.MaximumSliceMilliseconds:F3}");
            return BotRoute(current, path!);
        }
        catch (InvalidOperationException error)
        {
            current.Search?.Dispose(); current.Search = null;
            if (current.CoarseError is null && current.RefinedSpacing < current.Spacing)
            {
                current.CoarseError = error.Message; current.Spacing = current.RefinedSpacing;
                current.Search = NativeCapsuleNavigation.Search(player, Native(start), current.LocalTarget,
                    _configuration.Player.StepHeightMeters, current.Spacing, NativeCollisionResident, probe: current.Probe,
                    targetRadius: current.ArrivalRadius).GetEnumerator();
                return null;
            }
            if (current.Probe is { RejectedContact: { Reference: { } reference } } probe &&
                events.PlayerRouteDoor(reference) is { Reference: not null } door)
            {
                if (door.Error is not null) throw new InvalidOperationException(door.Error, error);
                if (door.Open && !door.Moving && !door.Pending)
                    throw new InvalidOperationException("Settled open source door still rejects the player's complete capsule corridor.", error);
                if (probe.Approach.Length == 0) throw new InvalidOperationException("Source route door has no supported approach.", error);
                return BotRoute(current, probe.Approach) with { RequiredDoor = reference.ToString() };
            }
            if (++current.Alternatives >= 8 || current.SourcePath.Count < 2)
                throw new InvalidOperationException($"No capsule-supported source corridor. Coarse: {current.CoarseError ?? error.Message} Refined: {error.Message}", error);
            // Exclusions live only for this request. A transient actor/door
            // cannot poison a later request's otherwise valid source graph.
            current.BlockedPortals.Add(current.SourcePath[Math.Min(current.Resume, current.SourcePath.Count - 2)]);
            BeginNativeBotRouteSearch(current);
            return null;
        }
        finally
        {
            current.MaximumSliceMilliseconds = Math.Max(current.MaximumSliceMilliseconds, Stopwatch.GetElapsedTime(slice).TotalMilliseconds);
        }
    }

    private void BeginNativeBotRouteSearch(NativeBotRouteRequest request)
    {
        var units = _configuration.World.GameUnitsToMeters;
        Vector3 Source(NumericVector point) => new Vector3(point.X, -point.Z, point.Y) / units;
        Vector3 World(Vector3 point) => new Vector3(point.X, point.Z, -point.Y) * units;
        request.SourcePath = _botNavigation!.FindPath(Source(request.Start), Source(request.End),
            point => !request.BlockedPortals.Contains(point), request.Projection / units);
        request.WorldPath = request.SourcePath.Select(World).ToArray();
        var origin = Native(request.Start);
        (request.LocalTarget, request.Resume) = NativeCapsuleNavigation.CorridorPrefix(origin, request.WorldPath, 8);
        request.ArrivalRadius = 0;
        if (origin.DistanceTo(Native(request.Target)) <= 8)
        {
            request.LocalTarget = Native(request.Target); request.ArrivalRadius = request.Distance;
            request.Resume = request.WorldPath.Length;
        }
        var corridor = request.WorldPath.Take(request.Resume).Append(request.LocalTarget).ToArray();
        request.Probe = new(NativeCapsuleNavigation.FirstCorridorContact(request.Player, origin, corridor), request.Events.CollisionReference);
        request.Spacing = Math.Max(.3f, _configuration.Player.CapsuleRadiusMeters);
        request.RefinedSpacing = Math.Min(request.Spacing, Math.Max(.15f,
            _configuration.Player.CapsuleRadiusMeters * request.Player.GlobalBasis.X.Length()));
        request.CoarseError = null;
        request.Search = NativeCapsuleNavigation.Search(request.Player, origin, request.LocalTarget,
            _configuration.Player.StepHeightMeters, request.Spacing, NativeCollisionResident, probe: request.Probe,
            targetRadius: request.ArrivalRadius).GetEnumerator();
    }

    private static Vector3 Native(NumericVector point) => new(point.X, point.Y, point.Z);
    private static BotNavigationRoute BotRoute(NativeBotRouteRequest request, IReadOnlyList<Vector3> path)
        => new(path.Select(point => new NumericVector(point.X, point.Y, point.Z)).ToArray(), request.End,
            new(request.WorldPath[^1].X, request.WorldPath[^1].Y, request.WorldPath[^1].Z),
            request.Resume == request.WorldPath.Length, request.Identity, request.Projection);
}
