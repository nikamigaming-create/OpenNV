using System.Security.Cryptography;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSkyCelestialPass(int Role, string EngineSha256, string SourceContract,
    string PackagePath, string ResourceSource, string PackageSha256, string VertexName, string VertexSha256,
    string PixelName, string PixelSha256, uint DepthWord, string GodotSource)
{
    internal string Identity => FalloutMoonSource.Hash(string.Join(";", EngineSha256, SourceContract, PackagePath,
        ResourceSource, PackageSha256, Role, VertexName, VertexSha256, PixelName, PixelSha256, DepthWord));
    internal OpenNV.Runtime.Gameplay.State.FalloutMoonProgramIdentity Declaration => new(Role, SourceContract,
        PackagePath, ResourceSource, PackageSha256, VertexName, VertexSha256, PixelName, PixelSha256, DepthWord, Identity);
}

internal static class FalloutSkyCelestialPrograms
{
    private const string Rules = "source-generated-Moon-property/v2;family-six;primary-role-six-pass-SKYT-SKYTEX;" +
        "shadow-role-seven-pass-SKYMM-SKYTEX;source-property-RGBA-to-first-colour-constant;other-two-colours-positive-zero;" +
        "primary-source-alpha-property-six-seven-no-alpha-test;shadow-pass-source-zero-destination-one-no-depth-write;" +
        "primary-sampler-and-source-fallback-second-sampler;Moon-property-blend-Params-x-positive-zero;" +
        "Params-y-requires-the-distinct-actual-HDR-conditioned-shader-field-writer;" +
        "source-vertex-far-depth-from-selected-literal;source-UV-colour-registers-and-all-straight-line-operations;" +
        "selected-SM2-fixed-output-and-texture-colour-input-banks-or-SM3-declared-semantics;SM2-pixel-colour-saturation;" +
        "no-weather-mode-HDR-TLS-writer-admission-from-program-selection";
    internal static FalloutSkyCelestialPass Read(RuntimeLiveContentSource source, FalloutMoonSource moon, int role)
    {
        if (role is not (6 or 7)) throw new InvalidDataException("Celestial material requested another original property role.");
        var selected = FalloutSkyTransferDeclaration.Read(source.FalloutExecutablePath);
        moon.Require(selected);
        var packagePath = $"shaders/shaderpackage{FalloutInstallationSettings.Read(source).Renderer.ShaderPackage:000}.sdp";
        if (!source.TryRead(packagePath, null, out var bytes, out var identity)) throw new FileNotFoundException(packagePath);
        return Decode(bytes, identity, packagePath, moon, role);
    }
    internal static FalloutSkyCelestialPass Decode(byte[] bytes, string identity, string path, FalloutMoonSource moon, int role)
    {
        if (role is not (6 or 7) || string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Celestial shader omitted its source role/package identity.");
        var declared = FalloutShaderPackage.Read(bytes);
        var vertexName = role == 6 ? "SKYT.vso" : "SKYMM.vso";
        const string pixelName = "SKYTEX.pso";
        FalloutPackagedShader Select(string name)
        {
            var rows = declared.Where(row => row.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return rows.Length == 1 ? rows[0] : throw new InvalidDataException("Selected celestial program is absent or ambiguous: " + name);
        }
        var vertexBytes = Select(vertexName); var pixelBytes = Select(pixelName);
        var vertex = FalloutD3D9SkyProgram.Read(vertexBytes.Bytecode, FalloutSkyProgramStage.Vertex);
        var pixel = FalloutD3D9SkyProgram.Read(pixelBytes.Bytecode, FalloutSkyProgramStage.Pixel);
        static bool Semantics(IReadOnlyList<FalloutSkyProgramSemantic> actual, params (int Usage, int Index, uint Mask)[] expected) =>
            actual.Count == expected.Length && actual.Select(row => (row.Usage, row.Index, row.Mask)).ToHashSet().SetEquals(expected);
        static int Register(IReadOnlyList<FalloutSkyProgramSemantic> rows, int usage, int index) =>
            rows.Single(row => row.Usage == usage && row.Index == index).Register;
        if (!Semantics(vertex.Inputs, (0, 0, 15), (5, 0, 15), (10, 0, 15)) ||
            !Semantics(vertex.Outputs, (0, 0, 15), (5, 0, 3), (5, 1, 3), (10, 0, 15)) ||
            !Semantics(pixel.Inputs, (5, 0, 3), (5, 1, 3), (10, 0, 15)) || !vertex.Constants.SequenceEqual([0, 1, 3, 4, 5, 6]) ||
            !pixel.Constants.SequenceEqual([4]) || vertex.Samplers.Count != 0 || !pixel.Samplers.SequenceEqual([0, 1]) ||
            vertex.DefinedWord(2, 1) != BitConverter.SingleToUInt32Bits(1f) ||
            vertex.DefinedWord(2, 2) != 0 || vertex.DefinedWord(2, 3) != 0 ||
            pixel.DefinedWord(0, 0) != BitConverter.SingleToUInt32Bits(1f) ||
            Enumerable.Range(1, 3).Any(component => pixel.DefinedWord(0, component) != 0))
            throw new NotSupportedException("Celestial program requires another actual attribute/constant/sampler owner.");
        var depth = vertex.DefinedWord(2, 0);
        var depthValue = BitConverter.UInt32BitsToSingle(depth);
        if (!float.IsFinite(depthValue) || depthValue <= 0 || depthValue > 1)
            throw new InvalidDataException("Celestial program has no admitted far-depth literal.");
        var vertexCode = vertex.Emit("vertex", new Dictionary<int, string>
        {
            [Register(vertex.Inputs, 0, 0)] = "vec4(VERTEX, 1.0)",
            [Register(vertex.Inputs, 5, 0)] = "vec4(UV, 0.0, 1.0)",
            [Register(vertex.Inputs, 10, 0)] = "COLOR",
        }, new Dictionary<int, string>
        {
            [0] = "source_transform[0]",
            [1] = "source_transform[1]",
            [3] = "source_transform[3]",
            [4] = "vec4(source_Moon_colour, source_Moon_alpha)",
            [5] = "vec4(0.0)",
            [6] = "vec4(0.0)",
        }, new Dictionary<int, string>(), new Dictionary<int, string>
        {
            [Register(vertex.Outputs, 0, 0)] = "POSITION",
            [Register(vertex.Outputs, 5, 0)] = "source_uv_zero",
            [Register(vertex.Outputs, 5, 1)] = "source_uv_one",
            [Register(vertex.Outputs, 10, 0)] = "source_vertex_colour",
        }, "    mat4 source_transform = transpose(PROJECTION_MATRIX * MODELVIEW_MATRIX);",
        "    POSITION.z = POSITION.w - POSITION.z * (1.0 - CLIP_SPACE_FAR);");
        var pixelCode = pixel.Emit("fragment", new Dictionary<int, string>
        {
            [Register(pixel.Inputs, 5, 0)] = "source_uv_zero",
            [Register(pixel.Inputs, 5, 1)] = "source_uv_one",
            [Register(pixel.Inputs, 10, 0)] = "source_vertex_colour",
        }, new Dictionary<int, string> { [4] = "vec4(0.0, source_Moon_rgb_multiplier, 0.0, 0.0)" },
        new Dictionary<int, string> { [0] = "source_Moon_sampler", [1] = "source_Moon_sampler" },
        new Dictionary<int, string> { [0] = "source_fragment" }, "    vec4 source_fragment;",
        role == 6 ? "    if (!source_Moon_draw_admitted) discard; ALBEDO = source_fragment.rgb; ALPHA = source_fragment.a;" :
        // The original shadow pass has source ZERO and destination ONE. It
        // retains its real sampled/property output but leaves the framebuffer
        // unchanged. It is not a black disc or a fabricated texture outcome.
        "    if (!source_Moon_draw_admitted || source_fragment.a <= 0.0) discard; ALBEDO = source_fragment.rgb; ALPHA = 0.0;");
        var code = """
            shader_type spatial;
            render_mode unshaded, fog_disabled, depth_draw_never, cull_back, blend_mix;
            uniform sampler2D source_Moon_sampler : filter_linear_mipmap, repeat_disable;
            uniform vec3 source_Moon_colour = vec3(1.0);
            uniform float source_Moon_alpha = 1.0;
            uniform float source_Moon_rgb_multiplier;
            uniform bool source_Moon_draw_admitted = false;
            varying vec4 source_uv_zero;
            varying vec4 source_uv_one;
            varying vec4 source_vertex_colour;
            """ + "\n" + vertexCode + "\n" + pixelCode;
        return new(role, moon.EngineSha256, FalloutMoonSource.Hash(moon.Contract + ";" + Rules), path, identity,
            Convert.ToHexString(SHA256.HashData(bytes)), vertexName, Convert.ToHexString(SHA256.HashData(vertexBytes.Bytecode.Span)),
            pixelName, Convert.ToHexString(SHA256.HashData(pixelBytes.Bytecode.Span)), depth, code);
    }
}
