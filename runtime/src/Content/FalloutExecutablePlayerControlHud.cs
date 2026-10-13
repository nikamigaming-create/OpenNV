using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutPlayerControlHudSource ReadPlayerControlHud(string path, FalloutPlayerMovementControlSource controls)
    {
        controls.Validate(); var bytes = File.ReadAllBytes(path);
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != controls.Main.EngineSha256)
            throw new InvalidDataException("Player control HUD changed its selected owned executable.");
        var (code, image) = Load(bytes);
        RequirePlayerControlFlagWriters(code, image);
        return FalloutPlayerControlHudSource.Reviewed(controls);
    }

    private static void RequirePlayerControlHud(byte[] code, Image image, int service)
    {
        if (!Match(code, service, [0x55, 0x8b, 0xec, 0x51, 0x8b, 0x0d]) ||
            !Match(code, service + 10, [0x85, 0xc9, 0x0f, 0x84]) ||
            !Match(code, service + 0x12, [0x83, 0x79, 0x24, 1, 0x56, 0x8b, 0x75, 8, 0x74, 5, 0xe8]))
            throw new NotSupportedException("Player mandatory input-type service lost its actual nullable HUD and independent Menu transition.");
        var hudPointer = Word(code, service + 6);
        if (Word(image.ReadLoaderData(hudPointer, 4), 0) != 0)
            throw new NotSupportedException("Player HUD global changed its independent loaded-null factory.");
        var tables = image.SourceClassTables(".?AVHUDMainMenu@@", 18).ToArray();
        if (tables.Length != 1)
            throw new NotSupportedException("Player controls lack their unique HUDMainMenu source class.");
        var constructorWrites = 0;
        for (var at = 0; at <= code.Length - 0x150; ++at)
        {
            if (!Match(code, at, [0xc7, 6]) || Word(code, at + 2) != tables[0]) continue;
            var body = code.AsSpan(at, Math.Min(0x200, code.Length - at));
            if (body.IndexOf(new byte[] { 0xc7, 0x86, 0x7c, 1, 0, 0, 0xff, 0xff, 0xff, 0xff }) < 0 ||
                body.IndexOf(new byte[] { 0xc7, 0x86, 0x94, 1, 0, 0, 4, 0, 0, 0 }) < 0 ||
                body.IndexOf(new byte[] { 0xc6, 0x86, 0x18, 2, 0, 0, 0 }) < 0)
                continue;
            ++constructorWrites;
        }
        if (constructorWrites != 1)
            throw new NotSupportedException("Source HUD has no unique returned field constructor.");
        var settings = ReadStaticSettings(image);
        RequireSetting(FalloutPlayerControlHudSource.CrosshairSetting, SettingCollection.Prefs, 1);
        foreach (var name in new[] { FalloutPlayerControlHudSource.SafeXSetting, FalloutPlayerControlHudSource.SafeYSetting,
                     FalloutPlayerControlHudSource.WideXSetting, FalloutPlayerControlHudSource.WideYSetting })
            RequireSetting(name, SettingCollection.Main, 15);
        var width = RequireSetting(FalloutPlayerControlHudSource.WidthSetting, SettingCollection.Renderer, FalloutPlayerControlHudSource.InitialWidth);
        var height = RequireSetting(FalloutPlayerControlHudSource.HeightSetting, SettingCollection.Renderer, FalloutPlayerControlHudSource.InitialHeight);
        var dimensionStores = 0;
        uint selectedWidthField = 0, selectedHeightField = 0;
        for (var at = 0; at <= code.Length - 26; ++at)
        {
            if (code[at] != 0xa1 || Word(code, at + 1) != width.Descriptor + 4 || code[at + 5] != 0xa3 ||
                code[at + 10] != 0xa1 || Word(code, at + 11) != height.Descriptor + 4 ||
                !Match(code, at + 15, [0x6a, 1, 0xa3]) || code[at + 22] != 0xe8) continue;
            var widthField = Word(code, at + 6); var heightField = Word(code, at + 18);
            if (Word(image.ReadLoaderData(widthField, 4), 0) != FalloutPlayerControlHudSource.InitialWidth ||
                Word(image.ReadLoaderData(heightField, 4), 0) != FalloutPlayerControlHudSource.InitialHeight)
                throw new NotSupportedException("Source UI dimensions lost constructor fields before declared settings.");
            ++dimensionStores;
            selectedWidthField = widthField; selectedHeightField = heightField;
        }
        if (dimensionStores != 1)
            throw new NotSupportedException("Source UI dimensions have no unique ordered Renderer-setting ingress.");
        var layouts = 0;
        for (var at = 0; at <= code.Length - 64; ++at)
        {
            if (!Match(code, at, [0x66, 0x0f, 0x6e, 0x0d]) || Word(code, at + 4) != selectedHeightField ||
                !Match(code, at + 8, [0x66, 0x0f, 0x6e, 5]) || Word(code, at + 12) != selectedWidthField ||
                !Match(code, at + 36, [0xf3, 0x0f, 0x5e, 0xc8, 0x0f, 0x2e, 0x0d]) ||
                !Match(code, at + 47, [0x9f, 0xf6, 0xc4, 0x44, 0x7a, 4, 0x32, 0xc0, 0xeb, 2, 0xb0, 1, 0xa2])) continue;
            var wide = Word(code, at + 60);
            if (Word(image.Read(Word(code, at + 43), 4), 0) != 0x3f400000 || image.ReadLoaderData(wide, 1)[0] != 0)
                throw new NotSupportedException("Source layout lost its independent loaded byte or exact Float32 ratio writer.");
            var getters = 0;
            for (var getter = 0; getter <= code.Length - 6; ++getter)
                if (code[getter] == 0xa0 && Word(code, getter + 1) == wide && code[getter + 5] == 0xc3) ++getters;
            if (getters != 1) throw new NotSupportedException("Source layout lacks its actual pure cached-wide getter.");
            ++layouts;
        }
        if (layouts != 1) throw new NotSupportedException("Source HUD layout has no unique current integer-dimension producer.");
        if (!Match(code, service + 0x325, [0x80, 0x3d]) ||
            image.ReadLoaderData(Word(code, service + 0x327), 1)[0] != 0)
            throw new NotSupportedException("Source HUD message suppression lost its independent loaded-zero field.");
        var remaining = new HashSet<string>(FalloutPlayerControlHudSource.VisibilityTiles.Append("template_justify_center_text"), StringComparer.Ordinal);
        for (var at = 0; at <= code.Length - 5 && remaining.Count != 0; ++at)
        {
            if (code[at] == 0x68 && image.Literal(Word(code, at + 1)) is { } name) remaining.Remove(name);
        }
        if (remaining.Count != 0) throw new NotSupportedException("Source HUD lost its actual tile/template associations: " + string.Join(",", remaining));
        StaticSetting RequireSetting(string name, SettingCollection collection, uint initial)
        {
            var rows = settings.Where(value => value.Name == name && value.Collection == collection).ToArray();
            if (rows is not [var setting] || setting.Payload != initial || !image.IsWritableExtent(setting.Descriptor + 4, 4))
                throw new NotSupportedException("Source HUD lost its actual typed declaration: " + name);
            return setting;
        }
    }

    private static void RequirePlayerWeaponDrawnFields(byte[] code, Image image, int middleConstructor, uint high, uint middle)
    {
        var getter = Word(image.Read(high + 0x448, 4), 0);
        if (Word(image.Read(middle + 0x448, 4), 0) != getter ||
            !Match(image.Read(getter, 7), 0, [0x8a, 0x81, 0x34, 1, 0, 0, 0xc3]) ||
            !Match(code, middleConstructor + 0x1ae, [0x66, 0xc7, 0x86, 0x34, 1, 0, 0, 0, 0]))
            throw new NotSupportedException("Current High/MiddleHigh weapon-drawn byte lost its independent constructor and pure virtual getter.");
    }
}
