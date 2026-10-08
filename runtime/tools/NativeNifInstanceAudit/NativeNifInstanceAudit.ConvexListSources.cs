using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

public partial class NativeNifInstanceAudit
{
    private static void AuditConvexListSources(string root, string[] options)
    {
        using var content = OpenConvexListContent(root, options);
        var paths = content.ResourcePathsUnder("meshes").Where(path => path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length == 0) throw new InvalidDataException("The selected source has no winning NIF models to inspect.");
        var failures = 0; var inspected = 0; var models = 0; var lists = 0;
        foreach (var path in paths)
        {
            string? identity = null;
            try
            {
                if (!content.TryRead(path, null, out var bytes, out identity)) throw new FileNotFoundException(path);
                var source = FalloutNifFile.Read(bytes);
                var declarations = source.Blocks.Where(block => block.TypeName == "bhkConvexListShape")
                    .Select(block => (FalloutNifConvexListShape)source.ReadObject(block.Index)).ToArray();
                ++inspected;
                if (declarations.Length == 0) continue;
                ++models; lists += declarations.Length;
                GD.Print("OPENNV_CONVEX_LIST_SOURCE " + JsonSerializer.Serialize(new
                {
                    path,
                    identity,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    stack = content.StackId,
                    lists = declarations.Select(shape => new { block = shape.Block.Index, children = shape.Children.Length }),
                    nativeModelAcceptance = false,
                }));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or IOException)
            {
                ++failures;
                GD.Print("OPENNV_CONVEX_LIST_SOURCE_REFUSAL " + JsonSerializer.Serialize(new
                {
                    path,
                    identity,
                    stack = content.StackId,
                    error = error.ToString(),
                }));
            }
        }
        GD.Print($"OPENNV_CONVEX_LIST_SOURCE_AUDIT_COMPLETE stack={content.StackId} selectedModels={paths.Length} inspected={inspected} " +
            $"convexModels={models} convexLists={lists} failures={failures} sourceOnly=true nativeModelAcceptance=false gameplayAcceptance=false");
        if (failures != 0) throw new InvalidDataException($"Convex-list source audit retains {failures} source refusals.");
    }
}
