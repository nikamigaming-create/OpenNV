using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeNativePlacedLight
{
    private bool _diffuseConfigured;

    internal void ConfigureSourceDiffuse(IReadOnlyList<float> diffuse, Func<float[]>? emittance = null)
    {
        if (_diffuseConfigured) throw new InvalidOperationException("Native light diffuse already has its source owner.");
        if (diffuse.Count != 3) throw new InvalidDataException("Native diffuse requires three source channels.");
        SourceFields.ConfigureDiffuse(diffuse, emittance); _diffuseConfigured = true; _dynamicColor = true;
        ApplySourceDiffuse(new(diffuse[0], diffuse[1], diffuse[2]));
    }

    internal void ApplySourceDiffuse(FalloutNifColor3 value)
    {
        if (!float.IsFinite(value.R) || !float.IsFinite(value.G) || !float.IsFinite(value.B))
            throw new InvalidDataException("Native light diffuse has a non-finite source value.");
        SourceFields.StoreDiffuse(value);
        SetMeta("opennv_ligh_current_diffuse", new float[] { value.R, value.G, value.B });
        PublishSourceDiffuse();
    }

    private void PublishSourceDiffuse()
    {
        var color = SourceFields.ReadDiffuse();
        if (_lastColor is not null && _lastColor.SequenceEqual(color)) return;
        LightColor = RetailLighting.GodotLightColor(new(color[0], color[1], color[2]));
        SetMeta("opennv_ligh_shader_rgb", color);
        _lastColor = color;
    }

}
