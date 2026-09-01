using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static partial class OwnedNifParticleEffect
{
    private const string Schema = "opennv-owned-nif-particle-effect/v1";

    internal static Node3D Create(
        JsonElement source,
        RuntimeMaterialLoader.LoadedTextures textures,
        uint renderLayer)
    {
        if (source.GetProperty("schema").GetString() != Schema ||
            source.GetProperty("status").GetString() != "source-particle-graph")
            throw new InvalidOperationException("Unexpected owned NIF particle contract.");
        var root = new Node3D { Name = "OWNED_NIF_PARTICLE_EFFECT" };
        foreach (var system in source.GetProperty("systems").EnumerateArray())
            root.AddChild(CreateSystem(system, textures, renderLayer));
        return root;
    }

    private static Node3D CreateSystem(
        JsonElement source,
        RuntimeMaterialLoader.LoadedTextures textures,
        uint renderLayer)
    {
        if (!source.GetProperty("worldSpace").GetBoolean())
            throw new InvalidOperationException("Local-space NIF particles are not admitted.");
        var emitter = source.GetProperty("emitter");
        var life = ReadRange(emitter.GetProperty("lifeSeconds"), "particle lifetime", true);
        var radius = ReadRange(emitter.GetProperty("radiusGameUnits"), "particle radius", true);
        var speed = ReadRange(emitter.GetProperty("speedGameUnitsPerSecond"), "particle speed", true);
        var birthRate = RequirePositive(source.GetProperty("birthRatePerSecond").GetSingle(), "birth rate");
        var maximum = source.GetProperty("maximumParticles").GetInt32();
        if (maximum <= 0)
            throw new InvalidOperationException("NIF particle maximum must be positive.");
        var amount = Math.Min(maximum, checked((int)MathF.Ceiling(birthRate * life.Maximum)));
        if (amount <= 0)
            throw new InvalidOperationException("NIF particle source produces no live particles.");

        var particles = new CpuParticles3D
        {
            Name = source.GetProperty("name").GetString()!,
            Amount = amount,
            Lifetime = life.Maximum,
            LifetimeRandomness = 1.0 - life.Minimum / life.Maximum,
            LocalCoords = false,
            Emitting = ReadInitialActive(source.GetProperty("activeKeys")),
            InitialVelocityMin = speed.Minimum,
            InitialVelocityMax = speed.Maximum,
            ScaleAmountMin = radius.Minimum,
            ScaleAmountMax = radius.Maximum,
            Color = ReadColor(emitter.GetProperty("initialColor")),
            VisibilityAabb = SourceVisibilityBounds(emitter, speed.Maximum * life.Maximum + radius.Maximum),
        };
        particles.SetLayerMaskValue(1, false);
        for (var layer = 1; layer <= 20; layer++)
            if ((renderLayer & (1u << (layer - 1))) != 0)
                particles.SetLayerMaskValue(layer, true);

        ApplyEmitter(particles, emitter);
        ApplyModifiers(particles, source.GetProperty("modifiers"));
        particles.Mesh = CreateParticleMesh(
            textures.TwoDimensional[source.GetProperty("textureAssetId").GetString()!]);
        return OwnedParticleClock.Create(
            particles,
            source.GetProperty("controller"),
            source.GetProperty("activeKeys"));
    }

    private static void ApplyEmitter(CpuParticles3D particles, JsonElement source)
    {
        var shape = source.GetProperty("shape").GetString();
        if (shape == "box")
        {
            particles.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
            particles.EmissionBoxExtents = ReadVector(source.GetProperty("extentsGameUnits"));
            particles.Position = ReadVector(source.GetProperty("originGodotUnits"));
            particles.Direction = Vector3.Right;
        }
        else if (shape == "mesh-surface")
        {
            var points = source.GetProperty("pointsGodotUnits").EnumerateArray()
                .Select(ReadVector).ToArray();
            var normals = source.GetProperty("normalsGodot").EnumerateArray()
                .Select(ReadVector).ToArray();
            if (points.Length == 0 || normals.Length != points.Length)
                throw new InvalidOperationException("NIF mesh emitter points/normals are incomplete.");
            particles.EmissionShape = CpuParticles3D.EmissionShapeEnum.DirectedPoints;
            particles.EmissionPoints = points;
            particles.EmissionNormals = normals;
            particles.Direction = ReadVector(source.GetProperty("emissionAxisGodot"));
        }
        else
        {
            throw new InvalidOperationException($"Unsupported NIF emitter shape: {shape}");
        }
        var declination = ReadRange(source.GetProperty("declinationRadians"), "declination");
        particles.Spread = Mathf.RadToDeg(Math.Max(Math.Abs(declination.Minimum), Math.Abs(declination.Maximum)));
    }

    private static void ApplyModifiers(CpuParticles3D particles, JsonElement modifiers)
    {
        foreach (var modifier in modifiers.EnumerateArray().OrderBy(RowOrder))
        {
            if (!modifier.GetProperty("active").GetBoolean())
                continue;
            switch (modifier.GetProperty("type").GetString())
            {
                case "NiPSysSpawnModifier":
                    break;
                case "BSPSysSimpleColorModifier":
                    particles.ColorRamp = ReadColorRamp(modifier);
                    break;
                case "NiPSysRotationModifier":
                    var rotation = ReadRange(modifier.GetProperty("speedRadiansPerSecond"), "rotation speed");
                    particles.AngularVelocityMin = Mathf.RadToDeg(
                        modifier.GetProperty("randomSpeedSign").GetBoolean()
                            ? -rotation.Maximum
                            : rotation.Minimum);
                    particles.AngularVelocityMax = Mathf.RadToDeg(rotation.Maximum);
                    var angle = ReadRange(modifier.GetProperty("angleRadians"), "rotation angle");
                    particles.AngleMin = Mathf.RadToDeg(angle.Minimum);
                    particles.AngleMax = Mathf.RadToDeg(angle.Maximum);
                    break;
                case "NiPSysGrowFadeModifier":
                    ApplyGrowFade(particles, modifier);
                    break;
                case "NiPSysGravityModifier":
                    particles.Gravity = ReadVector(modifier.GetProperty("axisGodot")) *
                        modifier.GetProperty("strength").GetSingle();
                    break;
                case "NiPSysBombModifier":
                    var deltaVelocity = modifier.GetProperty("deltaVelocity").GetSingle();
                    particles.RadialAccelMin = deltaVelocity;
                    particles.RadialAccelMax = deltaVelocity;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported NIF particle modifier: {modifier.GetProperty("type").GetString()}");
            }
        }
    }

    private static Gradient ReadColorRamp(JsonElement source)
    {
        var colors = source.GetProperty("colorStops").EnumerateArray().Select(ReadColor).ToArray();
        if (colors.Length != 3)
            throw new InvalidOperationException("NIF simple-color modifier requires three colors.");
        var positions = source.GetProperty("colorStopPercents").EnumerateArray()
            .Select(value => value.GetSingle()).ToArray();
        if (positions.Length != 4)
            throw new InvalidOperationException("NIF simple-color timing requires four stops.");
        var firstEnd = Math.Clamp(positions[0], 0.0f, 1.0f);
        var firstStart = Math.Clamp(positions[1], firstEnd, 1.0f);
        var secondEnd = Math.Clamp(positions[2], firstStart, 1.0f);
        var secondStart = Math.Clamp(positions[3], secondEnd, 1.0f);
        var gradient = new Gradient();
        gradient.SetColors(new[] { colors[0], colors[0], colors[1], colors[1], colors[2], colors[2] });
        gradient.SetOffsets(new[] { 0.0f, firstEnd, firstStart, secondEnd, secondStart, 1.0f });
        return gradient;
    }

    private static void ApplyGrowFade(CpuParticles3D particles, JsonElement source)
    {
        var baseScale = source.GetProperty("baseScale").GetSingle();
        var grow = source.GetProperty("growSeconds").GetSingle() / (float)particles.Lifetime;
        var fade = source.GetProperty("fadeSeconds").GetSingle() / (float)particles.Lifetime;
        var curve = new Curve { MinValue = 0.0f, MaxValue = Math.Max(1.0f, baseScale) };
        curve.AddPoint(Vector2.Zero);
        curve.AddPoint(new Vector2(Math.Clamp(grow, 0.0f, 1.0f), baseScale));
        curve.AddPoint(new Vector2(Math.Clamp(1.0f - fade, grow, 1.0f), baseScale));
        curve.AddPoint(new Vector2(1.0f, 0.0f));
        particles.ScaleAmountCurve = curve;
    }

    private static QuadMesh CreateParticleMesh(Texture2D texture)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = texture,
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return new QuadMesh { Size = new Vector2(2.0f, 2.0f), Material = material };
    }

    private static Aabb SourceVisibilityBounds(JsonElement emitter, float travel) =>
        new(-Vector3.One * travel, Vector3.One * travel * 2.0f);

    private static bool ReadInitialActive(JsonElement keys)
    {
        var rows = keys.EnumerateArray().ToArray();
        if (rows.Length == 0 || rows[0].GetProperty("timeSeconds").GetSingle() != 0.0f)
            throw new InvalidOperationException("NIF emitter active timeline has no time-zero key.");
        return rows[0].GetProperty("value").GetBoolean();
    }

    private static int RowOrder(JsonElement row) => row.GetProperty("order").GetInt32();

    private static SourceRange ReadRange(JsonElement source, string name, bool nonnegative = false)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 2 || values[1] < values[0] || nonnegative && values[0] < 0.0f)
            throw new InvalidOperationException($"Invalid source {name} range.");
        return new SourceRange(values[0], values[1]);
    }

    private static float RequirePositive(float value, string name) => value > 0.0f
        ? value
        : throw new InvalidOperationException($"NIF particle {name} must be positive.");

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 3)
            throw new InvalidOperationException("NIF particle vector must have three components.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Color ReadColor(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4)
            throw new InvalidOperationException("NIF particle color must have four components.");
        return new Color(values[0], values[1], values[2], values[3]);
    }

    private readonly record struct SourceRange(float Minimum, float Maximum);

    private sealed partial class OwnedParticleClock : Node3D
    {
        private CpuParticles3D _particles = null!;
        private ActiveKey[] _keys = Array.Empty<ActiveKey>();
        private double _start;
        private double _stop;
        private double _frequency;
        private double _elapsed;

        internal static OwnedParticleClock Create(
            CpuParticles3D particles,
            JsonElement controller,
            JsonElement activeKeys)
        {
            if (controller.GetProperty("cycleMode").GetString() != "loop")
                throw new InvalidOperationException("Unsupported NIF particle controller cycle.");
            var result = new OwnedParticleClock
            {
                Name = $"{particles.Name}_SOURCE_CLOCK",
                _particles = particles,
                _start = controller.GetProperty("startTimeSeconds").GetDouble(),
                _stop = controller.GetProperty("stopTimeSeconds").GetDouble(),
                _frequency = controller.GetProperty("frequency").GetDouble(),
                _elapsed = controller.GetProperty("phaseSeconds").GetDouble(),
                _keys = activeKeys.EnumerateArray()
                    .Select(row => new ActiveKey(
                        row.GetProperty("timeSeconds").GetDouble(),
                        row.GetProperty("value").GetBoolean()))
                    .OrderBy(row => row.TimeSeconds)
                    .ToArray(),
            };
            if (result._frequency <= 0.0 || result._stop <= result._start || result._keys.Length == 0)
                throw new InvalidOperationException("Invalid NIF particle controller timing.");
            result.AddChild(particles);
            return result;
        }

        public override void _Process(double delta)
        {
            _elapsed += delta * _frequency;
            var duration = _stop - _start;
            var sourceTime = _start + (_elapsed % duration);
            var active = _keys[0].Value;
            foreach (var key in _keys)
            {
                if (key.TimeSeconds > sourceTime)
                    break;
                active = key.Value;
            }
            _particles.Emitting = active;
        }

        private readonly record struct ActiveKey(double TimeSeconds, bool Value);
    }
}
