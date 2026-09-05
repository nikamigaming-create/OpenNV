using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

/// <summary>The renderer profile selected in the owned installation's configuration.</summary>
internal sealed record FalloutRendererConfiguration(int ShaderPackage)
{
    internal static FalloutRendererConfiguration Read(string text)
    {
        var rows = text.Split('\n').Select(line => line.Trim())
            .Where(line => line.StartsWith("Shader Package", StringComparison.Ordinal)).ToArray();
        if (rows.Length != 1)
            throw new InvalidDataException("Owned renderer configuration has no unique shader-package selection.");
        var match = Regex.Match(rows[0], @"^Shader Package\s*:\s*([0-9]+)$", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var package) || package <= 0)
            throw new InvalidDataException("Owned renderer shader-package selection is invalid.");
        return new(package);
    }
}
