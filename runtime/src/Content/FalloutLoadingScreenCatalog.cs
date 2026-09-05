using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLoadingScreen(FalloutFormKey Identity, string TexturePath);

internal static class FalloutLoadingScreenCatalog
{
    // LSCR record flag 10 selects the main-menu cycle. Resolve winners before
    // filtering, so a mod can replace, remove or add a menu loading screen.
    internal static IReadOnlyList<FalloutLoadingScreen> MainMenu(FalloutPluginStack stack) => stack
        .EffectiveRecords("LSCR")
        .Where(record => (record.Flags & 0x400) != 0)
        .Select(record => new FalloutLoadingScreen(record.FormKey, Texture(record)))
        .ToArray();

    private static string Texture(FalloutPluginRecord record)
    {
        var icons = record.ReadSubrecords().Where(field => field.Signature == "ICON").ToArray();
        if (icons.Length != 1) throw new InvalidDataException($"Loading screen {record.FormKey} requires one ICON.");
        var path = Encoding.UTF8.GetString(icons[0].Data.Span).TrimEnd('\0').Replace('/', '\\');
        if (path.Length == 0 || Path.IsPathRooted(path) || path.Split('\\').Any(part => part is ".." or "."))
            throw new InvalidDataException($"Loading screen {record.FormKey} has an invalid owned texture path.");
        return path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) ? path : "textures\\" + path;
    }
}
