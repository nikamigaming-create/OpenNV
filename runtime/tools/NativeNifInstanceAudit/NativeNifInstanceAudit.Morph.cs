using System.Text;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static readonly float[][] MorphWeights = [[0, 0], [1, 0], [0, 1], [1, 1], [0.35f, 0.7f], [-0.5f, 1.5f]];

    private void ExerciseMorphBasis()
    {
        var skeleton = RuntimeNativeNifMeshBuilder.BuildActorSkeleton(MorphFixture(), 1);
        try
        {
            AddChild(skeleton.Node);
            var mesh = skeleton.Node.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            CheckMorphBasis(mesh, true);
            GD.Print("OPENNV_MORPH_BASIS_PASS weights=isolated-overlapping-signed geometry=source-additive basis=packed-runtime");
        }
        finally { skeleton.Node.Free(); }
    }

    private static void CheckMorphBasis(MeshInstance3D instance, bool synthetic = false)
    {
        var source = instance.Mesh.SurfaceGetArrays(0);
        var vertices = source[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = source[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var tangents = source[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
        var count = ((ArrayMesh)instance.Mesh).GetBlendShapeCount();
        foreach (var weights in MorphWeights)
        {
            for (var shape = 0; shape < count; shape++) instance.SetBlendShapeValue(shape, shape < 2 ? weights[shape] : 0);
            using var baked = instance.BakeMeshFromCurrentBlendShapeMix();
            var actual = baked.SurfaceGetArrays(0);
            var actualNormals = actual[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var actualTangents = actual[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
            for (var vertex = 0; vertex < normals.Length; vertex++)
                if (normals[vertex].DistanceTo(actualNormals[vertex]) > 0.0005f)
                    throw new InvalidOperationException($"Morph changed the packed lighting normal at vertex {vertex} with weights {string.Join(',', weights)}.");
            for (var component = 0; component < tangents.Length; component++)
                if (MathF.Abs(tangents[component] - actualTangents[component]) > 0.0005f)
                    throw new InvalidOperationException("Morph changed the packed tangent basis or handedness.");
            if (synthetic)
            {
                var actualVertices = actual[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                for (var vertex = 0; vertex < vertices.Length; vertex++)
                {
                    var expected = vertices[vertex] + new Vector3(0.3f * weights[0], 0.2f * weights[1], 0);
                    if (expected.DistanceTo(actualVertices[vertex]) > 0.0001f)
                        throw new InvalidOperationException("Overlapping morphs did not preserve additive source geometry.");
                }
            }
        }
        for (var shape = 0; shape < count; shape++) instance.SetBlendShapeValue(shape, 0);
    }

    private async Task ExerciseMorphPixels()
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Morph pixel audit requires the normal renderer.");
        var skeleton = RuntimeNativeNifMeshBuilder.BuildActorSkeleton(MorphFixture(), 1);
        var view = new SubViewport
        {
            Size = new Vector2I(128, 128),
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false
        };
        AddChild(view);
        try
        {
            view.AddChild(skeleton.Node);
            var mesh = skeleton.Node.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Single();
            var shader = new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, fog_disabled;
                uniform int lane = 0;
                void fragment() {
                    vec3 basis = lane == 0 ? NORMAL : (lane == 1 ? TANGENT : BINORMAL);
                    ALBEDO = basis * 0.5 + 0.5;
                }
                """ };
            var material = new ShaderMaterial { Shader = shader };
            mesh.MaterialOverride = material;
            var camera = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal,
                Size = 3,
                Position = new Vector3(0.65f, 0.65f, 4),
                Current = true
            };
            view.AddChild(camera);
            for (var lane = 0; lane < 3; lane++)
            {
                material.SetShaderParameter("lane", lane);
                Color? baseline = null;
                foreach (var weights in MorphWeights)
                {
                    mesh.SetBlendShapeValue(0, weights[0]); mesh.SetBlendShapeValue(1, weights[1]);
                    for (var frame = 0; frame < 3; frame++)
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var pixels = view.GetTexture().GetImage();
                    var actual = pixels.GetPixel(64, 64);
                    if (baseline is { } expected && new Vector3(actual.R - expected.R, actual.G - expected.G, actual.B - expected.B).Length() > 2f / 255)
                        throw new InvalidOperationException($"Morph lighting basis changed GPU pixels: lane={lane}, expected={expected}, actual={actual}.");
                    if (actual.R + actual.G + actual.B < 0.5f) throw new InvalidOperationException("Morph pixel sample missed the diagnostic surface.");
                    baseline ??= actual;
                }
            }
            GD.Print("OPENNV_MORPH_GPU_PASS renderer=forward-plus samples=18 basis=normal-tangent-binormal sourceWeights=additive retailPixels=unverified");
        }
        finally { view.Free(); }
    }

    private static byte[] MorphFixture()
    {
        Vector3[] vertices = [Vector3.Zero, new(2, 0, 0), new(0, 0, 2)];
        var normal = new Vector3(0.4f, 0.3f, MathF.Sqrt(0.75f));
        var tangent = new Vector3(0.6f, -0.8f, 0);
        static void Vector(BinaryWriter writer, Vector3 value) { writer.Write(value.X); writer.Write(value.Y); writer.Write(value.Z); }
        static void Object(BinaryWriter writer, int name, int controller)
        {
            writer.Write(name); writer.Write(0); writer.Write(controller); writer.Write(14U);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
            writer.Write(name == 1 ? 1 : 0); if (name == 1) writer.Write(5); writer.Write(-1);
        }
        var blocks = new (string Type, byte[] Bytes)[]
        {
            ("NiNode", Bytes(writer => { Object(writer, 0, -1); writer.Write(1); writer.Write(1); writer.Write(0); })),
            ("NiTriShape", Bytes(writer =>
            {
                Object(writer, 1, 2); writer.Write(4); writer.Write(-1);
                writer.Write(0); writer.Write(-1); writer.Write((byte)0);
            })),
            ("NiGeomMorpherController", Bytes(writer =>
            {
                writer.Write(-1); writer.Write((ushort)76); writer.Write(1f); writer.Write(0f);
                writer.Write(0f); writer.Write(1f); writer.Write(1); writer.Write((ushort)0); writer.Write(3); writer.Write((byte)0);
                writer.Write(3);
                for (var index = 0; index < 3; index++) { writer.Write(-1); writer.Write(0f); }
            })),
            ("NiMorphData", Bytes(writer =>
            {
                writer.Write(3); writer.Write(3); writer.Write((byte)1);
                for (var morph = 0; morph < 3; morph++)
                {
                    writer.Write(morph + 2);
                    foreach (var vertex in vertices) Vector(writer, morph == 0 ? vertex : morph == 1 ? new(0.3f, 0, 0) : new(0, 0, 0.2f));
                }
            })),
            ("NiTriShapeData", Bytes(writer =>
            {
                writer.Write(0); writer.Write((ushort)3); writer.Write((ushort)0); writer.Write((byte)1);
                foreach (var vertex in vertices) Vector(writer, vertex);
                writer.Write((byte)0); writer.Write((byte)0x10); writer.Write((byte)1);
                foreach (var direction in new[] { normal, tangent, normal.Cross(tangent) })
                    foreach (var vertex in vertices) Vector(writer, direction);
                Vector(writer, Vector3.Zero); writer.Write(3f);
                writer.Write((byte)1);
                for (var component = 0; component < 12; component++) writer.Write(1f);
                writer.Write((ushort)0); writer.Write(-1);
                writer.Write((ushort)1); writer.Write(3U); writer.Write((byte)1);
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write((ushort)0);
            })),
            ("NiMaterialProperty", Bytes(writer =>
            {
                writer.Write(-1); writer.Write(0); writer.Write(-1);
                for (var component = 0; component < 7; component++) writer.Write(0f);
                writer.Write(1f); writer.Write(1f);
            })),
        };
        string[] names = ["SyntheticRoot", "SyntheticFace", "Base", "ExpressionA", "ExpressionB"];
        return Bytes(writer =>
        {
            writer.Write("Gamebryo File Format, Version 20.2.0.7\n"u8);
            writer.Write(FalloutNifFile.Version); writer.Write((byte)1); writer.Write(FalloutNifFile.UserVersion);
            writer.Write(blocks.Length); writer.Write(34U); writer.Write(new byte[] { 1, 0, 1, 0, 1, 0 });
            writer.Write((ushort)blocks.Length);
            foreach (var block in blocks) { writer.Write(block.Type.Length); writer.Write(Encoding.ASCII.GetBytes(block.Type)); }
            for (var index = 0; index < blocks.Length; index++) writer.Write((ushort)index);
            foreach (var block in blocks) writer.Write(block.Bytes.Length);
            writer.Write(names.Length); writer.Write(names.Max(name => name.Length));
            foreach (var name in names) { writer.Write(name.Length); writer.Write(Encoding.ASCII.GetBytes(name)); }
            writer.Write(0U);
            foreach (var block in blocks) writer.Write(block.Bytes);
            writer.Write(1U); writer.Write(0);
        });
    }
}
