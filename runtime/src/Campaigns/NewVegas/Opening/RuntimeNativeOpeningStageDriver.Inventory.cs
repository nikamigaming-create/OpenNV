using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutInventoryCommands? _inventoryCommands;
    internal FalloutInventoryCommands InventoryCommands => _inventoryCommands ??= new(_pluginStack,
        _scripts.References!, _inventory, () => SourcePlayerLevel, _globals, reference =>
        {
            foreach (var actor in ReferencePresentation().Actors.Where(actor => actor.Appearance.Reference == reference))
                actor.Combat?.PrepareInventoryChange();
        });
}
