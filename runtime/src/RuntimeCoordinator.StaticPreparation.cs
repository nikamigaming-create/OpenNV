using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private static async Task<Dictionary<string, FalloutNifFile>> PrepareStaticModels(
        FalloutCellScene cell, RuntimeLiveContentSource content, IReadOnlySet<string> resident,
        float units, CancellationToken cancellation)
    {
        var paths = cell.References.Select(reference => cell.BaseObjects[reference.Base])
            .Where(model => model.Signature is not ("NPC_" or "CREA") && model.ModelPath is not null)
            .Select(model => model.ModelPath!).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !resident.Contains(path) && !path.EndsWith(".spt", StringComparison.OrdinalIgnoreCase));
        var prepared = await Task.WhenAll(paths.Select(path => FalloutContentWorkers.Run(() =>
        {
            try
            {
                if (!content.TryRead(path, null, out var bytes, out _)) return (Path: path, Nif: (FalloutNifFile?)null);
                var nif = FalloutNifFile.Read(bytes);
                RuntimeNativeNifMeshBuilder.PrepareStaticGeometry(nif, units, cancellation);
                foreach (var block in nif.Blocks.Where(block => block.TypeName == "BSShaderTextureSet"))
                    foreach (var texture in ((FalloutNifShaderTextureSet)nif.ReadObject(block.Index)).Textures.Where(texture => texture.Length != 0))
                        _ = content.TryRead(FalloutNifSurfaceInputs.TexturePath(texture), null, out _, out _);
                return (Path: path, Nif: (FalloutNifFile?)nif);
            }
            // Preparation never substitutes or admits a failed source. Its
            // reference owner publishes the ordinary, source-specific failure.
            catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException)
            { return (Path: path, Nif: (FalloutNifFile?)null); }
        }, cancellation)));
        return prepared.Where(item => item.Nif is not null)
            .ToDictionary(item => item.Path, item => item.Nif!, StringComparer.OrdinalIgnoreCase);
    }
}
