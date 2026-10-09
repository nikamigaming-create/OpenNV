using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Rendering;

internal partial class RuntimeNativeSkyLayers
{
    private void RestoreSourceCloudFields(FalloutSkyChildSnapshot? saved, FalloutSkyCloudResource resource)
    {
        if (saved is null) return;
        if (saved.Role != "Clouds" || saved.SourceSha256 != resource.SourceSha256 || saved.Resource != resource.Resource ||
            saved.Precipitation is not null || saved.CloudSlots.Count != resource.Slots.Count ||
            saved.CloudSlots.Select(row => row.Slot).Distinct().Count() != saved.CloudSlots.Count)
            throw new InvalidDataException("Cold Clouds changed its actual model/child/property factory.");
        foreach (var slot in resource.Slots)
        {
            var retained = saved.CloudSlots.SingleOrDefault(row => row.Slot == slot.Slot) ??
                throw new InvalidDataException("Cold Clouds omitted an actual source child slot.");
            if (retained.Geometry != slot.Geometry || retained.Property != slot.Property || retained.Texture is not null ||
                retained.PropertyTexture is not null || retained.BlendBits != 0)
                throw new NotSupportedException("Cold Clouds requires an unowned cross-weather staged/secondary texture writer.");
            var (_, material, _) = _clouds.Single(row => row.Layer == slot.Slot);
            Texture2D? texture = null;
            if (retained.PrimaryTexture is { } path)
            {
                if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Cold Clouds has an empty retained primary texture.");
                if (!_textures.TryGetValue(path, out texture))
                    _textures.Add(path, texture = NativeOwnedMediaLoader.LoadTexture(path));
                _sourceCloudPrimary[slot.Slot] = path;
            }
            if (texture is null) material.SetShaderParameter("cloud_map", default(Variant));
            else material.SetShaderParameter("cloud_map", texture);
            material.SetShaderParameter("cloud_map_blend", default(Variant));
            material.SetShaderParameter("source_cloud_blend", 0f);
            _sourceCloudBlend[slot.Slot] = 0;
        }
    }
}
