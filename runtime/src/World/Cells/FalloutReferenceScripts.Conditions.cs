using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceScripts
{
    private FalloutConditionRuntime? _conditionRuntime;
    private FalloutConditionRuntime Conditions => _conditionRuntime ??= new(records, quests, world)
    {
        Globals = host.Globals,
        Hour = () => host.GameTime?.Hour ?? (host.Globals ??
            throw new NotSupportedException("Script has no game clock.")).Get(FalloutGameTimeBindings.Read(records).Hour),
        RandomPercent = world.ScriptValues.RandomPercent,
        ActorValue = (subject, index) =>
        {
            var name = FalloutConditionRuntime.ActorValueName((uint)index);
            return (float)(host.ReadActorValue is { } read ? read(subject, name, FalloutActorValueRead.Current) :
                host.ActorValue is { } query ? query(subject, name) : world.ActorValue(subject, name));
        },
        ItemCount = host.Inventory is { } inventory ? inventory.ItemCount : null,
        Equipped = host.Inventory is { } equipment ? (subject, item) => equipment.IsEquipped(subject, item) : null,
        IsDead = host.IsDead,
        HealthPercentage = host.HealthPercentage,
        Sitting = host.Sitting,
        CurrentPackage = host.CurrentPackage,
        Distance = host.Distance,
        HeadingAngle = (caller, target) => (host.HeadingAngle ??
            throw new NotSupportedException("GetHeadingAngle has no spatial owner."))(caller, target),
        SameCell = host.IsInSameCell,
        InCell = host.IsInCell,
        Interior = host.IsInInterior,
        InCombat = host.IsInCombat,
        Sneaking = host.IsSneaking,
        WeaponOut = host.Inventory is { } weapons ? weapons.IsWeaponOut : null,
        WeaponAnimationType = host.Inventory is { } weaponTypes ? subject => weaponTypes.WeaponAnimationType(subject) : null,
        OpenState = host.GetOpenState,
        Vampire = world.ActorQueries.GetVampire,
    };
}
