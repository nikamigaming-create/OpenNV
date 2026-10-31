using Godot;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeNativePlacedLight : OmniLight3D
{
    private Func<float[]>? _regionColor;
    private float[]? _lastColor;
    internal void ConfigureRegionColor(Func<float[]> color) => _regionColor = color;
    private FalloutLightFlicker? _flicker;
    private Transform3D _placement;
    private float _units, _sourceIntensity, _energyScale, _minimumEnergy;

    internal void ConfigureFlicker(FalloutPlacedLight source, float units, float movement,
        float energyScale, float minimumEnergy)
    {
        if (_flicker is not null) throw new InvalidOperationException("Placed-light animation is already bound.");
        _flicker = new(source.AnimationFlags, movement,
            static () => FalloutSharedUnsignedRandom.Process.NextBounded(uint.MaxValue));
        _placement = Transform; _units = units; _sourceIntensity = source.Intensity;
        _energyScale = energyScale; _minimumEnergy = minimumEnergy;
        SetMeta("opennv_ligh_animation_flags", source.AnimationFlags);
        SetMeta("opennv_ligh_animation_owner", "point-light-lifetime;shared-process-unsigned-rng");
    }

    public override void _Process(double delta)
    {
        if (_regionColor is null && _flicker is null) return;
        try
        {
            if (_flicker is not null)
            {
                var sample = _flicker.Advance(delta);
                var offset = GamebryoCoordinate.ConvertVector(new Vector3(sample.X, sample.Y, sample.Z)) * _units;
                Position = _placement.Origin + _placement.Basis * offset;
                LightEnergy = MathF.Max(_minimumEnergy, _sourceIntensity * sample.Intensity * _energyScale);
            }
            if (_regionColor is not null)
            {
                var color = _regionColor();
                if (_lastColor is null || !_lastColor.SequenceEqual(color))
                {
                    LightColor = RetailLighting.GodotLightColor(new(color[0], color[1], color[2]));
                    SetMeta("opennv_ligh_shader_rgb", color);
                    _lastColor = color;
                }
            }
        }
        catch (Exception error)
        {
            SetMeta("opennv_ligh_error", error.Message);
            SetProcess(false);
            GetTree().Paused = true;
            GD.PushError($"OPENNV_LIGHT_ANIMATION_UNBOUND {Name}: {error.Message}");
        }
    }
}
