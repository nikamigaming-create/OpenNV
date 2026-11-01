using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeVertexFogAudit
{
    private static void ExerciseVatsCounts(RenderingDevice device)
    {
        foreach (var format in new[] { RenderingDevice.DataFormat.R8G8B8A8Unorm, RenderingDevice.DataFormat.B8G8R8A8Unorm })
            for (var variant = 0; variant < 3; variant++)
            {
                var frames = Enumerable.Range(0, 4).Select(_ => new byte[256 * 256 * 4]).ToArray();
                for (var y = 0; y < 256; y++)
                    for (var x = 0; x < 256; x++)
                    {
                        var offset = (y * 256 + x) * 4;
                        var red = format == RenderingDevice.DataFormat.B8G8R8A8Unorm ? 2 : 0; var blue = 2 - red;
                        for (var frame = 0; frame < 4; frame++)
                        {
                            var data = frames[frame]; var first = frame is 0 or 2;
                            data[offset + red] = (byte)(first ? x : y);
                            data[offset + blue] = (byte)(first ? y : x);
                            data[offset + 3] = 255;
                            if (frame >= 2 && variant != 0)
                            {
                                data[offset + red] = (byte)Math.Clamp(data[offset + red] + (x % 5) - 2, 0, 255);
                                if (variant == 2 && first) data[offset + 1] = (byte)(y % 4);
                            }
                        }
                    }
                ExerciseVatsCountCase(device, format, 256, 256, frames);
            }

        var cases = new (Color R, Color B, Color MeasuredR, Color MeasuredB)[]
        {
            (new(1,0,0), new(0,0,1), new(1,0,0), new(0,0,1)),
            (new(1,0,0), new(0,0,1), new(0,0,0), new(0,0,0)),
            (new(1,2f/255,0), new(0,0,1), new(1,0,0), new(0,0,1)),
            (new(2f/255,0,0), new(0,0,2f/255), new(1f/255,0,0), new(0,0,2f/255)),
            (new(0,0,0), new(0,0,0), new(0,0,0), new(0,0,0)),
            (new(float.NaN,0,0), new(0,0,1), new(1,0,0), new(0,0,1)),
            (new(float.PositiveInfinity,0,0), new(0,0,float.PositiveInfinity), new(1,0,0), new(0,0,1)),
            (new(-1,0,-2), new(-2,0,-1), new(-1,0,-2), new(-2,0,-1)),
            (new(0.25f,1f/255,0), new(0,0,.25f), new(.25f,0,0), new(0,0,.25f)),
        };
        const int width = 19, height = 7; // Exercises partial workgroups.
        var halfFrames = Enumerable.Range(0, 4).Select(_ => new byte[width * height * 8]).ToArray();
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var item = cases[pixel % cases.Length]; var colors = new[] { item.R, item.B, item.MeasuredR, item.MeasuredB };
            for (var frame = 0; frame < 4; frame++)
                for (var channel = 0; channel < 4; channel++)
                {
                    var value = channel switch { 0 => colors[frame].R, 1 => colors[frame].G, 2 => colors[frame].B, _ => colors[frame].A };
                    BinaryPrimitives.WriteUInt16LittleEndian(halfFrames[frame].AsSpan(pixel * 8 + channel * 2), BitConverter.HalfToUInt16Bits((Half)value));
                }
        }
        ExerciseVatsCountCase(device, RenderingDevice.DataFormat.R16G16B16A16Sfloat, width, height, halfFrames);
        if (NativeVatsGpuCounts.Supports(RenderingDevice.DataFormat.R8G8B8A8Srgb) ||
            NativeVatsGpuCounts.Supports(RenderingDevice.DataFormat.B8G8R8A8Srgb))
            throw new InvalidOperationException("VATS sRGB format silently selected a non-equivalent GPU normalization.");
        GD.Print("OPENNV_VATS_MASK_REDUCTION_GPU_PASS formats=3 exhaustiveUnormPairs=true quantumEdges=true half=true partialWorkgroups=true cpuOracle=true");
    }

    private static void ExerciseVatsCountCase(RenderingDevice device, RenderingDevice.DataFormat format, uint width, uint height, byte[][] frames)
    {
        var sources = new Rid[4];
        try
        {
            using var textureFormat = new RDTextureFormat
            {
                Format = format,
                Width = width,
                Height = height,
                Depth = 1,
                ArrayLayers = 1,
                Mipmaps = 1,
                TextureType = RenderingDevice.TextureType.Type2D,
                UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanCopyFromBit
            };
            using var view = new RDTextureView();
            using var counts = new NativeVatsGpuCounts(device);
            for (var i = 0; i < 4; i++)
            {
                sources[i] = device.TextureCreate(textureFormat, view, [frames[i]]);
                if (!sources[i].IsValid) throw new InvalidOperationException("VATS GPU audit texture creation failed.");
                counts.Capture(sources[i], i);
            }
            counts.Dispatch(); device.Submit(); device.Sync(); // This is a local audit device, never the live main device.
            var observed = NativeVatsGpuCounts.Decode(device.BufferGetData(counts.Result));
            var expected = NativeVatsVisibilitySamples.CountReferencePixels(frames[0], frames[1], frames[2], frames[3], format);
            if (observed != expected)
                throw new InvalidOperationException($"VATS full-resolution GPU counts differ from CPU oracle: {format} expected={expected}, actual={observed}.");
        }
        finally { foreach (var source in sources) if (source.IsValid) device.FreeRid(source); }
    }
}
