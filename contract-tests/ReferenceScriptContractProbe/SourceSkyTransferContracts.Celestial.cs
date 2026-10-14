using System.Text;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceSkyTransferContracts
{
    private static void CelestialContracts()
    {
        CelestialShaderModel2Contracts();
        var source = FalloutMoonSource.Read(new(FalloutSourceMainFamily.Fallout3,
            FalloutSkyTransferDeclaration.StandaloneContractSha256));
        var package = CelestialPackage();
        var primary = FalloutSkyCelestialPrograms.Decode(package, "authored-SDP", "shaders/authored.sdp", source, 6);
        var shadow = FalloutSkyCelestialPrograms.Decode(package, "authored-SDP", "shaders/authored.sdp", source, 7);
        Require(primary.VertexName == "SKYT.vso" && shadow.VertexName == "SKYMM.vso" && primary.Identity != shadow.Identity &&
            primary.GodotSource.Contains("source_transform = transpose(PROJECTION_MATRIX * MODELVIEW_MATRIX)", StringComparison.Ordinal) &&
            primary.GodotSource.Contains("texture(source_Moon_sampler", StringComparison.Ordinal) &&
            primary.GodotSource.Contains("source_Moon_rgb_multiplier", StringComparison.Ordinal) &&
            !primary.GodotSource.Contains("source_Moon_rgb_multiplier = 1", StringComparison.Ordinal) &&
            shadow.GodotSource.Contains("ALPHA = 0.0", StringComparison.Ordinal),
            "Celestial publication lost actual stages/constants/samplers or replaced source ZERO/ONE with a black shadow disc.");
        Reject(() => FalloutSkyCelestialPrograms.Decode(CelestialPackage(duplicate: true), "authored-SDP", "p", source, 6));
        Reject(() => FalloutSkyCelestialPrograms.Decode(package, "authored-SDP", "p", source, 5));
        var invalid = CelestialVertex(); invalid[5] = Bits(float.NaN);
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(invalid), FalloutSkyProgramStage.Vertex));
        invalid = CelestialVertex(); invalid[1] = 0x01000028; // An independent branch instruction has no consumer.
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(invalid), FalloutSkyProgramStage.Vertex));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(CelestialVertex()[..^1]), FalloutSkyProgramStage.Vertex));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes([0xffff0300, 0x02000001, Destination(8, 0), Source(0, 7), 0xffff]),
            FalloutSkyProgramStage.Pixel));
        Reject(() => FalloutD3D9SkyProgram.Read(WordBytes(CelestialVertex()), FalloutSkyProgramStage.Pixel));
        var settings = new FalloutMoonSettings(FalloutMoonRole.Masser, Bits(20), Bits(10), Bits(5), Bits(1), Bits(0), 16);
        var identity = FalloutMoonDrawMath.ParentRotation(source, 0, 0);
        Require(identity == FalloutCelestialMatrix.Identity, "Moon constructor rotation did not retain the source identity stores.");
        var rotated = FalloutMoonDrawMath.ParentRotation(source, 90, 0);
        Require(BitConverter.UInt32BitsToSingle(rotated.M12) < -.99999f &&
            BitConverter.UInt32BitsToSingle(rotated.M21) > .99999f && rotated.M00 == Bits(1),
            "Moon negative-X rotation changed source handedness/rows.");
        Reject(() => FalloutMoonDrawMath.ParentRotation(source, float.NaN, 0));
        Reject(() => FalloutMoonDrawMath.ParentRotation(source, 1e30f, 0));
        var sky = Guid.NewGuid(); var process = Guid.NewGuid();
        var read = new FalloutMoonShaderFieldSample(source.EngineSha256, source.SkyContract, sky, process, Guid.NewGuid(), 1, true, Bits(.75f));
        FalloutMoonDrawMath.RequireShaderRead(read, source, sky, process);
        Reject(() => FalloutMoonDrawMath.RequireShaderRead(null, source, sky, process));
        Reject(() => FalloutMoonDrawMath.RequireShaderRead(read with { Process = Guid.NewGuid() }, source, sky, process));
        Reject(() => FalloutMoonDrawMath.RequireShaderRead(read with { HdrEnabled = false }, source, sky, process));
        var draw = new FalloutMoonNativeDrawFields(primary.Declaration, shadow.Declaration, FalloutCelestialMatrix.Identity,
            FalloutCelestialRgb.White, FalloutCelestialRgb.White, false, false, null, null, null, null, false);
        var native = new FalloutMoonNativeFields(true, true, true, true, true, false, false, null, null,
            false, false, false, Bits(1), Bits(1), draw);
        FalloutMoonDrawPersistence.Validate(native, source, settings, sky, process, primary.Declaration, shadow.Declaration);
        Reject(() => FalloutMoonDrawPersistence.Validate(native with { ShadowAlphaBits = Bits(.2f) }, source, settings,
            sky, process, primary.Declaration, shadow.Declaration));
        Reject(() => FalloutMoonDrawPersistence.Validate(native with { Draw = draw with { PrimaryProgram = shadow.Declaration } },
            source, settings, sky, process, primary.Declaration, shadow.Declaration));
        var upper = FalloutCelestialRgb.Read(new(.2f, .3f, .4f));
        var write = new FalloutMoonNativeFrameFields(Bits(90), Bits(.5f), Bits(.25f), Bits(.8f), upper, 3,
            read, true, true, true, false);
        draw = draw with
        {
            ParentMatrix = rotated, ShadowColor = upper, MatrixWrite = write, PrimaryWrite = write, ShadowWrite = write,
            LastFrame = write with { ShaderInputsReturned = true }, PrimaryDrawAdmitted = true, ShadowDrawAdmitted = true
        };
        native = native with
        {
            PrimaryHasTexture = true, ShadowHasTexture = true,
            PrimaryTexture = new("textures/sky/Masser_full.dds", "authored", new string('A', 64)),
            ShadowTexture = new("textures/sky/MoonShadow.dds", "authored", new string('B', 64)),
            PrimaryAlphaBits = Bits(.5f), ShadowAlphaBits = Bits(.25f), Draw = draw
        };
        FalloutMoonDrawPersistence.Validate(native, source, settings, sky, process, primary.Declaration, shadow.Declaration);
        var hidden = write with { PrimaryFadeBits = Bits(0), ShadowFadeBits = Bits(0), ShaderInputsReturned = false };
        var hiddenNative = native with { ParentHidden = true, Draw = draw with
        {
            PrimaryDrawAdmitted = false, ShadowDrawAdmitted = false, LastFrame = hidden, MatrixWrite = hidden
        } };
        FalloutMoonDrawPersistence.Validate(hiddenNative, source, settings, sky, process, primary.Declaration, shadow.Declaration);
        // The hidden branch retains prior visible property writes. It cannot
        // invent clearing alpha/RGB to match the new invisible frame inputs.
        Reject(() => FalloutMoonDrawPersistence.Validate(hiddenNative with { ShadowAlphaBits = Bits(0) }, source,
            settings, sky, process, primary.Declaration, shadow.Declaration));
        var cold = FalloutMoonDrawPersistence.PrepareCold(native);
        Require(!cold.Draw!.PrimaryDrawAdmitted && !cold.Draw.ShadowDrawAdmitted && cold.Draw.ColdAwaitingFrame &&
            cold.Draw.ParentMatrix == rotated && cold.Draw.ShadowColor == upper,
            "Cold recreation discarded fields or promoted an old native draw receipt.");
        FalloutMoonDrawPersistence.Validate(cold, source, settings, Guid.NewGuid(), Guid.NewGuid(), primary.Declaration, shadow.Declaration);
        Reject(() => FalloutMoonDrawPersistence.Validate(cold with { Draw = cold.Draw with { PrimaryDrawAdmitted = true } },
            source, settings, sky, process, primary.Declaration, shadow.Declaration));
        var times = new FalloutSkyNightTimes(Bits(5), Bits(7), Bits(18), Bits(20));
        var declaration = new FalloutSkyTransferDeclaration(source.EngineSha256, source.SkyContract);
        Require(FalloutSkyNightMath.Alpha(declaration, 5.5f, times) == .5f &&
            FalloutSkyNightMath.Alpha(declaration, 12, times) == 0 &&
            FalloutSkyNightMath.Alpha(declaration, 19.5f, times) == .5f &&
            FalloutSkyNightMath.Alpha(declaration, 0, times) == 1,
            "Standalone NightSky lost its independent half-sunrise/half-sunset opacity intervals.");
        Require(FalloutSkyNightMath.Rotation(declaration, 0, 0, new(Bits(4), Bits(0), Bits(0), Bits(1))) == FalloutCelestialMatrix.Identity,
            "NightSky source period/axis did not produce its actual zero-angle identity fields.");
        Reject(() => FalloutSkyNightMath.Rotation(declaration, 0, 0, new(0, 0, 0, Bits(1))));
        Reject(() => FalloutSkyNightMath.Alpha(Declaration, 12, times));
        Console.WriteLine("OPENNV_CELESTIAL_FIELD_CONTRACT_PASS authoredSM3=true selectedRoles=true driftRefused=true matrixRows=true hiddenFieldsRetained=true coldDrawDisabled=true distinctNightMath=true nativeDraw=unexecuted HDRWeatherModeWriters=unowned");
    }

    // Authored public SM3 tokens; no retail instruction stream is copied.
    private static uint Destination(int bank, int index, uint mask = 15) => 0x80000000U | (uint)index |
        ((uint)(bank & 7) << 28) | ((uint)(bank & 24) << 8) | mask << 16;
    private static uint Source(int bank, int index, uint swizzle = 0xe4) => Destination(bank, index, 0) | swizzle << 16;
    private static uint[] CelestialVertex()
    {
        var words = new List<uint> { 0xfffe0300 };
        void Op(uint opcode, params uint[] values) { words.Add(opcode | (uint)values.Length << 24); words.AddRange(values); }
        Op(81, Destination(2, 2), Bits(.999f), Bits(1), 0, 0);
        Op(31, 0x80000000, Destination(1, 0)); Op(31, 0x80000005, Destination(1, 1)); Op(31, 0x8000000a, Destination(1, 2));
        Op(31, 0x80000000, Destination(6, 0)); Op(31, 0x80000005, Destination(6, 1, 3));
        Op(31, 0x80010005, Destination(6, 2, 3)); Op(31, 0x8000000a, Destination(6, 3));
        Op(9, Destination(6, 0, 1), Source(1, 0), Source(2, 0));
        Op(9, Destination(6, 0, 2), Source(1, 0), Source(2, 1));
        Op(1, Destination(6, 0, 4), Source(2, 2, 0));
        Op(9, Destination(6, 0, 8), Source(1, 0), Source(2, 3));
        Op(1, Destination(6, 1, 3), Source(1, 1)); Op(1, Destination(6, 2, 3), Source(1, 1));
        Op(5, Destination(0, 0), Source(1, 2), Source(2, 4));
        Op(2, Destination(0, 0), Source(0, 0), Source(2, 5)); Op(2, Destination(6, 3), Source(0, 0), Source(2, 6));
        words.Add(0xffff); return words.ToArray();
    }
    private static uint[] CelestialPixel()
    {
        var words = new List<uint> { 0xffff0300 };
        void Op(uint opcode, params uint[] values) { words.Add(opcode | (uint)values.Length << 24); words.AddRange(values); }
        Op(81, Destination(2, 0), Bits(1), 0, 0, 0);
        Op(31, 0x80000005, Destination(1, 0, 3)); Op(31, 0x80010005, Destination(1, 1, 3));
        Op(31, 0x8000000a, Destination(1, 2)); Op(31, 0x90000000, Destination(10, 0)); Op(31, 0x90000000, Destination(10, 1));
        Op(66, Destination(0, 0), Source(1, 0), Source(10, 0)); Op(66, Destination(0, 1), Source(1, 1), Source(10, 1));
        Op(18, Destination(0, 2), Source(2, 4, 0), Source(0, 0), Source(0, 1));
        Op(5, Destination(0, 2), Source(0, 2), Source(1, 2));
        Op(5, Destination(0, 2, 7), Source(0, 2), Source(2, 4, 0x55));
        Op(1, Destination(8, 0), Source(0, 2)); words.Add(0xffff); return words.ToArray();
    }
    private static byte[] WordBytes(uint[] words)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        foreach (var word in words) writer.Write(word); return stream.ToArray();
    }
    private static byte[] CelestialPackage(bool duplicate = false)
    {
        using var body = new MemoryStream(); using (var writer = new BinaryWriter(body, Encoding.ASCII, true))
        {
            foreach (var name in duplicate ? new[] { "SKYT.vso", "SKYT.vso", "SKYMM.vso", "SKYTEX.pso" } :
                new[] { "SKYT.vso", "SKYMM.vso", "SKYTEX.pso" })
            {
                var bytes = WordBytes(name.EndsWith(".pso", StringComparison.Ordinal) ? CelestialPixel() : CelestialVertex());
                var buffer = new byte[256]; Encoding.ASCII.GetBytes(name).CopyTo(buffer, 0);
                writer.Write(buffer); writer.Write(bytes.Length); writer.Write(bytes);
            }
        }
        using var complete = new MemoryStream(); using var header = new BinaryWriter(complete);
        header.Write(100U); header.Write(duplicate ? 4U : 3U); header.Write((uint)body.Length); header.Write(body.ToArray());
        return complete.ToArray();
    }
}
