using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private NativeActorWeaponAttachment? _packageWeapon;
    private NativeActorCombatAnimation? _packageAim, _packageGrip;
    private bool _packageWeaponPrepared;
    private uint _packageWeaponType;
    internal uint WeaponAnimationType => _enemyWeapon?.WeaponAnimationType ?? _packageWeaponType;

    private void PreparePackageWeapon(bool drawn)
    {
        if (OwnsPose) return;
        _enemyObject?.Root.Hide();
        if (!drawn || _actor is not RuntimeNativeNpc) { Activity.SetWeaponDrawn(false); return; }
        if (!_packageWeaponPrepared)
        {
            _packageWeaponPrepared = true;
            if (SelectCombatWeapon() is { } item)
            {
                var weapon = FalloutWeaponPresentation.Read(_records, item.FormKey, false);
                _packageWeaponType = weapon.WeaponAnimationType;
                _world.Inventory(_state.Reference, _context!.Level(), _context.Globals).Contents.Equip(_records, item.FormKey);
                _packageWeapon = new(weapon, _skeleton, _content);
                var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
                _packageAim = new(SelectPath(directory, weapon.AnimationGroup + "aim"), _content, _skeleton, _packageWeapon, true);
                if (weapon.Grip != 255)
                {
                    if (weapon.Grip is < 230 or > 235) throw new NotSupportedException("Patrol weapon grip is unbound.");
                    _packageGrip = new(SelectPath(directory, weapon.AnimationGroup + "handgrip" + (weapon.Grip - 229)), _content, _skeleton, _packageWeapon, true);
                }
            }
        }
        _packageWeapon?.Root.Show(); Activity.SetWeaponDrawn(_packageWeapon is not null);
    }
}
