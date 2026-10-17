using Godot;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeNativePlacedLight : OmniLight3D
{
    private Func<float[]>? _regionColor;
    private float[]? _lastColor;
    internal void ConfigureRegionColor(Func<float[]> color) => _regionColor = color;

    public override void _Process(double delta)
    {
        if (_regionColor is null) return;
        try
        {
            var color = _regionColor();
            if (_lastColor is not null && _lastColor.SequenceEqual(color)) return;
            LightColor = RetailLighting.GodotLightColor(new(color[0], color[1], color[2]));
            SetMeta("opennv_ligh_shader_rgb", color);
            _lastColor = color;
        }
        catch (Exception error)
        {
            SetMeta("opennv_ligh_error", error.Message);
            SetProcess(false);
            GetTree().Paused = true;
            GD.PushError($"OPENNV_LIGHT_EMITTANCE_UNBOUND {Name}: {error.Message}");
        }
    }
}
