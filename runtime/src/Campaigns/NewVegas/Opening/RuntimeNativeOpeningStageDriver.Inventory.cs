using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutInventoryCommands? _inventoryCommands;
    internal FalloutInventoryCommands InventoryCommands
    {
        get
        {
            return _inventoryCommands ??= new(_pluginStack,
        _scripts.References!, _inventory, () => SourcePlayerLevel, _globals, reference =>
        {
            foreach (var actor in ReferencePresentation().Actors.Where(actor => actor.Appearance.Reference == reference))
                actor.Combat?.PrepareInventoryChange();
        }, weaponDrawn: reference => reference == _pluginStack.RuntimeFormKey(0x14) ? _player.WeaponDrawn :
            ReferencePresentation().Nodes.GetValueOrDefault(reference) switch
            {
                RuntimeNativeNpc actor => actor.Activity.WeaponDrawn,
                RuntimeNativeCreature creature => creature.Activity.WeaponDrawn,
                _ => throw new NotSupportedException($"Weapon draw query for {reference} has no active actor owner."),
            }, consumeIngestible: (reference, item) =>
            {
                if (reference == _pluginStack.RuntimeFormKey(0x14)) { UseAid(item); return; }
                var combat = ReferencePresentation().Nodes.GetValueOrDefault(reference) switch
                {
                    RuntimeNativeNpc actor => actor.Combat,
                    RuntimeNativeCreature creature => creature.Combat,
                    _ => null,
                };
                (combat ?? throw new NotSupportedException("Actor ingestion has no resident gameplay owner.")).UseIngestible(item);
            });
        }
    }

    internal void UseInventoryHotkey(byte slot)
    {
        var selected = _inventory.Hotkey(slot);
        if (selected is null) return;
        var item = _inventory.Item(selected.Form)!;
        if (item.RecordType is "ALCH" or "INGR")
        {
            UseAid(selected.Form, selected.Variant, _inventory.Revision);
        }
        else
        {
            _inventory.SelectInstance(selected.Form, selected.Variant, _inventory.Revision);
            _inventory.Equip(_pluginStack, selected.Form);
        }
    }
}
