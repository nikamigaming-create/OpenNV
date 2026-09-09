using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// One death presentation for the existing actor. Source bodies keep their bone
// attachment frames; physics publication replaces the living animation owner.
internal sealed partial class RuntimeNativeActorRagdoll : Node3D
{
    private sealed record Body(int Source, int Bone, Transform3D Attachment, RigidBody3D Node);
    private readonly List<Body> _bodies = [];
    private readonly List<Rid> _joints = [];
    private RuntimeNativeNifSkeleton _skeleton = null!;
    private FalloutReferenceInstance _state = null!;
    private string _sourceHash = "";
    private readonly Dictionary<int, Body> _bySource = [];
    private bool _active;
    internal bool Active => _active;
    internal object Observation => new
    {
        active = _active,
        bodies = _bodies.Count,
        joints = _joints.Count,
        source = _sourceHash,
        boundary = "Godot-6dof-angular-envelope;Havok-cone-friction-inertia-and-malleable-solver-parity-unverified"
    };

    internal static RuntimeNativeActorRagdoll Prepare(Node3D actor, RuntimeNativeNifSkeleton skeleton,
        FalloutReferenceInstance state, RuntimeLiveContentSource content, string path, uint layer, uint mask)
    {
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Ragdoll skeleton is absent: " + path);
        var result = new RuntimeNativeActorRagdoll
        {
            Name = "SourceDeathRagdoll",
            _skeleton = skeleton,
            _state = state,
            _sourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
        try
        {
            var source = skeleton.Source;
            foreach (var block in source.Blocks.Where(block => block.TypeName is "NiNode" or "NiBone" or "BSFadeNode"))
            {
                var node = source.ReadNode(block.Index);
                if (node.CollisionObject < 0) continue;
                var collision = (FalloutNifCollisionObject)source.ReadObject(node.CollisionObject);
                var body = (FalloutNifRigidBody)source.ReadObject(collision.Body);
                if (body.Mass <= 0) throw new NotSupportedException("Death ragdoll contains a massless body.");
                var built = NativeNifCollisionBuilder.Build(source, collision, skeleton.UnitsToMetres, layer);
                try
                {
                    var rigid = new RigidBody3D
                    {
                        Name = "SourceBody_" + body.Block.Index,
                        Mass = body.Mass,
                        Freeze = true,
                        TopLevel = true,
                        CollisionLayer = 0,
                        CollisionMask = mask,
                        LinearDampMode = RigidBody3D.DampMode.Replace,
                        LinearDamp = body.LinearDamping,
                        AngularDampMode = RigidBody3D.DampMode.Replace,
                        AngularDamp = body.AngularDamping,
                        PhysicsMaterialOverride = new() { Friction = body.Friction, Bounce = body.Restitution },
                        ContinuousCd = true
                    };
                    result.AddChild(rigid);
                    foreach (var shape in built.Body.GetChildren().OfType<CollisionShape3D>().ToArray())
                    {
                        built.Body.RemoveChild(shape); rigid.AddChild(shape);
                    }
                    rigid.SetMeta("opennv_nif_collision_bone", node.Name);
                    rigid.SetMeta("opennv_nif_collision_body", body.Block.Index);
                    rigid.SetMeta("opennv_reference_form_key", state.Reference.ToString());
                    var entry = new Body(body.Block.Index, skeleton.BoneIndex(node.Name), built.Body.Transform, rigid);
                    result._bodies.Add(entry); result._bySource.Add(entry.Source, entry);
                }
                finally { built.Body.Free(); }
            }
            if (result._bodies.Count == 0) throw new NotSupportedException("Actor has no source ragdoll bodies.");
            // Decode and validate every reached constraint before health changes.
            var joints = result._bodies.SelectMany(body => ((FalloutNifRigidBody)source.ReadObject(body.Source)).Constraints)
                .Where(index => index >= 0).Distinct().Select(source.ReadRagdollConstraint).ToArray();
            if (joints.Any(joint => !result._bySource.ContainsKey(joint.Header.EntityA) || !result._bySource.ContainsKey(joint.Header.EntityB)))
                throw new InvalidDataException("Ragdoll joint leaves the actor's body graph.");
            result._bodies.Sort((left, right) => left.Bone.CompareTo(right.Bone));
            actor.AddChild(result);
            if (!result.IsInsideTree()) throw new InvalidOperationException("Death rig did not enter the actor's live scene.");
            foreach (var body in result._bodies)
                body.Node.GlobalTransform = skeleton.Node.GlobalTransform * skeleton.Node.GetBoneGlobalPose(body.Bone) * body.Attachment;
            foreach (var joint in joints) result.BuildJoint(joint);
            result.SetMeta("opennv_body_layer", layer);
            return result;
        }
        catch { result.Free(); throw; }
    }

    private void BuildJoint(FalloutNifRagdollConstraint source)
    {
        var first = _bySource[source.Header.EntityA]; var second = _bySource[source.Header.EntityB];
        var joint = PhysicsServer3D.JointCreate(); _joints.Add(joint);
        var scale = _skeleton.Node.GlobalBasis.Scale;
        if (MathF.Abs(scale.X - scale.Y) > .001f || MathF.Abs(scale.X - scale.Z) > .001f)
            throw new NotSupportedException("Nonuniform ragdoll scale is unsupported.");
        Transform3D Frame(FalloutNifVector3 twist, FalloutNifVector3 plane, FalloutNifVector3 motor, FalloutNifVector3 pivot)
        {
            static Vector3 Convert(FalloutNifVector3 value) => GamebryoCoordinate.ConvertVector(new(value.X, value.Y, value.Z));
            var basis = new Basis(Convert(twist), Convert(plane), Convert(motor));
            if (MathF.Abs(MathF.Abs(basis.Determinant()) - 1) > .01f)
                throw new InvalidDataException("Source ragdoll joint axes are not orthonormal.");
            // Havok exports may carry a left-handed motor direction. Its two
            // declared rotation axes define the proper basis used by Godot.
            basis = new(basis.X.Normalized(), basis.Y.Normalized(), basis.X.Cross(basis.Y).Normalized());
            return new(basis, Convert(pivot) * (7 * _skeleton.UnitsToMetres * scale.X));
        }
        PhysicsServer3D.JointMakeGeneric6Dof(joint, first.Node.GetRid(),
            Frame(source.TwistA, source.PlaneA, source.MotorA, source.PivotA), second.Node.GetRid(),
            Frame(source.TwistB, source.PlaneB, source.MotorB, source.PivotB));
        PhysicsServer3D.JointDisableCollisionsBetweenBodies(joint, true);
        for (var axis = 0; axis < 3; axis++)
        {
            var index = (Vector3.Axis)axis;
            PhysicsServer3D.Generic6DofJointSetFlag(joint, index, PhysicsServer3D.G6DofJointAxisFlag.EnableLinearLimit, true);
            PhysicsServer3D.Generic6DofJointSetParam(joint, index, PhysicsServer3D.G6DofJointAxisParam.LinearLowerLimit, 0);
            PhysicsServer3D.Generic6DofJointSetParam(joint, index, PhysicsServer3D.G6DofJointAxisParam.LinearUpperLimit, 0);
            PhysicsServer3D.Generic6DofJointSetFlag(joint, index, PhysicsServer3D.G6DofJointAxisFlag.EnableAngularLimit, true);
            var lower = axis == 0 ? source.TwistMinimum : axis == 1 ? source.PlaneMinimum : -source.Cone;
            var upper = axis == 0 ? source.TwistMaximum : axis == 1 ? source.PlaneMaximum : source.Cone;
            PhysicsServer3D.Generic6DofJointSetParam(joint, index, PhysicsServer3D.G6DofJointAxisParam.AngularLowerLimit, lower);
            PhysicsServer3D.Generic6DofJointSetParam(joint, index, PhysicsServer3D.G6DofJointAxisParam.AngularUpperLimit, upper);
        }
    }

    internal void Activate()
    {
        if (_active) return;
        if (_state.Injury?.Dead != true) throw new InvalidOperationException("A living actor cannot activate a death ragdoll.");
        var saved = _state.Ragdoll;
        if (saved is not null)
        {
            saved.Validate();
            if (saved.SkeletonSha256 != _sourceHash || !saved.Bodies.Select(body => body.SourceBody).Order().SequenceEqual(_bySource.Keys.Order()))
                throw new InvalidDataException("Saved ragdoll differs from its winning source skeleton.");
        }
        foreach (var body in _bodies)
        {
            var snapshot = saved?.Bodies.Single(value => value.SourceBody == body.Source);
            body.Node.GlobalTransform = snapshot is null
                ? _skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(body.Bone) * body.Attachment : ReadTransform(snapshot.Transform);
            GamebryoReferenceEnableRuntime.SetCollisionFilter(body.Node, GetMeta("opennv_body_layer").AsUInt32(), body.Node.CollisionMask);
            body.Node.Freeze = false;
            if (snapshot is not null)
            {
                body.Node.LinearVelocity = Vector(snapshot.LinearVelocity); body.Node.AngularVelocity = Vector(snapshot.AngularVelocity);
                body.Node.Sleeping = snapshot.Sleeping;
            }
        }
        _active = true; _state.CaptureRagdoll = Capture;
        Publish();
    }

    public override void _Process(double delta) { if (_active) Publish(); }

    private void Publish()
    {
        var inverse = _skeleton.Node.GlobalTransform.AffineInverse();
        foreach (var body in _bodies)
        {
            var target = inverse * body.Node.GlobalTransform * body.Attachment.AffineInverse();
            var parent = _skeleton.Node.GetBoneParent(body.Bone);
            _skeleton.Node.SetBonePose(body.Bone, parent < 0 ? target : _skeleton.Node.GetBoneGlobalPose(parent).AffineInverse() * target);
        }
    }

    internal FalloutActorRagdollState Capture() => new(_sourceHash, _bodies.Select(body => new FalloutRagdollBodyState(body.Source,
        WriteTransform(body.Node.Transform), WriteVector(body.Node.LinearVelocity), WriteVector(body.Node.AngularVelocity), body.Node.Sleeping)).ToArray());

    public override void _ExitTree()
    {
        if (_active) { _state.Ragdoll = Capture(); _state.CaptureRagdoll = null; }
        foreach (var joint in _joints) PhysicsServer3D.FreeRid(joint);
        _joints.Clear();
    }

    private static float[] WriteVector(Vector3 value) => [value.X, value.Y, value.Z];
    private static Vector3 Vector(float[] value) => new(value[0], value[1], value[2]);
    private static float[] WriteTransform(Transform3D value) => [value.Basis.X.X, value.Basis.X.Y, value.Basis.X.Z,
        value.Basis.Y.X, value.Basis.Y.Y, value.Basis.Y.Z, value.Basis.Z.X, value.Basis.Z.Y, value.Basis.Z.Z,
        value.Origin.X, value.Origin.Y, value.Origin.Z];
    private static Transform3D ReadTransform(float[] value)
    {
        var result = new Transform3D(new(value[0], value[1], value[2]), new(value[3], value[4], value[5]),
            new(value[6], value[7], value[8]), new(value[9], value[10], value[11]));
        if (MathF.Abs(result.Basis.Determinant()) < .0001f) throw new InvalidDataException("Saved ragdoll transform is singular.");
        return result;
    }
}
