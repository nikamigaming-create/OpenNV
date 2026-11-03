using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativeReferenceEvents
{
    private readonly Dictionary<FalloutFormKey, string> _inventoryScriptErrors = [];
    private long _inventoryScriptBlocks;
    private Func<bool>? _continueInventoryGameMode, _continueInventoryMenuEvents;

    private void AdvancePlayerInventoryScripts(double seconds, bool gameMode)
    {
        if (Player is not null) AdvanceInventoryScripts(_records.RuntimeFormKey(0x14), seconds, gameMode);
    }

    private void AdvanceInventoryScripts(FalloutFormKey container, double seconds, bool gameMode)
    {
        try
        {
            var results = _scripts.AdvanceInventory(container, seconds, gameMode,
                gameMode ? _continueInventoryGameMode : _continueInventoryMenuEvents);
            _inventoryScriptBlocks += results.Sum(value => value.Blocks);
            var failures = results.Where(value => value.Error is not null)
                .Select(value => $"{value.Item}/{value.Identity}: {value.Error}").ToArray();
            if (failures.Length != 0) ReportInventoryScriptError(container, string.Join("; ", failures));
            else _inventoryScriptErrors.Remove(container);
        }
        catch (Exception error) { ReportInventoryScriptError(container, error.Message); }
    }

    private void ReportInventoryScriptError(FalloutFormKey container, string error)
    {
        if (_inventoryScriptErrors.TryGetValue(container, out var previous) && previous == error) return;
        _inventoryScriptErrors[container] = error;
        ReportDivergence($"OPENNV_NATIVE_INVENTORY_SCRIPT_DIVERGENCE {container}: {error}");
    }

    // A menu can pause ordinary world GameMode while still admitting native
    // inventory equipment events. This child owns only that pending-event
    // phase; it does not advance actors, quests, GameMode or source clocks.
    private sealed partial class RuntimeNativeInventoryEventDriver : Node
    {
        private readonly RuntimeNativeReferenceEvents _owner;
        internal RuntimeNativeInventoryEventDriver(RuntimeNativeReferenceEvents owner)
        {
            _owner = owner; Name = "InventoryEquipmentEvents"; ProcessMode = ProcessModeEnum.Always;
        }
        public override void _Process(double delta)
        {
            if (!GetTree().Paused || !_owner.IsProcessing() || _owner.IsQueuedForDeletion()) return;
            for (Node? ancestor = _owner; ancestor is not null; ancestor = ancestor.GetParent())
                if (ancestor.ProcessMode == ProcessModeEnum.Disabled) return;
            _owner.AdvancePlayerInventoryScripts(0, false);
            foreach (var binding in _owner._bindings.Values)
            {
                if (!_owner.IsProcessing()) return;
                if (binding.Instance.Inventory is not null && _owner._world.IsEnabled(binding.Reference.FormKey))
                    _owner.AdvanceInventoryScripts(binding.Reference.FormKey, 0, false);
            }
        }
    }
}
