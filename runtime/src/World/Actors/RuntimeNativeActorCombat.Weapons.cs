using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private BoneAttachment3D? _embeddedMuzzle;

    private FalloutActorEngagement ReselectCombatWeapon(FalloutActorEngagement state)
    {
        var previous = _enemyWeapon?.Form;
        RetireCombatWeaponPresentation();
        _state.Engagement = state.Transition("pursue") with
        { WeaponHandling = _enemyWeaponHandling?.Capture() ?? state.WeaponHandling };
        // Keep handling, random streams, fired projectiles and world effects
        // alive. A newly chosen empty magazine uses ordinary reload next tick.
        PrepareEngagement();
        Activity.SetWeaponDrawn(_enemyWeapon is not null);
        GD.Print($"OPENNV_ACTOR_WEAPON_RESELECT reference={_state.Reference} previous={previous} selected={_enemyWeapon?.Form}");
        return _state.Engagement;
    }

    private void RetireCombatWeaponPresentation()
    {
        _enemyMuzzle?.Retire(); _enemyMuzzle = null;
        RetireAttachment(_enemyObject); _enemyObject = null;
        _embeddedMuzzle?.Free(); _embeddedMuzzle = null;
        // A package must not later resurrect the exhausted combat choice.
        RetireAttachment(_packageWeapon); _packageWeapon = null;
        _packageBipedWeapon = null;
        _packageWeaponPrepared = false; _packageWeaponType = 0;
        _packageAim = _packageGrip = null;
        _enemyWeapon = null; _enemyDamage = null; _enemySkillValues = null;
        _enemyShot = null;
        if (_enemyRayExclusions is not null) ((Godot.Collections.Array)_enemyRayExclusions).Dispose();
        _enemyRayExclusions = null;
        _muzzlePresentationError = _casingPresentationError = _beamPresentationError = null;
        _combatIdle = _combatAim = _combatGrip = null;
        // These controllers bind directly to their weapon's source nodes.
        _combatClips.Clear(); _pathTurnAnimations.Clear();
        static void RetireAttachment(NativeActorWeaponAttachment? attachment)
        {
            if (attachment is null) return;
            foreach (var part in attachment.BodyAttachments) part.Free();
            attachment.Attachment.Free();
        }
    }

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
                HasUsableAmmunition(row.Weapon))
            .OrderByDescending(row => owned.Contents.Equipped.Contains(row.Item.RuntimeFormId))
            .ThenByDescending(row =>
            {
                var data = _records.GetEffective(row.Item.FormKey).ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span;
                return BinaryPrimitives.ReadInt16LittleEndian(data[12..]) *
                    FalloutWeaponDamageResolver.NewVegasConditionMultiplier(FalloutWeaponCondition.SelectedCondition(row.Item));
            }).ThenBy(row => row.Item.RuntimeFormId).ToArray();
        var selected = choices.FirstOrDefault().Item;
        var previous = owned.Contents.EquippedObject(_records, 5);
        owned.ResolveWeaponSelection(_records, selected?.FormKey);
        if (previous != selected?.FormKey) _world.InventoryChanged(_state.Reference);
        return selected;

        bool HasUsableAmmunition(FalloutWeaponPresentation weapon)
        {
            if (weapon.AmmoRegenerationSeconds > 0) return weapon.ClipSize >= weapon.AmmoUse;
            if (!weapon.HasAmmunitionSource) return true;
            if (handling.Ammunition(weapon) is not { } ammo) return false;
            if (weapon.AmmoUse == 0) return true;
            if (!weapon.NpcsUseAmmo) return weapon.ClipSize >= weapon.AmmoUse;
            var instance = owned.Contents.SelectedWeapon(weapon.Form).Extra.Instance;
            var available = Math.Max(0, (owned.Contents.Item(ammo)?.Count ?? 0) -
                owned.Contents.LoadedInventoryRounds(ammo, instance));
            return Math.Min(weapon.ClipSize, available) >= weapon.AmmoUse;
        }
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
        if (_enemyWeapon.Model is not null && NativeActorWeaponAttachment.CanAttachSourceModel(_enemyWeapon, _content))
            _enemyObject = new(_enemyWeapon, _skeleton, _content);
        else if (Ranged)
        {
            if (_enemyWeapon.Model is not null)
                throw new NotSupportedException("Actor skinned inventory model has no attached projectile socket owner.");
            var node = _enemyWeapon.ResolveEmbeddedProjectileNode(Enumerable.Range(0, _skeleton.Node.GetBoneCount())
                .Select(index => _skeleton.Node.GetBoneName(index).ToString()));
            _embeddedMuzzle = new() { Name = "SourceEmbeddedWeaponMuzzle", BoneName = node };
            _skeleton.Node.AddChild(_embeddedMuzzle);
        }
        if (_enemyWeaponHandling is null)
        {
            _enemyWeaponHandling = new(owned.Contents, nativeNpc: true);
            if (_state.Engagement!.WeaponHandling is { } restored)
                _enemyWeaponHandling.Restore(restored, key => _world.WeaponPresentation(key, false),
                    (key, mods) => _world.WeaponPresentation(key, false, mods));
            else if (Ranged) _enemyWeaponHandling.CompleteReload(_enemyWeapon);
        }
        if (_enemyWeapon.AmmoRegenerationSeconds > 0) _enemyWeaponHandling.MaterializeRecharge(_enemyWeapon);
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
