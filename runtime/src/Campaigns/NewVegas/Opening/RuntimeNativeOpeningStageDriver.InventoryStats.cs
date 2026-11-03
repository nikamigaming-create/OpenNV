using System.Buffers.Binary;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutActorDefense ReadSharedPlayerDefense()
    {
        _incomingDefense ??= new(_pluginStack);
        var armor = _inventory.Equipped.Select(_pluginStack.RuntimeFormKey)
            .Where(key => _pluginStack.GetEffective(key).Signature == "ARMO").ToArray();
        return _incomingDefense.Read(_raceSexContract.Player, _inventory, armor);
    }

    internal FalloutActorDefense PlayerInventoryDefense()
    {
        // The shared incoming-damage owner already includes base-actor SPLO
        // and equipped armor EITM. Do not present a complete AV when current
        // race/perk sources add a defense lane outside that owner.
        var ownedSpells = new HashSet<FalloutFormKey>();
        var source = FalloutActorTemplateOwner.Resolve(_pluginStack, _pluginStack.GetEffective(_raceSexContract.Player), 8);
        foreach (var (record, fieldName) in new[] { (source, "SPLO") }.Concat(_inventory.Equipped
            .Select(_pluginStack.RuntimeFormKey).Select(_pluginStack.GetEffective)
            .Where(record => record.Signature == "ARMO").Select(record => (record, "EITM"))))
            foreach (var field in record.ReadSubrecords().Where(field => field.Signature == fieldName))
            {
                if (field.Data.Length != 4) throw new InvalidDataException("Defense source spell identity has invalid extent.");
                if (record.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) is { } spell)
                    ownedSpells.Add(spell);
            }
        foreach (var value in new[] { 18, 76 })
            foreach (var pool in new[] { FalloutActorValuePool.Permanent, FalloutActorValuePool.Temporary, FalloutActorValuePool.Damage })
                if (_playerSkills.Modifiers(value, pool).Any(effect => !ownedSpells.Contains(effect.Spell)))
                    throw new NotSupportedException("Player defense requires its additional current race/perk modifier owner.");
        return ReadSharedPlayerDefense();
    }

    internal bool InventoryHardcore => _scripts.Session.Hardcore;
}
