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
        var shapes = nif.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips" or "BSSegmentedTriShape")
            .Select(block => nif.ReadGeometry(block.Index)).ToArray();
        FalloutNifGeometry Target(FalloutNpcTextureOverride entry)
        {
            if (entry.ShapeIndex >= 0 && entry.ShapeIndex < shapes.Length &&
                shapes[entry.ShapeIndex].Name.TrimStart('#') == entry.ShapeName.TrimStart('#'))
                return shapes[entry.ShapeIndex];
            var matches = shapes.Where(shape => shape.Name == entry.ShapeName).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Record alternate texture target differs: {part.Source}/{entry.ShapeIndex}/{entry.ShapeName}.");
            return matches[0];
        }
        var selected = part.AlternateTextures.SingleOrDefault(entry => Target(entry).Block.Index == geometry.Block.Index);
        if (selected is null) return null;
        var shader = geometry.Properties.Where(index => index >= 0).Select(nif.ReadObject).OfType<FalloutNifShaderProperty>().SingleOrDefault()
            ?? throw new NotSupportedException($"Record alternate texture target {part.Source}/{geometry.Name} has no lighting texture-set shader.");
        var original = (FalloutNifShaderTextureSet)nif.ReadObject(shader.TextureSet);
        var paths = original.Textures.ToArray();
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
        using var image = new Image();
        var error = image.LoadDdsFromBuffer(bytes);
        if (error != Error.Ok || image.IsEmpty())
            throw new InvalidDataException($"NPC texture {identity} failed DDS decoding: {error}");
        var texture = NativeDdsTexture.Create(image);
        texture.SetMeta("opennv_source_texture", identity);
        return texture;
    }
}
