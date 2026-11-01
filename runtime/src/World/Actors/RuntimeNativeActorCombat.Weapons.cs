using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private BoneAttachment3D? _embeddedMuzzle;

    private FalloutCampaignItem? SelectCombatWeapon()
    {
        var owned = _world.Inventory(_state.Reference, _context!.Level(), _context.Globals);
        var handling = new FalloutWeaponHandling(owned.Contents, nativeNpc: true);
        var style = _world.CombatStyle(_state.Reference);
        var choices = owned.Contents.Items.Where(item => item.RecordType == "WEAP" && !owned.Unequipped.Contains(item.FormKey))
            .Select(item => (Item: item, Weapon: _world.WeaponPresentation(item.FormKey, false, owned.Contents.WeaponModMask(item.FormKey))))
            .Where(row => FalloutWeaponCondition.CanUse(row.Weapon, row.Item) &&
                (style?.WeaponRestriction != 1 || row.Weapon.IsMeleeWeapon) &&
                (style?.WeaponRestriction != 2 || !row.Weapon.IsMeleeWeapon) &&
                (!row.Weapon.HasAmmunitionSource || handling.Ammunition(row.Weapon) is not null))
            .OrderByDescending(row => owned.Contents.Equipped.Contains(row.Item.RuntimeFormId))
            .ThenByDescending(row =>
            {
                var data = _records.GetEffective(row.Item.FormKey).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
                return BinaryPrimitives.ReadInt16LittleEndian(data[12..]) *
                    FalloutWeaponDamageResolver.NewVegasConditionMultiplier(FalloutWeaponCondition.SelectedCondition(row.Item));
            }).ThenBy(row => row.Item.RuntimeFormId).ToArray();
        var selected = choices.FirstOrDefault().Item;
        owned.ResolveWeaponSelection(_records, selected?.FormKey);
        return selected;
    }

    private void PrepareWeapon(FalloutCampaignItem item, FalloutPluginRecord stats, string directory)
    {
        var owned = _world.Inventory(_state.Reference, _context!.Level(), _context.Globals);
        owned.Contents.EnsureWeaponInstance(item.FormKey);
        _enemyWeapon = _world.WeaponPresentation(item.FormKey, firstPerson: false, owned.Contents.WeaponModMask(item.FormKey));
        if (_enemyWeapon.IsMine) throw new NotSupportedException("Actor mine placement and proximity detonation are unbound.");
        if (_enemyWeapon.Automatic && (!float.IsFinite(_enemyWeapon.AttackShotsPerSecond) || _enemyWeapon.AttackShotsPerSecond <= 0))
            throw new InvalidDataException("Actor automatic weapon has no valid source attack-shot rate.");
        owned.Contents.Equip(_records, item.FormKey);
        if (_enemyWeapon.Model is not null) _enemyObject = new(_enemyWeapon, _skeleton, _content);
        else if (Ranged)
        {
            var node = _enemyWeapon.ResolveEmbeddedProjectileNode(Enumerable.Range(0, _skeleton.Node.GetBoneCount())
                .Select(index => _skeleton.Node.GetBoneName(index).ToString()));
            _embeddedMuzzle = new() { Name = "SourceEmbeddedWeaponMuzzle", BoneName = node };
            _skeleton.Node.AddChild(_embeddedMuzzle);
        }
        _enemyWeaponHandling = new(owned.Contents, nativeNpc: true);
        if (_state.Engagement!.WeaponHandling is { } restored)
            _enemyWeaponHandling.Restore(restored, key => _world.WeaponPresentation(key, false),
                (key, mods) => _world.WeaponPresentation(key, false, mods));
        else if (Ranged) _enemyWeaponHandling.CompleteReload(_enemyWeapon);
        var itemData = _records.GetEffective(item.FormKey).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        _baseWeaponDamage = BinaryPrimitives.ReadInt16LittleEndian(itemData[12..]);
        var data = stats.ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
        if (stats.Signature == "NPC_")
        {
            var skills = stats.ReadSubrecords().Single(field => field.Signature == "DNAM").Data.ToArray();
            if (data.Length != 11 || skills.Length != 28) throw new NotSupportedException("NPC attack stat/skill extent is unbound.");
            _enemySkillValues = skills;
        }
        else
        {
            if (data.Length != 17) throw new NotSupportedException("Creature attack stat extent is unbound.");
            _enemySkillValues = Enumerable.Repeat(data[1], 28).ToArray();
        }
        _enemyDamage = new(_records, owned.Contents, value => value is >= 32 and <= 45 ?
            _actor is RuntimeNativeNpc ? _world.ActorValue(_state.Reference, FalloutActorValue.SourceSlot(value)) : _enemySkillValues[value - 32] :
            throw new NotSupportedException("Actor weapon skill is outside its source skill block."), () => _world.PerkEntries(_state.Reference).ToArray());
        var group = _enemyWeapon.AnimationGroup;
        var style = _world.CombatStyle(_state.Reference);
        _attackRange = Ranged ? _enemyWeapon.MaximumRange * _skeleton.UnitsToMetres * (style?.MaximumRangeMultiplier ?? 1) :
            _enemyWeapon.Reach * FalloutGameSettingFloats.ReadRetained(_records, "fCombatDistance", nameof(RuntimeNativeActorCombat)) * _skeleton.UnitsToMetres;
        _movementPath = SelectPath(directory, $"locomotion/{group}fastforward", $"locomotion/{group}forward",
            $"locomotion/{(_actor is RuntimeNativeNpc npc && npc.Appearance.Female ? "female" : "male")}/mtfastforward",
            "locomotion/mtfastforward", "locomotion/mtforward", "mtforward");
        _combatIdle = Clip(SelectPath(directory, "locomotion/mtidle", "mtidle"), true);
        if (_actor is RuntimeNativeNpc)
        {
            _combatAim = Clip(SelectPath(directory, group + "aim"), true);
            if (_enemyWeapon.Grip != 255)
            {
                if (_enemyWeapon.Grip is < 230 or > 235) throw new NotSupportedException("Source weapon grip index is unbound.");
                _combatGrip = Clip(SelectPath(directory, group + "handgrip" + (_enemyWeapon.Grip - 229)), true);
            }
        }
        var attack = group + _enemyWeapon.AttackGroup;
        _attackPaths = SelectAttackPaths(directory, attack);
    }

    private Transform3D MuzzlePose() => _enemyObject is not null ?
        _enemyWeapon is { IsThrownWeapon: true } or { IsMine: true } ? _enemyObject.ReleaseTransform(_skeleton) : _enemyObject.Socket(_skeleton, "ProjectileNode") :
        _skeleton.Node.GlobalTransform * _skeleton.Node.GetBoneGlobalPose(_skeleton.BoneIndex(_embeddedMuzzle!.BoneName.ToString()));

    private Node3D MuzzleNode() => _embeddedMuzzle is not null ? _embeddedMuzzle :
        _enemyObject!.Nodes.Single(node => node.GetMeta("opennv_nif_source_name", "").AsString() == "ProjectileNode");
}
