using System.IO.Compression;
using System.Text.Json;

internal static partial class CellGraphAudit
{
    private static void WriteReport(string path, object report)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var compressed = path.EndsWith(".gz", StringComparison.Ordinal)
            ? new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true) : null;
        var output = (Stream?)compressed ?? stream;
        JsonSerializer.Serialize(output, report, Json);
        output.WriteByte((byte)'\n');
    }

    private static JsonDocument ReadReport(string path)
    {
        using var stream = File.OpenRead(path);
        using var compressed = path.EndsWith(".gz", StringComparison.Ordinal)
            ? new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true) : null;
        return JsonDocument.Parse((Stream?)compressed ?? stream);
    }
}
