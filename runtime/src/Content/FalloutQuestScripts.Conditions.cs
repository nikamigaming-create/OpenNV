using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutQuestScripts
{
    private FalloutConditionRuntime Conditions(FalloutQuestScriptHost? host)
    {
        var inventory = host?.Inventory ?? (References is { } world ? new FalloutInventoryCommands(_records, world, _inventory,
            () => throw new NotSupportedException("NPC inventory has no player-level owner."), _globals) : null);
        return new(_records, _quests, References)
        {
            Globals = _globals,
            Hour = () => host?.GameTime?.Hour ?? (_globals ?? throw new NotSupportedException("Quest has no game clock."))
                .Get(FalloutGameTimeBindings.Read(_records).Hour),
            RandomPercent = ScriptValues.RandomPercent,
            ActorValue = (subject, index) =>
            {
                var name = FalloutConditionRuntime.ActorValueName((uint)index);
                if (subject == _records.RuntimeFormKey(0x14))
                    return (float)(host?.ReadPlayerActorValue is { } read ? read(name, FalloutActorValueRead.Current) :
                        (host ?? throw new NotSupportedException("Quest has no player value owner.")).PlayerActorValue(name));
                return (References ?? throw new NotSupportedException("Quest has no actor value owner.")).ActorValue(subject, name);
            },
            ItemCount = (subject, item) => inventory is not null ? inventory.ItemCount(subject, item) :
                subject == _records.RuntimeFormKey(0x14) ? FalloutInventoryItemCounts.Count(_records, _inventory, item, References?.FormLists) :
                throw new NotSupportedException("Quest has no actor inventory owner."),
            Equipped = (subject, item) => host?.Inventory is { } equipment ? equipment.IsEquipped(subject, item) :
                subject == _records.RuntimeFormKey(0x14) && inventory is not null ? inventory.IsEquipped(subject, item) : null,
            WeaponOut = host?.Inventory is { } weapons ? weapons.IsWeaponOut : null,
            WeaponAnimationType = subject => host?.Inventory is { } types ? types.WeaponAnimationType(subject) :
                subject == _records.RuntimeFormKey(0x14) && inventory is not null ? inventory.WeaponAnimationType(subject) : null,
            CurrentPackage = host?.CurrentPackage,
            HeadingAngle = (caller, target) => (host?.HeadingAngle ??
                throw new NotSupportedException("GetHeadingAngle has no spatial owner."))(caller, target),
            Sitting = host?.Sitting,
            InCell = host?.IsInCell,
            Vampire = ActorQueries.GetVampire,
        };
    }
}
