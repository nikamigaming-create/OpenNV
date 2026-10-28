using System.Text.Json;
using OpenNV.Runtime.Content;

internal static class MenuSourceAudit
{
    internal static int Run(RuntimeLiveContentSource content)
    {
        var paths = content.ResourcePathsUnder("menus")
            .Where(path => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var failures = new List<object>();
        var parsed = 0; var expanded = 0; var lexed = 0; var lexicalFailures = new List<object>();
        foreach (var path in paths)
        {
            try
            {
                if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException("Winning menu is absent.", path);
                _ = FalloutMenuTokenStream.Read(bytes); ++lexed;
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or IOException or System.Text.DecoderFallbackException)
            { lexicalFailures.Add(new { path, error = error.Message }); }
            System.Xml.Linq.XElement source;
            try { source = FalloutMenuXml.Read(content, path); ++parsed; }
            catch (Exception error) when (error is System.Xml.XmlException or InvalidDataException or NotSupportedException or IOException)
            { failures.Add(new { path, phase = "parse", error = error.Message }); continue; }
            try { _ = FalloutMenuXml.Expand(content, source); ++expanded; }
            catch (Exception error) when (error is System.Xml.XmlException or InvalidDataException or NotSupportedException or IOException)
            { failures.Add(new { path, phase = "expand", error = error.Message }); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { schema = "opennv-menu-source-audit/v1",
            denominator = paths.Length, lexed, lexicalFailures, parsed, expanded, failures,
            boundary = "Lexical and XML source parsing and prefab expansion only; token consumer, tile evaluation, input, gameplay and retail presentation are unverified." }));
        return failures.Count == 0 && lexicalFailures.Count == 0 ? 0 : 1;
    }
}
