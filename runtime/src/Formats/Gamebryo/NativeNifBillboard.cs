using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

// The shader uses the camera of the current draw, including each submitted XR
// eye. Moving a world node toward a desktop camera would give both eyes one pose.
internal sealed partial class NativeNifBillboard : Node
{
    internal const string ShaderSource = """
        uniform int source_billboard_mode = -1;
        uniform mat4 source_billboard_local = mat4(1.0);
        mat4 owned_billboard(mat4 model, mat4 camera) {
            if (source_billboard_mode < 0) return model;
            mat4 root = model * inverse(source_billboard_local);
            vec3 normal = source_billboard_mode == 4 ? normalize(camera[3].xyz - root[3].xyz) : normalize(camera[2].xyz);
            vec3 up = source_billboard_mode == 1 ? normalize(-root[2].xyz) : normalize(camera[1].xyz);
            vec3 right = cross(up, normal);
            if (dot(right, right) < 0.000001) return model;
            right = normalize(right);
            if (source_billboard_mode == 1) normal = normalize(cross(right, up));
            else up = normalize(cross(normal, right));
            // NIF XY faces +Z; the shared (x,z,-y) mapping makes +Y
            // the normal and -Z the source up axis in model space.
            mat4 facing = mat4(vec4(right * length(root[0].xyz), 0.0),
                vec4(normal * length(root[1].xyz), 0.0),
                vec4(-up * length(root[2].xyz), 0.0), root[3]);
            return facing * source_billboard_local;
        }
        """;
    private static readonly StringName LocalParameter = "source_billboard_local";
    private static readonly StringName ModeParameter = "source_billboard_mode";
    private Node3D _root = null!;
    private readonly List<(MeshInstance3D Mesh, ShaderMaterial Material, Transform3D Last)> _meshes = [];

    internal void Configure(Node3D root, ushort mode)
    {
        if (mode is not (1 or 4)) throw new NotSupportedException($"NIF billboard mode {mode} is unbound.");
        _root = root; Name = "SourceBillboard"; ProcessPriority = 2;
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.GetActiveMaterial(surface) is not ShaderMaterial { ResourceName: NativeNifEffectMaterial.ResourceIdentity } material)
                    throw new NotSupportedException("Billboard surface has no source no-lighting shader owner.");
                var local = Local(mesh);
                material.SetShaderParameter(ModeParameter, (int)mode); material.SetShaderParameter(LocalParameter, local);
                mesh.ExtraCullMargin = Math.Max(mesh.ExtraCullMargin, mesh.GetAabb().Size.Length() * 2);
                _meshes.Add((mesh, material, local));
            }
        if (_meshes.Count == 0) throw new NotSupportedException("Billboard has no bound mesh surfaces.");
    }
    private Transform3D Local(Node3D node)
    {
        var value = Transform3D.Identity;
        while (node != _root)
        {
            value = node.Transform * value;
            node = node.GetParent() as Node3D ?? throw new InvalidOperationException("Billboard surface left its owner.");
        }
        return value;
    }
    public override void _Process(double delta)
    {
        for (var i = 0; i < _meshes.Count; i++)
        {
            var (mesh, material, last) = _meshes[i]; var local = Local(mesh);
            if (local == last) continue;
            material.SetShaderParameter(LocalParameter, local); _meshes[i] = (mesh, material, local);
        }
    }
}
