using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutWeaponShot? _shot;
    private int _pendingShotCount;
    private long _shotsFired, _emptyTriggers;
    private object? _lastShot;
    private string? _firePreparationError;
    private string? _muzzleError;
    private ulong _muzzleOwner;
    private FalloutFormKey? _muzzleProjectile;
    private RuntimeNativeShotEffects? _shotEffects;
    private FalloutWeaponDamageResolver? _damage;
    private FalloutGlobalState? _combatGlobals;
    private Func<int>? _combatLevel;
    private string? _damageError;
    private double _firePreparationMilliseconds;
    private object? _firePreparationTiming;
    internal void ConfigureCombat(FalloutPluginStack records, FalloutGlobalState globals, Func<int> level,
        Func<int, float> actorValue, Func<IReadOnlyList<FalloutPerkEntry>> perks)
    {
        _damage = new(records, _presentationInventory!, actorValue, perks); _combatGlobals = globals; _combatLevel = level;
    }
    private readonly Dictionary<string, string> _shotEffectErrors = new(StringComparer.Ordinal);
    internal object FiringState => new
    {
        shots = _shotsFired,
        meleeAttacks = _meleeAttacks,
        emptyTriggers = _emptyTriggers,
        prepared = _shot,
        last = _lastShot,
        error = _firePreparationError,
        muzzleError = _muzzleError,
        muzzle = _firstPerson?.MuzzleState,
        effects = _shotEffects?.State,
        effectErrors = _shotEffectErrors,
        damageError = _damageError,
        preparationMilliseconds = _firePreparationMilliseconds,
        preparationTiming = _firePreparationTiming,
        unbound = "encounter-leveled-NPC-health,conditional-resistance,armor-wear,skill-condition-spread,critical,sneak,weapon-wear,other-projectile-types,tracers,explosions"
    };

    private void RequestWeaponFire()
    {
        if (IsDefeated?.Invoke() == true) return;
        if (_weaponAction is not null || _firstPerson?.Weapon is not { } weapon) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (IsMeleeWeapon(weapon))
            {
                RequestMeleeWeaponFire(weapon);
                return;
            }
            if (weapon.Ammunition.Count == 0 || weapon.ClipSize == 0 || weapon.AmmoUse == 0)
                throw new NotSupportedException("This weapon needs its melee, thrown or ammo-free attack owner.");
            if (weapon.Automatic && (!float.IsFinite(weapon.AttackShotsPerSecond) || weapon.AttackShotsPerSecond <= 0))
                throw new NotSupportedException("Automatic weapon has no valid source attack-shot rate.");
            if (!_weaponHandling!.CanFire(weapon))
            {
                _emptyTriggers++;
                EnsureWeaponSounds();
                if (weapon.Sounds.TryGetValue("empty", out var sound)) _weaponSounds!.DispatchSound(sound);
                return;
            }
            var ammunition = _weaponHandling.Ammunition(weapon)!.Value;
            if (_shot?.Weapon != weapon.Form || _shot.Ammunition != ammunition)
                _shot = FalloutWeaponShot.Read(_presentationRecords!, weapon.Form, ammunition);
            _shot.RequireRuntimeAttackOwner();
            var definitionDone = System.Diagnostics.Stopwatch.GetTimestamp();
            _shotEffects ??= new(_presentationRecords!, RuntimeLiveContentSource.Current!, UnitsToMeters, this, CollisionMask);
            if (!_shotEffects.IsInsideTree()) AddChild(_shotEffects);
            TryShotEffect("casing-prepare", () => _shotEffects.PrepareShell(weapon));
            var casingDone = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!float.IsFinite(weapon.AttackMultiplier) || weapon.AttackMultiplier <= 0)
                throw new InvalidDataException("WEAP attack multiplier is invalid.");
            var clip = _firstPerson.PrepareAction(weapon.AttackGroup);
            var hitEvents = clip.TextKeys
                .SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                .Count(text => text.Trim().Equals("Hit", StringComparison.OrdinalIgnoreCase));
            if (hitEvents == 0)
                throw new NotSupportedException("Weapon attack needs its source Hit event count.");
            var clipDone = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_muzzleOwner != _firstPerson.GetInstanceId() || _muzzleProjectile != _shot.Projectile.Form)
            {
                _muzzleOwner = _firstPerson.GetInstanceId(); _muzzleProjectile = _shot.Projectile.Form; _muzzleError = null;
                try { _firstPerson.PrepareMuzzle(_shot.Projectile); _thirdPerson?.PrepareMuzzle(_shot.Projectile); }
                catch (Exception error)
                {
                    // Presentation failure does not turn a valid source shot
                    // into an empty magazine or silently accept the missing FX.
                    _muzzleError = error.Message;
                    GD.PushError("OPENNV_MUZZLE_PRESENTATION_UNBOUND " + error.Message);
                }
            }
            var muzzleDone = System.Diagnostics.Stopwatch.GetTimestamp();
            _firePreparationError = null;
            RequestWeaponAction(weapon.AttackGroup);
            var actionDone = System.Diagnostics.Stopwatch.GetTimestamp();
            _firePreparationTiming = new
            {
                definitionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started, definitionDone).TotalMilliseconds,
                casingMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(definitionDone, casingDone).TotalMilliseconds,
                clipMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(casingDone, clipDone).TotalMilliseconds,
                muzzleMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(clipDone, muzzleDone).TotalMilliseconds,
                actionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(muzzleDone, actionDone).TotalMilliseconds
            };
        }
        catch (Exception error)
        {
            _firePreparationError = error.Message; _weaponActionError = error.Message;
            GD.PushError("OPENNV_WEAPON_FIRE_UNBOUND " + error.Message);
        }
        finally { _firePreparationMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }

    // Called after publishing the source arm/weapon pose. A hit event never
    // samples a previous BoneAttachment transform or a screen-centre VR ray.
    private void PublishPendingShot()
    {
        var pending = _pendingShotCount;
        _pendingShotCount = 0;
        if (pending == 0) return;
        if (_firstPerson?.Weapon is not { } weapon || _modalInput ||
            _xr is { } xr && (!xr.RightGrip.GetHasTrackingData() || !xr.RightAim.GetHasTrackingData() || xr.PointAtPipBoy is not null ||
                xr.WorldPointer || _xrRightContact?.Valid != true || !_firstPerson.XrRightContactReached)) return;
        if (_shot is null || _shot.Weapon != weapon.Form)
        {
            if (!IsMeleeWeapon(weapon)) return;
            for (var strike = 0; strike < pending; strike++) PublishPendingMeleeStrike(weapon);
            return;
        }
        for (var pendingIndex = 0; pendingIndex < pending; pendingIndex++)
        {
            if (weapon.Automatic && _automaticFireStopped) return;
            try
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var actor = _xr is null && _thirdPersonMode ? _thirdPerson! : _firstPerson;
                var muzzle = actor.ProjectileTransform();
                if (_xr is null && !_thirdPersonMode)
                    muzzle = _camera.GlobalTransform * _firstPerson.SourceCamera.AffineInverse() * muzzle;
                var from = muzzle.Origin;
                var direction = -muzzle.Basis.Z.Normalized();
                if (_xr is null)
                {
                    var aim = CastShotRay(_camera.GlobalPosition, _camera.GlobalPosition - _camera.GlobalBasis.Z * (_shot.Projectile.Range * UnitsToMeters));
                    var target = aim.TryGetValue("position", out var hit) ? hit.AsVector3() : _camera.GlobalPosition - _camera.GlobalBasis.Z * (_shot.Projectile.Range * UnitsToMeters);
                    direction = (target - from).Normalized();
                }
                if (!from.IsFinite() || !direction.IsFinite() || direction.LengthSquared() < .99f)
                    throw new InvalidDataException("Source projectile transform is invalid.");
                // Collision resolution is independent of later damage/effect lanes.
                // Unsupported impact behavior remains identified in the observation.
                var isHitscan = _shot.Projectile.Hitscan;
                IReadOnlyList<PlayerProjectileTrace> traces = isHitscan ? TraceProjectiles(from, direction) : [];
                List<RuntimeNativeProjectileFlight> preparedFlights = isHitscan
                    ? []
                    : PrepareProjectileFlights(from, direction,
                        (_damage ?? throw new InvalidOperationException("Player damage owner is absent.")).Resolve(_shot));
                if (!_weaponHandling!.ConsumeShot(weapon, _shot, _presentationRecords!))
                {
                    foreach (var flight in preparedFlights) flight.Free();
                    if (weapon.Automatic)
                    {
                        _automaticFireStopped = true;
                        _pendingShotCount = 0;
                        EnsureWeaponSounds();
                        if (weapon.Sounds.TryGetValue("empty", out var empty)) _weaponSounds!.DispatchSound(empty);
                    }
                    return;
                }
                if (weapon.Automatic && !_weaponHandling.CanFire(weapon)) _automaticFireStopped = true;
                _shotsFired++;
                var collisionDone = System.Diagnostics.Stopwatch.GetTimestamp();
                EnsureWeaponSounds();
                if (weapon.Sounds.TryGetValue("shoot", out var sound)) _weaponSounds!.DispatchSound(sound);
                else _weaponUnboundEvents.Add("weapon-shoot-sound");
                actor.FlashMuzzle();
                var audioDone = System.Diagnostics.Stopwatch.GetTimestamp();
                var damage = isHitscan ? ApplyProjectileDamage(traces) : new PlayerProjectileDamageSummary(0, 0, null);
                var damageDone = System.Diagnostics.Stopwatch.GetTimestamp();
                if (weapon.ShellModel is not null && !_shotEffectErrors.ContainsKey("casing-prepare"))
                    TryShotEffect("casing", () =>
                    {
                        var shell = actor.ShellTransform();
                        if (_xr is null && !_thirdPersonMode) shell = _camera.GlobalTransform * _firstPerson.SourceCamera.AffineInverse() * shell;
                        _shotEffects!.EjectCasing(shell, _camera.GlobalPosition);
                    });
                var casingDone = System.Diagnostics.Stopwatch.GetTimestamp();
                var impactCount = isHitscan ? ApplyProjectileImpacts(traces) : 0;
                foreach (var flight in preparedFlights) _shotEffects!.LaunchProjectile(flight);
                var impactDone = System.Diagnostics.Stopwatch.GetTimestamp();
                var lastTrace = traces.LastOrDefault(trace => trace.Collider is not null) ?? traces.LastOrDefault();
                _lastShot = new
                {
                    ordinal = _shotsFired,
                    weapon = weapon.Form.ToString(),
                    ammunition = _shot.Ammunition.ToString(),
                    projectile = _shot.Projectile.Form.ToString(),
                    projectiles = _shot.Projectiles,
                    projectileHits = damage.HitCount,
                    projectileActorHits = damage.ActorHitCount,
                    projectileImpactRequests = impactCount,
                    origin = new[] { from.X, from.Y, from.Z },
                    direction = lastTrace is null
                        ? new[] { direction.X, direction.Y, direction.Z }
                        : new[] { lastTrace.Direction.X, lastTrace.Direction.Y, lastTrace.Direction.Z },
                    point = lastTrace is null ? (float[]?)null : new[] { lastTrace.Point.X, lastTrace.Point.Y, lastTrace.Point.Z },
                    distanceMeters = lastTrace is null ? 0 : from.DistanceTo(lastTrace.Point),
                    collider = lastTrace?.Collider?.GetPath().ToString(),
                    reference = lastTrace?.Reference,
                    hit = damage.HitCount != 0,
                    projectileFlights = preparedFlights.Count,
                    flightState = isHitscan ? "hitscan-complete" : "in-flight",
                    loaded = _weaponHandling.Loaded(weapon.Form),
                    damage = damage.LastDamage,
                    damageError = _damageError,
                    timing = new
                    {
                        collisionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started, collisionDone).TotalMilliseconds,
                        audioMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(collisionDone, audioDone).TotalMilliseconds,
                        damageMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(audioDone, damageDone).TotalMilliseconds,
                        casingMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(damageDone, casingDone).TotalMilliseconds,
                        impactMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(casingDone, impactDone).TotalMilliseconds,
                        totalMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started, impactDone).TotalMilliseconds
                    },
                    spread = "source-minimum-spread;skill-condition-perk-ammo-modifiers-unbound",
                    tracer = _shot.Projectile.TracerChance == 0 ? "source-disabled" : "unbound-source-tracer"
                };
                GD.Print($"OPENNV_WEAPON_SHOT weapon={weapon.Form} projectile={_shot.Projectile.Form} reference={lastTrace?.Reference} pellets={_shot.Projectiles} hits={damage.HitCount} loaded={_weaponHandling.Loaded(weapon.Form)} damage={damage.LastDamage?.HealthDamage} damageError={_damageError}");
            }
            catch (Exception error)
            {
                _weaponActionError = error.Message; _firePreparationError = error.Message;
                GD.PushError("OPENNV_WEAPON_SHOT_UNBOUND " + error.Message);
                return;
            }
        }
    }

    private Godot.Collections.Dictionary CastShotRay(Vector3 from, Vector3 to)
    {
        using var query = PhysicsRayQueryParameters3D.Create(from, to, CollisionMask | CollisionLayer);
        query.CollideWithAreas = true;
        query.Exclude = SelfQueryBodies;
        return GetWorld3D().DirectSpaceState.IntersectRay(query);
    }

    private void TryShotEffect(string lane, Action action)
    {
        try { action(); _shotEffectErrors.Remove(lane); }
        catch (Exception error)
        {
            if (!_shotEffectErrors.TryGetValue(lane, out var previous) || previous != error.Message)
                GD.PushError($"OPENNV_SHOT_EFFECT_UNBOUND lane={lane} {error.Message}");
            _shotEffectErrors[lane] = error.Message;
        }
    }

    private static int ShotMaterial(Godot.Collections.Dictionary collision)
    {
        return FalloutImpact.MaterialIndex(NativeNifCollisionBuilder.HitMaterial(collision));
    }

    private static string? ShotReference(Node? collider)
    {
        for (var node = collider; node is not null; node = node.GetParent())
        {
            if (node is RuntimeNativeNpc { Appearance.Reference: { } actor }) return actor.ToString();
            if (node.HasMeta("opennv_reference_form_key")) return node.GetMeta("opennv_reference_form_key").AsString();
        }
        return null;
    }
}
