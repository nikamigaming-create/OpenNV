namespace OpenNV.Runtime.Content;

internal sealed record FalloutIniStringDefault(FalloutIniDeclaration Declaration, string Value);

internal static partial class FalloutExecutableStringTable
{
    internal static IReadOnlyList<FalloutIniStringDefault> ReadIniStringDefaults(byte[] bytes, IReadOnlyList<string> names)
    {
        // Reuse the same declared collection/constructor owner as numeric INI
        // reads. A string-shaped literal alone cannot create a setting.
        var (_, image) = Load(bytes);
        var result = new List<FalloutIniStringDefault>();
        foreach (var declaration in ReadIniDeclarations(bytes).Where(row => row.Kind == 's' &&
            names.Contains(row.Name, StringComparer.OrdinalIgnoreCase)))
        {
            var value = image.Literal(declaration.Payload) ?? throw new NotSupportedException(
                "Original string INI declaration has no owned literal payload.");
            result.Add(new(declaration, value));
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
