using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed partial class RuntimeNifParticleSystem
{
    private readonly Dictionary<int, FalloutNifParticlePlanarCollider[]> _colliders = [];
    internal long CollisionCount { get; private set; }

    private void ConfigureColliders(FalloutNifFile file, FalloutNifParticleColliderManager manager)
    {
        var colliders = new List<FalloutNifParticlePlanarCollider>();
        var seen = new HashSet<int>();
        for (var index = manager.Collider; index >= 0;)
        {
            if (!seen.Add(index)) throw new InvalidDataException("Particle collider chain contains a cycle.");
            if (file.ReadObject(index) is not FalloutNifParticlePlanarCollider plane || plane.Manager != manager.Block.Index ||
                plane.Bounce < 0 || plane.SpawnOnCollide)
                throw new NotSupportedException("Particle collider has an unowned manager, bounce or secondary spawn.");
            RequireNode(plane.Object);
            FalloutNifParticlePlane.Validate(plane.Width, plane.Height,
                new(plane.XAxis.X, plane.XAxis.Y, plane.XAxis.Z), new(plane.YAxis.X, plane.YAxis.Y, plane.YAxis.Z));
            colliders.Add(plane); index = plane.Next;
        }
        if (colliders.Count == 0) throw new InvalidDataException("Particle collider manager has no source collider.");
        _colliders.Add(manager.Block.Index, colliders.ToArray());
    }

    private void Collide(FalloutNifParticleColliderManager manager)
    {
        for (var index = ActiveCount - 1; index >= 0; index--)
        {
            var collisions = 0;
            while (_particles[index].MotionSeconds > 0)
            {
                FalloutNifParticlePlanarCollider? selected = null;
                FalloutNifParticlePlaneHit? closest = null;
                var selectedTransform = Transform3D.Identity;
                foreach (var plane in _colliders[manager.Block.Index])
                {
                    var transform = TransformOf(plane.Object);
                    var inverse = transform.AffineInverse();
                    var position = inverse * _particles[index].Position;
                    var velocity = inverse.Basis * _particles[index].Velocity;
                    var x = Convert(plane.XAxis); var y = Convert(plane.YAxis);
                    var hit = FalloutNifParticlePlane.Sweep(new(position.X, position.Y, position.Z), new(velocity.X, velocity.Y, velocity.Z),
                        _particles[index].MotionSeconds, plane.Width * _units, plane.Height * _units, new(x.X, x.Y, x.Z), new(y.X, y.Y, y.Z));
                    if (hit is null || closest is { } previous && hit.Value.Fraction >= previous.Fraction) continue;
                    selected = plane; closest = hit; selectedTransform = transform;
                }
                if (closest is not { } contact) break;
                if (++collisions > 16) throw new NotSupportedException("Particle collision response exceeded its bounded contact continuation.");
                CollisionCount++;
                if (selected!.DieOnCollide)
                {
                    _particles[index] = _particles[--ActiveCount]; DeathCount++;
                    break;
                }
                var normal = (selectedTransform.Basis.Inverse().Transposed() * new Vector3(contact.Normal.X, contact.Normal.Y, contact.Normal.Z)).Normalized();
                ref var particle = ref _particles[index];
                particle.Position = selectedTransform * new Vector3(contact.Point.X, contact.Point.Y, contact.Point.Z);
                var incoming = particle.Velocity.Dot(normal);
                particle.Velocity -= normal * ((1 + selected.Bounce) * incoming);
                particle.MotionSeconds *= 1 - contact.Fraction;
            }
        }
    }
}
