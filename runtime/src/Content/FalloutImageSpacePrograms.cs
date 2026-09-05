using System.Globalization;
using System.Text;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

/// <summary>Selected owned shader programs and executable filter declarations for image-space blur.</summary>
internal sealed record FalloutImageSpacePrograms(FalloutImageSpaceKernels Kernels,
    FalloutD3D9PixelProgram Prefilter, IReadOnlyList<FalloutD3D9PixelProgram> Blur,
    FalloutD3D9PixelProgram DoubleVision, FalloutDoubleVisionPhase DoubleVisionPhase, string SourceIdentity)
{
    internal static FalloutImageSpacePrograms Read(RuntimeLiveContentSource source)
    {
        var settings = FalloutInstallationSettings.Read(source);
        var path = $"shaders/shaderpackage{settings.Renderer.ShaderPackage:000}.sdp";
        if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
        var executable = Path.Combine(Path.GetDirectoryName(source.ContentRoot)!,
            source.Game == RuntimeLiveContentSource.FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe");
        return Decode(bytes, FalloutImageSpaceKernels.Read(executable), FalloutExecutableStringTable.ReadDoubleVisionPhase(executable), identity);
    }

    internal static FalloutImageSpacePrograms Decode(byte[] package, FalloutImageSpaceKernels kernels,
        FalloutDoubleVisionPhase phase, string identity)
    {
        var shaders = FalloutShaderPackage.Read(package);
        FalloutD3D9PixelProgram Program(string name, int[] constants, int[]? samplers = null)
        {
            var shader = shaders.Single(shader => shader.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var decoded = FalloutD3D9PixelProgram.Read(shader.Bytecode);
            if (!decoded.Constants.SequenceEqual(constants) || !decoded.Samplers.SequenceEqual(samplers ?? [0]))
                throw new NotSupportedException($"Image-space program {name} requires another parameter owner.");
            return decoded;
        }
        var blur = Enumerable.Range(1, kernels.Blur.Length).Select(radius =>
            Program($"ISBLUR{2 * radius + 1}.pso", Enumerable.Range(0, 2 * radius + 2).ToArray())).ToArray();
        return new(kernels, Program("ISHDRDOWN4.pso", [2, 3, 4, 5, 6]), blur,
            Program("ISDOUBLEVIS.pso", [0, 1], [0, 1]), phase, identity);
    }

    // The host provides source sampler/size, the requested radius and pass axis.
    // Kernel arithmetic and constants retain the selected shader and executable
    // declarations. No derivative shader asset is read at subsequent launches.
    internal string ComputeFunctions()
    {
        static string Number(float value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return text.Contains('.') || text.Contains('E') ? text : text + ".0";
        }
        static string Vector(System.Numerics.Vector4 value) =>
            $"vec4({Number(value.X)}, {Number(value.Y)}, {Number(value.Z)}, {Number(value.W)})";
        var result = new StringBuilder();
        result.AppendLine($"const vec4 owned_blur_kernels[{Kernels.Blur.Sum(row => row.Length)}] = vec4[](");
        result.AppendLine(string.Join(",\n", Kernels.Blur.SelectMany(row => row).Select(Vector)) + ");");
        result.AppendLine("""
            vec4 owned_blur_offset(int tap) {
                int upper = int(ceil(params.image_effects.x));
                int lower = max(upper - 1, 1);
                float fraction = params.image_effects.x - float(upper - 1);
                if (fraction == 0.0) fraction = 1.0;
                vec4 value = owned_blur_kernels[(upper - 1) * 15 + tap];
                float previous = owned_blur_kernels[(lower - 1) * 15 + tap].z;
                value.z = previous + (value.z - previous) * fraction;
                return value;
            }
            """);
        var prefilterConstants = new Dictionary<int, string>
        {
            [2] = "vec4(1.0 / params.image_effects.yz, params.image_effects.x, 0.0)",
        };
        for (var tap = 0; tap < Kernels.Prefilter.Length; tap++) prefilterConstants.Add(tap + 3, Vector(Kernels.Prefilter[tap]));
        var samplers = new Dictionary<int, string> { [0] = "source_zero" };
        result.AppendLine(Prefilter.ComputeFunction("owned_blur_prefilter", prefilterConstants, samplers));
        for (var index = 0; index < Blur.Count; index++)
        {
            var radius = index + 1;
            var constants = new Dictionary<int, string>
            {
                [0] = "vec4(params.image_effects.w / params.dimensions.x, (1.0 - params.image_effects.w) / params.dimensions.y, 0.0, 0.0)",
            };
            for (var tap = 0; tap < 2 * radius + 1; tap++) constants.Add(tap + 1, $"owned_blur_offset({7 - radius + tap})");
            result.AppendLine(Blur[index].ComputeFunction($"owned_blur_{radius}", constants, samplers));
        }
        result.AppendLine("vec4 owned_blur(vec2 uv) {");
        for (var radius = 1; radius <= Blur.Count; radius++)
            result.AppendLine($"    if (params.image_effects.x <= {radius}.0) return owned_blur_{radius}(uv);");
        result.AppendLine("    return vec4(0.0);\n}");
        // VNAM owns the two shifted scene samples. The separate peripheral
        // blur controller is neutral here; it has no IMAD VNAM parameter.
        result.AppendLine(DoubleVision.ComputeFunction("owned_double_vision", new Dictionary<int, string>
        {
            [0] = "vec4(0.0)",
            [1] = "params.double_vision",
        }, new Dictionary<int, string> { [0] = "source_zero", [1] = "source_one" }));
        return result.ToString();
    }
}
