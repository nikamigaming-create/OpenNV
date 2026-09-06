using Godot;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeVertexFogAudit
{
    private void ExerciseReferenceEmittance()
    {
        using var shader = new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode unshaded;
                {{NativeNifEmittanceMaterial.ShaderSource}}
                void fragment() { ALBEDO = owned_emissive_color(vec3(0.3, 0.6, 0.9), 2.0); }
                """,
        };
        using var shared = new ShaderMaterial { Shader = shader };
        using var excluded = new ShaderMaterial { Shader = shader };
        NativeNifEmittanceMaterial.Configure(shared, NativeNifEmittanceMaterial.ShaderFlag);
        NativeNifEmittanceMaterial.Configure(excluded, 0);
        using var geometry = new QuadMesh();
        MeshInstance3D Mesh(Material? material = null) => new() { Mesh = geometry, MaterialOverride = material ?? shared };
        float[] firstColor = [0.25f, 0.5f, 0.75f];
        var first = new Node3D(); var second = new Node3D();
        var firstBinding = new RuntimeNativeReferenceEmittance();
        var secondBinding = new RuntimeNativeReferenceEmittance();
        firstBinding.Configure(() => firstColor);
        secondBinding.Configure(() => [0.75f, 0.5f, 0.25f]);
        first.AddChild(firstBinding); second.AddChild(secondBinding);
        try
        {
            var initial = Mesh(); first.AddChild(initial);
            AddChild(first); AddChild(second);
            Check(initial, new(0.25f, 0.5f, 0.75f));
            var late = Mesh(); first.AddChild(late); Check(late, new(0.25f, 0.5f, 0.75f));
            var noFlag = Mesh(excluded); first.AddChild(noFlag); Check(noFlag, null);
            var preview = new SubViewport { OwnWorld3D = true };
            var portrait = Mesh(); preview.AddChild(portrait); first.AddChild(preview); Check(portrait, null);

            initial.Reparent(second); Check(initial, new(0.75f, 0.5f, 0.25f));
            firstColor = [0.125f, 0.25f, 0.5f]; firstBinding._Process(0);
            Check(late, new(0.125f, 0.25f, 0.5f)); Check(initial, new(0.75f, 0.5f, 0.25f));
            if (initial.MaterialOverride != shared || late.MaterialOverride != shared)
                throw new InvalidOperationException("Per-reference colour duplicated shared materials.");
            second.RemoveChild(initial); AddChild(initial); Check(initial, null); initial.Free();
            first.RemoveChild(firstBinding); Check(late, null);
            first.AddChild(firstBinding); Check(late, new(0.125f, 0.25f, 0.5f));
            GD.Print("OPENNV_MATERIAL_EMITTANCE_LIFECYCLE_PASS sourceFlag=true sharedMaterial=true lateAttachment=true transfer=true clockSample=true previewIsolation=true removal=true");
        }
        finally { first.Free(); second.Free(); }

        static void Check(MeshInstance3D mesh, Vector3? color)
        {
            if (mesh.GetInstanceShaderParameter("source_use_external_emittance").AsBool() != (color is not null) ||
                mesh.GetInstanceShaderParameter("source_external_emittance").AsVector3() != (color ?? Vector3.Zero))
                throw new InvalidOperationException("Geometry has a missing, shared or stale reference emittance binding.");
        }
    }
}
