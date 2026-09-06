using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class NativeDdsTexture
{
    internal static ImageTexture Create(Image image)
    {
        var sourceFormat = image.GetFormat();
        var expanded = PreserveAlpha(image);
        var texture = ImageTexture.CreateFromImage(image);
        texture.SetMeta("opennv_dds_source_format", sourceFormat.ToString());
        texture.SetMeta("opennv_dds_upload_format", image.GetFormat().ToString());
        texture.SetMeta("opennv_dds_alpha_owner", expanded ? "BC1-encoded-texels;authored-mips-RGBA8" : "source-format");
        return texture;
    }

    internal static bool PreserveAlpha(Image image)
    {
        if (image.GetFormat() != Image.Format.Dxt1 || !FalloutBc1Alpha.ContainsTransparency(
            image.GetData(), image.GetWidth(), image.GetHeight(), image.GetMipmapCount() + 1)) return false;
        // Godot maps DXT1 to BC1_RGB with an opaque alpha swizzle. Expand only
        // images that actually use the transparent selector, retaining every
        // authored mip in memory. Do not generate replacement mip levels.
        var levels = image.GetMipmapCount();
        var result = image.Decompress();
        if (result != Error.Ok || image.GetFormat() != Image.Format.Rgba8 || image.GetMipmapCount() != levels)
            throw new InvalidDataException($"BC1 alpha/mip preservation failed: {result}.");
        return true;
    }

    internal static void PreserveCubeAlpha(Godot.Collections.Array<Image> faces)
    {
        var expanded = false;
        foreach (var face in faces) expanded |= PreserveAlpha(face);
        if (!expanded) return;
        // A layered texture requires a common format. Expand the other BC1
        // faces too when any face uses encoded transparency.
        foreach (var face in faces)
        {
            if (face.GetFormat() != Image.Format.Dxt1) continue;
            var levels = face.GetMipmapCount();
            if (face.Decompress() != Error.Ok || face.GetFormat() != Image.Format.Rgba8 || face.GetMipmapCount() != levels)
                throw new InvalidDataException("BC1 cubemap alpha/mip preservation failed.");
        }
    }
}
