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

        var clip = _firstPerson!.PrepareAction(weapon.AttackGroup);
        var hitEvents = clip.TextKeys
            .SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Count(text => text.Trim().Equals("Hit", StringComparison.OrdinalIgnoreCase));
        if (hitEvents == 0) throw new NotSupportedException("Melee attack has no source Hit event.");
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
        var reach = FalloutProjectile.Number(dnam, 8);
        var range = reach * FalloutGameSettingFloats.Read(_presentationRecords, "fCombatDistance") * UnitsToMeters + CombatRadius;
        if (!float.IsFinite(range) || range <= 0) throw new InvalidDataException("Melee source reach is invalid.");

        Transform3D aim;
        if (_xr is { } xr)
        {
            if (!xr.RightAim.GetHasTrackingData()) return;
            aim = xr.RightAim.GlobalTransform;
        }
        else aim = _camera.GlobalTransform;
        var origin = aim.Origin;
        var direction = -aim.Basis.Z.Normalized();
        var end = origin + direction * range;
        if (!origin.IsFinite() || !direction.IsFinite() || direction.LengthSquared() < .99f || !end.IsFinite())
            throw new InvalidDataException("Melee attack transform is invalid.");
        var collision = CastShotRay(origin, end);
        var collider = collision.TryGetValue("collider", out var value) ? value.AsGodotObject() as Node : null;
        var point = collision.TryGetValue("position", out var position) ? position.AsVector3() : end;
        var normal = collision.TryGetValue("normal", out var colNorm) ? colNorm.AsVector3() : -direction;

        if (collider is null || RuntimeNativeActorCombat.Find(collider) is null)
        {
            var coneAngle = FalloutGameSettingFloats.Read(_presentationRecords, "fCombatHitConeAngle");
            var halfAngleRad = Mathf.DegToRad(Math.Clamp(coneAngle, 1f, 89f));
            const int sides = 16;
            var radius = range * MathF.Tan(halfAngleRad) / MathF.Cos(MathF.PI / sides);
            using var cone = new ConvexPolygonShape3D
            {
                Points = Enumerable.Range(0, sides).Select(index => new Vector3(
                    radius * MathF.Cos(index * MathF.Tau / sides), radius * MathF.Sin(index * MathF.Tau / sides), -range))
                    .Prepend(Vector3.Zero).ToArray()
            };
            var up = MathF.Abs(direction.Dot(Vector3.Up)) < .99f ? Vector3.Up : Vector3.Right;
            using var query = new PhysicsShapeQueryParameters3D
            {
                Shape = cone,
                Transform = new(Basis.LookingAt(direction, up), origin),
                CollisionMask = CollisionLayer,
                CollideWithAreas = true,
                Exclude = SelfQueryBodies
            };
            var contacts = GetWorld3D().DirectSpaceState.IntersectShape(query, 32);
            Node? bestCollider = null;
            var bestPoint = end;
            var bestNormal = normal;
            var bestScore = float.NegativeInfinity;
            foreach (var hitDict in contacts)
            {
                if (hitDict.TryGetValue("collider", out var hitObj) && hitObj.AsGodotObject() is Node candidateCollider)
                {
                    if (RuntimeNativeActorCombat.Find(candidateCollider) is not { Dead: false } targetCombat) continue;
                    var candidatePos = candidateCollider is Node3D node3D ? node3D.GlobalPosition : origin;
                    var toCandidate = candidatePos - origin;
                    var candidateDist = toCandidate.Length();
                    if (candidateDist <= 0.001f || candidateDist > range) continue;
                    var candidateDir = toCandidate / candidateDist;
                    var dot = direction.Dot(candidateDir);
                    if (dot < MathF.Cos(halfAngleRad)) continue;
                    var sightCheck = CastShotRay(origin, candidatePos);
                    if (sightCheck.TryGetValue("collider", out var sightObj) && sightObj.AsGodotObject() is Node sightCollider)
                    {
                        var sightCombat = RuntimeNativeActorCombat.Find(sightCollider);
                        if (sightCombat != targetCombat && sightCombat is null)
                        {
                            if (sightCheck.TryGetValue("position", out var sightPos) && origin.DistanceTo(sightPos.AsVector3()) < candidateDist - 0.05f)
                                continue;
                        }
                    }
                    var score = dot * 10f - candidateDist;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCollider = candidateCollider;
                        bestPoint = candidatePos;
                        bestNormal = (origin - candidatePos).Normalized();
                    }
                }
            }
            if (bestCollider is not null)
            {
                collider = bestCollider;
                point = bestPoint;
                normal = bestNormal;
            }
        }

        var reference = ShotReference(collider);
        var impactBytes = fields.SingleOrDefault(field => field.Signature == "INAM").Data;
        FalloutActorHit? hit = null;
        _damageError = null;
        try
        {
            if (collider is not null && RuntimeNativeActorCombat.Find(collider) is { } combat)
            {
                var baseDamage = BinaryPrimitives.ReadInt16LittleEndian(itemData[12..]);
                var damage = (_damage ?? throw new InvalidOperationException("Player damage owner is absent."))
                    .Resolve(weapon.Form, baseDamage);
                hit = combat.Hit(collider, damage, _presentationRecords.RuntimeFormKey(0x14),
                    _combatLevel!(), _combatGlobals!, weapon.OnHitBehavior, _weaponHandling!.NextShotRandomUnit);
            }
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
                _shotEffects ??= new(_presentationRecords, RuntimeLiveContentSource.Current!, UnitsToMeters, this, CollisionMask);
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
