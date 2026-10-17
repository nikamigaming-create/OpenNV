namespace OpenNV.Runtime.World.Physics;

internal sealed partial record FalloutRagdollFootIkSnapshot
{
    internal void Validate(FalloutLivingModelSnapshot model)
    {
        if (Load is null || Nodes is null || Shapes is null || string.IsNullOrWhiteSpace(Step) ||
            model.Controller is not { Returned: true } controller || controller.Load != Load ||
            model.BodyPartPreparation is not { Enumerated: true, Prime: false } parts || parts.Load != Load ||
            !Nodes.SequenceEqual(parts.IkNodes) || Shapes.Count > 3 ||
            Returned && (Failure is not null || Step != "returned") || Failure is not null && string.IsNullOrWhiteSpace(Failure) ||
            AvailabilityWritten != (Available is not null))
            throw new InvalidDataException("Foot IK lost its same controller, source node list or constructor prefix.");
        foreach (var shape in Shapes) Shape(shape);
        foreach (var joint in new[] { KneeJoint, AnkleJoint }.OfType<FalloutFootIkJointRead>())
        {
            joint.Parameters.Validate();
            if (joint.Wrapper == Guid.Empty || joint.Parameters.Type != 2 || model.RagdollBinding is not { } binding ||
                !binding.Joints.Any(current => current.Constraint == joint.Constraint && current.NativeJoint == joint.NativeJoint))
                throw new InvalidDataException("Foot IK borrowed another current limited-hinge object.");
        }
        if (Legs is { } legs)
        {
            if (!legs.Select(leg => leg.Name).SequenceEqual(Nodes) || SetupResult != true || Setup?.Skeleton is null ||
                model.AnimationSkeleton?.Descriptor is not { } animation || Setup.Skeleton != animation.Identity ||
                model.RagdollBinding is not { } binding || model.PhysicalSkeleton is not { } physical || model.Mapping?.Mappings is not { } maps)
                throw new InvalidDataException("Foot solvers lost their current calibration, animation or physical mapping.");
            var identities = new HashSet<Guid>();
            foreach (var leg in legs)
            {
                if (leg.AnkleShape is { } shape) Shape(shape);
                if (leg.PointerWritten != (leg.Solver is not null))
                    throw new InvalidDataException("Foot solver pointer was fabricated without an actual constructor writer.");
                var radius = leg.AnkleShape is { } ankle ? FalloutFootIkMath.Value(ankle.Parameters.Radius) : 0;
                if (leg.RayHeight is { } height && height != FalloutFootIkMath.Bits(FalloutFootIkMath.Add(radius, 7)))
                    throw new InvalidDataException("Foot ray height did not consume its own current ankle radius.");
                if (leg.Solver is not { } solver) continue;
                var setup = solver.Setup;
                var indices = new[] { setup.Hip, setup.Knee, setup.Ankle };
                if (solver.Identity == Guid.Empty || !identities.Add(solver.Identity) || solver.Model != controller.Model ||
                    solver.Skeleton != animation.Identity || setup.Skeleton != animation.Identity || solver.Retired ||
                    solver.Bodies.Count != 3 || solver.NativeBodies.Count != 3 || solver.NativeBodies.Distinct().Count() != 3 ||
                    indices.Any(index => index < 0 || index >= animation.Bones.Count) ||
                    solver.FirstAccumulator != 0 || solver.SecondAccumulator != 0 || solver.Correction != 0 || solver.HasGround ||
                    solver.GroundNormal != setup.WorldUp || solver.Vectors.Count != 3 || solver.Vectors.Any(vector => vector != default) ||
                    leg.RayHeight != setup.RayDistanceDown || setup.RayDistanceUp != FalloutFootIkMath.Bits(FalloutFootIkMath.Add(radius, 4)))
                    throw new InvalidDataException("Foot solver changed its actual constructor fields or current owner.");
                for (var index = 0; index < 3; ++index)
                    if (!binding.Bodies.Any(body => body.Body == solver.Bodies[index] && body.NativeBody == solver.NativeBodies[index] &&
                        FalloutRagdollSkeletonMappingConstruction.Name(body.Name) ==
                        FalloutRagdollSkeletonMappingConstruction.Name(animation.Bones[indices[index]].Name)))
                        throw new InvalidDataException("Foot solver borrowed another real physical body or animation bone.");
                var matches = maps.AnimationToPhysical.Simple.Where(entry => entry.Source == setup.Ankle).ToArray();
                if (leg.PhysicalAnkle is { } physicalAnkle && (physicalAnkle < 0 || physicalAnkle >= physical.Descriptor.Bones.Count ||
                    matches.Length == 0 || matches[^1].Target != physicalAnkle) ||
                    Returned && leg.PhysicalAnkle is null && matches.Length != 0)
                    throw new InvalidDataException("Foot solver physical ankle lost its actual mapper writer.");
            }
            if (AvailabilityWritten && (legs.Any(leg => !leg.PointerWritten) || Available != legs.All(leg => leg.Solver is not null)))
                throw new InvalidDataException("Foot availability was inferred without actual solver objects.");
        }
        else if (AvailabilityWritten || Returned && SetupResult == true)
            throw new InvalidDataException("Foot IK returned a successful setup without its source solver array.");

        void Shape(FalloutLivingShapeInfo shape)
        {
            var body = model.Bodies.SingleOrDefault(body => body.Key == shape.Body);
            if (body?.ShapeInfo is not { } current || current.Identity != shape.Identity || current.NativeBody != shape.NativeBody ||
                current.Parameters != shape.Parameters || current.Writer != Load)
                throw new InvalidDataException("Foot IK borrowed another current body's shape-info object.");
        }
    }
}
