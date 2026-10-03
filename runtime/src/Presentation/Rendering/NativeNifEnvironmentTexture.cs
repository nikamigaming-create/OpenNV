using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Rendering;

// DDS dimensionality belongs to the resource, independently of the shader's
// environment slot. D3D9 ignores excess lookup coordinates on a 2D resource.
internal static class NativeNifEnvironmentTexture
{
    private static readonly int[] FaceOrder = [0, 1, 4, 5, 3, 2];

    internal static Texture Load(byte[] payload, string source)
    {
        NativeOwnedMediaFormat.ValidateDds(payload);
        var caps = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(112));
        if (caps == 0 && BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(24)) <= 1)
        {
            var texture = NativeDdsTexture.Load(payload, source);
            texture.SetMeta("opennv_environment_texture_kind", "2D");
            return texture;
        }
        if (caps != 0xfe00 || (payload.Length - 128) % 6 != 0)
            throw new InvalidDataException($"NIF environment DDS has incomplete cube faces or unsupported dimensions: {source}");
        var faceBytes = (payload.Length - 128) / 6;
        var images = new Godot.Collections.Array<Image>();
        try
        {
            foreach (var face in FaceOrder)
            {
                var bytes = new byte[checked(128 + faceBytes)];
                payload.AsSpan(0, 128).CopyTo(bytes);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0);
                payload.AsSpan(128 + face * faceBytes, faceBytes).CopyTo(bytes.AsSpan(128));
                var image = new Image();
                images.Add(image);
                var error = image.LoadDdsFromBuffer(bytes);
                if (error != Error.Ok || image.IsEmpty())
                    throw new InvalidDataException($"NIF environment cube face {face} failed decoding: {source}, {error}");
            }
            NativeDdsTexture.PreserveCubeAlpha(images);
            var texture = new Cubemap();
            var result = texture.CreateFromImages(images);
            if (result != Error.Ok)
            {
                texture.Dispose();
                throw new InvalidDataException($"NIF environment cube creation failed: {source}, {result}");
            }
            texture.SetMeta("opennv_environment_texture_kind", "cube");
            return texture;
        }
        finally { foreach (var image in images) image.Dispose(); }
    }
}
