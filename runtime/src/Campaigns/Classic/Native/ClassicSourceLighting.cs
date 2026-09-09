using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Campaigns.Fallout1;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

/// <summary>Source object light radius/intensity, adapted to the shared 3D hex scale.</summary>
internal static class ClassicSourceLighting
{
    internal static void Add(Node3D parent, Fallout1NativeMapObject placed, string artPath, ClassicBlockoutPolicy policy)
    {
        if (placed.LightRadius == 0 || placed.LightIntensity == 0 || (placed.Flags & 1) != 0) return;
        if (placed.LightRadius < 0 || placed.LightIntensity < 0)
            throw new InvalidDataException($"Source light {placed.Serial} has a negative radius or intensity.");
        var name = Path.GetFileName(artPath.Replace('\\', '/'));
        var fire = name.Equals("woodfire.frm", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("barrel.frm", StringComparison.OrdinalIgnoreCase);
        var offset = ClassicMapProjection.World(4816 + placed.PixelX, 11 + placed.PixelY);
        var light = new OmniLight3D
        {
            Name = $"SourceLight_{placed.Serial}",
            Position = Fo1HexMath.Center(placed.Tile) + new Vector3((float)offset.X,
                fire ? policy.FireLightHeight : Math.Min(policy.FixtureLightHeight, placed.LightRadius * 0.45f), (float)offset.Z),
            OmniRange = placed.LightRadius,
            OmniAttenuation = 1.25f,
            LightEnergy = placed.LightIntensity / 65536f * policy.SourceLightEnergy,
            LightColor = new Color(fire ? "ffc17a" : "e7dfc6"),
            ShadowEnabled = true,
            ShadowBias = 0.04f,
            ShadowNormalBias = 0.3f,
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 32,
            DistanceFadeLength = 16,
            DistanceFadeShadow = 24,
        };
        light.SetMeta("source_serial", placed.Serial); light.SetMeta("source_tile", placed.Tile);
        light.SetMeta("source_light_radius", placed.LightRadius); light.SetMeta("source_light_intensity", placed.LightIntensity);
        light.SetMeta("presentation", "classic-source-light-3d-adaptation");
        parent.AddChild(light);
    }
}
