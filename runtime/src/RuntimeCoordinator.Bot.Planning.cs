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
        internal Vector3 LocalTarget;
        internal float ArrivalRadius, Spacing, RefinedSpacing;
        internal int Resume;
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
                $"scope={Scope(current)} target={current.LocalTarget} sourceSha256={_botNavigation!.SourceSha256} " +
                $"spacing={current.Spacing} arrivalRadius={current.ArrivalRadius} sourceExclusions=0 " +
                $"coarseError={current.CoarseError ?? "none"} sourceMs={current.SourceMilliseconds:F3} maxSliceMs={current.MaximumSliceMilliseconds:F3}");
            return BotRoute(current, path!);
        }
        catch (InvalidOperationException error)
        {
            current.Search?.Dispose(); current.Search = null;
            GD.Print($"OPENNV_BOT_CAPSULE_REJECT ownerBuild={typeof(NativeCapsuleNavigation).Module.ModuleVersionId} " +
                $"source={current.Identity} sourceSha256={_botNavigation!.SourceSha256} start={current.Start} " +
                $"requested={current.End} projected={current.WorldPath[^1]} referenceTarget={current.Target} " +
                $"scope={Scope(current)} localTarget={current.LocalTarget} arrivalRadius={current.ArrivalRadius} " +
                $"basis={current.BodyBasis} stepHeight={_configuration.Player.StepHeightMeters} spacing={current.Spacing} " +
                $"resume={current.Resume} sourceExclusions=0 nativeError={error.Message}");
            if (current.CoarseError is null && current.RefinedSpacing < current.Spacing)
            {
                current.CoarseError = error.Message; current.Spacing = current.RefinedSpacing;
                current.Search = NativeCapsuleNavigation.Search(player, Native(start), current.LocalTarget,
                    _configuration.Player.StepHeightMeters, current.Spacing, NativeCollisionResident, probe: current.Probe,
                    targetRadius: current.ArrivalRadius, corridor: current.Intent!.Corridor).GetEnumerator();
                return null;
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
                    return BotRoute(current, probe.Approach) with { RequiredDoor = reference.ToString() };
                }
            }
            // A failed point/lattice/approach query does not attest the full
            // traversable width of any source portal. Keep NAVM unchanged and
            // retain the actual native error, including support-only failures.
            throw new InvalidOperationException($"No capsule-supported {Scope(current)}; source portals were not excluded. " +
                $"Coarse: {current.CoarseError ?? error.Message} Refined: {error.Message}", error);
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
            destinationRadiusGameUnits: request.Projection / units);
        request.WorldPath = request.SourcePath.Select(World).ToArray();
        var origin = Native(request.Start);
        var intent = request.Intent = NativeCapsuleNavigation.Intent(origin, request.WorldPath, Native(request.Target), request.Distance);
        request.LocalTarget = intent.Target; request.Resume = intent.Resume; request.ArrivalRadius = intent.ArrivalRadius;
        IReadOnlyList<Vector3> corridor = intent.ReferenceApproach ? [intent.Target] : intent.Corridor;
        request.Probe = new(NativeCapsuleNavigation.FirstCorridorContact(request.Player, origin, corridor), request.Events.CollisionReference);
        request.Spacing = Math.Max(.3f, _configuration.Player.CapsuleRadiusMeters);
        request.RefinedSpacing = Math.Min(request.Spacing, Math.Max(.15f,
            _configuration.Player.CapsuleRadiusMeters * request.Player.GlobalBasis.X.Length()));
        request.CoarseError = null;
        request.Search = NativeCapsuleNavigation.Search(request.Player, origin, request.LocalTarget,
            _configuration.Player.StepHeightMeters, request.Spacing, NativeCollisionResident, probe: request.Probe,
            targetRadius: request.ArrivalRadius, corridor: intent.Corridor).GetEnumerator();
    }

    private static Vector3 Native(NumericVector point) => new(point.X, point.Y, point.Z);
    private static string Scope(NativeBotRouteRequest request) => request.Intent!.ReferenceApproach ? "reference-approach" : "source-corridor";
    private BotNavigationRoute BotRoute(NativeBotRouteRequest request, IReadOnlyList<Vector3> path)
        => new(path.Select(point => new NumericVector(point.X, point.Y, point.Z)).ToArray(), request.End,
            new(request.WorldPath[^1].X, request.WorldPath[^1].Y, request.WorldPath[^1].Z),
            request.Resume == request.WorldPath.Length, request.Identity, request.Projection,
            Refinement: new(Scope(request), new(request.LocalTarget.X, request.LocalTarget.Y, request.LocalTarget.Z),
                request.ArrivalRadius, _botNavigation!.SourceSha256));
}
