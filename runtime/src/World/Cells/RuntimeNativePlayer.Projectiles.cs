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
        Vector3 point)
    {
        internal Vector3 Direction { get; } = direction;
        internal Godot.Collections.Dictionary Collision { get; } = collision;
        internal Node? Collider { get; } = collider;
        internal string? Reference { get; } = reference;
        internal Vector3 Point { get; } = point;
        internal FalloutActorHit? ActorHit { get; set; }
    }

    private readonly record struct PlayerProjectileDamageSummary(
        int HitCount,
        int ActorHitCount,
        FalloutActorHit? LastDamage);

    private IReadOnlyList<PlayerProjectileTrace> TraceProjectiles(Vector3 origin, Vector3 direction)
    {
        var shot = _shot ?? throw new InvalidOperationException("Projectile source is absent.");
        var traces = new List<PlayerProjectileTrace>();
        for (var index = 0; index < shot.Projectiles; index++)
        {
            var pelletDirection = FalloutWeaponSpread.Deviate(
                direction,
                shot.MinimumSpread,
                _weaponHandling!.NextShotRandomUnit);
            var end = origin + pelletDirection * (shot.Projectile.Range * UnitsToMeters);
            var collision = CastShotRay(origin, end);
            var collider = collision.TryGetValue("collider", out var value) ? value.AsGodotObject() as Node : null;
            var point = collision.TryGetValue("position", out var position) ? position.AsVector3() : end;
            traces.Add(new(pelletDirection, collision, collider, ShotReference(collider), point));
        }
        return traces;
    }

    private List<RuntimeNativeProjectileFlight> PrepareProjectileFlights(
        Vector3 origin, Vector3 direction, FalloutWeaponDamage damage)
    {
        var shot = _shot ?? throw new InvalidOperationException("Projectile source is absent.");
        var effects = _shotEffects ?? throw new InvalidOperationException("Projectile effects owner is absent.");
        var flights = new List<RuntimeNativeProjectileFlight>(shot.Projectiles);
        try
        {
            for (var index = 0; index < shot.Projectiles; index++)
            {
                var projectileDirection = FalloutWeaponSpread.Deviate(
                    direction, shot.MinimumSpread, _weaponHandling!.NextShotRandomUnit);
                var flight = effects.PrepareProjectile(shot.Projectile,
                    _configuration.Simulation.GravityMetersPerSecondSquared,
                    origin, projectileDirection, CollisionMask | CollisionLayer, SelfQueryBodies);
                flight.OnContact = contact => ApplyProjectileFlightContact(shot, damage, contact);
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

    private PlayerProjectileDamageSummary ApplyProjectileDamage(IReadOnlyList<PlayerProjectileTrace> traces)
    {
        _damageError = null;
        var hitCount = 0;
        var actorHitCount = 0;
        var damageResolved = false;
        FalloutWeaponDamage? resolvedDamage = null;
        FalloutActorHit? lastDamage = null;
        foreach (var trace in traces)
        {
            if (trace.Collider is not { } collider) continue;
            hitCount++;
            var combat = RuntimeNativeActorCombat.Find(collider);
            if (combat is null || _damageError is not null) continue;
            if (!damageResolved)
            {
                damageResolved = true;
                try
                {
                    resolvedDamage = (_damage ?? throw new InvalidOperationException("Player damage owner is absent."))
                        .Resolve(_shot ?? throw new InvalidOperationException("Projectile source is absent."));
                }
                catch (Exception error)
                {
                    _damageError = error.Message;
                    GD.PushError($"OPENNV_ACTOR_DAMAGE_UNBOUND reference={trace.Reference} {error.Message}");
                }
            }
            if (resolvedDamage is not { } damage) continue;
            try
            {
                trace.ActorHit = combat.Hit(collider, damage, _presentationRecords!.RuntimeFormKey(0x14),
                    _combatLevel!(), _combatGlobals!);
                lastDamage = trace.ActorHit;
                actorHitCount++;
            }
            catch (Exception error)
            {
                _damageError = error.Message;
                GD.PushError($"OPENNV_ACTOR_DAMAGE_UNBOUND reference={trace.Reference} {error.Message}");
            }
        }
        return new(hitCount, actorHitCount, lastDamage);
    }

    private int ApplyProjectileImpacts(IReadOnlyList<PlayerProjectileTrace> traces)
    {
        if (_shot?.ImpactDataSet is not { } impactSet) return 0;
        var requests = 0;
        foreach (var trace in traces)
        {
            if (trace.Collider is not { } collider) continue;
            requests++;
            TryShotEffect("impact", () =>
            {
                var material = trace.ActorHit is { } hit
                    ? checked((int)hit.ImpactMaterial)
                    : ShotMaterial(trace.Collision);
                if (FalloutImpact.Resolve(_presentationRecords!, impactSet, material) is { } impact)
                    _shotEffects!.Impact(impact, trace.Point, trace.Collision["normal"].AsVector3(),
                        trace.Direction, collider as Node3D);
            });
        }
        return requests;
    }
}
