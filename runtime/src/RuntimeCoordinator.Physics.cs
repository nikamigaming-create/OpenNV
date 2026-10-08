using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private object[] CaptureNativeRigidBodies() => _nativeReferencePresentation?.Nodes
        .SelectMany(reference => NodeTraversal.SelfAndDescendants<RuntimeNifRigidBody>(reference.Value)
            .Select(body => (object)new
            {
                reference = reference.Key.ToString(),
                model = reference.Value.GetMeta("opennv_source_model", "").AsString(),
                path = body.GetPath().ToString(),
                sourceBody = body.GetMeta("opennv_nif_collision_body").AsInt32(),
                position = new[] { body.GlobalPosition.X, body.GlobalPosition.Y, body.GlobalPosition.Z },
                rotation = new[] { body.Quaternion.X, body.Quaternion.Y, body.Quaternion.Z, body.Quaternion.W },
                linearVelocity = new[] { body.LinearVelocity.X, body.LinearVelocity.Y, body.LinearVelocity.Z },
                angularVelocity = new[] { body.AngularVelocity.X, body.AngularVelocity.Y, body.AngularVelocity.Z },
                scale = new[] { body.GlobalBasis.Scale.X, body.GlobalBasis.Scale.Y, body.GlobalBasis.Scale.Z },
                body.Mass,
                body.Sleeping,
                body.Freeze,
                body.CollisionLayer,
                body.CollisionMask,
            })).ToArray() ?? [];
}
