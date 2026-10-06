using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutInventoryCommands? _nativeActorInventoryQueries;

    private double NativeActorItemCount(FalloutFormKey subject, FalloutFormKey item) =>
        (_nativeActorInventoryQueries ??= new(
            _nativePluginStack ?? throw new InvalidOperationException("Actor inventory query has no source stack."),
            _nativeReferences ?? throw new InvalidOperationException("Actor inventory query has no reference world."),
            _nativeInventory, () => _nativeOpeningStageDriver?.PlayerLevel ?? _nativeOpeningRestore?.State.Vitals?.Level ?? 1,
            _nativeGlobals)).ItemCount(subject, item);
}
