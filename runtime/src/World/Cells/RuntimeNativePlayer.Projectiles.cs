using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private sealed class PlayerProjectileTrace(
        Vector3 direction,
        Godot.Collections.Dictionary collision,
        Node? collider,
        string? reference,
        Vector3 point,
        float damageDelaySeconds)
    {
        internal Vector3 Direction { get; } = direction;
        internal Godot.Collections.Dictionary Collision { get; } = collision;
        internal Node? Collider { get; } = collider;
        internal string? Reference { get; } = reference;
        internal Vector3 Point { get; } = point;
        internal float DamageDelaySeconds { get; } = damageDelaySeconds;
        internal FalloutActorHit? ActorHit { get; set; }
    }

    private readonly record struct PlayerProjectileDamageSummary(
        int HitCount,
        int ActorHitCount,
        int PendingActorHits,
        int PendingEvents,
        int ImpactRequests,
        FalloutActorHit? LastDamage);

    private sealed class PendingProjectileImpact
    {
        internal FalloutPluginStack Records { get; }
        internal FalloutWeaponShot Shot { get; }
        internal FalloutWeaponDamage? Damage { get; }
        internal RuntimeNativeActorCombat? Combat { get; }
        internal FalloutFormKey Attacker { get; }
        internal int? AttackerLevel { get; }
        internal FalloutGlobalState? Globals { get; }
        internal PlayerProjectileTrace Trace { get; }
        internal float RemainingSeconds { get; set; }

        internal PendingProjectileImpact(FalloutPluginStack records, FalloutWeaponShot shot,
            FalloutWeaponDamage? damage, RuntimeNativeActorCombat? combat, FalloutFormKey attacker,
            int? attackerLevel, FalloutGlobalState? globals, PlayerProjectileTrace trace)
        {
            Records = records; Shot = shot; Damage = damage; Combat = combat; Attacker = attacker;
            AttackerLevel = attackerLevel; Globals = globals; Trace = trace;
            RemainingSeconds = trace.DamageDelaySeconds;
        }
    }

    private readonly List<PendingProjectileImpact> _pendingProjectileImpacts = [];
    private object? _lastHitscanImpact;
    internal int PendingProjectileImpactCount => _pendingProjectileImpacts.Count;
    internal object? LastHitscanImpact => _lastHitscanImpact;
    internal object PendingProjectileImpactState => _pendingProjectileImpacts.Select(pending => new
    {
        weapon = pending.Shot.Weapon.ToString(),
        projectile = pending.Shot.Projectile.Form.ToString(),
        reference = pending.Trace.Reference,
        remainingSeconds = pending.RemainingSeconds,
        actorDamage = pending.Damage is not null && pending.Combat is not null,
        impact = pending.Shot.ImpactDataSet is not null
    }).ToArray();

    private IReadOnlyList<PlayerProjectileTrace> TraceProjectiles(Vector3 origin, Vector3 direction,
        float medianSpreadDegrees)
    {
        var shot = _shot ?? throw new InvalidOperationException("Projectile source is absent.");
        var traces = new List<PlayerProjectileTrace>();
        for (var index = 0; index < shot.Projectiles; index++)
        {
            var pelletDirection = FalloutWeaponSpread.Deviate(
                direction,
                medianSpreadDegrees,
                _weaponHandling!.NextShotRandomUnit);
            var end = origin + pelletDirection * (shot.Projectile.Range * UnitsToMeters);
            if (!shot.Projectile.PassesThroughActors)
            {
                AddTrace(origin, end, pelletDirection, SelfQueryBodies);
                continue;
            }

            var exclusions = new Godot.Collections.Array<Rid>();
            foreach (var body in SelfQueryBodies) exclusions.Add(body);
            var start = origin;
            var traceCount = traces.Count;
            while (start.DistanceTo(end) > .001f)
            {
                var collision = CastShotRay(start, end, exclusions);
                if (!collision.TryGetValue("collider", out var value) || value.AsGodotObject() is not Node collider)
                {
                    traces.Add(MakeTrace(pelletDirection, new(), null, end));
                    break;
                }

                var point = collision.TryGetValue("position", out var position) ? position.AsVector3() : end;
                traces.Add(MakeTrace(pelletDirection, collision, collider, point));
                if (RuntimeNativeActorCombat.Find(collider) is not { } combat) break;

                var added = 0;
                foreach (var body in combat.CollisionRids)
                {
                    if (exclusions.Contains(body)) continue;
                    exclusions.Add(body);
                    added++;
                }
                if (added == 0)
                    throw new InvalidDataException($"Flame projectile {shot.Projectile.Form} cannot advance past actor collision {ShotReference(collider) ?? collider.Name}.");
                start = point + pelletDirection * .001f;
            }
            if (traces.Count == traceCount) traces.Add(MakeTrace(pelletDirection, new(), null, end));
        }
        return traces;

        void AddTrace(Vector3 from, Vector3 to, Vector3 pelletDirection, Godot.Collections.Array<Rid> exclusions)
        {
            var collision = CastShotRay(from, to, exclusions);
            var collider = collision.TryGetValue("collider", out var value) ? value.AsGodotObject() as Node : null;
            var point = collision.TryGetValue("position", out var position) ? position.AsVector3() : to;
            traces.Add(MakeTrace(pelletDirection, collision, collider, point));
        }

        PlayerProjectileTrace MakeTrace(Vector3 pelletDirection, Godot.Collections.Dictionary collision,
            Node? collider, Vector3 point)
        {
            var delay = shot.Projectile.HitscanImpactDelaySeconds(origin.DistanceTo(point), UnitsToMeters);
            return new(pelletDirection, collision, collider, ShotReference(collider), point, delay);
        }
    }

    private List<RuntimeNativeProjectileFlight> PrepareProjectileFlights(
        Vector3 origin, Vector3 direction, float medianSpreadDegrees, FalloutWeaponDamage damage)
    {
        var shot = _shot ?? throw new InvalidOperationException("Projectile source is absent.");
        var effects = _shotEffects ?? throw new InvalidOperationException("Projectile effects owner is absent.");
        var flights = new List<RuntimeNativeProjectileFlight>(shot.Projectiles);
        FalloutWeaponDamage? explosionDamage = shot.Projectile.ExplosionSource is { } explosion
            ? (_damage ?? throw new InvalidOperationException("Player damage owner is absent.")).Resolve(shot, explosion.Damage)
            : null;
        try
        {
            for (var index = 0; index < shot.Projectiles; index++)
            {
                var projectileDirection = FalloutWeaponSpread.Deviate(
                    direction, medianSpreadDegrees, _weaponHandling!.NextShotRandomUnit);
                var flight = effects.PrepareProjectile(shot.Projectile,
                    _configuration.Simulation.GravityMetersPerSecondSquared,
                    origin, projectileDirection, CollisionMask | CollisionLayer, SelfQueryBodies);
                flight.OnContact = contact => ApplyProjectileFlightContact(shot, damage, contact);
                if (shot.Projectile.ExplosionSource is not null)
                    flight.OnDetonate = point => ApplyProjectileFlightDetonation(shot, explosionDamage!.Value, point);
                flights.Add(flight);
            }
            return flights;
        }
        catch
        {
            foreach (var flight in flights) flight.Free();
            throw;
        }
    }

    private void ApplyProjectileFlightContact(FalloutWeaponShot shot, FalloutWeaponDamage damage,
        RuntimeNativeProjectileContact contact)
    {
        _damageError = null;
        FalloutActorHit? actorHit = null;
        if (contact.Collider is { } collider && RuntimeNativeActorCombat.Find(collider) is { } combat)
        {
            try
            {
                actorHit = combat.Hit(collider, damage, _presentationRecords!.RuntimeFormKey(0x14),
                    _combatLevel!(), _combatGlobals!);
                GD.Print($"OPENNV_WEAPON_PROJECTILE_ACTOR_HIT projectile={shot.Projectile.Form} reference={actorHit.Reference} part={actorHit.Part} damage={actorHit.HealthDamage:R}");
            }
            catch (Exception error)
            {
                _damageError = error.Message;
                GD.PushError($"OPENNV_ACTOR_DAMAGE_UNBOUND reference={ShotReference(collider)} {error.Message}");
            }
        }

        if (contact.Collider is null || shot.ImpactDataSet is not { } impactSet) return;
        TryShotEffect("flight-impact", () =>
        {
            var material = actorHit is { } hit
                ? checked((int)hit.ImpactMaterial)
                : ShotMaterial(contact.Collision);
            if (FalloutImpact.Resolve(_presentationRecords!, impactSet, material) is { } impact)
                _shotEffects!.Impact(impact, contact.Point, contact.Normal, contact.Direction, contact.Collider as Node3D);
        });
    }

    private void ApplyProjectileFlightDetonation(FalloutWeaponShot shot, FalloutWeaponDamage blastDamage, Vector3 point)
    {
        if (shot.Projectile.ExplosionSource is not { } explosion) return;
        _lastExplosion = RuntimeNativeExplosionCombat.Detonate(this, this, _presentationRecords!, explosion,
            blastDamage, point, CollisionMask | CollisionLayer, _configuration.World.GameUnitsToMeters,
            _presentationRecords!.RuntimeFormKey(0x14), _combatLevel!(), _combatGlobals!, this,
            _damageSelf ?? throw new InvalidOperationException("Player self-damage owner is absent."));
        GD.Print($"OPENNV_WEAPON_EXPLOSION weapon={shot.Weapon} explosion={explosion.Form} result={System.Text.Json.JsonSerializer.Serialize(_lastExplosion)}");
    }

    private PlayerProjectileDamageSummary ApplyProjectileTraces(IReadOnlyList<PlayerProjectileTrace> traces)
    {
        _damageError = null;
        var hitCount = 0;
        var actorHitCount = 0;
        var pendingActorHits = 0;
        var pendingEvents = 0;
        var impactRequests = 0;
        var damageResolved = false;
        FalloutWeaponDamage? resolvedDamage = null;
        FalloutActorHit? lastDamage = null;
        var shot = _shot ?? throw new InvalidOperationException("Projectile source is absent.");
        var records = _presentationRecords ?? throw new InvalidOperationException("Projectile records are absent.");
        var attacker = records.RuntimeFormKey(0x14);
        var impactSet = shot.ImpactDataSet;
        foreach (var trace in traces)
        {
            if (trace.Collider is not { } collider) continue;
            hitCount++;
            var combat = RuntimeNativeActorCombat.Find(collider);
            if (combat is not null && !damageResolved)
            {
                damageResolved = true;
                try
                {
                    resolvedDamage = (_damage ?? throw new InvalidOperationException("Player damage owner is absent."))
                        .Resolve(shot);
                }
                catch (Exception error)
                {
                    _damageError = error.Message;
                    GD.PushError($"OPENNV_ACTOR_DAMAGE_UNBOUND reference={trace.Reference} {error.Message}");
                }
            }

            var actorDamage = combat is not null && resolvedDamage is not null;
            if (impactSet is not null) impactRequests++;
            if (!actorDamage && impactSet is null) continue;
            int? level = actorDamage ? _combatLevel!() : null;
            var globals = actorDamage ? _combatGlobals : null;
            if (actorDamage && globals is null)
            {
                _damageError = "Player global combat state is absent.";
                GD.PushError($"OPENNV_ACTOR_DAMAGE_UNBOUND reference={trace.Reference} {_damageError}");
                actorDamage = false;
            }

            if (trace.DamageDelaySeconds > 0)
            {
                _pendingProjectileImpacts.Add(new(records, shot, actorDamage ? resolvedDamage : null,
                    actorDamage ? combat : null, attacker, level, globals, trace));
                pendingEvents++;
                if (actorDamage) pendingActorHits++;
                continue;
            }

            var applied = CompleteProjectileImpact(trace, actorDamage ? combat : null,
                actorDamage ? resolvedDamage : null, impactSet, records, shot, attacker, level, globals);
            if (applied is { } hit) { lastDamage = hit; actorHitCount++; }
        }
        return new(hitCount, actorHitCount, pendingActorHits, pendingEvents, impactRequests, lastDamage);
    }

    private FalloutActorHit? CompleteProjectileImpact(PlayerProjectileTrace trace, RuntimeNativeActorCombat? combat,
        FalloutWeaponDamage? damage, FalloutFormKey? impactSet, FalloutPluginStack records, FalloutWeaponShot shot,
        FalloutFormKey attacker, int? level, FalloutGlobalState? globals)
    {
        FalloutActorHit? actorHit = null;
        if (trace.Collider is { } collider && combat is not null && damage is { } resolvedDamage)
        {
            try
            {
                actorHit = combat.Hit(collider, resolvedDamage, attacker,
                    level ?? throw new InvalidOperationException("Projectile impact has no attacker level."),
                    globals ?? throw new InvalidOperationException("Projectile impact has no global state."));
                trace.ActorHit = actorHit;
                GD.Print($"OPENNV_WEAPON_PROJECTILE_ACTOR_HIT projectile={shot.Projectile.Form} reference={actorHit.Reference} part={actorHit.Part} damage={actorHit.HealthDamage:R} delayed={trace.DamageDelaySeconds:R}");
            }
            catch (Exception error)
            {
                _damageError = error.Message;
                GD.PushError($"OPENNV_ACTOR_DAMAGE_UNBOUND reference={trace.Reference} {error.Message}");
            }
        }

        if (trace.Collider is { } hitCollider && impactSet is { } set)
        {
            TryShotEffect("impact", () =>
            {
                var material = actorHit is { } hit
                    ? checked((int)hit.ImpactMaterial)
                    : ShotMaterial(trace.Collision);
                if (FalloutImpact.Resolve(records, set, material) is { } impact)
                    _shotEffects!.Impact(impact, trace.Point, trace.Collision["normal"].AsVector3(),
                        trace.Direction, hitCollider as Node3D);
            });
        }
        return actorHit;
    }

    private void AdvancePendingProjectileImpacts(float delta)
    {
        if (!float.IsFinite(delta) || delta <= 0) return;
        for (var index = _pendingProjectileImpacts.Count - 1; index >= 0; index--)
        {
            var pending = _pendingProjectileImpacts[index];
            pending.RemainingSeconds = Math.Max(0, pending.RemainingSeconds - delta);
            if (pending.RemainingSeconds > 0) continue;
            _pendingProjectileImpacts.RemoveAt(index);
            if (pending.Trace.Collider is not { } collider || !IsInstanceValid(collider) || collider.IsQueuedForDeletion())
            {
                _damageError = "Hitscan target left the scene before source impact time.";
                _lastHitscanImpact = new
                {
                    weapon = pending.Shot.Weapon.ToString(),
                    projectile = pending.Shot.Projectile.Form.ToString(),
                    reference = pending.Trace.Reference,
                    state = "target-unbound",
                    error = _damageError
                };
                GD.PushError($"OPENNV_HITSCAN_TARGET_UNBOUND projectile={pending.Shot.Projectile.Form} reference={pending.Trace.Reference} {_damageError}");
                continue;
            }
            var combat = pending.Combat is { } target && IsInstanceValid(target) && !target.IsQueuedForDeletion()
                ? target
                : null;
            if (pending.Damage is not null && combat is null)
            {
                _damageError = "Hitscan actor combat owner left the scene before source impact time.";
                GD.PushError($"OPENNV_HITSCAN_TARGET_UNBOUND projectile={pending.Shot.Projectile.Form} reference={pending.Trace.Reference} {_damageError}");
            }
            var hit = CompleteProjectileImpact(pending.Trace, combat, pending.Damage, pending.Shot.ImpactDataSet,
                pending.Records, pending.Shot, pending.Attacker, pending.AttackerLevel, pending.Globals);
            _lastHitscanImpact = new
            {
                weapon = pending.Shot.Weapon.ToString(),
                projectile = pending.Shot.Projectile.Form.ToString(),
                reference = pending.Trace.Reference,
                delaySeconds = pending.Trace.DamageDelaySeconds,
                state = hit is not null ? "actor-damaged" : pending.Damage is not null ? "damage-unbound" : "impact-resolved",
                healthDamage = hit?.HealthDamage,
                part = hit?.Part,
                died = hit?.Died,
                error = _damageError
            };
            GD.Print($"OPENNV_HITSCAN_IMPACT projectile={pending.Shot.Projectile.Form} reference={pending.Trace.Reference} delay={pending.Trace.DamageDelaySeconds:R} healthDamage={hit?.HealthDamage}");
        }
    }
}
