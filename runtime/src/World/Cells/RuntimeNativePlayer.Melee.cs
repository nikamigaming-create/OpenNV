using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private long _meleeAttacks;

    private static bool IsMeleeWeapon(FalloutWeaponPresentation weapon)
        => weapon.IsMeleeWeapon;

    private void RequestMeleeWeaponFire(FalloutWeaponPresentation weapon)
    {
        if (!_weaponHandling!.Drawn || !_weaponHandling.CanUse(weapon)) return;
        if (weapon.Automatic && (!float.IsFinite(weapon.AttackShotsPerSecond) || weapon.AttackShotsPerSecond <= 0))
            throw new NotSupportedException("Automatic melee weapon has no valid source attack-shot rate.");
        if (!float.IsFinite(weapon.AttackMultiplier) || weapon.AttackMultiplier <= 0)
            throw new InvalidDataException("WEAP attack multiplier is invalid.");

        var fields = _presentationRecords!.GetEffective(weapon.Form).ReadSubrecords().ToArray();
        var itemData = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (itemData.Length != 15) throw new NotSupportedException("Melee WEAP DATA extent is unbound.");
        var baseDamage = BinaryPrimitives.ReadInt16LittleEndian(itemData[12..]);
        if (baseDamage < 0) throw new InvalidDataException("Melee WEAP damage is negative.");
        _ = (_damage ?? throw new InvalidOperationException("Player damage owner is absent.")).Resolve(weapon.Form, baseDamage);

        // Admit every actual paired-view candidate before the shared action
        // owner selects its one retained variant. A singular PrepareAction
        // cannot preflight a legitimate multi-variant melee source group.
        _ = PrepareAttackVariants(weapon, weapon.AttackGroup);
        _shot = null;
        _firePreparationError = null;
        RequestWeaponAction(weapon.AttackGroup);
    }

    private bool CanContinueAutomaticFire(FalloutWeaponPresentation weapon)
        => IsMeleeWeapon(weapon) ? _weaponHandling?.Drawn == true && _weaponHandling.CanUse(weapon) : _weaponHandling?.CanFire(weapon) == true;

    private void PublishPendingMeleeStrike(FalloutWeaponPresentation weapon)
    {
        var fields = _presentationRecords!.GetEffective(weapon.Form).ReadSubrecords().ToArray();
        var dnam = fields.Single(field => field.Signature == "DNAM").Data.Span;
        var itemData = fields.Single(field => field.Signature == "DATA").Data.Span;
        if (dnam.Length < 12 || itemData.Length != 15) throw new NotSupportedException("Melee WEAP layout is unbound.");
        var objectReach = weapon.WeaponAnimationType switch
        {
            0 => "fObjectHitH2HReach",
            1 => "fObjectHitOneHandReach",
            2 => "fObjectHitTwoHandReach",
            _ => "fObjectHitWeaponReach"
        };
        var range = FalloutGameSettingFloats.Read(_presentationRecords, objectReach) * UnitsToMeters;
        if (!float.IsFinite(range) || range <= 0) throw new InvalidDataException("Melee source reach is invalid.");

        Transform3D aim;
        if (_xr is { } xr)
        {
            if (!xr.RightAim.GetHasTrackingData()) return;
            aim = xr.RightAim.GlobalTransform;
        }
        // PublishThirdPersonCamera has already published the player-eye frame
        // used by world audio this tick. Camera pullback must never move the
        // attack origin backwards or consume the weapon's physical reach.
        else aim = _sourceAudioFramePublished ? _sourceFirstPersonAudioFrame :
            throw new InvalidOperationException("Melee has no published player-eye frame.");
        var origin = aim.Origin;
        var direction = -aim.Basis.Z.Normalized();
        var end = origin + direction * range;
        if (!origin.IsFinite() || !direction.IsFinite() || direction.LengthSquared() < .99f || !end.IsFinite())
            throw new InvalidDataException("Melee attack transform is invalid.");
        var selectedActor = SelectPlayerMeleeActor(weapon, aim);
        using var collision = selectedActor is null ? CastShotRay(origin, end) : new Godot.Collections.Dictionary();
        var collider = collision.TryGetValue("collider", out var value) ? value.AsGodotObject() as Node : null;
        var point = collision.TryGetValue("position", out var position) ? position.AsVector3() : end;
        var normal = collision.TryGetValue("normal", out var colNorm) ? colNorm.AsVector3() : -direction;

        if (selectedActor is { } selected)
        {
            collider = selected.Collider;
            point = selected.Point;
            normal = selected.Normal;
        }

        var reference = ShotReference(collider);
        var hitReference = HitReference(collider);
        var impactBytes = fields.SingleOrDefault(field => field.Signature == "INAM").Data;
        FalloutActorHit? hit = null;
        _damageError = null;
        try
        {
            if (collider is not null && RuntimeNativeDestructible.Find(collider) is { } destructible)
            {
                var baseDamage = BinaryPrimitives.ReadInt16LittleEndian(itemData[12..]);
                destructible.Hit(_damage!.Resolve(weapon.Form, baseDamage).Amount, _presentationRecords.RuntimeFormKey(0x14));
            }
            if (collider is not null && RuntimeNativeActorCombat.Find(collider) is { } combat)
            {
                var baseDamage = BinaryPrimitives.ReadInt16LittleEndian(itemData[12..]);
                var damage = (_damage ?? throw new InvalidOperationException("Player damage owner is absent."))
                    .Resolve(weapon.Form, baseDamage);
                hit = combat.Hit(collider, damage, _presentationRecords.RuntimeFormKey(0x14),
                    _combatLevel!(), _combatGlobals!, weapon.OnHitBehavior, _weaponHandling!.NextShotRandomUnit,
                    hitWeapon: weapon.Form);
                combat.ReceiveMeleeKnockback(GlobalPosition, damage.Amount);
            }
            else if (collider is not null)
                MarkWeaponHit(hitReference, _presentationRecords.RuntimeFormKey(0x14), weapon.Form, FalloutReferenceHitKind.Melee);
        }
        catch (Exception error)
        {
            _damageError = error.Message;
            throw;
        }
        _weaponHandling!.ApplyMeleeConditionWear(weapon, _presentationRecords!);
        FalloutImpact? impact = null;
        if (collider is not null && !impactBytes.IsEmpty)
        {
            TryShotEffect("melee-impact", () =>
            {
                if (impactBytes.Length != 4) throw new InvalidDataException("Melee WEAP impact-set extent is invalid.");
                var set = _presentationRecords.GetEffective(weapon.Form).Plugin.AdjustOptionalFormId(
                    BinaryPrimitives.ReadUInt32LittleEndian(impactBytes.Span));
                if (set is null) return;
                var material = hit is { } actorHit
                    ? checked((int)actorHit.ImpactMaterial)
                    : collision.ContainsKey("collider") ? ShotMaterial(collision) : 6;
                impact = FalloutImpact.Resolve(_presentationRecords, set.Value, material);
                if (impact is not { } source) return;
                EnsureWeaponSounds();
                _shotEffects ??= new(_presentationRecords, RuntimeLiveContentSource.Current!, UnitsToMeters, this, CollisionMask, _weaponSounds);
                if (!_shotEffects.IsInsideTree()) AddChild(_shotEffects);
                _shotEffects.Impact(source, point, normal, direction);
            });
        }
        var ordinal = ++_meleeAttacks;
        _lastShot = new
        {
            ordinal,
            kind = "source-melee-Hit",
            weapon = weapon.Form.ToString(),
            reference,
            part = hit?.Part,
            point = new[] { point.X, point.Y, point.Z },
            distanceMeters = origin.DistanceTo(point),
            hit = hit is not null,
            damage = hit?.HealthDamage,
            limbDamage = hit?.LimbDamage,
            healthBefore = hit?.HealthBefore,
            healthAfter = hit?.HealthAfter,
            impact = impact?.Form.ToString(),
            impactError = _shotEffectErrors.GetValueOrDefault("melee-impact"),
            unbound = "attack-specific-audio"
        };
        GD.Print($"OPENNV_WEAPON_MELEE weapon={weapon.Form} reference={reference} part={hit?.Part} damage={hit?.HealthDamage}");
    }
}
