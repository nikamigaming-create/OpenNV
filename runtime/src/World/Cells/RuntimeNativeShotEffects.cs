using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

// Short-lived source effects in world space. Gameplay owns the shot and hit;
// these nodes cannot change ammunition, health, inventory or actor identity.
internal sealed partial class RuntimeNativeShotEffects : Node3D
{
    private readonly FalloutPluginStack _records;
    private readonly RuntimeLiveContentSource _content;
    private readonly float _units;
    private readonly uint _worldMask;
    private readonly PhysicsBody3D? _shooter;
    private readonly Random _random = new();
    private readonly List<Effect> _effects = [];
    private Effect? _spareImpact;
    private sealed class Effect(Node3D root, double remaining, NativeNifEffectPlayback? playback, FalloutFormKey? impact = null)
    {
        internal Node3D Root { get; } = root;
        internal double Remaining { get; set; } = remaining;
        internal NativeNifEffectPlayback? Playback { get; } = playback;
        internal FalloutFormKey? Impact { get; } = impact;
        internal Func<Transform3D>? Follow { get; set; }
    }
    private readonly NativeOwnedAnimationSoundPlayer _sounds;
    private RuntimeNativeImpactDecals? _decals;
    private FalloutShellCasing? _shell;
    private string? _shellPath;
    private RuntimeNativeNifPrototype? _shellPrototype;
    private string? _projectilePath;
    private RuntimeNativeNifPrototype? _projectilePrototype;
    private readonly HashSet<RuntimeNativeProjectileFlight> _projectiles = [];
    private long _projectileLaunches, _projectileHits, _projectileMisses, _projectileErrors;
    private long _casings, _impacts;
    private object? _lastCasing, _lastImpact;
    private string? _decalError;
    internal object State => new
    {
        casings = _casings,
        impacts = _impacts,
        active = _effects.Count,
        retainedImpacts = _spareImpact is null ? 0 : 1,
        particles = _effects.Where(effect => effect.Playback is not null).SelectMany(effect => effect.Playback!.Particles)
            .Select(particle => new { particle.BirthCount, particle.ActiveCount, particle.EmissionEnabled }).ToArray(),
        projectileLaunches = _projectileLaunches,
        projectileHits = _projectileHits,
        projectileMisses = _projectileMisses,
        projectileErrors = _projectileErrors,
        projectileFlights = _projectiles.Select(projectile => projectile.Observation).ToArray(),
        lastCasing = _lastCasing,
        lastImpact = _lastImpact,
        decals = _decals?.Observation,
        decalError = _decalError,
        sounds = _sounds.State,
        unbound = "decal-projection-parity,casing-contact-audio,retail-physics-and-pixel-match"
    };

    internal RuntimeNativeShotEffects(FalloutPluginStack records, RuntimeLiveContentSource content, float units,
        PhysicsBody3D? shooter, uint worldMask)
    {
        Name = "SourceShotEffects"; TopLevel = true;
        _records = records; _content = content; _units = units; _shooter = shooter; _worldMask = worldMask;
        _sounds = new(records, content, this, units, new FalloutSoundRandomState(
            BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)))));
        AddChild(_sounds);
    }

    internal void PrepareShell(FalloutWeaponPresentation weapon)
    {
        if (_shellPath == weapon.ShellModel) return;
        _shell ??= FalloutShellCasing.Read(_records);
        var prototype = weapon.ShellModel is { } path ? new RuntimeNativeNifPrototype(ReadBytes(path), _units) : null;
        _shellPrototype?.Scene.Root.Free();
        _spareImpact?.Root.Free(); _spareImpact = null;
        _shellPrototype = prototype; _shellPath = weapon.ShellModel;
    }

    internal void EjectCasing(Transform3D socket, Vector3 camera)
    {
        if (_shellPrototype is null || _shell is null || socket.Origin.DistanceTo(camera) > _shell.CameraDistance * _units) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var root = _shellPrototype.Instantiate();
        try
        {
            var bodies = root.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>().ToArray();
            if (bodies.Length != 1) throw new NotSupportedException("Casing model has no unique source dynamic body.");
            var body = bodies[0];
            // Source local +Z is Godot local +Y. The independent world-space
            // jitter is additive, and must not be normalized after addition.
            var velocity = (socket.Basis.Y.Normalized() + new Vector3(Signed(), Signed(), Signed()) * _shell.DirectionVariation) * (_shell.Speed * _units);
            var spin = socket.Basis.X.Normalized() * Mathf.DegToRad(_shell.RotationDegrees * (1 + Signed() * _shell.RotationVariation));
            body.CollisionLayer = 0; body.CollisionMask = _worldMask;
            body.ContinuousCd = true;
            if (_shooter is not null) body.AddCollisionExceptionWith(_shooter);
            root.Transform = socket; AddChild(root);
            body.LinearVelocity = velocity; body.AngularVelocity = spin;
            foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                    if (mesh.GetActiveMaterial(i) is ShaderMaterial { ResourceName: NativeNifLightingMaterial.ResourceIdentity } shader)
                        shader.SetShaderParameter("source_store_encoded", false);
            _effects.Add(new(root, _shell.Lifetime, null));
            _lastCasing = new
            {
                ordinal = ++_casings,
                path = _shellPath,
                origin = V(socket.Origin),
                velocity = V(velocity),
                spin = V(spin),
                sourceAxis = "+Z",
                lifetime = _shell.Lifetime,
                instantiateMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                owner = "source-havok-body"
            };
        }
        catch { root.Free(); throw; }
    }

    internal RuntimeNativeProjectileFlight PrepareProjectile(FalloutProjectile source, float gravity,
        Vector3 origin, Vector3 direction, uint collisionMask, IEnumerable<Rid> exclusions)
    {
        var path = source.Model ?? throw new NotSupportedException($"Projectile {source.Form} has no source model.");
        if (_projectilePath != path)
        {
            _projectilePrototype?.Scene.Root.Free();
            _projectilePrototype = new(ReadModel(path), _units);
            _projectilePath = path;
        }
        return new(source, _projectilePrototype!.Instantiate(), _units, gravity, origin, direction, collisionMask, exclusions);
    }

    internal void LaunchProjectile(RuntimeNativeProjectileFlight projectile)
    {
        if (!IsInsideTree() || projectile.IsFinished || _projectiles.Contains(projectile))
            throw new InvalidOperationException("Source projectile cannot be launched by this effects owner.");
        AddChild(projectile);
        _projectiles.Add(projectile);
        projectile.OnFinished = ProjectileFinished;
        _projectileLaunches++;
        projectile.Start();
    }

    internal void Impact(FalloutImpact source, Vector3 point, Vector3 normal, Vector3 incoming, Node3D? target = null, bool decal = true,
        Func<Transform3D>? follow = null)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var direction = source.Orientation switch { 0 => normal, 1 => incoming, 2 => incoming.Bounce(normal), _ => throw new InvalidDataException("Impact orientation is invalid.") };
        if (!direction.IsFinite() || direction.LengthSquared() < .0001f) throw new InvalidDataException("Impact frame is degenerate.");
        var reused = _spareImpact?.Impact == source.Form;
        var effect = reused ? _spareImpact : null;
        if (reused) _spareImpact = null;
        var root = effect?.Root ?? new Node3D();
        if (!reused && source.Model is { } path)
        {
            try { root.AddChild(RuntimeNativeNifMeshBuilder.Build(ReadModel(path), _units, contentSource: _content).Root); }
            catch { root.Free(); throw; }
        }
        var built = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // Source impact effects emit along their local +Z (Godot +Y).
            var y = direction.Normalized(); var helper = Mathf.Abs(y.Dot(Vector3.Up)) < .99f ? Vector3.Up : Vector3.Right;
            var x = helper.Cross(y).Normalized();
            root.Transform = follow?.Invoke() ?? new(new Basis(x, y, x.Cross(y)), point);
            if (!reused) AddChild(root);
            var playback = effect?.Playback ?? (source.Model is null ? null : new NativeNifEffectPlayback(root, source.Duration, encoded: false));
            if (reused) playback!.RestartCompleted();
            else playback?.Start();
            if (effect is null)
            {
                // Retain one completed effect. Concurrent shots keep independent
                // controllers, particles and sounds; physics-bearing effects are
                // rebuilt because their full reset contract is not established.
                var recyclable = playback is not null && !root.FindChildren("*", "", true, false).Any(node => node is CollisionObject3D);
                effect = new(root, source.Duration, playback, recyclable ? source.Form : null);
            }
            effect.Remaining = source.Duration;
            effect.Follow = follow;
            var playing = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var sound in source.Sounds) _sounds.DispatchSound(sound, root);
            var sounded = System.Diagnostics.Stopwatch.GetTimestamp();
            _effects.Add(effect);
            if (decal && source.Decal is not null) PlaceDecal(source, point, normal, target);
            _lastImpact = new
            {
                ordinal = ++_impacts,
                form = source.Form.ToString(),
                source.Model,
                point = V(point),
                normal = V(normal),
                orientation = source.Orientation,
                source.TextureSet,
                reused,
                buildMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started, built).TotalMilliseconds,
                playbackMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(built, playing).TotalMilliseconds,
                soundMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(playing, sounded).TotalMilliseconds,
                decal = !decal ? "separate-projection" : source.Decal is not null ? "source-projected" : "source-disabled"
            };
        }
        catch { root.Free(); throw; }
    }

    internal void PlaceDecal(FalloutImpact source, Vector3 point, Vector3 normal, Node3D? target = null)
    {
        try
        {
            if (_decals is null) { _decals = new(_records, _units); AddChild(_decals); }
            _decals.Place(source, point, normal, target); _decalError = null;
        }
        catch (Exception error)
        {
            _decalError = error.Message;
            GD.PushError("OPENNV_IMPACT_DECAL_UNBOUND " + error.Message);
        }
    }

    public override void _Process(double delta)
    {
        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            if (effect.Follow is not null) effect.Root.Transform = effect.Follow();
            effect.Remaining -= delta; effect.Playback?.Advance(delta);
            if (effect.Remaining > 0 || effect.Playback is { Active: true } ||
                effect.Root.FindChildren("*", "AudioStreamPlayer3D", true, false).OfType<AudioStreamPlayer3D>().Any(voice => voice.Playing)) continue;
            _effects.RemoveAt(i);
            if (effect.Impact is not null)
            {
                _spareImpact?.Root.Free(); _spareImpact = effect;
            }
            else effect.Root.Free();
        }
    }

    public override void _ExitTree()
    {
        _shellPrototype?.Scene.Root.Free(); _shellPrototype = null;
        _projectilePrototype?.Scene.Root.Free(); _projectilePrototype = null;
    }

    private void ProjectileFinished(RuntimeNativeProjectileFlight projectile)
    {
        _projectiles.Remove(projectile);
        if (projectile.Error is not null || projectile.Status is not ("hit" or "range-ended" or "stopped"))
            _projectileErrors++;
        else if (projectile.Contacts != 0) _projectileHits++;
        else _projectileMisses++;
    }

    private FalloutNifFile ReadModel(string path) => FalloutNifFile.Read(ReadBytes(path));
    private byte[] ReadBytes(string path) => _content.TryRead(path, null, out var bytes, out _) ? bytes :
        throw new FileNotFoundException("Source shot effect model is missing.", path);
    private float Signed() => _random.NextSingle() * 2 - 1;
    private static float[] V(Vector3 vector) => [vector.X, vector.Y, vector.Z];
}
