using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutWeaponShot? _enemyShot;
    private FalloutWeaponSpread? _enemyWeaponSpread;
    private RuntimeNativeShotEffects? _enemyShotEffects;
    private NativeActorMuzzle? _enemyMuzzle;
    private Godot.Collections.Array<Rid>? _enemyRayExclusions;
    private string? _muzzlePresentationError;
    private string? _casingPresentationError;

    private void ShootPlayer(RuntimeNativePlayer player)
    {
        var weapon = _enemyWeapon!;
        var handling = _enemyWeaponHandling ?? throw new InvalidOperationException("Actor weapon handling is absent.");
        var ammo = _enemyWeaponHandling!.Ammunition(weapon);
        if (_enemyShot is null || _enemyShot.Ammunition != ammo)
        {
            _enemyShot = FalloutWeaponShot.Read(_records, weapon.Form, ammo, weapon.HasAmmunitionSource);
            _enemyShot.RequireRuntimeAttackOwner();
            var socket = _enemyObject!.Nodes.Single(node => node.GetMeta("opennv_nif_source_name", "").AsString() == "ProjectileNode");
            _enemyMuzzle ??= new(_records, _content, socket, _skeleton.UnitsToMetres);
            try { _enemyMuzzle.Prepare(_enemyShot.Projectile, encoded: false); _muzzlePresentationError = null; }
            catch (Exception error)
            {
                _muzzlePresentationError = error.Message;
                GD.PushError($"OPENNV_MUZZLE_PRESENTATION_UNBOUND reference={_state.Reference} {error.Message}");
            }
        }
        _enemyShotEffects ??= new(_records, _content, _skeleton.UnitsToMetres, _mover, _mask);
        if (!_enemyShotEffects.IsInsideTree()) _actor.AddChild(_enemyShotEffects);
        try { _enemyShotEffects.PrepareShell(weapon); _casingPresentationError = null; }
        catch (Exception error)
        {
            _casingPresentationError = error.Message;
            GD.PushError($"OPENNV_CASING_PRESENTATION_UNBOUND reference={_state.Reference} {error.Message}");
        }
        _enemyRayExclusions ??= new(_actor.FindChildren("*", "", true, false).OfType<CollisionObject3D>()
            .Select(body => body.GetRid()).Append(_mover!.GetRid()));
        var socketPose = _enemyObject!.Socket(_skeleton, "ProjectileNode");
        var from = socketPose.Origin;
        var direction = (player.CombatTargetPoint - from).Normalized();
        var resolvedDamage = _enemyDamage!.Resolve(_enemyShot);
        var spreadDegrees = ResolveNpcShotSpread(_enemyShot);
        if (!_enemyShot.Projectile.IsInstantRayAttack)
        {
            ShootProjectilePlayer(player, weapon, handling, from, direction, resolvedDamage, spreadDegrees);
            return;
        }
        if (!handling.ConsumeShot(weapon, _enemyShot, _records)) return;
        (_enemyMuzzle ?? throw new InvalidOperationException("Actor muzzle is absent.")).Flash();
        if (weapon.Sounds.TryGetValue("shoot", out var sound)) _enemySounds!.DispatchSound(sound);
        var before = _context!.Vitals().ExactHitPoints;
        Node? lastCollider = null;
        byte? lastPart = null;
        var totalLimbDamage = 0.0f;
        var hits = 0;
        var pellets = new List<object>(_enemyShot.Projectiles);
        for (var pellet = 0; pellet < _enemyShot.Projectiles; pellet++)
        {
            var pelletDirection = FalloutWeaponSpread.Deviate(direction, spreadDegrees, handling.NextShotRandomUnit);
            using var query = PhysicsRayQueryParameters3D.Create(from,
                from + pelletDirection * (_enemyShot.Projectile.Range * _skeleton.UnitsToMetres), _mask, _enemyRayExclusions);
            query.CollideWithAreas = true;
            using var collision = _actor.GetWorld3D().DirectSpaceState.IntersectRay(query);
            Node? collider = collision.Count == 0 ? null : collision["collider"].AsGodotObject() as Node;
            var point = collision.Count == 0 ? Vector3.Zero : collision["position"].AsVector3();
            var normal = collision.Count == 0 ? Vector3.Zero : collision["normal"].AsVector3();
            byte? part = null;
            float? healthBefore = null;
            float? healthAfter = null;
            if (collider == player || collider is not null && player.IsAncestorOf(collider))
            {
                part = collider == player ? (byte)0 : player.CombatHitPart(collider!);
                healthBefore = _context.Vitals().ExactHitPoints;
                _context.DamagePlayer(resolvedDamage, part.Value);
                healthAfter = _context.Vitals().ExactHitPoints;
                totalLimbDamage += resolvedDamage.Amount * resolvedDamage.LimbMultiplier;
                ++hits;
                ++_hits;
            }
            if (collider is not null && collision.Count > 0 && _enemyShot.ImpactDataSet is { } set)
            {
                var material = collider == player || player.IsAncestorOf(collider)
                    ? 6
                    : FalloutImpact.MaterialIndex(NativeNifCollisionBuilder.HitMaterial(collision));
                if (FalloutImpact.Resolve(_records, set, material) is { } impact)
                    _enemyShotEffects.Impact(impact, point, normal, pelletDirection, collider as Node3D);
            }
            pellets.Add(new
            {
                index = pellet,
                collider = collider?.GetPath().ToString(),
                part,
                point = collision.Count == 0 ? (float[]?)null : new[] { point.X, point.Y, point.Z },
                damage = part is null ? (float?)null : resolvedDamage.Amount,
                limbDamage = part is null ? (float?)null : resolvedDamage.Amount * resolvedDamage.LimbMultiplier,
                healthBefore,
                healthAfter,
                direction = new[] { pelletDirection.X, pelletDirection.Y, pelletDirection.Z }
            });
            if (collider is not null) lastCollider = collider;
            if (part is not null) lastPart = part;
        }
        if (_casingPresentationError is null)
        {
            try { _enemyShotEffects.EjectCasing(_enemyObject.Socket(_skeleton, "ShellCasingNode"), player.Camera.GlobalPosition); }
            catch (Exception error)
            {
                _casingPresentationError = error.Message;
                GD.PushError($"OPENNV_CASING_PRESENTATION_UNBOUND reference={_state.Reference} {error.Message}");
            }
        }
        var after = _context.Vitals().ExactHitPoints;
        _lastAttack = new
        {
            kind = "source-weapon-Hit",
            weapon = weapon.Form.ToString(),
            loaded = _enemyWeaponHandling.Loaded(weapon.Form),
            collider = lastCollider?.GetPath().ToString(),
            part = lastPart,
            projectiles = _enemyShot.Projectiles,
            hits,
            damage = resolvedDamage.Amount,
            limbDamage = totalLimbDamage,
            spreadDegrees,
            healthBefore = before,
            healthAfter = after,
            pellets,
            boundary = "NPC source skill, movement, arm injury and wobble applied;NPC perk conditions,cover,weapon-mod spread and retail cadence unmatched"
        };
        GD.Print($"OPENNV_ACTOR_ATTACK reference={_state.Reference} kind=ranged projectiles={_enemyShot.Projectiles} hits={hits} health={before:R}->{after:R}");
    }

    private void ShootProjectilePlayer(RuntimeNativePlayer player, FalloutWeaponPresentation weapon,
        FalloutWeaponHandling handling, Vector3 origin, Vector3 direction, FalloutWeaponDamage damage,
        float spreadDegrees)
    {
        var shot = _enemyShot!;
        var effects = _enemyShotEffects!;
        FalloutWeaponDamage? explosionDamage = shot.Projectile.ExplosionSource is { } explosion
            ? (_enemyDamage ?? throw new InvalidOperationException("Actor damage owner is absent.")).Resolve(shot, explosion.Damage)
            : null;
        var flights = new List<RuntimeNativeProjectileFlight>(shot.Projectiles);
        try
        {
            for (var pellet = 0; pellet < shot.Projectiles; pellet++)
            {
                var projectileDirection = FalloutWeaponSpread.Deviate(
                    direction, spreadDegrees, handling.NextShotRandomUnit);
                var flight = effects.PrepareProjectile(shot.Projectile, _context!.Gravity,
                    origin, projectileDirection, _mask, _enemyRayExclusions!);
                flight.OnContact = contact => ApplyProjectilePlayerContact(player, shot, damage, contact);
                if (shot.Projectile.ExplosionSource is not null)
                    flight.OnDetonate = point => ApplyEnemyProjectileDetonation(player, shot, explosionDamage!.Value, point);
                flights.Add(flight);
            }
            if (!handling.ConsumeShot(weapon, shot, _records))
            {
                foreach (var flight in flights) flight.Free();
                return;
            }
            foreach (var flight in flights) effects.LaunchProjectile(flight);
            (_enemyMuzzle ?? throw new InvalidOperationException("Actor muzzle is absent.")).Flash();
            if (weapon.Sounds.TryGetValue("shoot", out var sound)) _enemySounds!.DispatchSound(sound);
            if (_casingPresentationError is null)
            {
                try { _enemyShotEffects!.EjectCasing(_enemyObject!.Socket(_skeleton, "ShellCasingNode"), player.Camera.GlobalPosition); }
                catch (Exception error)
                {
                    _casingPresentationError = error.Message;
                    GD.PushError($"OPENNV_CASING_PRESENTATION_UNBOUND reference={_state.Reference} {error.Message}");
                }
            }
            _lastAttack = new
            {
                kind = "source-projectile-flight",
                weapon = weapon.Form.ToString(),
                weaponAnimationType = shot.WeaponAnimationType,
                attackAnimation = shot.AttackAnimation,
                projectile = shot.Projectile.Form.ToString(),
                loaded = handling.Loaded(weapon.Form),
                projectiles = shot.Projectiles,
                projectileFlights = flights.Count,
                spreadDegrees,
                direction = new[] { direction.X, direction.Y, direction.Z },
                origin = new[] { origin.X, origin.Y, origin.Z },
                boundary = "missile-lobber-flight;source-speed,gravity,bounce-and-EXPL-radius-damage;distance-attenuation,force,radiation,visuals,ammo-effects,tracer,rotation-and-retail-cadence-unmatched"
            };
            GD.Print($"OPENNV_ACTOR_ATTACK reference={_state.Reference} kind=projectile-flight projectiles={flights.Count}");
        }
        catch
        {
            foreach (var flight in flights)
                if (GodotObject.IsInstanceValid(flight) && !flight.IsInsideTree()) flight.Free();
            throw;
        }
    }

    private float ResolveNpcShotSpread(FalloutWeaponShot shot)
    {
        var skillValues = _enemySkillValues ?? throw new InvalidOperationException("NPC weapon skill source is absent.");
        if (shot.SkillActorValue is < 32 or > 45 || skillValues.Length != 28)
            throw new NotSupportedException("NPC weapon skill does not resolve to the source skill block.");

        var health = _world.Health(_state.Reference);
        var limbDamage = _state.Injury?.LimbDamage;
        var parts = _world.BodyParts(_state.Reference).Parts;
        bool Crippled(IEnumerable<FalloutBodyPart> arms) => arms.Any(part =>
        {
            var threshold = health.Base * part.HealthPercent / 100.0f;
            return threshold > 0 && (limbDamage?.GetValueOrDefault(part.Type) ?? 0) >= threshold;
        });
        var leftArms = parts.Where(part => part.Type is 3 or 4).ToArray();
        var rightArms = parts.Where(part => part.Type is 5 or 6).ToArray();
        if (leftArms.Length == 0 || rightArms.Length == 0)
            throw new NotSupportedException("NPC weapon spread requires source left and right arm parts.");

        var running = Activity.Running;
        return (_enemyWeaponSpread ??= new(_records)).NpcMedianDeviationDegrees(shot,
            skillValues[shot.SkillActorValue - 32], _enemyStrength,
            moving: running, running: running, sneaking: Activity.Sneaking,
            leftArmCrippled: Crippled(leftArms), rightArmCrippled: Crippled(rightArms));
    }

    private void ApplyProjectilePlayerContact(RuntimeNativePlayer player, FalloutWeaponShot shot,
        FalloutWeaponDamage damage, RuntimeNativeProjectileContact contact)
    {
        byte? part = null;
        float? healthBefore = null;
        float? healthAfter = null;
        if (contact.Collider == player || contact.Collider is not null && player.IsAncestorOf(contact.Collider))
        {
            part = contact.Collider == player ? (byte)0 : player.CombatHitPart(contact.Collider!);
            healthBefore = _context!.Vitals().ExactHitPoints;
            _context.DamagePlayer(damage, part.Value);
            healthAfter = _context.Vitals().ExactHitPoints;
            _hits++;
        }
        string? impactError = null;
        try
        {
            if (contact.Collider is not null && shot.ImpactDataSet is { } set &&
                FalloutImpact.Resolve(_records, set, part is null ? FalloutImpact.MaterialIndex(
                    NativeNifCollisionBuilder.HitMaterial(contact.Collision)) : 6) is { } impact)
                _enemyShotEffects!.Impact(impact, contact.Point, contact.Normal, contact.Direction, contact.Collider as Node3D);
        }
        catch (Exception error)
        {
            impactError = error.Message;
            GD.PushError($"OPENNV_PROJECTILE_IMPACT_UNBOUND reference={_state.Reference} {error.Message}");
        }
        _lastAttack = new
        {
            kind = "source-projectile-flight-contact",
            weapon = _enemyWeapon!.Form.ToString(),
            projectile = shot.Projectile.Form.ToString(),
            collider = contact.Collider?.GetPath().ToString(),
            part,
            point = new[] { contact.Point.X, contact.Point.Y, contact.Point.Z },
            damage = part is null ? (float?)null : damage.Amount,
            limbDamage = part is null ? (float?)null : damage.Amount * damage.LimbMultiplier,
            impactError,
            healthBefore,
            healthAfter,
            direction = new[] { contact.Direction.X, contact.Direction.Y, contact.Direction.Z }
        };
    }

    private void ApplyEnemyProjectileDetonation(RuntimeNativePlayer player, FalloutWeaponShot shot,
        FalloutWeaponDamage blastDamage, Vector3 point)
    {
        if (shot.Projectile.ExplosionSource is not { } explosion) return;
        _lastExplosion = RuntimeNativeExplosionCombat.Detonate(_actor, _actor, _records, explosion, blastDamage,
            point, _mask, _skeleton.UnitsToMetres, _state.Reference, _context!.Level(), _context.Globals,
            player, _context.DamagePlayer);
        GD.Print($"OPENNV_ACTOR_WEAPON_EXPLOSION reference={_state.Reference} weapon={shot.Weapon} explosion={explosion.Form} result={System.Text.Json.JsonSerializer.Serialize(_lastExplosion)}");
    }
}
