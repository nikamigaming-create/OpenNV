using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifParticleSystem
{
    private readonly Dictionary<int, FalloutNifParticleCollider[]> _colliders = [];
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
    }

    private void Collide(FalloutNifParticleColliderManager manager)
    {
        _collisionWork.Clear();
        for (var index = ActiveCount - 1; index >= 0; index--) _collisionWork.Enqueue(_particles[index].Identity);
        while (_collisionWork.TryDequeue(out var identity))
        {
            if (!_particleIndices.TryGetValue(identity, out var index)) continue;
            var collisions = 0;
            while (_particles[index].MotionSeconds > 0)
            {
                FalloutNifParticleCollider? selected = null;
                FalloutNifParticleCollisionHit? closest = null;
                var selectedTransform = Transform3D.Identity;
                foreach (var candidateCollider in _colliders[manager.Block.Index])
                {
                    var transform = TransformOf(candidateCollider.Object);
                    var inverse = transform.AffineInverse();
                    var position = inverse * _particles[index].Position;
                    var velocity = inverse.Basis * _particles[index].Velocity;
                    FalloutNifParticleCollisionHit? hit;
                    if (candidateCollider is FalloutNifParticlePlanarCollider plane)
                    {
                        var x = Convert(plane.XAxis); var y = Convert(plane.YAxis);
                        hit = FalloutNifParticlePlane.Sweep(new(position.X, position.Y, position.Z), new(velocity.X, velocity.Y, velocity.Z),
                            _particles[index].MotionSeconds, plane.Width * _units, plane.Height * _units, new(x.X, x.Y, x.Z), new(y.X, y.Y, y.Z));
                    }
                    else if (candidateCollider is FalloutNifParticleSphericalCollider sphere)
                        hit = FalloutNifParticleSphere.Sweep(new(position.X, position.Y, position.Z), new(velocity.X, velocity.Y, velocity.Z),
                            _particles[index].MotionSeconds, sphere.Radius * _units);
                    else throw new NotSupportedException("Particle collider shape has no trajectory owner.");
                    if (hit is null || closest is { } previous && hit.Value.Fraction >= previous.Fraction) continue;
                    selected = candidateCollider; closest = hit; selectedTransform = transform;
                }
                if (closest is not { } contact) break;
                if (++collisions > 16) throw new NotSupportedException("Particle collision response exceeded its bounded contact continuation.");
                var collider = selected ?? throw new InvalidDataException("Particle contact has no declared collider.");
                CollisionCount = checked(CollisionCount + 1);
                var normal = (selectedTransform.Basis.Inverse().Transposed() * new Vector3(contact.Normal.X, contact.Normal.Y, contact.Normal.Z)).Normalized();
                ref var particle = ref _particles[index];
                particle.Position = selectedTransform * new Vector3(contact.Point.X, contact.Point.Y, contact.Point.Z);
                var incoming = particle.Velocity.Dot(normal);
                particle.Velocity -= normal * ((1 + collider.Bounce) * incoming);
                particle.MotionSeconds *= 1 - contact.Fraction;
                var atContact = particle;
                RecordLifecycle("collision", atContact, collider.Block.Index, "source-contact");
                if (collider.DieOnCollide)
                {
                    RetireParticle(index, collider.Block.Index, "collision-death");
                    if (collider.SpawnOnCollide)
                        Spawn(atContact, collider.Spawn, collider.Block.Index, "collision", atContact.MotionSeconds, _collisionWork);
                    break;
                }
                if (collider.SpawnOnCollide)
                    Spawn(atContact, collider.Spawn, collider.Block.Index, "collision", atContact.MotionSeconds, _collisionWork);
            }
        }
    }
}
