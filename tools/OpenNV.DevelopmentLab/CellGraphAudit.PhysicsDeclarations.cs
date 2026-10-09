using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    internal static class SourcePhysicsDeclarations
    {
        internal static void Inspect(FalloutNifFile source, FalloutNifObject value, ResourceRow row)
        {
            if (value is not (FalloutNifBlendController or FalloutNifRagdollConstraint or FalloutNifHingeConstraint)) return;
            var block = value.Block;
            if (block.Index < 0 || block.Index >= source.Blocks.Count || source.Blocks[block.Index] != block ||
                !ReferenceEquals(source.ReadObject(block.Index), value))
                throw new InvalidDataException("Physics declaration belongs to another decoded source object.");
            switch (value)
            {
                case FalloutNifBlendController blend:
                    row.DependencyDeclarations.Add(new { kind = "nif-havok-blend-controller", sourceSha256 = source.Sha256,
                        block = block.Index, type = block.TypeName, offset = block.Offset, bytes = block.Size,
                        flags = blend.Time.Flags, frequency = blend.Time.Frequency, phase = blend.Time.Phase,
                        startTime = blend.Time.StartTime, stopTime = blend.Time.StopTime, keys = blend.Keys,
                        activation = "uninspected", physicsBlend = "uninspected", nativeAdmission = "unverified" });
                    Link("Time.NextController", blend.Time.NextController, timeController: true);
                    Link("Time.Target", blend.Time.Target);
                    break;
                case FalloutNifRagdollConstraint joint:
                    row.DependencyDeclarations.Add(new { kind = "nif-havok-joint", sourceSha256 = source.Sha256,
                        block = block.Index, type = block.TypeName, offset = block.Offset, bytes = block.Size,
                        wrappedType = joint.Header.WrappedType, priority = joint.Header.Priority,
                        twistA = joint.TwistA, planeA = joint.PlaneA, motorA = joint.MotorA, pivotA = joint.PivotA,
                        twistB = joint.TwistB, planeB = joint.PlaneB, motorB = joint.MotorB, pivotB = joint.PivotB,
                        cone = joint.Cone, planeMinimum = joint.PlaneMinimum, planeMaximum = joint.PlaneMaximum,
                        twistMinimum = joint.TwistMinimum, twistMaximum = joint.TwistMaximum,
                        friction = joint.Friction, strength = joint.Strength, padding = joint.VectorPadding,
                        motor = (object?)joint.Motor, motorDispatch = "not performed", nativeAdmission = "unverified" });
                    Link("Header.EntityA", joint.Header.EntityA, rigidBody: true);
                    Link("Header.EntityB", joint.Header.EntityB, rigidBody: true);
                    break;
                case FalloutNifHingeConstraint hinge:
                    row.DependencyDeclarations.Add(new { kind = "nif-havok-joint", sourceSha256 = source.Sha256,
                        block = block.Index, type = block.TypeName, offset = block.Offset, bytes = block.Size,
                        wrappedType = hinge.Header.WrappedType, priority = hinge.Header.Priority,
                        axisA = hinge.AxisA, perpendicularA1 = hinge.PerpendicularA1, perpendicularA2 = hinge.PerpendicularA2, pivotA = hinge.PivotA,
                        axisB = hinge.AxisB, perpendicularB1 = hinge.PerpendicularB1, perpendicularB2 = hinge.PerpendicularB2, pivotB = hinge.PivotB,
                        minimum = hinge.Minimum, maximum = hinge.Maximum, friction = hinge.Friction,
                        padding = hinge.VectorPadding, nativeAdmission = "unverified" });
                    Link("Header.EntityA", hinge.Header.EntityA, rigidBody: true);
                    Link("Header.EntityB", hinge.Header.EntityB, rigidBody: true);
                    break;
            }

            void Link(string field, int index, bool timeController = false, bool rigidBody = false)
            {
                var declaration = new ControllerLinkDeclaration
                {
                    SourceSha256 = source.Sha256, Block = block.Index, Type = block.TypeName,
                    Offset = block.Offset, Bytes = block.Size, Field = field, TargetBlock = index,
                    TargetType = index >= 0 && index < source.Blocks.Count ? source.Blocks[index].TypeName : null,
                    TargetOffset = index >= 0 && index < source.Blocks.Count ? source.Blocks[index].Offset : null,
                    TargetBytes = index >= 0 && index < source.Blocks.Count ? source.Blocks[index].Size : null,
                    TypedOwner = timeController ? "FalloutNifNodeControllerChain.Time" : rigidBody ? "FalloutNifFile.ReadObject: rigid-body declaration" : null,
                };
                row.DependencyDeclarations.Add(declaration);
                try
                {
                    if (index == -1 && !rigidBody)
                    {
                        declaration.DecodeDisposition = "encoded-null";
                        declaration.TypedDisposition = "encoded-absent; no owner substituted";
                        return;
                    }
                    var target = source.ReadObject(index);
                    declaration.DecodeDisposition = "target-block-decoded";
                    if (timeController) _ = FalloutNifNodeControllerChain.Time(target);
                    if (rigidBody && target is not FalloutNifRigidBody)
                        throw new InvalidDataException("Constraint target is not a decoded rigid-body declaration.");
                    declaration.TypedDisposition = timeController || rigidBody ? "typed-source-target-admitted; native binding unverified" : "target decoded; native target binding uninspected";
                }
                catch (Exception error)
                {
                    if (declaration.DecodeDisposition == "uninspected") declaration.DecodeDisposition = "target-reader-refused";
                    declaration.TypedDisposition = "consumer-link-refused"; declaration.Error = error.Message;
                    row.Failures.Add(new { lane = "nif-physics-link", block = block.Index, type = block.TypeName,
                        field, targetBlock = index, targetType = declaration.TargetType, error = error.Message });
                }
            }
        }
    }
}
