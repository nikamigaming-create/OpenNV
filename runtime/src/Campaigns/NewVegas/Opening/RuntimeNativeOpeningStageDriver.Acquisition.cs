using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutReferenceWorld? _acquisitionWorld;
    private FalloutInventoryAcquisition? _inventoryAcquisition;
    internal FalloutInventoryAcquisition InventoryAcquisition
    {
        get
        {
            var world = _scripts.References ?? throw new InvalidOperationException("Inventory acquisition has no world owner.");
            if (!ReferenceEquals(world, _acquisitionWorld))
            {
                _inventoryAcquisition = new(_pluginStack, world, _inventory, _quests, _scripts,
                    () => _stageResults ?? throw new InvalidOperationException("Inventory SetStage has no result owner."),
                    () => SourcePlayerLevel, _globals);
                _acquisitionWorld = world;
                world.InventoryAcquisition = _inventoryAcquisition;
            }
            return _inventoryAcquisition!;
        }
    }
}
