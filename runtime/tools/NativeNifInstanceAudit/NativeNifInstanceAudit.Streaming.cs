using System.Diagnostics;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;

public partial class NativeNifInstanceAudit
{
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
