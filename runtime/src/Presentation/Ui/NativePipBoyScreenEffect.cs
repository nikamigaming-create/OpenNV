using System.Runtime.CompilerServices;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

// Interface screen composition, independently implemented from its source
// texture/parameter and timing contracts. Screen curvature stays in the NIF.
internal sealed class NativePipBoyScreenEffect
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, GradientNoise> Noise = new();
    private static Shader? _shader;
    private readonly SubViewport _canvas;
    private readonly GradientNoise _noise;
    private readonly Random _random = new();
    private readonly float _radius, _intensity, _frequency, _pulseRate, _pulseRadius, _pulseIntensity;
    private readonly float _burstDuration, _burstIntensity, _distortDuration, _distortScale, _distortIntensity;
    private readonly int _distortInterval;
    private readonly string _scanlinePath;
    private double _burstElapsed, _distortElapsed, _nextDistort;
    private bool _distorting;
    private float _currentRadius, _currentIntensity;
    internal ShaderMaterial Material { get; }
    internal object State => new
    {
        scanlineTexture = _scanlinePath,
        scanlineFrequency = _frequency,
        blurRadiusPixels = _currentRadius,
        blurIntensity = _currentIntensity,
        openingBurstMilliseconds = _burstElapsed,
        distortionActive = _distorting,
        distortionProgress = _distortElapsed / _distortDuration,
        owner = "owned-interface-textures;source-nine-tap-glow;gradient-noise-pulse;opening-burst;traveling-disturbance",
        unbound = "native-random-table-and-clock-phase;separate-native-mask-color-targets;page-event-bursts;vertical-hold-and-shudder-activation;matched-pixels"
    };

    internal NativePipBoyScreenEffect(SubViewport canvas, FalloutInstallationSettings settings)
    {
        _canvas = canvas;
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Pip-Boy screen has no owned content.");
        _noise = Noise.GetValue(content, _ => new());
        _radius = settings.Number("Pipboy", "fBlurRadiusPipboy");
        _intensity = settings.Number("Pipboy", "fBlurIntensityPipboy");
        _frequency = settings.Number("Pipboy", "fScanlineFrequencyPipboy");
        _pulseRate = settings.Number("InterfaceFX", "fPulseRate");
        _pulseRadius = settings.Number("InterfaceFX", "fPulseRadiusIntensity");
        _pulseIntensity = settings.Number("InterfaceFX", "fPulseBrightenIntensity");
        _burstDuration = settings.Number("InterfaceFX", "fDefaultBurstDuration");
        _burstIntensity = settings.Number("InterfaceFX", "fDefaultBurstIntensity");
        _distortDuration = settings.Number("InterfaceFX", "fDistortDuration");
        _distortScale = settings.Number("InterfaceFX", "fDistortVerticalScale");
        _distortIntensity = settings.Number("InterfaceFX", "fDistortHorizontalScale");
        var interval = settings.NumericIni.Find(FalloutIniCollection.Main, "iDistortMaxInterval:InterfaceFX");
        if (interval is not { Declaration.Kind: 'i', Number: { } maximumInterval } || maximumInterval <= 0 ||
            maximumInterval > int.MaxValue || maximumInterval != Math.Truncate(maximumInterval))
            throw new NotSupportedException("Interface distortion interval has no positive source integer owner.");
        _distortInterval = checked((int)maximumInterval);
        if (new[] { _radius, _intensity, _frequency, _pulseRate, _pulseRadius, _pulseIntensity,
                _burstDuration, _burstIntensity, _distortDuration, _distortScale, _distortIntensity }
            .Any(value => !float.IsFinite(value) || value < 0) || _burstDuration <= 0 || _distortDuration < 1)
            throw new InvalidDataException("Owned interface screen parameters are invalid.");
        // Native stores this duration in an integer field before advancing it.
        _distortDuration = MathF.Truncate(_distortDuration);
        _scanlinePath = settings.String("InterfaceFX", "SScanlineTexture").Replace('\\', '/');
        if (_scanlinePath.StartsWith("Data/", StringComparison.OrdinalIgnoreCase)) _scanlinePath = _scanlinePath[5..];
        _shader ??= new Shader { Code = ShaderSource };
        Material = new ShaderMaterial { Shader = _shader, ResourceName = "Owned Pip-Boy interface screen" };
        Material.SetShaderParameter("source_menu", canvas.GetTexture());
        Material.SetShaderParameter("source_scanlines", NativeOwnedMediaLoader.LoadTexture(_scanlinePath));
        Material.SetShaderParameter("source_disturbance", NativeOwnedMediaLoader.LoadTexture("textures/pipboy3000/pipboydistorteffectmap.dds"));
        var tint = FalloutUiSystemColors.Read(settings, "entity_pipboy");
        Material.SetShaderParameter("source_tint", new Vector4(tint.X, tint.Y, tint.Z, 0));
        Material.SetShaderParameter("source_scanline_frequency", _frequency);
        Advance(0);
    }

    internal void Open() => _burstElapsed = 0;

    internal void Advance(double seconds)
    {
        _burstElapsed += seconds * 1000;
        var now = (double)Time.GetTicksMsec();
        var pulse = 2 * _noise.Sample((float)(now * _pulseRate));
        var burst = _burstIntensity * (float)Math.Max(0, 1 - _burstElapsed / _burstDuration);
        _currentRadius = _radius * (1 + burst) + pulse * _pulseRadius;
        _currentIntensity = _intensity * (1 + burst) + pulse * _pulseIntensity;
        if (_distorting)
        {
            _distortElapsed += seconds * 1000;
            if (_distortElapsed / _distortDuration > _distortScale + 1)
            {
                _distorting = false;
                _nextDistort = now + _random.Next(32768) % _distortInterval;
            }
        }
        else if (now > _nextDistort) { _distorting = true; _distortElapsed = 0; }
        Material.SetShaderParameter("source_blur_offset", new Vector2(_currentRadius / _canvas.Size.X, _currentRadius / _canvas.Size.Y));
        Material.SetShaderParameter("source_blur_intensity", _currentIntensity);
        Material.SetShaderParameter("source_disturbance_parameters", _distorting
            ? new Vector3(_distortScale, (float)(_distortElapsed / _distortDuration), _distortIntensity) : Vector3.Zero);
    }

    private sealed class GradientNoise
    {
        private readonly float[] _gradient = new float[256];
        private readonly int[] _permutation = Enumerable.Range(0, 256).ToArray();
        internal GradientNoise()
        {
            var random = new Random();
            for (var index = 0; index < _gradient.Length; index++) _gradient[index] = (random.Next(32768) % 512 - 256) / 256f;
            for (var index = 255; index > 0; index--)
            {
                var other = random.Next(32768) % 256;
                (_permutation[index], _permutation[other]) = (_permutation[other], _permutation[index]);
            }
        }
        internal float Sample(float position)
        {
            position += 4096;
            var cell = (int)position;
            var part = position - cell;
            var smooth = part * part * part * (part * (part * 6 - 15) + 10);
            var left = part * _gradient[_permutation[cell & 255]];
            var right = (part - 1) * _gradient[_permutation[(cell + 1) & 255]];
            return left + smooth * (right - left);
        }
    }

    internal const string ShaderSource = """
        shader_type spatial;
        render_mode unshaded, fog_disabled, cull_back;
        uniform sampler2D source_menu : filter_linear, repeat_disable;
        uniform sampler2D source_scanlines : filter_linear, repeat_enable;
        uniform sampler2D source_disturbance : filter_linear, repeat_disable;
        uniform vec4 source_tint;
        uniform vec2 source_blur_offset;
        uniform float source_blur_intensity;
        uniform float source_scanline_frequency;
        uniform vec3 source_disturbance_parameters;
        void fragment() {
            vec4 center = texture(source_menu, UV);
            vec4 glow = vec4(0.0);
            for (int y = -1; y <= 1; y++) {
                for (int x = -1; x <= 1; x++) {
                    glow += texture(source_menu, UV + vec2(float(x), float(y)) * source_blur_offset);
                }
            }
            vec3 color = center.rgb + glow.rgb * (source_blur_intensity / 9.0) * source_tint.rgb;
            if (source_disturbance_parameters.x > 0.0) {
                vec2 band_uv = vec2(0.5, 1.0 + UV.y * source_disturbance_parameters.x - source_disturbance_parameters.y);
                color += texture(source_disturbance, band_uv).rgb * source_disturbance_parameters.z * source_tint.rgb * 0.35;
            }
            if (source_scanline_frequency > 0.0) {
                color *= texture(source_scanlines, UV * source_scanline_frequency).rgb;
            }
            // The interface samples and composition above are native encoded
            // RGB. Godot's spatial output applies its transfer, so supply the
            // corresponding linear value only after the complete composition.
            ALBEDO = mix(color / 12.92, pow((max(color, vec3(0.0)) + 0.055) / 1.055,
                vec3(2.4)), step(vec3(0.04045), color));
        }
        """;
}
