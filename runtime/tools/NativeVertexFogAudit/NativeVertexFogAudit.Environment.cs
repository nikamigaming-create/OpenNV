using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeVertexFogAudit
{
    private void ExerciseCellEnvironment()
    {
        using var shader = new Shader
        {
            Code = """
                shader_type spatial;
                instance uniform vec3 source_ambient;
                instance uniform vec3 source_fog_color;
                instance uniform vec3 source_fog_range;
                instance uniform float source_fog_game_units_per_meter;
                void fragment() {
                    ALBEDO = source_ambient;
                    FOG = vec4(source_fog_color, source_fog_range.x / source_fog_game_units_per_meter);
                }
                """,
        };
        using var material = new ShaderMaterial { Shader = shader, ResourceName = NativeNifLightingMaterial.ResourceIdentity };
        using var geometry = new QuadMesh();
        MeshInstance3D Mesh() => new() { Mesh = geometry, MaterialOverride = material };
        var firstLighting = new FalloutCellLighting([24, 48, 96], [0, 0, 0], [12, 36, 72], 8, 72, 0, 0, 1, 0, 0.75f);
        var secondLighting = firstLighting with { AmbientRgb = [96, 48, 24], FogNear = 16, FogFar = 144 };
        Node3D Cell(FalloutCellLighting lighting, float units)
        {
            var cell = new Node3D();
            var binding = new RuntimeNativeCellLighting();
            binding.Configure(lighting, units);
            cell.AddChild(binding);
            return cell;
        }
        var first = Cell(firstLighting, 1f / 8);
        var second = Cell(secondLighting, 1f / 16);
        var unrelated = Mesh();
        try
        {
            var early = Mesh(); first.AddChild(early);
            var preview = new SubViewport { OwnWorld3D = true };
            var portrait = Mesh(); preview.AddChild(portrait); first.AddChild(preview);
            AddChild(first); AddChild(second); AddChild(unrelated);
            Check(early, firstLighting, 8);
            Unbound(portrait); Unbound(unrelated);

            var attachment = new Node3D();
            var pivot = new Node3D(); attachment.AddChild(pivot);
            var late = Mesh(); pivot.AddChild(late); first.AddChild(attachment);
            Check(late, firstLighting, 8);
            var latePortrait = Mesh(); preview.AddChild(latePortrait); Unbound(latePortrait);

            attachment.Reparent(second);
            Check(late, secondLighting, 16);
            Check(early, firstLighting, 8);

            RemoveChild(first);
            var detached = Mesh(); first.AddChild(detached); Unbound(detached);
            AddChild(first); Check(detached, firstLighting, 8);
            RemoveChild(first);
            var afterRemoval = Mesh(); second.AddChild(afterRemoval); Check(afterRemoval, secondLighting, 16);
            GD.Print("OPENNV_CELL_ENVIRONMENT_LIFECYCLE_PASS initial=true lateAttachment=true transfer=true reentry=true previewIsolation=true removal=true");
        }
        finally { first.Free(); second.Free(); unrelated.Free(); }

        static void Check(MeshInstance3D mesh, FalloutCellLighting source, float reciprocalUnits)
        {
            if (mesh.GetInstanceShaderParameter("source_ambient").AsVector3() !=
                    new Vector3(source.AmbientRgb[0] / 255f, source.AmbientRgb[1] / 255f, source.AmbientRgb[2] / 255f) ||
                mesh.GetInstanceShaderParameter("source_fog_color").AsVector3() !=
                    new Vector3(source.FogRgb[0] / 255f, source.FogRgb[1] / 255f, source.FogRgb[2] / 255f) ||
                mesh.GetInstanceShaderParameter("source_fog_range").AsVector3() != new Vector3(source.FogNear, source.FogFar, source.FogPower) ||
                mesh.GetInstanceShaderParameter("source_fog_game_units_per_meter").AsSingle() != reciprocalUnits)
                throw new InvalidOperationException("Geometry did not inherit its current cell environment before drawing.");
        }
        static void Unbound(MeshInstance3D mesh)
        {
            if (mesh.GetInstanceShaderParameter("source_fog_game_units_per_meter").AsSingle() != 0)
                throw new InvalidOperationException("Cell environment leaked into detached geometry or a separate preview.");
        }
    }
}
