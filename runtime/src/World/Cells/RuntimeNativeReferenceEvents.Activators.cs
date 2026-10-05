using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferenceEvents
{
    private void DefaultActivateAnimatedActivator(Binding binding, FalloutFormKey actor)
    {
        var reference = binding.Reference.FormKey;
        var node = binding.Node;
        if (node is null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
            throw new NotSupportedException($"Default activation of ACTI {reference} has no resident source presentation.");
        var motions = node.GetChildren().OfType<RuntimeNativeDoorMotion>().ToArray();
        if (motions.Length != 1 || !GodotObject.IsInstanceValid(motions[0]) || !motions[0].IsInsideTree())
            throw new NotSupportedException($"Default activation of ACTI {reference} has no unique admitted source Open/Close motion owner.");
        // This is reached only by the shared source event's default action.
        // Authored suppression, action identity and consumed failures stay in
        // that event owner; the existing source clock owns pose and collision.
        motions[0].Activate();
        GD.Print($"OPENNV_NATIVE_ACTIVATOR_DEFAULT reference={reference} actor={actor} source=owned-Open-Close-motion");
    }
}
