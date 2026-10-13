using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed partial class RuntimeNativeLivingPhysicsBackend
{
    private sealed class NativeConstraint : IFalloutLivingConstraintBackend
    {
        private readonly RuntimeNativeLivingPhysicsBackend _owner;
        private readonly FalloutNifPhysicalConstraint _source;
        private bool _retired;
        internal NativeConstraint(RuntimeNativeLivingPhysicsBackend owner, FalloutNifPhysicalConstraint source,
            NativeBody first, NativeBody second)
        {
            _owner = owner; _source = source; First = first; Second = second;
            Handle = PhysicsServer3D.JointCreate();
            if (!Handle.IsValid) throw new InvalidDataException("Living joint factory lost its actual constraint resource.");
        }
        internal RuntimeNativeLivingPhysicsBackend Owner => _owner;
        internal NativeBody First { get; }
        internal NativeBody Second { get; }
        internal Rid Handle { get; private set; }
        internal bool Attached { get; set; }
        public bool HasPhysicalObject { get { if (_retired) return false; RequireCurrent(); return Handle.IsValid; } }
        public bool IsWorldAttached
        { get { RequireCurrent(); return Attached && PhysicsServer3D.JointGetType(Handle) == PhysicsServer3D.JointType.Type6Dof; } }
        public ulong PhysicalObjectId { get { RequireCurrent(); return Handle.Id; } }
        public void RequireCurrent()
        { _owner.RequireCurrent(); ObjectDisposedException.ThrowIf(_retired, this); First.RequireCurrent(); Second.RequireCurrent(); }

        internal void Admit()
        {
            var source = _source.Declaration;
            var first = source switch
            {
                FalloutNifRagdollConstraint ragdoll => Frame(ragdoll.TwistA, ragdoll.PlaneA, ragdoll.PivotA),
                FalloutNifHingeConstraint hinge => Frame(hinge.AxisA, hinge.PerpendicularA1, hinge.PivotA),
                _ => throw new NotSupportedException("Living structural joint frame is unowned."),
            };
            var second = source switch
            {
                FalloutNifRagdollConstraint ragdoll => Frame(ragdoll.TwistB, ragdoll.PlaneB, ragdoll.PivotB),
                FalloutNifHingeConstraint hinge => Frame(hinge.AxisB, hinge.PerpendicularB1, hinge.PivotB),
                _ => throw new NotSupportedException("Living structural joint frame is unowned."),
            };
            PhysicsServer3D.JointMakeGeneric6Dof(Handle, First.Handle, first, Second.Handle, second);
            PhysicsServer3D.JointDisableCollisionsBetweenBodies(Handle, true);
            for (var axis = 0; axis < 3; axis++)
            {
                var dimension = (Vector3.Axis)axis;
                PhysicsServer3D.Generic6DofJointSetFlag(Handle, dimension, PhysicsServer3D.G6DofJointAxisFlag.EnableLinearLimit, true);
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.LinearLowerLimit, 0);
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.LinearUpperLimit, 0);
                var lower = 0f; var upper = 0f; var bounded = true;
                if (source is FalloutNifRagdollConstraint angular)
                {
                    lower = axis == 0 ? angular.TwistMinimum : axis == 1 ? angular.PlaneMinimum : -angular.Cone;
                    upper = axis == 0 ? angular.TwistMaximum : axis == 1 ? angular.PlaneMaximum : angular.Cone;
                }
                else if (source is FalloutNifHingeConstraint hinge && axis == 0)
                {
                    lower = hinge.Minimum ?? 0; upper = hinge.Maximum ?? 0;
                    bounded = hinge.Minimum.HasValue && hinge.Maximum.HasValue;
                }
                PhysicsServer3D.Generic6DofJointSetFlag(Handle, dimension, PhysicsServer3D.G6DofJointAxisFlag.EnableAngularLimit, bounded);
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.AngularLowerLimit, lower);
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.AngularUpperLimit, upper);
            }
            Attached = true;
        }

        private Transform3D Frame(FalloutNifVector3 axis, FalloutNifVector3 plane, FalloutNifVector3 pivot)
        {
            var x = GamebryoCoordinate.ConvertVector(new(axis.X, axis.Y, axis.Z));
            var y = GamebryoCoordinate.ConvertVector(new(plane.X, plane.Y, plane.Z));
            const float orthogonalTolerance = .001f;
            if (MathF.Abs(x.LengthSquared() - 1) > orthogonalTolerance || MathF.Abs(y.LengthSquared() - 1) > orthogonalTolerance ||
                MathF.Abs(x.Dot(y)) > orthogonalTolerance)
                throw new InvalidDataException("Living source joint frame is not orthonormal.");
            x = x.Normalized(); y = y.Normalized();
            return new(new(x, y, x.Cross(y).Normalized()), _owner.ToNative(FalloutPhysicalVector3.FromValues(pivot.X, pivot.Y, pivot.Z)));
        }

        public void Dispose()
        {
            if (_retired) return; _owner.RequireCurrent();
            if (Handle.IsValid) { PhysicsServer3D.FreeRid(Handle); Handle = default; }
            Attached = false; _retired = true;
        }
    }
}
