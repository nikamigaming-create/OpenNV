using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class NativeNpcMaterial
{
    internal static Material Resolve(FalloutNpcAppearance appearance, FalloutNpcAppearancePart part,
        FalloutNifFile nif, FalloutNifGeometry geometry, FalloutPluginStack stack, Color sourceAmbient,
        RuntimeLiveContentSource? contentSource = null)
    {
        var alternate = Alternate(part, nif, geometry);
        var source = contentSource ?? RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned content is absent.");
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
        var material = NativeNifMeshBuilder.BuildMaterial(nif, geometry, hairColor: hairColor, contentSource: source, texturePaths: alternate);
        // A race body model can contain both skin and clothing/gore surfaces.
        // Its ICON belongs to the skin shader; other shapes retain their NIF
        // texture set instead of receiving a skin image over their clothing.
        if (part.TexturePath is null || shaders.Length == 1 &&
            !FalloutNpcFaceMaterial.UsesRecordTexture(part, shaders[0].ShaderType))
            return material;
        if (material is not ShaderMaterial lighting || lighting.ResourceName != NativeNifLightingMaterial.ResourceIdentity)
            throw new NotSupportedException($"NPC {appearance.Npc}: source texture substitution needs the declared shader owner.");
        if (shaders.Length != 1 || shaders[0].TextureSet < 0 ||
            nif.ReadObject(shaders[0].TextureSet) is not FalloutNifShaderTextureSet original || original.Textures.Length != 6)
            throw new NotSupportedException("Actor texture substitution requires its source shader texture set.");
        var paths = FalloutNpcFaceMaterial.ResolvePartTexturePaths(part, original.Textures[0], original.Textures[1],
            string.IsNullOrEmpty(original.Textures[2]) ? null : original.Textures[2], path => source.TryResolve(path, null, out _));
        NativeNifLightingMaterial.SetTexture(lighting, "base", Load(source, paths.BaseTexturePath));
        if (!paths.NormalTexturePath.Equals(original.Textures[1], StringComparison.OrdinalIgnoreCase))
        {
            NativeNifLightingMaterial.SetTexture(lighting, "normal", Load(source, paths.NormalTexturePath));
        }
        lighting.SetMeta("opennv_record_texture", part.TexturePath);
        lighting.SetMeta("opennv_record_texture_owner", (part.TextureSource ?? part.Source).ToString());
        return lighting;
    }

    internal static IReadOnlyList<string>? Alternate(FalloutNpcAppearancePart part, FalloutNifFile nif, FalloutNifGeometry geometry)
    {
        if (part.AlternateTextures.Count == 0) return null;
        var shapes = FalloutNifGeometryOrder.Read(nif);
        foreach (var entry in part.AlternateTextures)
        {
            if (entry.ShapeIndex < 0 || entry.ShapeIndex >= shapes.Count)
                throw new InvalidDataException($"Record alternate texture index is outside its source model: {part.Source}/{entry.ShapeIndex}/{entry.ShapeName}.");
            if (shapes[entry.ShapeIndex].Block.TypeName == "NiParticleSystem")
                throw new NotSupportedException($"Record particle texture substitution is unbound: {part.Source}/{entry.ShapeIndex}.");
        }
        // The model loader keys entries by 3D index, with later entries replacing
        // the texture at that index. Stored names do not redirect or validate it.
        var selected = part.AlternateTextures.LastOrDefault(entry => shapes[entry.ShapeIndex].Block.Index == geometry.Block.Index);
        if (selected is null) return null;
        var properties = geometry.Properties.Where(index => index >= 0).Select(nif.ReadObject).ToArray();
        var shader = properties.OfType<FalloutNifShaderProperty>().SingleOrDefault();
        var noLighting = properties.OfType<FalloutNifNoLightingProperty>().SingleOrDefault();
        // Some weapon NIFs retain a vertex-only placeholder with an alternate
        // texture entry. Without a source sampler there is no texture to swap;
        // retain that material rather than inventing a lighting shader for it.
        if (shader is null && noLighting is null && !properties.OfType<FalloutNifTexturingProperty>().Any()) return null;
        var paths = shader is not null ? ((FalloutNifShaderTextureSet)nif.ReadObject(shader.TextureSet)).Textures.ToArray() :
            noLighting is not null ? new[] { noLighting.FileName, "", "", "", "", "" } :
                throw new NotSupportedException($"Record alternate texture target {part.Source}/{geometry.Name} needs its legacy texturing binding.");
        foreach (var (field, path) in selected.Textures)
        {
            var slot = field switch
            {
                "TX00" => 0,
                "TX01" => 1,
                "TX02" => 2,
                "TX03" => 3,
                "TX04" => 4,
                "TX05" => 5,
                _ => throw new NotSupportedException($"Record texture slot {field} has no source shader binding.")
            };
            paths[slot] = path;
        }
        return paths;
    }

    private static Texture2D Load(RuntimeLiveContentSource source, string path)
    {
        if (!source.TryRead(path, null, out var bytes, out var identity))
            throw new FileNotFoundException($"NPC texture is absent: {path}");
        var texture = NativeDdsTexture.Load(bytes, identity);
        texture.SetMeta("opennv_source_texture", identity);
        return texture;
    }
}
