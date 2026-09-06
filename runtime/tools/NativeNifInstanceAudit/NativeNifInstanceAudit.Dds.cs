using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseDdsImages()
    {
        using var source = ReadDds(DdsFixture());
        using var expected = (Image)source.Duplicate();
        if (expected.Decompress() != Error.Ok) throw new InvalidOperationException("Synthetic DDS did not decode.");
        var bytes = expected.GetData();
        using var texture = NativeDdsTexture.Create(source);
        if (source.GetFormat() != Image.Format.Rgba8 || source.GetMipmapCount() != 3 || !source.GetData().SequenceEqual(bytes))
            throw new InvalidOperationException("BC1 upload changed authored RGBA or mip levels.");
        using var opaque = ReadDds(DdsFixture(opaque: true));
        var compressed = opaque.GetData();
        using var opaqueTexture = NativeDdsTexture.Create(opaque);
        if (opaque.GetFormat() != Image.Format.Dxt1 || !opaque.GetData().SequenceEqual(compressed))
            throw new InvalidOperationException("Opaque BC1 was unnecessarily expanded or modified.");
        var faces = new Godot.Collections.Array<Image>();
        try
        {
            for (var face = 0; face < 6; face++) faces.Add(ReadDds(DdsFixture(opaque: face != 4)));
            NativeDdsTexture.PreserveCubeAlpha(faces);
            if (faces.Any(face => face.GetFormat() != Image.Format.Rgba8 || face.GetMipmapCount() != 3))
                throw new InvalidOperationException("Cubemap faces lost their common format or mip levels.");
        }
        finally { foreach (var face in faces) face.Dispose(); }
        GD.Print("OPENNV_DDS_UPLOAD_PASS encodedAlpha=true authoredMipBytes=true opaqueCompressed=true cubemapFaces=true");
    }

    private async Task ExerciseDdsPixels(string[] args)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("DDS GPU audit requires the normal renderer.");
        await DdsPixels(DdsFixture(), "synthetic");
        if (args is [var dataRoot, var path])
        {
            RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
            using var content = RuntimeLiveContentSource.Current!;
            if (!content.TryRead(path, null, out var bytes, out var source)) throw new FileNotFoundException(path);
            await DdsPixels(bytes, source);
        }
        else if (args.Length != 0) throw new ArgumentException("DDS GPU audit accepts an optional owned root and texture path.");
    }

    private async Task DdsPixels(byte[] payload, string source)
    {
        using var image = ReadDds(payload);
        if (image.GetFormat() != Image.Format.Dxt1) throw new InvalidOperationException("Selected DDS is not BC1.");
        using var raw = ImageTexture.CreateFromImage(image);
        using var expected = (Image)image.Duplicate();
        if (expected.Decompress() != Error.Ok) throw new InvalidOperationException("Selected DDS failed CPU decoding.");
        using var corrected = NativeDdsTexture.Create(image);
        var view = new SubViewport { RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        using var shader = new Shader { Code = """
            shader_type canvas_item;
            render_mode unshaded, blend_disabled;
            uniform sampler2D source_texture : filter_nearest, repeat_disable;
            uniform int source_level;
            void fragment() {
                ivec2 extent = textureSize(source_texture, source_level);
                ivec2 pixel = ivec2(FRAGCOORD.xy) % extent;
                COLOR = vec4(vec3(texelFetch(source_texture, pixel, source_level).a), 1.0);
            }
            """ };
        using var material = new ShaderMaterial { Shader = shader };
        var rect = new ColorRect { Material = material };
        AddChild(view); view.AddChild(rect);
        try
        {
            var rgba = expected.GetData();
            long samples = 0;
            long rawMismatches = 0;
            for (var level = 0; level <= expected.GetMipmapCount(); level++)
            {
                var width = Math.Max(1, expected.GetWidth() >> level);
                var height = Math.Max(1, expected.GetHeight() >> level);
                view.Size = new Vector2I(Math.Max(8, width), Math.Max(8, height));
                rect.Size = view.Size;
                material.SetShaderParameter("source_level", level);
                foreach (var texture in new[] { raw, corrected })
                {
                    material.SetShaderParameter("source_texture", texture);
                    for (var frame = 0; frame < 3; frame++)
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var pixels = view.GetTexture().GetImage();
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var alpha = rgba[expected.GetMipmapOffset(level) + (y * width + x) * 4 + 3];
                            var actual = (int)MathF.Round(pixels.GetPixel(x, y).R * 255);
                            if (texture == raw) { if (actual != alpha) rawMismatches++; }
                            else
                            {
                                samples++;
                                if (actual != alpha) throw new InvalidOperationException($"DDS GPU alpha differs: {source}, mip={level}, pixel={x},{y}, expected={alpha}, actual={actual}.");
                            }
                        }
                    }
                }
            }
            if (rawMismatches == 0) throw new InvalidOperationException("The original upload did not reproduce the selected BC1 alpha failure.");
            GD.Print($"OPENNV_DDS_GPU_PASS source={source} sha256={Convert.ToHexString(SHA256.HashData(payload))} mips={expected.GetMipmapCount() + 1} samples={samples} correctedMismatches=0 originalMismatches={rawMismatches}");
        }
        finally { view.Free(); }
    }

    private static Image ReadDds(byte[] bytes)
    {
        var image = new Image();
        if (image.LoadDdsFromBuffer(bytes) == Error.Ok && !image.IsEmpty()) return image;
        image.Dispose();
        throw new InvalidDataException("DDS audit input failed decoding.");
    }

    private static byte[] DdsFixture(bool opaque = false)
    {
        var bytes = new byte[128 + 56];
        "DDS "u8.CopyTo(bytes);
        void UInt(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        UInt(4, 124); UInt(8, 0xa1007); UInt(12, 8); UInt(16, 8); UInt(20, 32); UInt(28, 4);
        UInt(76, 32); UInt(80, 4); "DXT1"u8.CopyTo(bytes.AsSpan(84)); UInt(108, 0x401008);
        for (var offset = 128; offset < bytes.Length; offset += 8)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), opaque ? ushort.MaxValue : (ushort)0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), opaque ? (ushort)0 : ushort.MaxValue);
            UInt(offset + 4, 0xe4e4e4e4);
        }
        // The smallest authored mip contains alpha even though its padding
        // and header do not declare separate alpha storage.
        UInt(bytes.Length - 4, 3);
        return bytes;
    }
}
