using Godot;

namespace OpenNV.Runtime.World.Cells;

internal static partial class NativeCapsuleNavigation
{
    // Same complete edge query used by route refinement and actual signed
    // accumulation admission. This owns query resources, never body motion.
    internal sealed class EdgeQuery : IDisposable
    {
        private readonly CharacterBody3D _body;
        private readonly float _stepHeight;
        private readonly Func<Vector3, bool> _resident;
        private readonly NativeNavigationProbe? _probe;
        private readonly PhysicsTestMotionParameters3D _query;
        private readonly PhysicsTestMotionResult3D _hit = new();
        private readonly NativeCharacterStep.QueryResources _steps;
        private readonly PhysicsRayQueryParameters3D _support;
        private readonly Rid _rid;
        private readonly ulong _identity, _world;
        private bool _disposed;
        internal float SampleLength { get; }
        internal NativeNavigationContact? RejectedContact { get; private set; }
        internal string? RejectionReason { get; private set; }
        internal EdgeQuery(CharacterBody3D body, float stepHeight, float spacing,
            Func<Vector3, bool> resident, NativeNavigationProbe? probe = null)
        {
            if (!float.IsFinite(stepHeight) || stepHeight <= 0 || !float.IsFinite(spacing) || spacing <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepHeight));
            _body = body; _stepHeight = stepHeight; _resident = resident; _probe = probe;
            _rid = body.GetRid(); _identity = body.GetInstanceId(); _world = body.GetWorld3D().GetInstanceId();
            _query = new() { Margin = body.SafeMargin, MaxCollisions = 4 };
            _steps = new(body);
            _support = PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, body.CollisionMask, [_rid]);
            SampleLength = EdgeSampleLength(body, stepHeight, spacing);
        }
        private void RequireOwner()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!GodotObject.IsInstanceValid(_body) || !_body.IsInsideTree() || _body.IsQueuedForDeletion() ||
                _body.GetInstanceId() != _identity || !_body.GetRid().Equals(_rid) || _body.GetWorld3D().GetInstanceId() != _world)
                throw new InvalidOperationException("Actual movement edge lost its native body/world owner.");
        }
        internal bool TrySingleEdge(Vector3 from, Vector3 desired, Basis basis, out Vector3 landing, bool allowStep = true)
        {
            RequireOwner();
            if (!from.IsFinite() || !desired.IsFinite()) throw new InvalidDataException("Movement edge requires finite endpoints.");
            var body = _body; var stepHeight = _stepHeight; var resident = _resident; var probe = _probe;
            var query = _query; var hit = _hit; var stepQueries = _steps; var supportRay = _support; var rid = _rid;
            var floorCosine = MathF.Cos(body.FloorMaxAngle);
            var maximumDrop = stepHeight + Math.Max(stepHeight, body.FloorSnapLength);
            bool Sweep(Vector3 origin, Vector3 motion)
            {
                query.From = new(basis, origin); query.Motion = motion;
                return NativeCharacterStep.TestMotion(rid, query, hit);
            }
            RejectionReason = null;
            probe?.BeginEdge();
            RejectedContact = null;
            landing = default;
            bool Reject(string reason, NativeNavigationContact? contact = null)
            { RejectedContact = contact; RejectionReason = reason; probe?.Reject(reason, from, desired, contact); return false; }
            if (!resident(from)) return Reject("source-not-resident");
            if (!resident(desired)) return Reject("destination-not-resident");
            var motion = desired - from; motion.Y = 0;
            var supportedFrom = from + motion;
            var drop = maximumDrop + body.SafeMargin * 8;
            // Ordinary flat motion needs no step headroom. Only test a raised
            // sweep when the direct capsule motion actually meets an obstacle.
            if (Sweep(from, motion))
            {
                var obstacle = Contact(hit, from, desired, floorCosine);
                if (!allowStep) return Reject("flat-clearance", obstacle);
                if (obstacle is not null)
                {
                    if (!NativeCharacterStep.TryQuery(body, new(basis, from), motion, stepHeight,
                        out var stepped, out var reason, out var contact, resources: stepQueries)) return Reject("step-" + reason, contact ?? obstacle);
                    drop += stepped.Y - from.Y;
                    supportedFrom = stepped;
                }
                else
                {
                    if (!NativeCharacterStep.TrySlope(body, new(basis, from), motion, stepHeight, query, hit,
                        out var sloped, out var reason, out var contact)) return Reject(reason!, contact);
                    supportedFrom = sloped;
                    drop += sloped.Y - from.Y;
                }
            }
            var downward = Vector3.Down * drop;
            var collided = Sweep(supportedFrom, downward);
            probe?.ObserveDownward(hit, supportedFrom, supportedFrom + downward, collided);
            if (!collided) return Reject("landing-sweep-no-floor");
            if (hit.GetCollisionCount() == 0) return Reject("landing-sweep-no-contact");
            landing = supportedFrom + hit.GetTravel();
            NativeNavigationContact? reconciledFloor = null;
            var walkable = false;
            var collisionCount = hit.GetCollisionCount();
            for (var index = 0; index < collisionCount; index++)
                if (hit.GetCollisionNormal(index).Dot(Vector3.Up) >= floorCosine) { walkable = true; break; }
            if (!walkable &&
                !NativeCharacterStep.TrySupport(body, landing, stepHeight, out reconciledFloor, stepQueries)) return Reject("landing-slope",
                    FirstContact(hit, supportedFrom, supportedFrom + downward));
            // The rounded bottom can stop on a steep stair edge although the
            // same pose has a walkable root floor within the mover's snap
            // interval. Only that shared native support reconciles it; the
            // complete capsule sweeps, root floor, height and residency still
            // apply. A source guide's incline supplies no support evidence.
            // A rounded capsule can touch a walkable normal on a ledge while
            // its feet remain over empty space. Sliding then pushes that body
            // off the edge instead of executing the planned waypoint. Require
            // the same native floor beneath the controller root as well.
            var surface = landing + Vector3.Up * stepQueries.ContactOffset;
            supportRay.From = surface + Vector3.Up * body.SafeMargin * 8;
            supportRay.To = surface - Vector3.Up * Math.Max(stepHeight, body.FloorSnapLength);
            using var support = NativeCharacterStep.QuerySupport(body, supportRay);
            if (support.Count == 0) return Reject("root-floor-missing");
            if (support["normal"].AsVector3().Dot(Vector3.Up) < floorCosine) return Reject("root-floor-slope");
            var height = landing.Y - from.Y;
            if (height > stepHeight + body.SafeMargin * 8) return Reject("landing-too-high");
            if (height < -maximumDrop - body.SafeMargin * 8) return Reject("landing-too-low");
            if (!resident(landing)) return Reject("landing-not-resident");
            if (reconciledFloor is not null) probe?.ObserveReconciledLanding(reconciledFloor);
            return true;
        }
        internal bool ValidateSegment(Vector3 start, Vector3 desired, Func<Vector3, bool> sourceFloor,
            out Vector3 landing, out string? reason)
        {
            RequireOwner();
            ArgumentNullException.ThrowIfNull(sourceFloor);
            if (!start.IsFinite() || !desired.IsFinite()) throw new InvalidDataException("Movement segment requires finite endpoints.");
            landing = start;
            var distance = new Vector2(desired.X - start.X, desired.Z - start.Z).Length();
            if (!float.IsFinite(distance)) throw new InvalidDataException("Movement segment extent overflowed.");
            var count = Math.Ceiling((double)distance / SampleLength);
            if (count > NativeNavigationWorkSchedule.MaximumIteratorSteps) { reason = "segment-sample-bound"; return false; }
            var samples = Math.Max(1, (int)count);
            var basis = _body.GlobalBasis;
            for (var index = 1; index <= samples; ++index)
            {
                if (!TrySingleEdge(landing, start.Lerp(desired, (float)index / samples), basis, out var next))
                { reason = RejectionReason; return false; }
                if (!sourceFloor(next)) { reason = "source-corridor-floor"; return false; }
                landing = next;
            }
            reason = null;
            return true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _query.Dispose(); _hit.Dispose(); _steps.Dispose(); _support.Dispose();
        }
    }

    private static float EdgeSampleLength(CharacterBody3D body, float stepHeight, float spacing)
    {
        var children = body.GetChildren();
        using ((Godot.Collections.Array)children)
            return Math.Min(spacing, Math.Max(.03f, Math.Min(stepHeight * .5f,
                children.OfType<CollisionShape3D>()
                    .Where(shape => shape.Shape is CapsuleShape3D).Select(shape =>
                        ((CapsuleShape3D)shape.Shape).Radius * Math.Min(shape.GlobalBasis.X.Length(), shape.GlobalBasis.Z.Length()) * .5f)
                    .DefaultIfEmpty(spacing).Min())));
    }
}
