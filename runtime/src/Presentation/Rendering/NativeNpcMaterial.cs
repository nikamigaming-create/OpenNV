using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class NativeNpcMaterial
{
    internal static Material Resolve(FalloutNpcAppearance appearance, FalloutNpcAppearancePart part,
        FalloutNifFile nif, FalloutNifGeometry geometry, FalloutPluginStack stack, Color sourceAmbient)
    {
        if (part.AlternateTextures.Count != 0)
            throw new NotSupportedException($"NPC {appearance.Npc}: alternate texture shape-index binding is unresolved.");
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned content is absent.");
        var shaders = geometry.Properties.Where(index => index >= 0).Select(nif.ReadObject)
            .OfType<FalloutNifShaderProperty>().ToArray();
        if (shaders.Length == 1 && shaders[0].ShaderType == 14)
        {
            if (shaders[0].TextureSet < 0 || nif.ReadObject(shaders[0].TextureSet) is not FalloutNifShaderTextureSet textures ||
                textures.Textures.Length != 6)
                throw new InvalidDataException("Source FaceGen shader has no complete texture set.");
            var inputs = FalloutNpcFaceMaterial.Resolve(source, appearance, part,
                textures.Textures[0], textures.Textures[1],
                string.IsNullOrEmpty(textures.Textures[2]) ? null : textures.Textures[2], stack);
            return NativeFaceGenMaterial.Create(inputs, nif, geometry, source, sourceAmbient);
        }
        // Every other source shader retains its own material policy.
        Color? hairColor = null;
        if (shaders.Length == 1 && (shaders[0].ShaderFlags & FalloutNpcAppearanceHairColor.ShaderFlag) != 0)
        {
            var rgb = FalloutNpcAppearanceHairColor.Resolve(stack, appearance, part);
            hairColor = new Color(rgb.X, rgb.Y, rgb.Z);
        }
        var material = NativeNifMeshBuilder.BuildMaterial(nif, geometry, hairColor: hairColor);
        if (part.TexturePath is null)
            return material;
        if (material is not ShaderMaterial lighting || lighting.ResourceName != NativeNifLightingMaterial.ResourceIdentity)
            throw new NotSupportedException($"NPC {appearance.Npc}: source texture substitution needs the declared shader owner.");
        if (shaders.Length != 1 || shaders[0].TextureSet < 0 ||
            nif.ReadObject(shaders[0].TextureSet) is not FalloutNifShaderTextureSet original || original.Textures.Length != 6)
            throw new NotSupportedException("Actor texture substitution requires its source shader texture set.");
        var paths = FalloutNpcFaceMaterial.ResolvePartTexturePaths(part, original.Textures[0], original.Textures[1],
            string.IsNullOrEmpty(original.Textures[2]) ? null : original.Textures[2]);
        NativeNifLightingMaterial.SetTexture(lighting, "base", Load(paths.BaseTexturePath));
        if (!paths.NormalTexturePath.Equals(original.Textures[1], StringComparison.OrdinalIgnoreCase))
        {
            NativeNifLightingMaterial.SetTexture(lighting, "normal", Load(paths.NormalTexturePath));
        }
        lighting.SetMeta("opennv_record_texture", part.TexturePath);
        lighting.SetMeta("opennv_record_texture_owner", (part.TextureSource ?? part.Source).ToString());
        return lighting;
    }

    private static Texture2D Load(string path)
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned content is absent.");
        if (!source.TryRead(path, null, out var bytes, out var identity))
            throw new FileNotFoundException($"NPC texture is absent: {path}");
        using var image = new Image();
        var error = image.LoadDdsFromBuffer(bytes);
        if (error != Error.Ok || image.IsEmpty())
            throw new InvalidDataException($"NPC texture {identity} failed DDS decoding: {error}");
        var texture = ImageTexture.CreateFromImage(image);
        texture.SetMeta("opennv_source_texture", identity);
        return texture;
    }
}
