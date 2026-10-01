using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutLoadingScreen(FalloutFormKey Identity, string TexturePath,
    string Description = "", FalloutFormKey? Type = null);

internal sealed record FalloutLoadingTip(uint X, uint Y, uint Width, uint Height,
    int Font, float Red, float Green, float Blue, uint Alignment);

internal static class FalloutLoadingScreenType
{
    internal static FalloutLoadingTip? Tip(FalloutPluginRecord record)
    {
        if (record.Signature != "LSCT") throw new InvalidDataException("Loading type is not LSCT.");
        var data = record.ReadSubrecords().Single(field => field.Signature == "DATA").Data;
        if (data.Length != 88) throw new NotSupportedException("Loading type DATA layout is unknown.");
        var bytes = data.Span;
        uint Integer(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.Span[offset..]);
        float Float(int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.Span[offset..]);
        if (Integer(0) == 0) return null;
        if (Integer(0) != 3) throw new NotSupportedException("Loading type requires an unbound XP, objective or statistics owner.");
        if (Float(20) != 0 || bytes[44..64].IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("Loading tip orientation or unknown DATA-1 fields are unbound.");
        var font = Integer(24); var alignment = Integer(40);
        var colors = new[] { Float(28), Float(32), Float(36) };
        if (font is < 1 or > 7 || alignment is not (1 or 2 or 4) || Integer(12) == 0 || Integer(16) == 0 ||
            colors.Any(value => !float.IsFinite(value) || value < 0 || value > 255))
            throw new InvalidDataException("Loading tip font, bounds, alignment or color is invalid.");
        return new(Integer(4), Integer(8), Integer(12), Integer(16), checked((int)font + 1),
            colors[0], colors[1], colors[2], alignment);
    }
}

internal static class FalloutLoadingScreenCatalog
{
    private sealed record Entry(FalloutLoadingScreen Screen, bool MainMenu, bool HasLocations,
        IReadOnlySet<FalloutFormKey> Cells, IReadOnlySet<FalloutFormKey> Worlds,
        IReadOnlySet<(FalloutFormKey World, int X, int Y)> Grids);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FalloutPluginStack, Entry[]> Catalogs = new();

    // LSCR record flag 10 selects the main-menu cycle. Resolve winners before
    // filtering, so a mod can replace, remove or add a menu loading screen.
    internal static IReadOnlyList<FalloutLoadingScreen> MainMenu(FalloutPluginStack stack) => stack
        .EffectiveRecords("LSCR")
        .Where(record => (record.Flags & 0x400) != 0)
        .Select(record => new FalloutLoadingScreen(record.FormKey, Texture(record)))
        .ToArray();

    internal static IReadOnlyList<FalloutLoadingScreen> InGame(FalloutPluginStack stack,
        FalloutCellDefinition cell, bool locationSpecificOnly) => Catalogs.GetValue(stack, Read)
        .Where(entry => !entry.MainMenu && (!entry.HasLocations ? !locationSpecificOnly :
            entry.Cells.Contains(cell.FormKey) || cell.Worldspace is { } world &&
            (entry.Worlds.Contains(world) || cell.Coordinates is { } grid && entry.Grids.Contains((world, grid.X, grid.Y)))))
        .Select(entry => entry.Screen).ToArray();

    private static Entry[] Read(FalloutPluginStack stack) => stack.EffectiveRecords("LSCR").Select(record =>
    {
        var cells = new HashSet<FalloutFormKey>();
        var worlds = new HashSet<FalloutFormKey>();
        var grids = new HashSet<(FalloutFormKey, int, int)>();
        var fields = record.ReadSubrecords().ToArray();
        var locations = fields.Where(field => field.Signature == "LNAM").ToArray();
        foreach (var location in locations)
        {
            var data = location.Data.Span;
            if (data.Length != 12) throw new NotSupportedException($"Loading screen {record.FormKey} has an unknown LNAM layout.");
            var direct = BinaryPrimitives.ReadUInt32LittleEndian(data);
            var indirect = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            if ((direct == 0) == (indirect == 0)) throw new NotSupportedException("Loading location must select one direct or grid identity.");
            var target = record.Plugin.AdjustFormId(direct != 0 ? direct : indirect);
            var kind = stack.GetEffective(target).Signature;
            if (direct != 0)
            {
                if (kind == "CELL") cells.Add(target);
                else if (kind == "WRLD") worlds.Add(target);
                else throw new InvalidDataException("Direct loading location is not CELL or WRLD.");
            }
            else
            {
                if (kind != "WRLD") throw new InvalidDataException("Grid loading location is not WRLD.");
                grids.Add((target, BinaryPrimitives.ReadInt16LittleEndian(data[10..]), BinaryPrimitives.ReadInt16LittleEndian(data[8..])));
            }
        }
        var descriptions = fields.Where(field => field.Signature == "DESC").ToArray();
        if (descriptions.Length > 1) throw new InvalidDataException("Loading screen has duplicate descriptions.");
        var types = fields.Where(field => field.Signature == "WMI1").ToArray();
        if (types.Length > 1 || types.Any(field => field.Data.Length != 4)) throw new InvalidDataException("Loading screen type identity is malformed.");
        var rawType = types.Length == 0 ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(types[0].Data.Span);
        FalloutFormKey? type = rawType == 0 ? null : record.Plugin.AdjustFormId(rawType);
        if (type is { } identity && stack.GetEffective(identity).Signature != "LSCT")
            throw new InvalidDataException("Loading screen type is not LSCT.");
        var text = descriptions.Length == 0 ? "" : FalloutDialogueTopic.Text(descriptions[0].Data.Span);
        return new Entry(new(record.FormKey, Texture(record), text, type), (record.Flags & 0x400) != 0,
            locations.Length != 0, cells, worlds, grids);
    }).ToArray();

    private static string Texture(FalloutPluginRecord record)
    {
        var icons = record.ReadSubrecords().Where(field => field.Signature == "ICON").ToArray();
        if (icons.Length != 1) throw new InvalidDataException($"Loading screen {record.FormKey} requires one ICON.");
        var path = FalloutDialogueTopic.Text(icons[0].Data.Span).Replace('/', '\\');
        if (path.Length == 0 || Path.IsPathRooted(path) || path.Split('\\').Any(part => part is ".." or "."))
            throw new InvalidDataException($"Loading screen {record.FormKey} has an invalid owned texture path.");
        return path.StartsWith("textures\\", StringComparison.OrdinalIgnoreCase) ? path : "textures\\" + path;
    }
}
