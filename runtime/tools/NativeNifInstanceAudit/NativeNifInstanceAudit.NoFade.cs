using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseShaderNoFade()
    {
        foreach (var decal in new uint[] { 0, 1U << 26, 1U << 27 })
        {
            var bytes = NoFadeFixture(9, decal);
            var original = bytes.ToArray();
            var prototype = new RuntimeNativeNifPrototype(bytes, .02f);
            var first = prototype.Instantiate();
            var second = prototype.Instantiate();
            try
            {
                var sourceGeometry = Check(prototype.Scene.Root, decal);
                var firstGeometry = Check(first, decal);
                var secondGeometry = Check(second, decal);
                foreach (var opacity in new[] { .35f, 0f, 1f })
                {
                    RuntimeNativeReferenceOpacity.Publish(firstGeometry, opacity);
                    if (firstGeometry.Any(mesh => !Mathf.IsEqualApprox(mesh.Transparency, 1 - opacity)) ||
                        sourceGeometry.Concat(secondGeometry).Any(mesh => mesh.Transparency != 0))
                        throw new InvalidDataException("Shader NoFade bypassed explicit fade or mutated another instance.");
                }
                RuntimeNativeReferenceOpacity.Publish(secondGeometry, .7f);
                if (secondGeometry.Any(mesh => !Mathf.IsEqualApprox(mesh.Transparency, .3f)) ||
                    firstGeometry.Concat(sourceGeometry).Any(mesh => mesh.Transparency != 0) ||
                    !bytes.AsSpan().SequenceEqual(original))
                    throw new InvalidDataException("Independent reference fade lost prototype or source isolation.");
            }
            finally { first.Free(); second.Free(); prototype.Scene.Root.Free(); }
        }
        try
        {
            RuntimeNativeNifMeshBuilder.Build(FalloutNifFile.Read(NoFadeFixture(9 | (1U << 16))), .02f).Root.Free();
            throw new InvalidDataException("NoFade admission accepted an unknown neighboring shader flag.");
        }
        catch (NotSupportedException error) when (error.Message.Contains("unsupported lighting semantics", StringComparison.Ordinal)) { }
        GD.Print("OPENNV_SHADER_NO_FADE_PASS flags2NineBuild=true materialAlphaTestDepth=true explicitFadeBoth=true cloneIsolation=true unknownFlagRefused=true ordinaryDecalDistanceFade=unverified pixels=unverified");

        static GeometryInstance3D[] Check(Node3D root, uint decal)
        {
            var geometry = RuntimeNativeReferenceOpacity.Collect(root);
            if (geometry.Length != 2) throw new InvalidDataException("BSFadeNode lost its two source geometry owners.");
            var blocks = new HashSet<int>();
            foreach (var mesh in geometry.Cast<MeshInstance3D>())
            {
                var block = mesh.GetMeta("opennv_nif_geometry_block").AsInt32();
                blocks.Add(block);
                if (mesh.Mesh.SurfaceGetMaterial(0) is not ShaderMaterial material || !mesh.Visible || mesh.Layers == 0 ||
                    material.GetMeta("opennv_nif_shader_flags2").AsUInt32() != (block == 1 ? 9U : 1U) ||
                    material.GetMeta("opennv_nif_shader_flags").AsUInt32() != (0x80000100U | decal) ||
                    !Mathf.IsEqualApprox(material.GetShaderParameter("base_factor").AsVector4().W, .65f) ||
                    material.GetMeta("opennv_nif_alpha_flags").AsUInt32() != 4845 ||
                    !material.Shader.Code.Contains("base.a > 0.47058824", StringComparison.Ordinal) ||
                    !material.Shader.Code.Contains("depth_draw_always", StringComparison.Ordinal) ||
                    material.Shader.Code.Contains("depth_test_disabled", StringComparison.Ordinal))
                    throw new InvalidDataException("NoFade geometry lost source identity, alpha GREATER120 or depth state.");
            }
            if (!blocks.SetEquals([1, 8])) throw new InvalidDataException("Prototype clone lost source geometry identities.");
            return geometry;
        }
    }

    private static byte[] NoFadeFixture(uint flags2, uint decal = 0)
    {
        var template = SurfaceFixture(0x80000100U | decal, flags2: flags2);
        var source = FalloutNifFile.Read(template);
        var blocks = source.Blocks.Select(block => (TypeName: block.TypeName,
            Bytes: template.AsSpan(block.Offset, block.Size).ToArray())).ToList();
        static void Net(BinaryWriter writer) { writer.Write(0); writer.Write(0); writer.Write(-1); }
        static void Av(BinaryWriter writer, params int[] properties)
        {
            Net(writer); writer.Write(14U);
            foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 }) writer.Write(value);
            writer.Write(properties.Length);
            foreach (var property in properties) writer.Write(property);
            writer.Write(-1);
        }
        byte[] Shape(int shader) => Bytes(writer =>
        {
            Av(writer, shader, 5, 6); writer.Write(2); writer.Write(-1); writer.Write(0); writer.Write(-1); writer.Write(false);
        });
        blocks[0] = ("BSFadeNode", Bytes(writer => { Av(writer); writer.Write(2); writer.Write(1); writer.Write(8); writer.Write(0); }));
        blocks[1] = ("NiTriShape", Shape(3));
        blocks[5] = ("NiAlphaProperty", Bytes(writer => { Net(writer); writer.Write((ushort)4845); writer.Write((byte)120); }));
        blocks.Add(("NiMaterialProperty", Bytes(writer =>
        {
            Net(writer);
            for (var component = 0; component < 7; component++) writer.Write(0f);
            writer.Write(.65f); writer.Write(1f);
        })));
        var ordinaryBytes = SurfaceFixture(0x80000100U | decal);
        var ordinary = FalloutNifFile.Read(ordinaryBytes).Blocks[3];
        blocks.Add(("BSShaderPPLightingProperty", ordinaryBytes.AsSpan(ordinary.Offset, ordinary.Size).ToArray()));
        blocks.Add(("NiTriShape", Shape(7)));
        return WriteParticleShapeColorNif(blocks, ["surface"]);
    }
}
