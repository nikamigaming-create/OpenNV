using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseOwnedMaterialSequences(Node3D root)
    {
        var surfaces = root.FindChildren("*", "", true, false).OfType<MeshInstance3D>()
            .Where(mesh => mesh.Mesh is not null).SelectMany(mesh => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount())
                .Select(index => (Mesh: mesh, Material: mesh.Mesh.SurfaceGetMaterial(index))))
            .Where(surface => surface.Material is ShaderMaterial { ResourceName: NativeNifLightingMaterial.ResourceIdentity }).ToArray();
        static string Bits(float value) => Convert.ToHexString(BitConverter.GetBytes(value));
        foreach (var player in root.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>()
            .Where(player => player.SourceController >= 0))
            foreach (var sequence in player.SequenceNames)
            {
                player.RequestSourceSequence(sequence, 1);
                var range = player.SequenceRange(sequence);
                foreach (var time in new[] { range.StartTime, range.StartTime + (range.StopTime - range.StartTime) / 2, range.StopTime })
                {
                    player.SeekSourceTime(time);
                    foreach (var (mesh, material) in surfaces)
                    {
                        var shader = (ShaderMaterial)material;
                        var emissive = shader.GetShaderParameter("emissive_color").AsVector3();
                        var specular = shader.GetShaderParameter("source_specular").AsVector3();
                        GD.Print("OPENNV_NIF_SOURCE_MATERIAL_SAMPLE " + JsonSerializer.Serialize(new
                        {
                            geometry = mesh.GetMeta("opennv_nif_geometry_block").AsInt32(),
                            controller = player.SourceController,
                            sequence,
                            time,
                            alpha = Bits(shader.GetShaderParameter("base_factor").AsVector4().W),
                            emissive = new[] { Bits(emissive.X), Bits(emissive.Y), Bits(emissive.Z) },
                            specular = new[] { Bits(specular.X), Bits(specular.Y), Bits(specular.Z) },
                            multiple = Bits(shader.GetShaderParameter("emissive_multiple").AsSingle()),
                        }));
                    }
                }
            }
    }

    private static void ExerciseUploadCost(string dataRoot, string model)
    {
        RuntimeLiveContentSource.Configure(dataRoot, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        content.ArchiveWarmup.GetAwaiter().GetResult();
        if (!content.TryRead(model, null, out var payload, out var identity)) throw new FileNotFoundException(model);
        var nif = FalloutNifFile.Read(payload);
        // Match exterior preparation: original bytes are already in memory.
        foreach (var block in nif.Blocks.Where(block => block.TypeName == "BSShaderTextureSet"))
            foreach (var texture in ((FalloutNifShaderTextureSet)nif.ReadObject(block.Index)).Textures.Where(path => path.Length != 0))
                _ = content.TryRead(FalloutNifSurfaceInputs.TexturePath(texture), null, out _, out _);
        var textures = new List<object>();
        NativeDdsTexture.UploadObserver = (format, width, height, prepare, upload) =>
            textures.Add(new { format, width, height, prepare, upload });
        try
        {
            for (var pass = 0; pass < 3; pass++)
            {
                textures.Clear();
                var started = Stopwatch.GetTimestamp();
                var scene = RuntimeNativeNifMeshBuilder.Build(nif, .0142875f);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                GD.Print("OPENNV_NATIVE_UPLOAD_COST " + JsonSerializer.Serialize(new
                { identity, pass, elapsed, scene.Surfaces, scene.Vertices, textures }));
                scene.Root.Free();
            }
        }
        finally { NativeDdsTexture.UploadObserver = null; }
    }
}
