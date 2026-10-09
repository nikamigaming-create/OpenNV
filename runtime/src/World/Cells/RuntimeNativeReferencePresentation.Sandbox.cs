using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferencePresentation
{
    private Func<FalloutFormKey, FalloutSandboxPublishedReference?>? _sandboxPublication;
    private float _sandboxPublicationUnits;

    internal void BindSandboxPublication(float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        if (!IsInsideTree() || IsQueuedForDeletion())
            throw new NotSupportedException("Sandbox publication requires its actual attached native reference presenter.");
        if (_sandboxPublication is not null)
        {
            if (_sandboxPublicationUnits != unitsToMetres)
                throw new InvalidOperationException("Sandbox publication units changed in the same native lifetime.");
            _world.RequireSandboxReferencePublication(_sandboxPublication);
            return;
        }
        _sandboxPublicationUnits = unitsToMetres;
        _world.BindSandboxReferencePublication(_sandboxPublication = reference =>
        {
            if (Error is not null) throw new InvalidOperationException("Sandbox publication retains reference presentation failure: " + Error);
            // Observe the current actual node. Discovery never calls Resolve,
            // manufactures disabled bodies or materializes a candidate model.
            if (!_nodes.TryGetValue(reference, out var native) || !GodotObject.IsInstanceValid(native) ||
                native.IsQueuedForDeletion() || !native.IsInsideTree()) return null;
            var position = native.GlobalPosition / unitsToMetres;
            var hasModel = native is RuntimeNativeNpc or RuntimeNativeCreature ||
                NodeTraversal.SelfAndDescendants<Node3D>(native).Any(node => node.HasMeta("opennv_nif_fixed_strings"));
            return new(reference, _world.Placement(reference).Cell,
                [position.X, -position.Z, position.Y], hasModel, native.GetInstanceId());
        });
    }

    internal void RetireSandboxPublication()
    {
        if (_sandboxPublication is null) return;
        _world.RetireSandboxReferencePublication(_sandboxPublication);
        _sandboxPublication = null;
    }
}
