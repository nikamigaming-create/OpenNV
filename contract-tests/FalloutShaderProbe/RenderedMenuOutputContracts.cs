using System.Buffers.Binary;
using System.Numerics;
using OpenNV.Runtime.Formats.Gamebryo;

internal static class RenderedMenuOutputContracts
{
    internal static void Run()
    {
        // Independent complete ps_2_0 instruction stream: MOV oC0, c0.
        var direct = FalloutD3D9PixelProgram.Read(Words(0xffff0200,
            0x02000001, 0x800f0800, 0xa0e40000, 0x0000ffff));
        var green = new Vector4(26f / 255, 1, 128f / 255, 1);
        var constants = new Dictionary<int, Vector4> { [0] = green };
        var output = direct.Evaluate(Vector2.Zero, constants, NoSample);
        Require(output == green, "Decoded pixel output changed its source constant.");
        constants[0] = new(0, 0, 0, 1);
        Require(direct.Evaluate(Vector2.One, constants, NoSample) == constants[0],
            "A genuine black source output acquired another colour.");

        var surface = FalloutD3D9MenuSurface.SpatialShader(direct);
        Require(surface.Contains(direct.GodotSource, StringComparison.Ordinal) &&
            surface.Contains("vec3 encoded = owned_pixel_program(UV).rgb;", StringComparison.Ordinal),
            "Menu surface did not publish the complete decoded pixel program.");
        Require(!surface.Contains("COLOR", StringComparison.Ordinal) &&
            !surface.Contains("source_color", StringComparison.Ordinal),
            "Menu surface appended an unowned vertex tint or texture conversion.");

        // A source shader that reads a colour interpolator remains unowned;
        // removing an invented final tint does not admit this register bank.
        Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0200,
            0x0200001f, 0x80000000, 0x900f0000,
            0x02000001, 0x800f0800, 0x90e40000, 0x0000ffff)));
        Reject(() => FalloutD3D9PixelProgram.Read(Words(0xffff0200,
            0x02000001, 0x800f0800, 0xa0e40000)));
        Console.WriteLine("OPENNV_MENU_PIXEL_OUTPUT_CONTRACT_PASS completeProgram=true originalBlack=true colourInputRefused=true nativePixels=unverified");
    }

    private static Vector4 NoSample(int sampler, Vector2 coordinate) =>
        throw new InvalidOperationException("Authored MOV-only program sampled a texture.");

    private static byte[] Words(params uint[] words)
    {
        var result = new byte[checked(words.Length * sizeof(uint))];
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)), words[index]);
        return result;
    }

    private static void Require(bool accepted, string message)
    {
        if (!accepted) throw new InvalidDataException(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("An unsupported pixel input or missing END was admitted.");
    }
}
