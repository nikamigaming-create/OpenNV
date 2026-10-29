using Godot;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

internal static class NativeDdsTexture
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, NativeDdsTextureCache> Caches = new();
    internal static Action<Image.Format, int, int, double, double>? UploadObserver { get; set; }
    internal static Action<string, bool, int, bool, double>? LoadObserver { get; set; }
    internal static Action<string, double>? ResolveObserver { get; set; }

    internal static Texture2D Load(byte[] payload, string source, bool normal = false)
        => LoadCore(payload, source, normal, out _);

    internal static Texture2D LoadCached(RuntimeLiveContentSource owner, byte[] payload, string source,
        bool normal = false, string? logicalPath = null)
    {
        var cache = Caches.GetValue(owner, content =>
        {
            var created = new NativeDdsTextureCache(128L * 1024 * 1024);
            content.RegisterRetirement(created.Retire);
            return created;
        });
        return cache.Get(new(source, normal, logicalPath), payload, () =>
        {
            var texture = LoadCore(payload, source, normal, out var bytes);
            return (texture, bytes);
        });
    }

    internal static object? CacheState(RuntimeLiveContentSource owner) => Caches.TryGetValue(owner, out var cache) ? cache.State : null;

    private static Texture2D LoadCore(byte[] payload, string source, bool normal, out long uploadedBytes)
    {
        var observer = LoadObserver;
        var started = observer is null ? 0 : Stopwatch.GetTimestamp();
        if (FalloutDdsMipChain.ReadPartial(payload) is { } partial)
        {
            var partialTexture = NativePartialMipTexture.Create(payload, partial);
            uploadedBytes = partial.Levels.Sum(level => (long)level.Bytes);
            observer?.Invoke(source, normal, payload.Length, true, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return partialTexture;
        }
        using var image = new Image();
        var error = image.LoadDdsFromBuffer(payload);
        if (error != Error.Ok || image.IsEmpty()) throw new InvalidDataException($"Owned DDS decoding failed: {source}, {error}.");
        if (normal && image.GetFormat() == Image.Format.L8)
            throw new NotSupportedException($"Normal texture has an unsupported single-channel format: {source}");
        var texture = Create(image);
        uploadedBytes = image.GetDataSize();
        observer?.Invoke(source, normal, payload.Length, false, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return texture;
    }

    internal static ImageTexture Create(Image image)
    {
        var observer = UploadObserver;
        var started = observer is null ? 0 : Stopwatch.GetTimestamp();
        var sourceFormat = image.GetFormat();
        var expanded = PreserveAlpha(image);
        var prepared = observer is null ? 0 : Stopwatch.GetTimestamp();
        var texture = ImageTexture.CreateFromImage(image);
        observer?.Invoke(sourceFormat, image.GetWidth(), image.GetHeight(),
            Stopwatch.GetElapsedTime(started, prepared).TotalMilliseconds,
            Stopwatch.GetElapsedTime(prepared).TotalMilliseconds);
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
