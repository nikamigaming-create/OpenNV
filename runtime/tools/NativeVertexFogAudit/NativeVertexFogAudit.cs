using Godot;
using OpenNV.Runtime.Diagnostics.Parity;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using Matrix = System.Numerics.Matrix4x4;
using Vec3 = System.Numerics.Vector3;
using Vec4 = System.Numerics.Vector4;

// Optional GPU audit: synthetic forward-depth projections are independent of
// the production shader's Godot reverse-depth conversion. No owned assets.
public partial class NativeVertexFogAudit : Node
{
    public override void _Ready()
    {
        try
        {
            using var device = RenderingServer.CreateLocalRenderingDevice()
                ?? throw new InvalidOperationException("Vertex fog audit requires a local GPU rendering device.");
            foreach (var clipFar in new[] { 0f, -1f }) Exercise(device, clipFar);
            ExerciseInstanceInventory();
            ExerciseCellEnvironment();
            GD.Print("OPENNV_VERTEX_FOG_GPU_PASS forwardDepth=true perspective=true orthographic=true units=true boundaries=true smoothFalloff=true pixels=unverified");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void ExerciseInstanceInventory()
    {
        using var shader = new Shader
        {
            Code = """
                shader_type spatial;
                instance uniform float audit_units = 1.0;
                instance uniform vec3 audit_tint = vec3(1.0);
                void fragment() { ALBEDO = audit_tint / audit_units; }
                """,
        };
        using var material = new ShaderMaterial { Shader = shader };
        using var geometry = new QuadMesh();
        var mesh = new MeshInstance3D { Mesh = geometry, MaterialOverride = material };
        try
        {
            AddChild(mesh);
            mesh.SetInstanceShaderParameter("audit_units", 64f);
            mesh.SetInstanceShaderParameter("audit_tint", new Vector3(0.25f, 0.5f, 0.75f));
            var values = RuntimeRenderTrace.InstanceShaderParameters(mesh);
            if (values.Count != 2 || !values.TryGetValue("audit_units", out var units) || units.AsSingle() != 64 ||
                !values.TryGetValue("audit_tint", out var tint) || tint.AsVector3() != new Vector3(0.25f, 0.5f, 0.75f))
                throw new InvalidOperationException("Render trace omitted or changed a declared instance parameter.");
            GD.Print("OPENNV_INSTANCE_PARAMETER_INVENTORY_PASS discovered=2 presetNames=false");
        }
        finally { mesh.Free(); }
    }

    private static void Exercise(RenderingDevice device, float clipFar)
    {
        var input = new List<float>(); var expected = new List<float>();
        foreach (var perspective in new[] { true, false })
            foreach (var units in new[] { 1f, 64f })
                foreach (var fov in new[] { MathF.PI / 3, MathF.PI / 2 })
                    foreach (var position in new[]
                    {
                        new Vec4(0, 0, -5, 1), new Vec4(3, 4, -100, 1),
                        new Vec4(160, 90, -200, 1), new Vec4(-30, 70, -800, 1),
                    })
                    {
                        var source = Projection(perspective, fov, 1);
                        var forward = Vec4.Transform(position, source);
                        var distance = new Vec3(forward.X, forward.Y, forward.Z).Length();
                        var near = perspective ? 10f : 0.01f; var far = perspective ? 500f : 1.5f;
                        expected.Add(MathF.Pow(Math.Clamp((distance - near) / (far - near), 0, 1), 0.6f));
                        var projection = Projection(perspective, fov, units);
                        var scale = 1 - clipFar;
                        projection.M13 = projection.M14 - scale * projection.M13;
                        projection.M23 = projection.M24 - scale * projection.M23;
                        projection.M33 = projection.M34 - scale * projection.M33;
                        projection.M43 = projection.M44 - scale * projection.M43;
                        input.AddRange([position.X / units, position.Y / units, position.Z / units, 1, near, far, 0.6f, units,
                            projection.M11, projection.M12, projection.M13, projection.M14,
                            projection.M21, projection.M22, projection.M23, projection.M24,
                            projection.M31, projection.M32, projection.M33, projection.M34,
                            projection.M41, projection.M42, projection.M43, projection.M44]);
                    }
        var rids = new List<Rid>();
        try
        {
            using var source = new RDShaderSource
            {
                Language = RenderingDevice.ShaderLanguage.Glsl,
                SourceCompute = $$"""
                    #version 450
                    layout(local_size_x = 1) in;
                    const float CLIP_SPACE_FAR = {{(clipFar == 0 ? "0.0" : "-1.0")}};
                    struct Sample { vec4 position; vec4 fog; mat4 projection; };
                    layout(set=0, binding=0, std430) readonly buffer Inputs { Sample samples[]; };
                    layout(set=0, binding=1, std430) writeonly buffer Outputs { float results[]; };
                    {{RetailVertexFog.ShaderSource}}
                    {{FalloutNifAngleFalloff.ShaderSource}}
                    void main() {
                        uint i = gl_GlobalInvocationID.x;
                        results[i * 2] = owned_vertex_fog(samples[i].position, samples[i].projection, samples[i].fog.xyz, samples[i].fog.w);
                        float cosine = float(i % 9) / 8.0 * (i % 2 == 0 ? 1.0 : -1.0);
                        results[i * 2 + 1] = owned_angle_opacity(cosine, vec4(1.0, 0.0, 1.0, 0.0));
                    }
                    """,
            };
            using var spirV = device.ShaderCompileSpirVFromSource(source);
            if (!string.IsNullOrEmpty(spirV.CompileErrorCompute)) throw new InvalidOperationException(spirV.CompileErrorCompute);
            var shader = device.ShaderCreateFromSpirV(spirV); rids.Add(shader);
            var pipeline = device.ComputePipelineCreate(shader); rids.Add(pipeline);
            var bytes = new byte[input.Count * sizeof(float)]; Buffer.BlockCopy(input.ToArray(), 0, bytes, 0, bytes.Length);
            var values = device.StorageBufferCreate((uint)bytes.Length, bytes); rids.Add(values);
            var output = device.StorageBufferCreate((uint)(expected.Count * 2 * sizeof(float))); rids.Add(output);
            using var inputUniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 0 };
            using var outputUniform = new RDUniform { UniformType = RenderingDevice.UniformType.StorageBuffer, Binding = 1 };
            inputUniform.AddId(values); outputUniform.AddId(output);
            var uniforms = device.UniformSetCreate([inputUniform, outputUniform], shader, 0); rids.Add(uniforms);
            var list = device.ComputeListBegin();
            device.ComputeListBindComputePipeline(list, pipeline);
            device.ComputeListBindUniformSet(list, uniforms, 0);
            device.ComputeListDispatch(list, (uint)expected.Count, 1, 1);
            device.ComputeListEnd(); device.Submit(); device.Sync();
            var result = device.BufferGetData(output);
            if (result.Length != expected.Count * 2 * sizeof(float)) throw new InvalidOperationException("Fog GPU output extent differs.");
            float[] opacity = [0, 0.04296875f, 0.15625f, 0.31640625f, 0.5f, 0.68359375f, 0.84375f, 0.95703125f, 1];
            for (var i = 0; i < expected.Count; i++)
            {
                var actual = BitConverter.ToSingle(result, i * 2 * sizeof(float));
                if (!float.IsFinite(actual) || MathF.Abs(actual - expected[i]) > 0.00002f)
                    throw new InvalidOperationException($"Fog projection fixture {clipFar}/{i}: expected {expected[i]:R}; actual {actual:R}.");
                if (BitConverter.ToSingle(result, (i * 2 + 1) * sizeof(float)) != opacity[i % opacity.Length])
                    throw new InvalidOperationException("GPU angle opacity differs from the smooth-curve fixture.");
            }
            GD.Print($"OPENNV_VERTEX_FOG_GPU_SAMPLES clipFar={clipFar} compared={expected.Count}");
        }
        finally { for (var i = rids.Count - 1; i >= 0; i--) if (rids[i].IsValid) device.FreeRid(rids[i]); }
    }

    private static Matrix Projection(bool perspective, float fov, float units) => perspective
        ? Matrix.CreatePerspectiveFieldOfView(fov, 16f / 9, 5 / units, 1000 / units)
        : Matrix.CreateOrthographic(200 / units, 100 / units, 5 / units, 1000 / units);
}
