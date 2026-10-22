using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private NativeActorWeaponAttachment? _packageWeapon;
    private NativeActorCombatAnimation? _packageAim, _packageGrip;
    private bool _packageWeaponPrepared;
    private uint _packageWeaponType;
    private bool _packageAimSneaking;
    private FalloutPluginRecord? _packageWeaponPolicySource;
    private bool _packageWeaponsVisible = true;
    internal uint WeaponAnimationType => _enemyWeapon?.WeaponAnimationType ?? _packageWeaponType;
    internal uint ScriptWeaponAnimationType => _enemyWeapon is not null || _packageWeapon is not null
        ? FalloutWeaponPresentation.ScriptAnimationType(WeaponAnimationType) : 0;
    internal NativeActorWeaponAttachment? AnimationWeapon => OwnsPose ? _enemyObject : _packageWeaponsVisible ? _packageWeapon : null;

    internal bool PackageIsEquipped(FalloutFormKey item)
    {
        var context = _context ?? throw new NotSupportedException("Actor equipment query has no gameplay context.");
        return _world.IsEquipped(_state.Reference, item, context.Level(), context.Globals);
    }

    internal void PrepareInventoryChange()
    {
        if (OwnsPose || _enemyWeapon is not null || _packageWeapon is not null)
            throw new NotSupportedException("Changing a presented actor weapon requires its animation and attachment retirement owner.");
        _packageWeaponPrepared = false;
    }

    private void PreparePackageWeapon(FalloutPluginRecord package, bool drawn, bool sneaking)
    {
        if (OwnsPose) return;
        if (_packageWeaponPolicySource != package)
        {
            _packageWeaponsVisible = FalloutScriptPackage.Read(package).WeaponsVisible;
            _packageWeaponPolicySource = package;
        }
        _enemyObject?.Root.Hide();
        if (!drawn || !_packageWeaponsVisible || _actor is not RuntimeNativeNpc)
        {
            _packageWeapon?.Root.Hide();
            Activity.SetWeaponDrawn(false);
            return;
        }
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
                if (weapon.Grip != 255)
                {
                    if (weapon.Grip is < 230 or > 235) throw new NotSupportedException("Patrol weapon grip is unbound.");
                    _packageGrip = new(SelectPath(directory, weapon.AnimationGroup + "handgrip" + (weapon.Grip - 229)), _content, _skeleton, _packageWeapon, true);
                }
            }
        }
        if (_packageWeapon is not null && (_packageAim is null || _packageAimSneaking != sneaking))
        {
            var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
            _packageAim = new(SelectPath(directory, (sneaking ? "sneak" : "") + _packageWeapon.Weapon.AnimationGroup + "aim"),
                _content, _skeleton, _packageWeapon, true);
            _packageAimSneaking = sneaking;
        }
        _packageWeapon?.Root.Show(); Activity.SetWeaponDrawn(_packageWeapon is not null);
    }
}
