using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeNativePlacedLight
{
    private float[]? _diffuse;
    private Func<float[]>? _emittance;

    internal void ConfigureSourceDiffuse(IReadOnlyList<float> diffuse, Func<float[]>? emittance = null)
    {
        if (_diffuse is not null) throw new InvalidOperationException("Native light diffuse already has its source owner.");
        if (diffuse.Count != 3) throw new InvalidDataException("Native diffuse requires three source channels.");
        _regionColor = null;
        _emittance = emittance;
        ApplySourceDiffuse(new(diffuse[0], diffuse[1], diffuse[2]));
    }

    internal void ApplySourceDiffuse(FalloutNifColor3 value)
    {
        if (!float.IsFinite(value.R) || !float.IsFinite(value.G) || !float.IsFinite(value.B))
            throw new InvalidDataException("Native light diffuse has a non-finite source value.");
        _diffuse = [value.R, value.G, value.B];
        SetMeta("opennv_ligh_current_diffuse", _diffuse);
        PublishSourceDiffuse();
    }

    private void PublishSourceDiffuse()
    {
        var emittance = _emittance?.Invoke();
        if (emittance is not null && (emittance.Length != 3 || emittance.Any(value => !float.IsFinite(value) || value < 0)))
            throw new InvalidDataException("Native light emittance has invalid source channels.");
        var color = new[] { _diffuse![0] * (emittance?[0] ?? 1f),
            _diffuse[1] * (emittance?[1] ?? 1f), _diffuse[2] * (emittance?[2] ?? 1f) };
        if (_lastColor is not null && _lastColor.SequenceEqual(color)) return;
        LightColor = RetailLighting.GodotLightColor(new(color[0], color[1], color[2]));
        SetMeta("opennv_ligh_shader_rgb", color);
        _lastColor = color;
    }

    public override void _ExitTree()
    {
        // A retired native instance must not retain a source-world callback.
        _regionColor = null;
        _emittance = null;
    }
}
