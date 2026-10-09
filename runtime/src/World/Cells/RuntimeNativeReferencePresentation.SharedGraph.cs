using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferencePresentation
{
    internal void ForgetDestroyedSourceReference(FalloutFormKey reference, IReadOnlyList<ulong> ownedObjects)
    {
        if (_warmNodes.ContainsKey(reference))
            throw new NotSupportedException("Shared CELL retirement encountered an unaccounted native warm-reference owner.");
        if (_nodes.TryGetValue(reference, out var node))
        {
            if (!GodotObject.IsInstanceValid(node) || !ownedObjects.Contains(node.GetInstanceId()))
                throw new InvalidDataException("Shared source retirement borrowed another reference's native presentation.");
            // Capture the living source animation clock before its native node
            // is destroyed. A capture exception retains the node and bindings.
            UnbindObjectAnimation(reference);
            if (node is RuntimeNativeNpc actor) actor.AppearanceChanged = null;
            if (node.GetChildren().OfType<RuntimeNativeDestructible>().SingleOrDefault() is { } destruction)
                destruction.ModelChanged = null;
            _nodes.Remove(reference);
        }
        _enabled.Remove(reference); _fadeGeometry.Remove(reference); _publishedOpacity.Remove(reference);
    }
}
