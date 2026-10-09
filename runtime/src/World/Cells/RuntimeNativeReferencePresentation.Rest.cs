using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferencePresentation
{
    internal FalloutRestObservation ObserveRestBedModel(FalloutSleepWaitBed bed)
    {
        bed.Validate();
        if (Error is not null)
            return new(FalloutRestFactState.Unowned, "actual-reference-presentation", Error);
        if (!_nodes.TryGetValue(bed.Reference, out var model))
            return new(FalloutRestFactState.Unowned, $"current-bed-model:{bed.Reference}",
                "The actual source bed has no loaded model in the current reference presentation.");
        var owners = model.GetChildren().OfType<RuntimeNativeRestFurniturePublication>().ToArray();
        if (owners.Length != 1)
            return new(FalloutRestFactState.Unowned, $"current-bed-model:{bed.Reference}",
                "The actual loaded bed does not own one typed source-model publication lease.");
        return owners[0].Observe(bed);
    }
}
