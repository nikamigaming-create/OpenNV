namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record FalloutNifPhysicalSceneObject(FalloutNifObject Declaration, int? Parent,
    int Slot, IReadOnlyList<int> Children, FalloutNifCollisionObject? Collision);
internal sealed record FalloutNifPhysicalConstraint(FalloutNifConstraintHeader Header,
    FalloutNifObject Declaration, FalloutNifRigidBody First, FalloutNifRigidBody Second);
internal sealed record FalloutNifPhysicalRegistration(FalloutNifRigidBody Body, int Slot,
    FalloutNifPhysicalConstraint? Constraint);

// The original selected scene and its authored physical declarations. This is
// independent of meshes, native handles and the current physical world. Runtime
// contact constraints, motion and world residency need their own live producers;
// an authored registration must never stand in for those changing memberships.
internal sealed class FalloutNifPhysicalModel
{
    private readonly IReadOnlyDictionary<int, FalloutNifPhysicalSceneObject> _sceneByBlock;
    private readonly IReadOnlyDictionary<int, FalloutNifRigidBody> _bodyByBlock;

    private FalloutNifPhysicalModel(FalloutNifFile source,
        List<FalloutNifPhysicalSceneObject> scene, List<FalloutNifRigidBody> bodies,
        List<FalloutNifPhysicalConstraint> constraints, List<FalloutNifPhysicalRegistration> registrations)
    {
        Source = source;
        Roots = Array.AsReadOnly(source.Roots.ToArray());
        Scene = scene.AsReadOnly(); Bodies = bodies.AsReadOnly();
        Constraints = constraints.AsReadOnly(); Registrations = registrations.AsReadOnly();
        _sceneByBlock = scene.ToDictionary(value => value.Declaration.Block.Index);
        _bodyByBlock = bodies.ToDictionary(value => value.Block.Index);
    }

    internal FalloutNifFile Source { get; }
    internal IReadOnlyList<int> Roots { get; }
    internal IReadOnlyList<FalloutNifPhysicalSceneObject> Scene { get; }
    internal IReadOnlyList<FalloutNifRigidBody> Bodies { get; }
    internal IReadOnlyList<FalloutNifPhysicalConstraint> Constraints { get; }
    internal IReadOnlyList<FalloutNifPhysicalRegistration> Registrations { get; }
    internal FalloutNifPhysicalSceneObject? FindSceneObject(int block) => _sceneByBlock.GetValueOrDefault(block);
    internal FalloutNifRigidBody? FindAttachedBody(int block) => _bodyByBlock.GetValueOrDefault(block);

    internal static FalloutNifPhysicalModel Read(FalloutNifFile source, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellation.ThrowIfCancellationRequested();
        var scene = new List<FalloutNifPhysicalSceneObject>();
        var bodies = new List<FalloutNifRigidBody>();
        var visited = new HashSet<int>();
        var attachedBodies = new HashSet<int>();
        // An explicit stack keeps source child-slot order without a call-stack
        // limit for a large authored hierarchy. Null slots remain in the owner.
        var pending = new Stack<(int Block, int? Parent, int Slot)>();
        for (var slot = source.Roots.Count - 1; slot >= 0; slot--)
            pending.Push((source.Roots[slot], null, slot));
        while (pending.TryPop(out var next))
        {
            cancellation.ThrowIfCancellationRequested();
            if (next.Block == -1) continue;
            if (!visited.Add(next.Block))
                throw new InvalidDataException($"NIF physical scene repeats or cycles through block {next.Block}.");
            var declaration = source.ReadObject(next.Block);
            var (children, collisionIndex) = declaration switch
            {
                FalloutNifNode node => (node.Children, node.CollisionObject),
                FalloutNifGeometry geometry => (Array.Empty<int>(), geometry.CollisionObject),
                FalloutNifParticleSystem particle => (Array.Empty<int>(), particle.Geometry.CollisionObject),
                FalloutNifAmbientLight light => (Array.Empty<int>(), light.CollisionObject),
                FalloutNifPointLight light => (Array.Empty<int>(), light.Light.CollisionObject),
                _ => throw new NotSupportedException($"NIF physical scene block {next.Block} {declaration.Block.TypeName} has no scene declaration owner."),
            };
            FalloutNifCollisionObject? collision = null;
            if (collisionIndex != -1)
            {
                collision = source.ReadObject(collisionIndex) as FalloutNifCollisionObject ??
                    throw new NotSupportedException($"NIF physical scene block {next.Block} has an unowned collision wrapper.");
                if (collision.Target != next.Block)
                    throw new InvalidDataException($"NIF collision {collision.Block.Index} targets another scene object.");
                if (collision.Body != -1)
                {
                    // A shape phantom is a different physical object. Preserve
                    // its source wrapper; do not manufacture a rigid body.
                    switch (source.ReadObject(collision.Body))
                    {
                        case FalloutNifRigidBody body:
                            if (attachedBodies.Add(body.Block.Index)) bodies.Add(body);
                            break;
                        case FalloutNifSimpleShapePhantom: break;
                        default: throw new NotSupportedException($"NIF collision {collision.Block.Index} has an unowned physical body declaration.");
                    }
                }
            }
            scene.Add(new(declaration, next.Parent, next.Slot, Array.AsReadOnly((int[])children.Clone()), collision));
            for (var slot = children.Length - 1; slot >= 0; slot--)
                pending.Push((children[slot], next.Block, slot));
        }
        var constraints = new List<FalloutNifPhysicalConstraint>();
        var registrations = new List<FalloutNifPhysicalRegistration>();
        var byConstraint = new Dictionary<int, FalloutNifPhysicalConstraint>();
        foreach (var body in bodies)
            for (var slot = 0; slot < body.Constraints.Length; slot++)
            {
                cancellation.ThrowIfCancellationRequested();
                var index = body.Constraints[slot];
                if (index == -1) { registrations.Add(new(body, slot, null)); continue; }
                if (!byConstraint.TryGetValue(index, out var constraint))
                {
                    var header = source.ReadConstraintHeader(index);
                    var descriptor = source.ReadObject(index);
                    constraint = new(header, descriptor,
                        (FalloutNifRigidBody)source.ReadObject(header.EntityA),
                        (FalloutNifRigidBody)source.ReadObject(header.EntityB));
                    byConstraint.Add(index, constraint); constraints.Add(constraint);
                }
                if (constraint.Header.EntityA != body.Block.Index && constraint.Header.EntityB != body.Block.Index)
                    throw new InvalidDataException($"NIF body {body.Block.Index} registers another pair's constraint {index}.");
                registrations.Add(new(body, slot, constraint));
            }
        return new(source, scene, bodies, constraints, registrations);
    }
}
