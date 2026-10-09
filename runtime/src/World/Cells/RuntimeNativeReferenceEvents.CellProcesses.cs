using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferenceEvents
{
    // Binding keys and returned native objects come from this real event
    // factory, including its primitive contact consumer. A metadata-only
    // reference or presentation registration cannot assert its completion.
    internal IReadOnlyList<Node> SourceCellNativeConsumers(FalloutFormKey reference)
    {
        if (_scripts is null || !_bindings.TryGetValue(reference, out var binding) || binding.Instance.Reference != reference ||
            binding.Reference.FormKey != reference || binding.Instance.Base != binding.Reference.Base)
            throw new NotSupportedException("Actual source reference-event construction has not returned for this CELL child.");
        var requiredPrimitive = binding.Instance.Script is not null
            ? FalloutReferencePrimitive.Read(_records.GetEffective(reference)) : null;
        if (requiredPrimitive is not null && (binding.Trigger is null || !GodotObject.IsInstanceValid(binding.Trigger)))
            throw new NotSupportedException(binding.Instance.ScriptError ?? "Actual source primitive contact factory has no living native result.");
        var nodes = new List<Node>(2);
        if (binding.Node is not null)
        {
            RuntimeNativeCellProcessAttachment.RequireNativeReference(binding.Node, reference);
            nodes.Add(binding.Node);
        }
        if (binding.Trigger is not null) nodes.Add(binding.Trigger);
        return nodes.DistinctBy(node => node.GetInstanceId()).ToArray();
    }
}
