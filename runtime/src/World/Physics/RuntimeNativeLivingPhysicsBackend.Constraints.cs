using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Physics;

internal sealed partial class RuntimeNativeLivingPhysicsBackend
{
    private sealed class NativeConstraint : IFalloutLivingConstraintBackend
    {
        private readonly RuntimeNativeLivingPhysicsBackend _owner;
        private readonly FalloutLivingJointParameters _parameters;
        private bool _retired;
        internal NativeConstraint(RuntimeNativeLivingPhysicsBackend owner, FalloutLivingJointParameters parameters,
            NativeBody first, NativeBody second)
        {
            _owner = owner; _parameters = parameters; First = first; Second = second;
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
            _parameters.Validate();
            var first = Frame(_parameters.First);
            var second = Frame(_parameters.Second);
            PhysicsServer3D.JointMakeGeneric6Dof(Handle, First.Handle, first, Second.Handle, second);
            PhysicsServer3D.JointDisableCollisionsBetweenBodies(Handle, true);
            for (var axis = 0; axis < 3; axis++)
            {
                var dimension = (Vector3.Axis)axis;
                PhysicsServer3D.Generic6DofJointSetFlag(Handle, dimension, PhysicsServer3D.G6DofJointAxisFlag.EnableLinearLimit, true);
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.LinearLowerLimit, 0);
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.LinearUpperLimit, 0);
                var limit = _parameters.Angular[axis];
                PhysicsServer3D.Generic6DofJointSetFlag(Handle, dimension, PhysicsServer3D.G6DofJointAxisFlag.EnableAngularLimit, limit.Enabled);
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.AngularLowerLimit, BitConverter.UInt32BitsToSingle(limit.Lower));
                PhysicsServer3D.Generic6DofJointSetParam(Handle, dimension, PhysicsServer3D.G6DofJointAxisParam.AngularUpperLimit, BitConverter.UInt32BitsToSingle(limit.Upper));
            }
            Attached = true;
        }

        private Transform3D Frame(FalloutLivingJointFrame frame)
        {
            var x = GamebryoCoordinate.ConvertVector(new(frame.Axis.XValue, frame.Axis.YValue, frame.Axis.ZValue));
            var y = GamebryoCoordinate.ConvertVector(new(frame.Plane.XValue, frame.Plane.YValue, frame.Plane.ZValue));
            const float orthogonalTolerance = .001f;
            if (MathF.Abs(x.LengthSquared() - 1) > orthogonalTolerance || MathF.Abs(y.LengthSquared() - 1) > orthogonalTolerance ||
                MathF.Abs(x.Dot(y)) > orthogonalTolerance)
                throw new InvalidDataException("Living source joint frame is not orthonormal.");
            x = x.Normalized(); y = y.Normalized();
            return new(new(x, y, x.Cross(y).Normalized()), _owner.ToNative(frame.Pivot));
        }

        public void Dispose()
        {
            if (_retired) return; _owner.RequireCurrent();
            if (Handle.IsValid) { PhysicsServer3D.FreeRid(Handle); Handle = default; }
            Attached = false; _retired = true;
        }
    }
}
