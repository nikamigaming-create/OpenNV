using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifParticleSystem
{
    private readonly Dictionary<int, FalloutNifParticleCollider[]> _colliders = [];
    private ColliderPose[] _colliderPoses = [];
    private readonly record struct ColliderPose(Transform3D Transform, Transform3D Inverse, Basis Normal);
    internal long CollisionCount { get; private set; }

    private void ConfigureColliders(FalloutNifFile file, FalloutNifParticleColliderManager manager)
    {
        var colliders = new List<FalloutNifParticleCollider>();
        var seen = new HashSet<int>();
        for (var index = manager.Collider; index >= 0;)
        {
            if (!seen.Add(index)) throw new InvalidDataException("Particle collider chain contains a cycle.");
            if (file.ReadObject(index) is not FalloutNifParticleCollider collider || collider.Manager != manager.Block.Index ||
                collider.Bounce < 0)
                throw new NotSupportedException("Particle collider has an unowned manager or bounce.");
            RequireNode(collider.Object);
            switch (collider)
            {
                case FalloutNifParticlePlanarCollider plane:
                    FalloutNifParticlePlane.Validate(plane.Width, plane.Height,
                        new(plane.XAxis.X, plane.XAxis.Y, plane.XAxis.Z), new(plane.YAxis.X, plane.YAxis.Y, plane.YAxis.Z));
                    break;
                case FalloutNifParticleSphericalCollider sphere:
                    FalloutNifParticleSphere.Validate(sphere.Radius);
                    break;
                default: throw new NotSupportedException("Particle collider shape has no trajectory owner.");
            }
            colliders.Add(collider); index = collider.Next;
        }
        if (colliders.Count == 0) throw new InvalidDataException("Particle collider manager has no source collider.");
        _colliders.Add(manager.Block.Index, colliders.ToArray());
        if (_colliderPoses.Length < colliders.Count) _colliderPoses = new ColliderPose[colliders.Count];
    }

    private void Collide(FalloutNifParticleColliderManager manager)
    {
        var colliders = _colliders[manager.Block.Index];
        var refreshPoses = true;
        _collisionWork.Clear();
        for (var index = ActiveCount - 1; index >= 0; index--) _collisionWork.Enqueue(_particles[index].Identity);
        while (_collisionWork.TryDequeue(out var identity))
        {
            if (!_particleIndices.TryGetValue(identity, out var index)) continue;
            // The source manager admits one selected contact per particle per update.
            // Its position modifier consumes the remaining time after that response.
            if (_particles[index].MotionSeconds <= 0) continue;

            // Node poses stay fixed during a synchronous modifier pass.
            // Resolve native transforms and their inverses once per pass,
            // then reuse them for every particle and spawned descendant.
            if (refreshPoses)
            {
                for (var colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    var transform = TransformOf(colliders[colliderIndex].Object);
                    _colliderPoses[colliderIndex] = new(transform, transform.AffineInverse(), transform.Basis.Inverse().Transposed());
                }
                refreshPoses = false;
            }
            FalloutNifParticleCollider? selected = null;
            FalloutNifParticleCollisionHit? closest = null;
            var selectedPose = default(ColliderPose);
            var selectedPoint = default(Vector3);
            for (var colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                var candidateCollider = colliders[colliderIndex];
                var pose = _colliderPoses[colliderIndex];
                var position = pose.Inverse * _particles[index].Position;
                var velocity = pose.Inverse.Basis * _particles[index].Velocity;
                var motionSeconds = _particles[index].MotionSeconds;
                var querySeconds = closest is { } bound ? motionSeconds * bound.Fraction : motionSeconds;
                FalloutNifParticleCollisionHit? hit;
                if (candidateCollider is FalloutNifParticlePlanarCollider plane)
                {
                    var x = Convert(plane.XAxis); var y = Convert(plane.YAxis);
                    hit = FalloutNifParticlePlane.Sweep(new(position.X, position.Y, position.Z), new(velocity.X, velocity.Y, velocity.Z),
                        querySeconds, plane.Width * _units, plane.Height * _units, new(x.X, x.Y, x.Z), new(y.X, y.Y, y.Z),
                        .001f * _units * (pose.Normal * x.Cross(y).Normalized()).Length());
                }
                else if (candidateCollider is FalloutNifParticleSphericalCollider sphere)
                    hit = FalloutNifParticleSphere.Sweep(new(position.X, position.Y, position.Z), new(velocity.X, velocity.Y, velocity.Z),
                        querySeconds, sphere.Radius * _units);
                else throw new NotSupportedException("Particle collider shape has no trajectory owner.");
                if (hit is not { } candidate) continue;
                if (closest is not null)
                    candidate = candidate with { Fraction = candidate.Fraction * querySeconds / motionSeconds };
                var point = pose.Transform * new Vector3(candidate.Point.X, candidate.Point.Y, candidate.Point.Z);
                if (candidateCollider is FalloutNifParticlePlanarCollider)
                {
                    var candidateNormal = (pose.Normal * new Vector3(candidate.Normal.X, candidate.Normal.Y, candidate.Normal.Z)).Normalized();
                    var before = _particles[index].Position;
                    var origin = pose.Transform.Origin;
                    var corrected = FalloutNifParticlePlane.CorrectPoint(new(before.X, before.Y, before.Z),
                        new(point.X, point.Y, point.Z), new(candidateNormal.X, candidateNormal.Y, candidateNormal.Z), new(origin.X, origin.Y, origin.Z));
                    point = candidate.Fraction == 0 ? before : new(corrected.X, corrected.Y, corrected.Z);
                }
                selected = candidateCollider; closest = candidate; selectedPose = pose; selectedPoint = point;
                // An immediate planar contact returns before traversing the rest of the chain.
                if (candidateCollider is FalloutNifParticlePlanarCollider && candidate.Fraction == 0) break;
            }
            if (closest is not { } contact) continue;
            var collider = selected ?? throw new InvalidDataException("Particle contact has no declared collider.");
            CollisionCount = checked(CollisionCount + 1);
            var normal = (selectedPose.Normal * new Vector3(contact.Normal.X, contact.Normal.Y, contact.Normal.Z)).Normalized();
            ref var particle = ref _particles[index];
            particle.Position = selectedPoint;
            var incoming = particle.Velocity.Dot(normal);
            if (collider is FalloutNifParticlePlanarCollider)
            {
                var response = FalloutNifParticlePlane.Respond(new(particle.Velocity.X, particle.Velocity.Y, particle.Velocity.Z),
                    new(normal.X, normal.Y, normal.Z), collider.Bounce, contact.Fraction == 1, .001f * _units);
                particle.Velocity = new(response.X, response.Y, response.Z);
            }
            else particle.Velocity -= normal * ((1 + collider.Bounce) * incoming);
            particle.MotionSeconds *= 1 - contact.Fraction;
            var atContact = particle;
            // A lifecycle observer may synchronously move a source node.
            // Refresh after its callbacks before testing the next particle.
            refreshPoses = LifecycleEvent is not null;
            RecordLifecycle("collision", atContact, collider.Block.Index, "source-contact");
            if (collider.DieOnCollide)
            {
                RetireParticle(index, collider.Block.Index, "collision-death");
                if (collider.SpawnOnCollide)
                    Spawn(atContact, collider.Spawn, collider.Block.Index, "collision", atContact.MotionSeconds, _collisionWork);
                continue;
            }
            if (collider.SpawnOnCollide)
                Spawn(atContact, collider.Spawn, collider.Block.Index, "collision", atContact.MotionSeconds, _collisionWork);
        }
    }
}
