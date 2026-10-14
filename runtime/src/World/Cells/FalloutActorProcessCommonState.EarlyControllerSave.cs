using OpenNV.Runtime.World.Physics;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessCommonState
{
    // Replace the blanket EarlyControllerSaveBlocker expression with this
    // getter. The World also requires its actual factory/resource readiness.
    private string? ReadEarlyControllerSaveBlocker()
    {
        if (_enteredEarlyControllerStore is not null)
            return "source-current-controller-field-publication-in-flight";
        foreach (var entry in _current.Values)
        {
            if (!_earlyControllerFields.TryGetValue(entry.Source.Reference, out var field))
            {
                if (entry.SceneFields?.PostSceneContext is not null)
                    return "source-cold-current-controller-resource-publication-unbound";
                continue;
            }
            if (field.Failure is not null) return field.Failure;
            if (field.Controller.SaveBlocker is { } failure) return failure;
            if (entry.SceneFields?.PostSceneContext != field.Controller.Identity || field.Epoch != entry.Epoch ||
                field.Controller.Lineage.CommonProcess != _process || field.Controller.Lineage.Actor != entry.Source ||
                field.Controller.Lineage.Epoch != entry.Epoch || field.Lease is null ||
                field.Stores.Count == 0 || field.Stores.Any(value => !value.Returned) ||
                field.ColdStores.Any(value => !value.Returned))
                return "source-current-controller-pointer-retain-unreturned";
            field.Lease.RequireCurrent();
            // These component cold constructors exist and are already tested.
            // Unsupported payloads remain specific failures at their owner.
            FalloutEarlyControllerFactory.RequireColdArrays(field.Controller.Capture().Proxy);
        }
        return null;
    }
}
