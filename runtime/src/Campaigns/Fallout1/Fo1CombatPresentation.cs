using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1CombatPresentationNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point0001f = 0.0001f;
    internal const float PresentationFloat0Point5f = 0.5f;
    internal const int PresentationInt32 = 32;
    internal const int PresentationInt64 = 64;
}

internal partial class Fo1CombatPresentation : Node3D
{
    private const string Schema = "opennv-fo1-combat-presentation/v1";
    private readonly List<TimedNode> _timedNodes = [];
    private readonly Dictionary<string, AudioStream> _audio = new(StringComparer.Ordinal);
    private Fo1CombatPresentationProfile _profile = null!;
    private Node3D _casingTemplate = null!;
    private float _casingUnitsToMeters;
    private int _tracers;
    private int _impacts;
    private int _casings;
    private int _groundedCasings;
    private int _ricochets;
    private int _meleeSweeps;
    private int _audioEvents;

    internal int Tracers => _tracers;
    internal int Impacts => _impacts;
    internal int Casings => _casings;
    internal int GroundedCasings => _groundedCasings;
    internal int Ricochets => _ricochets;
    internal int MeleeSweeps => _meleeSweeps;
    internal int AudioEvents => _audioEvents;

    internal void Configure(JsonElement source, Fo1CombatPresentationProfile profile)
    {
        if (RequiredString(source, "schema") != Schema)
            throw new InvalidOperationException("Unexpected Fallout combat-presentation contract.");
        _profile = profile;
        _casingUnitsToMeters = source.GetProperty("unitsToMeters").GetSingle();
        if (_casingUnitsToMeters <= 0.0f)
            throw new InvalidOperationException("Fallout casing scale is invalid.");
        if (profile.CasingCollisionLayer is < 1 or > Fo1CombatPresentationNumericContracts.PresentationInt32)
            throw new InvalidOperationException("Fallout casing collision layer is invalid.");
        AttachCasingGround();

        var casing = source.GetProperty("casing");
        var asset = casing.GetProperty("asset");
        var loaded = VerifiedGltfLoader.Load(
            RequiredString(asset, "model"),
            RequiredString(asset, "sidecar"));
        if (!loaded.SourceSha256.Equals(
                RequiredString(asset, "sourceSha256"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout casing source hash drifted.");
        var textures = RuntimeMaterialLoader.LoadTextures(casing);
        var bindings = RuntimeMaterialLoader.Apply(loaded.Scene, asset, textures);
        var surfaces = Descendants<MeshInstance3D>(loaded.Scene)
            .Sum(mesh => mesh.Mesh?.GetSurfaceCount() ?? 0);
        if (surfaces < 1 || bindings != surfaces)
            throw new InvalidOperationException("Fallout casing material coverage drifted.");
        loaded.Scene.Name = "OwnedPistolCasingTemplate";
        loaded.Scene.Visible = false;
        AddChild(loaded.Scene);
        _casingTemplate = loaded.Scene;

        var audio = source.GetProperty("audio");
        var archiveHash = RequiredString(audio, "archiveSha256");
        if (archiveHash.Length != Fo1CombatPresentationNumericContracts.PresentationInt64)
            throw new InvalidOperationException("Fallout combat-audio archive hash is invalid.");
        foreach (var row in audio.GetProperty("events").EnumerateArray())
        {
            var role = RequiredString(row, "role");
            var path = RequiredString(row, "wav");
            var expectedHash = RequiredString(row, "wavSha256");
            if (Sha256Path(path) != expectedHash)
                throw new InvalidOperationException($"Fallout combat-audio hash drifted: {role}");
            var stream = AudioStreamWav.LoadFromFile(path);
            if (stream is null || !_audio.TryAdd(role, stream))
                throw new InvalidOperationException($"Fallout combat audio could not load: {role}");
        }
        var required = new[]
        {
            "pistol-fire", "pistol-reload", "pistol-dry", "casing-impact",
            "earth-impact", "ricochet", "flesh-impact", "knife-swing",
            "knife-flesh", "rat-injured",
        };
        if (required.Any(role => !_audio.ContainsKey(role)))
            throw new InvalidOperationException("Fallout combat-audio role coverage is incomplete.");
        Name = "Fo1CombatPresentation";
    }

    public override void _Process(double delta)
    {
        for (var index = _timedNodes.Count - 1; index >= 0; index--)
        {
            var row = _timedNodes[index];
            row = row with { RemainingSeconds = row.RemainingSeconds - delta };
            if (row.RemainingSeconds > 0.0)
            {
                _timedNodes[index] = row;
                continue;
            }
            if (GodotObject.IsInstanceValid(row.Node))
                row.Node.QueueFree();
            _timedNodes.RemoveAt(index);
        }
    }

    internal void PresentRanged(
        Vector3 origin,
        Vector3 endpoint,
        bool hit,
        Vector3 casingOrigin,
        Vector3 casingRight)
    {
        Beam(
            "BulletTracer",
            origin,
            endpoint,
            _profile.TracerRadiusMeters,
            _profile.TracerColor,
            _profile.TracerLifetimeSeconds);
        _tracers++;
        Impact(endpoint, _profile.ImpactColor);
        SpawnCasing(casingOrigin, casingRight);
        PlayAudio("pistol-fire", origin);
        PlayAudio(hit ? "flesh-impact" : "earth-impact", endpoint);
        if (hit)
            PlayAudio("rat-injured", endpoint);
        if (_impacts % _profile.RicochetEveryImpacts == 0)
        {
            var ricochetEnd = endpoint + _profile.RicochetDirection.Normalized() *
                _profile.RicochetLengthMeters;
            Beam(
                "BulletRicochet",
                endpoint,
                ricochetEnd,
                _profile.TracerRadiusMeters,
                _profile.RicochetColor,
                _profile.TracerLifetimeSeconds);
            _ricochets++;
            PlayAudio("ricochet", endpoint);
        }
    }

    internal void PresentMelee(Vector3 origin, Vector3 endpoint, bool hit)
    {
        Beam(
            "KnifeSweep",
            origin,
            endpoint,
            _profile.MeleeSweepRadiusMeters,
            _profile.MeleeSweepColor,
            _profile.MeleeSweepLifetimeSeconds);
        _meleeSweeps++;
        PlayAudio("knife-swing", origin);
        if (hit)
        {
            PlayAudio("knife-flesh", endpoint);
            PlayAudio("rat-injured", endpoint);
        }
    }

    internal void PresentReload(Vector3 position) => PlayAudio("pistol-reload", position);

    internal void PresentDryFire(Vector3 position) => PlayAudio("pistol-dry", position);

    internal object Report() => new
    {
        schema = Schema,
        tracers = _tracers,
        impacts = _impacts,
        casings = _casings,
        groundedCasings = _groundedCasings,
        ricochets = _ricochets,
        meleeSweeps = _meleeSweeps,
        impactRadiusMeters = _profile.ImpactRadiusMeters,
        audioEvents = _audioEvents,
        audioRoles = _audio.Keys.Order(StringComparer.Ordinal).ToArray(),
    };

    internal void ClearTransientEffects()
    {
        foreach (var row in _timedNodes)
        {
            if (GodotObject.IsInstanceValid(row.Node) && !row.Node.IsQueuedForDeletion())
                row.Node.QueueFree();
        }
        _timedNodes.Clear();
        foreach (var player in Descendants<AudioStreamPlayer3D>(this))
        {
            if (!player.IsQueuedForDeletion())
                player.QueueFree();
        }
        _audio.Clear();
    }

    private void Impact(Vector3 position, Color color)
    {
        var node = new MeshInstance3D
        {
            Name = "BulletImpact",
            Mesh = new SphereMesh
            {
                Radius = _profile.ImpactRadiusMeters,
                Height = _profile.ImpactRadiusMeters * 2.0f,
                RadialSegments = _profile.MeshRadialSegments,
                Rings = _profile.ImpactRings,
            },
            MaterialOverride = Material(color, _profile.ImpactEmissionEnergy),
        };
        AddChild(node);
        node.GlobalPosition = position;
        _timedNodes.Add(new TimedNode(node, _profile.ImpactLifetimeSeconds));
        _impacts++;
    }

    private void SpawnCasing(Vector3 origin, Vector3 right)
    {
        var body = new RigidBody3D
        {
            Name = "EjectedPistolCasing",
            Mass = _profile.CasingMassKilograms,
            ContactMonitor = true,
            MaxContactsReported = 1,
            CollisionLayer = CasingCollisionMask,
            CollisionMask = CasingCollisionMask,
            PhysicsMaterialOverride = new PhysicsMaterial
            {
                Bounce = _profile.CasingBounce,
                Friction = _profile.CasingFriction,
            },
            AngularVelocity = _profile.CasingAngularVelocityRadiansPerSecond,
        };
        AddChild(body);
        body.GlobalPosition = origin;
        var visual = _casingTemplate.Duplicate() as Node3D ??
            throw new InvalidOperationException("Fallout casing template could not be duplicated.");
        visual.Visible = true;
        visual.Scale = Vector3.One * _casingUnitsToMeters;
        body.AddChild(visual);
        body.AddChild(new CollisionShape3D
        {
            Shape = new SphereShape3D { Radius = _profile.CasingCollisionRadiusMeters },
        });
        var ejection = right.Normalized() * _profile.CasingEjectionSpeedMetersPerSecond +
            Vector3.Up * _profile.CasingUpwardSpeedMetersPerSecond;
        body.ApplyCentralImpulse(ejection * _profile.CasingMassKilograms);
        var grounded = false;
        body.BodyEntered += _ =>
        {
            if (grounded || !GodotObject.IsInstanceValid(body))
                return;
            grounded = true;
            _groundedCasings++;
            PlayAudio("casing-impact", body.GlobalPosition);
        };
        _timedNodes.Add(new TimedNode(body, _profile.CasingLifetimeSeconds));
        _casings++;
    }

    private uint CasingCollisionMask =>
        1u << (_profile.CasingCollisionLayer - 1);

    private void AttachCasingGround()
    {
        var ground = new StaticBody3D
        {
            Name = "CasingPresentationGround",
            CollisionLayer = CasingCollisionMask,
            CollisionMask = CasingCollisionMask,
            Position = new Vector3(
                _profile.CasingGroundHalfExtentMeters,
                _profile.CasingGroundHeightMeters -
                    _profile.CasingGroundThicknessMeters * Fo1CombatPresentationNumericContracts.PresentationFloat0Point5f,
                _profile.CasingGroundHalfExtentMeters),
        };
        ground.AddChild(new CollisionShape3D
        {
            Name = "CasingPresentationGroundShape",
            Shape = new BoxShape3D
            {
                Size = new Vector3(
                    _profile.CasingGroundHalfExtentMeters * 2.0f,
                    _profile.CasingGroundThicknessMeters,
                    _profile.CasingGroundHalfExtentMeters * 2.0f),
            },
        });
        AddChild(ground);
    }

    private void Beam(
        string name,
        Vector3 origin,
        Vector3 endpoint,
        float radius,
        Color color,
        double lifetime)
    {
        var delta = endpoint - origin;
        var length = delta.Length();
        if (length <= Fo1CombatPresentationNumericContracts.PresentationFloat0Point0001f)
            return;
        var node = new MeshInstance3D
        {
            Name = name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius,
                BottomRadius = radius,
                Height = length,
                RadialSegments = _profile.MeshRadialSegments,
            },
            MaterialOverride = Material(color, _profile.TracerEmissionEnergy),
        };
        AddChild(node);
        node.GlobalPosition = (origin + endpoint) * Fo1CombatPresentationNumericContracts.PresentationFloat0Point5f;
        node.Quaternion = new Quaternion(Vector3.Up, delta.Normalized());
        _timedNodes.Add(new TimedNode(node, lifetime));
    }

    private void PlayAudio(string role, Vector3 position)
    {
        var player = new AudioStreamPlayer3D
        {
            Name = "CombatAudio_" + role.Replace('-', '_'),
            Stream = _audio[role],
            UnitSize = _profile.AudioUnitSizeMeters,
            MaxDistance = _profile.AudioMaximumDistanceMeters,
        };
        AddChild(player);
        player.GlobalPosition = position;
        player.Finished += player.QueueFree;
        player.Play();
        _audioEvents++;
    }

    private StandardMaterial3D Material(Color color, float emission) => new()
    {
        AlbedoColor = color,
        Roughness = _profile.MaterialRoughness,
        EmissionEnabled = emission > 0.0f,
        Emission = color,
        EmissionEnergyMultiplier = emission,
    };

    private static string Sha256Path(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string RequiredString(JsonElement source, string name)
    {
        var value = source.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Fallout combat presentation requires {name}.");
        return value;
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private readonly record struct TimedNode(Node Node, double RemainingSeconds);
}
