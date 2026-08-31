using System.Security.Cryptography;
using System.Text.Json;


namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed class Fo3TtwCg00Stage10SurfaceContract
{
    internal const string Schema =
        "opennv-ttw-fo3-cg00-stage10-retail-surface-depth/v1";
    internal const string Status =
        "exact-live-retail-surface-depth-distribution-derived";

    internal string Path { get; }
    internal string Sha256 { get; }
    internal string PresentationSha256 { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<Surface>> Participants { get; }

    private Fo3TtwCg00Stage10SurfaceContract(
        string path,
        string sha256,
        string presentationSha256,
        IReadOnlyDictionary<string, IReadOnlyList<Surface>> participants)
    {
        Path = path;
        Sha256 = sha256;
        PresentationSha256 = presentationSha256;
        Participants = participants;
    }

    internal static Fo3TtwCg00Stage10SurfaceContract Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != Schema ||
            root.GetProperty("status").GetString() != Status ||
            root.GetProperty("campaign").GetString() != "Fallout3" ||
            root.GetProperty("edition").GetString() != "TTW" ||
            root.GetProperty("stage").GetInt32() != 10)
            throw new InvalidOperationException("FO3 TTW stage-10 surface contract differs.");
        var roles = new Dictionary<string, IReadOnlyList<Surface>>(StringComparer.Ordinal);
        foreach (var role in new[] { "father", "doctor", "mother" })
        {
            var rows = new List<Surface>();
            foreach (var row in root.GetProperty("participants").GetProperty(role)
                         .GetProperty("surfaces").EnumerateArray())
            {
                if (row.GetProperty("appCulled").GetBoolean() ||
                    row.GetProperty("name").GetString() == "HeadAnims:0")
                    continue;
                rows.Add(new Surface(
                    row.GetProperty("name").GetString()!,
                    row.GetProperty("vertexCount").GetInt32(),
                    row.GetProperty("sourceVertexFnv1a32").GetUInt32(),
                    row.GetProperty("sortedDepthsGameUnits").EnumerateArray()
                        .Select(value => value.GetDouble()).ToArray()));
            }
            roles.Add(role, rows);
        }
        return new Fo3TtwCg00Stage10SurfaceContract(
            fullPath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            root.GetProperty("presentationContract").GetProperty("sha256").GetString()!,
            roles);
    }

    internal sealed record Surface(
        string Name,
        int VertexCount,
        uint SourceVertexFnv1a32,
        IReadOnlyList<double> SortedDepthsGameUnits);
}
