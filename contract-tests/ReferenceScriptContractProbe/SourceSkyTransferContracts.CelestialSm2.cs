using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class SourceSkyTransferContracts
{
    private static void CelestialShaderModel2Contracts()
    {
        var vertexWords = AuthoredModel2(CelestialVertex(), false);
        var pixelWords = AuthoredModel2(CelestialPixel(), true);
        CelestialTextureModifierContracts(pixelWords);
        var vertex = FalloutD3D9SkyProgram.Read(WordBytes(vertexWords), FalloutSkyProgramStage.Vertex);
        var pixel = FalloutD3D9SkyProgram.Read(WordBytes(pixelWords), FalloutSkyProgramStage.Pixel);
        Require(vertex is { MajorVersion: 2, MinorVersion: 0 } && pixel is { MajorVersion: 2, MinorVersion: 1 } &&
            vertex.Outputs.Single(row => row.Usage == 0).Register == 0 &&
            vertex.Outputs.Single(row => row.Usage == 5 && row.Index == 0).Register == 1 &&
            vertex.Outputs.Single(row => row.Usage == 10 && row.Index == 0).Register == 9 &&
            pixel.Inputs.Single(row => row.Usage == 5 && row.Index == 0).Register == 0 &&
            pixel.Inputs.Single(row => row.Usage == 10 && row.Index == 0).Register == 8,
            "SM2 decoder aliased fixed position/texture/colour banks or substituted another stage/version.");
        var source = FalloutMoonSource.Read(new(FalloutSourceMainFamily.Fallout3,
            FalloutSkyTransferDeclaration.StandaloneContractSha256));
        var package = AuthoredModel2Package(vertexWords, pixelWords);
        foreach (var role in new[] { 6, 7 })
        {
            var pass = FalloutSkyCelestialPrograms.Decode(package, "authored-SM2-package", "shaders/authored-sm2.sdp", source, role);
            Require(pass.GodotSource.Contains("source_vertex_colour = o9", StringComparison.Ordinal) &&
                pass.GodotSource.Contains("vec4 v8 = clamp(source_vertex_colour", StringComparison.Ordinal) &&
                pass.GodotSource.Contains("texture(source_Moon_sampler", StringComparison.Ordinal) &&
                pass.GodotSource.Contains("POSITION.z = POSITION.w", StringComparison.Ordinal),
                "SM2 publication bypassed actual operations or lost fixed colour input range/depth/sampler owners.");
        }
        var ps20 = pixelWords.ToArray(); ps20[0] = 0xffff0200;
        Require(FalloutD3D9SkyProgram.Read(WordBytes(ps20), FalloutSkyProgramStage.Pixel).MinorVersion == 0,
            "PS2.0 rejected the same supported straight-line instruction/bank format.");
        foreach (var header in new uint[] { 0xfffe0201, 0xfffe0400, 0xffff0201 })
        {
            var wrong = vertexWords.ToArray(); wrong[0] = header;
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(wrong), FalloutSkyProgramStage.Vertex));
        }
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(pixelWords), FalloutSkyProgramStage.Vertex));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(vertexWords), FalloutSkyProgramStage.Pixel));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes([
            0xfffe0200, 0x02000001, Destination(4, 1), Source(2, 0), 0xffff]), FalloutSkyProgramStage.Vertex));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes([
            0xfffe0200, 0x02000001, Destination(4, 0), Source(3, 0), 0xffff]), FalloutSkyProgramStage.Vertex));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes([
            0xffff0201, 0x0200001f, 0x80000000, Destination(3, 0, 1),
            0x02000001, Destination(8, 0), Source(3, 0), 0xffff]), FalloutSkyProgramStage.Pixel));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes([
            0xffff0201, 0x0200001f, 0x80000000, Destination(1, 2),
            0x02000001, Destination(8, 0), Source(1, 2), 0xffff]), FalloutSkyProgramStage.Pixel));
        Console.WriteLine("OPENNV_CELESTIAL_SM2_PASS authoredTokens=true VS20=true PS20PS2x=true " +
            "fixedBanksDistinct=true semanticConsumers=true wrongStagesAndBanksRefused=true nativePixels=UNEXECUTED");
    }

    private static void CelestialTextureModifierContracts(uint[] pixelWords)
    {
        uint[] Changed(uint[] original, uint opcode, int operand, uint replacement)
        {
            var words = original.ToArray();
            for (var at = 1; at < words.Length;)
            {
                var instruction = words[at++]; var count = (int)((instruction >> 24) & 15);
                if ((instruction & 0xffff) == opcode) { words[at + operand] = replacement; return words; }
                at += count;
            }
            throw new InvalidDataException("Authored fixture omitted its requested opcode.");
        }
        foreach (var coordinate in new[] { Source(3, 0) | 0x01000000, Source(3, 0, 0), Source(1, 0), Source(2, 0) })
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 66, 1, coordinate)), FalloutSkyProgramStage.Pixel));
        foreach (var sampler in new[] { Source(10, 0) | 0x01000000, Source(10, 0) | 0x0b000000,
            Source(10, 0) | 0x0c000000, Source(10, 0, 0) })
        {
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 66, 2, sampler)), FalloutSkyProgramStage.Pixel));
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(CelestialPixel(), 66, 2, sampler)), FalloutSkyProgramStage.Pixel));
        }
        foreach (var destination in new[] { Destination(8, 0), Destination(0, 0, 3), Destination(0, 0) | 0x00100000 })
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 66, 0, destination)), FalloutSkyProgramStage.Pixel));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(CelestialPixel(), 66, 0,
            Destination(0, 0) | 0x00100000)), FalloutSkyProgramStage.Pixel));
        foreach (var absolute in new uint[] { 0x0b000000, 0x0c000000 })
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 5, 1, Source(0, 2) | absolute)), FalloutSkyProgramStage.Pixel));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 1, 0,
            Destination(8, 0) | 0x00100000)), FalloutSkyProgramStage.Pixel));
        Require(FalloutD3D9SkyProgram.Read(WordBytes(Changed(CelestialPixel(), 5, 1,
            Source(0, 2) | 0x0b000000)), FalloutSkyProgramStage.Pixel).MajorVersion == 3 &&
            FalloutD3D9SkyProgram.Read(WordBytes(Changed(CelestialPixel(), 1, 0,
                Destination(8, 0) | 0x00100000)), FalloutSkyProgramStage.Pixel).MajorVersion == 3,
            "PS2 source/output restrictions removed independently valid SM3 arithmetic semantics.");
        // Actual legal SM2 temporary coordinates and temporary arithmetic
        // saturation remain supported; the read-before-write checks still run.
        var temporary = Changed(pixelWords, 66, 1, Source(0, 0));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(temporary), FalloutSkyProgramStage.Pixel));
        var writtenTemporary = WordBytes([
            0xffff0201, 0x0200001f, 0x80000000, Destination(3, 0, 3),
            0x0200001f, 0x90000000, Destination(10, 0),
            0x02000001, Destination(0, 1, 3), Source(3, 0),
            0x03000042, Destination(0, 0), Source(0, 1), Source(10, 0),
            0x02000001, Destination(8, 0), Source(0, 0), 0xffff]);
        Require(FalloutD3D9SkyProgram.Read(writtenTemporary, FalloutSkyProgramStage.Pixel).Samplers.SequenceEqual([0]),
            "A real preceding temporary coordinate writer was rejected by the TEXLD-specific admission.");
        var saturated = Changed(pixelWords, 5, 0, Destination(0, 2) | 0x00100000);
        Require(FalloutD3D9SkyProgram.Read(WordBytes(saturated), FalloutSkyProgramStage.Pixel).MajorVersion == 2,
            "SM2 arithmetic temporary saturation was rejected with its independent invalid output/texture forms.");
        foreach (var semantic in new uint[] { 0x80000010, 0x00000000, 0x80000020, 0x81000000 })
        {
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(AuthoredModel2(CelestialVertex(), false), 31, 0,
                semantic)), FalloutSkyProgramStage.Vertex));
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(CelestialVertex(), 31, 0,
                semantic)), FalloutSkyProgramStage.Vertex));
        }
        foreach (var samplerDeclaration in new uint[] { 0x10000000, 0x90000001, 0x90000020, 0x91000000 })
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes([
                0xffff0201, 0x0200001f, samplerDeclaration, Destination(10, 0),
                0x02000001, Destination(8, 0), Source(2, 0), 0xffff]), FalloutSkyProgramStage.Pixel));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 31, 1,
            Destination(3, 0, 3) | 0x00004000)), FalloutSkyProgramStage.Pixel));
        foreach (var reserved in new uint[] { 0x00004000, 0x00008000 })
        {
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 66, 1,
                Source(3, 0) | reserved)), FalloutSkyProgramStage.Pixel));
            Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(Changed(pixelWords, 66, 2,
                Source(10, 0) | reserved)), FalloutSkyProgramStage.Pixel));
        }
        Console.WriteLine("OPENNV_CELESTIAL_TEXTURE_MODIFIER_PASS samplerIdentity=true " +
            "PS2CoordinateAndDestinationRules=true illegalAbsOutputSatTextureSatRefused=true " +
            "fullDeclarationUsage=true declarationReservedBitsRefused=true nativePixels=UNEXECUTED");
    }

    // Convert only this fixture's independently authored instructions. The
    // mapping is the public D3D9 SM2 register ABI, not a retail instruction copy.
    private static uint[] AuthoredModel2(uint[] original, bool pixel)
    {
        var words = new List<uint> { pixel ? 0xffff0201U : 0xfffe0200U };
        for (var at = 1; at < original.Length;)
        {
            var instruction = original[at++]; var opcode = instruction & 0xffff;
            if (opcode == 0xffff) { words.Add(instruction); break; }
            var count = (int)((instruction >> 24) & 15);
            var operands = original.AsSpan(at, count).ToArray(); at += count;
            static int Bank(uint token) => (int)(((token >> 28) & 7) | ((token >> 8) & 0x18));
            static uint Rebank(uint token, int bank, int index) => (token & ~0x70001fffU) |
                (uint)index | (uint)(bank & 7) << 28 | (uint)(bank & 24) << 8;
            uint Fixed(uint token)
            {
                var bank = Bank(token); var index = (int)(token & 0x7ff);
                if (pixel && bank == 1) return index < 2 ? Rebank(token, 3, index) : Rebank(token, 1, index - 2);
                if (!pixel && bank == 6) return index switch
                {
                    0 => Rebank(token, 4, 0), 1 or 2 => Rebank(token, 6, index - 1),
                    3 => Rebank(token, 5, 0), _ => throw new InvalidDataException("Authored output fixture has an unknown semantic.")
                };
                return token;
            }
            if (opcode == 31)
            {
                if (!pixel && Bank(operands[1]) == 6) continue;
                if (pixel && Bank(operands[1]) == 1) operands[0] = 0x80000000;
                operands[1] = Fixed(operands[1]);
            }
            else if (opcode != 81)
                for (var index = 0; index < operands.Length; index++) operands[index] = Fixed(operands[index]);
            words.Add(instruction); words.AddRange(operands);
        }
        return words.ToArray();
    }

    private static byte[] AuthoredModel2Package(uint[] vertex, uint[] pixel)
    {
        using var body = new MemoryStream(); using (var writer = new BinaryWriter(body, System.Text.Encoding.ASCII, true))
            foreach (var name in new[] { "SKYT.vso", "SKYMM.vso", "SKYTEX.pso" })
            {
                var buffer = new byte[256]; System.Text.Encoding.ASCII.GetBytes(name).CopyTo(buffer, 0);
                var bytes = WordBytes(name.EndsWith(".pso", StringComparison.Ordinal) ? pixel : vertex);
                writer.Write(buffer); writer.Write(bytes.Length); writer.Write(bytes);
            }
        using var complete = new MemoryStream(); using var header = new BinaryWriter(complete);
        header.Write(100U); header.Write(3U); header.Write((uint)body.Length); header.Write(body.ToArray());
        return complete.ToArray();
    }

    internal static int AuditOwnedCelestialPrograms(string dataRoot)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var source = RuntimeLiveContentSource.Current!;
        var sky = FalloutSkyTransferDeclaration.Read(source.FalloutExecutablePath);
        var moon = FalloutMoonSource.Read(sky);
        foreach (var role in new[] { 6, 7 })
        {
            var pass = FalloutSkyCelestialPrograms.Read(source, moon, role);
            if (pass.PackagePath != $"shaders/shaderpackage{FalloutInstallationSettings.Read(source).Renderer.ShaderPackage:000}.sdp" ||
                !pass.GodotSource.Contains("source_Moon_sampler", StringComparison.Ordinal))
                throw new InvalidDataException("Owned celestial decode changed the actual selected package or output consumer.");
            Console.WriteLine($"OPENNV_OWNED_CELESTIAL_PROGRAM_PASS role={role} package={pass.PackagePath} " +
                $"vertex={pass.VertexName} pixel={pass.PixelName} packageSha256={pass.PackageSha256} nativePixels=UNEXECUTED");
        }
        return 0;
    }
}
