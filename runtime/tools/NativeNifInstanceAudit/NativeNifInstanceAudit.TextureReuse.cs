using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseTextureReuse()
    {
        var cache = new NativeDdsTextureCache(32, 2);
        var payload = new byte[] { 17 };
        var changed = new byte[] { 91 };
        var key = new NativeDdsTextureCache.Key("owned.dds", false, "textures/owned.dds");
        var created = new List<Texture2D>();
        (Texture2D Texture, long Bytes) Create(byte[] bytes)
        {
            var pixels = Enumerable.Range(0, 4).SelectMany(_ => new byte[] { bytes[0], 0, 0, 255 }).ToArray();
            using var image = Image.CreateFromData(2, 2, false, Image.Format.Rgba8, pixels);
            var texture = NativeDdsTexture.Create(image); created.Add(texture);
            return (texture, image.GetDataSize());
        }
        try
        {
            var first = cache.Get(key, payload, () => Create(payload));
            if (!ReferenceEquals(first, cache.Get(key, payload, () => throw new InvalidOperationException("A retained source was uploaded twice."))))
                throw new InvalidDataException("Identical source/payload did not reuse its image.");
            using var left = new StandardMaterial3D { AlbedoTexture = first };
            using var right = new StandardMaterial3D { AlbedoTexture = first };
            left.AlbedoColor = Colors.Red;
            if (right.AlbedoColor != Colors.White) throw new InvalidDataException("Texture reuse shared mutable material state.");
            var normal = cache.Get(key with { Normal = true }, payload, () => Create(payload));
            if (ReferenceEquals(first, normal)) throw new InvalidDataException("Normal validation was bypassed by a diffuse hit.");
            _ = cache.Get(key, payload, () => throw new InvalidOperationException("LRU hit was lost."));
            _ = cache.Get(key with { Source = "other.dds" }, payload, () => Create(payload));
            if (!ReferenceEquals(first, cache.Get(key, payload, () => throw new InvalidOperationException("The recently used image was evicted."))))
                throw new InvalidDataException("Texture LRU did not retain its recent image.");
            if (ReferenceEquals(normal, cache.Get(key with { Normal = true }, payload, () => Create(payload))))
                throw new InvalidDataException("Texture cache exceeded its entry/data budget.");
            var replacement = cache.Get(key, changed, () => Create(changed));
            if (ReferenceEquals(first, replacement)) throw new InvalidDataException("Changed source bytes reused a stale image.");
            using var oldImage = left.AlbedoTexture.GetImage();
            using var newImage = replacement.GetImage();
            if (oldImage.GetData()[0] != 17 || newImage.GetData()[0] != 91)
                throw new InvalidDataException("Cache replacement mutated or disposed a live material's pixels.");
            cache.Retire();
            using var retained = right.AlbedoTexture.GetImage();
            var retired = JsonSerializer.SerializeToElement(cache.State);
            if (retained.GetData()[0] != 17 || retired.GetProperty("bytes").GetInt64() != 0 ||
                retired.GetProperty("entries").GetInt32() != 0 || !retired.GetProperty("retired").GetBoolean())
                throw new InvalidDataException("Texture retirement retained cache storage or invalidated a live native sampler.");
            try { cache.Get(key, payload, () => Create(payload)); throw new InvalidOperationException("Retired cache accepted publication."); }
            catch (ObjectDisposedException) { }
            var oversized = new NativeDdsTextureCache(8);
            if (ReferenceEquals(oversized.Get(key, payload, () => Create(payload)), oversized.Get(key, payload, () => Create(payload))))
                throw new InvalidDataException("Oversized images were retained beyond the cache budget.");
            oversized.Retire();
            GD.Print("OPENNV_NATIVE_TEXTURE_REUSE_PASS payloadIdentity=true normalSeparate=true materialIndependent=true boundedLru=true changedBytes=true liveEviction=true retiredClosed=true oversizedUncached=true");
        }
        finally
        {
            cache.Retire();
            foreach (var texture in created) texture.Dispose();
        }
    }
}
