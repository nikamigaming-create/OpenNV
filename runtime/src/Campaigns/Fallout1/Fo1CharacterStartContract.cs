using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1CharacterStartContractNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const int SourcePresentationInt10 = 10;
    internal const int SourcePresentationInt100 = 100;
    internal const double SourcePresentationDouble100Point0 = 100.0;
    internal const int SourcePresentationInt11 = 11;
    internal const int SourcePresentationInt12 = 12;
    internal const double SourcePresentationDouble130Point0 = 130.0;
    internal const int SourcePresentationInt14 = 14;
    internal const int SourcePresentationInt15 = 15;
    internal const int SourcePresentationInt16 = 16;
    internal const int SourcePresentationInt17 = 17;
    internal const int SourcePresentationInt18 = 18;
    internal const int SourcePresentationInt187 = 187;
    internal const int SourcePresentationInt188 = 188;
    internal const int SourcePresentationInt19 = 19;
    internal const int SourcePresentationInt1500 = 1_500;
    internal const int SourcePresentationInt2048 = 2048;
    internal const int SourcePresentationInt21 = 21;
    internal const int SourcePresentationInt212 = 212;
    internal const int SourcePresentationInt22 = 22;
    internal const int SourcePresentationInt24 = 24;
    internal const int SourcePresentationInt248 = 248;
    internal const int SourcePresentationInt255 = 255;
    internal const float SourcePresentationFloat255Point0f = 255.0f;
    internal const int SourcePresentationInt256 = 256;
    internal const int SourcePresentationInt32 = 32;
    internal const int SourcePresentationInt320 = 320;
    internal const int SourcePresentationInt336 = 336;
    internal const int SourcePresentationInt34 = 34;
    internal const int SourcePresentationInt360 = 360;
    internal const int SourcePresentationInt38 = 38;
    internal const int SourcePresentationInt41 = 41;
    internal const int SourcePresentationInt428 = 428;
    internal const int SourcePresentationInt432 = 432;
    internal const int SourcePresentationInt480 = 480;
    internal const int SourcePresentationInt5 = 5;
    internal const int SourcePresentationInt52 = 52;
    internal const int SourcePresentationInt57 = 57;
    internal const int SourcePresentationInt58 = 58;
    internal const int SourcePresentationInt6 = 6;
    internal const int SourcePresentationInt60 = 60;
    internal const int SourcePresentationInt64 = 64;
    internal const int SourcePresentationInt640 = 640;
    internal const int SourcePresentationInt67 = 67;
    internal const int SourcePresentationInt7 = 7;
    internal const int SourcePresentationInt80 = 80;
    internal const int SourcePresentationInt9 = 9;
    internal const int SourcePresentationInt99 = 99;
    internal const int SourcePresentationInt992 = 992;
}

internal sealed record Fo1OwnedUiTexture(
    string Id,
    string Path,
    string Sha256,
    int Width,
    int Height,
    string SourceFrmSha256)
{
    internal Image LoadImage()
    {
        var image = Image.LoadFromFile(Path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != Width || image.GetHeight() != Height)
            throw new InvalidOperationException(
                $"Prepared Fallout UI texture failed validation: {Id} {Width}x{Height}.");
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    internal Texture2D Load()
    {
        return ImageTexture.CreateFromImage(LoadImage());
    }

    internal object Report() => new
    {
        id = Id,
        path = Path,
        sha256 = Sha256,
        width = Width,
        height = Height,
        sourceFrmSha256 = SourceFrmSha256,
    };
}

internal readonly record struct Fo1HudPoint(int X, int Y)
{
    internal Vector2I Pixels => new(X, Y);
}

internal readonly record struct Fo1HudRect(int X, int Y, int Width, int Height)
{
    internal Rect2I Pixels => new(X, Y, Width, Height);

    internal bool Fits(int width, int height) =>
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 &&
        X + Width <= width && Y + Height <= height;
}

internal sealed record Fo1HudMessageLayout(
    Fo1HudRect Bounds,
    int MaximumLines,
    int LineIndent,
    int PrefixCodePoint);

internal sealed record Fo1HudNumberLayout(
    int DigitWidth,
    int SignWidth,
    int Height,
    int MinusX,
    int PlusX,
    int WhiteOffset,
    int YellowOffset,
    int RedOffset);

internal sealed record Fo1HudActionPointLayout(
    Fo1HudRect Bounds,
    int Slots,
    int Stride);

internal sealed record Fo1HudItemLayout(
    Fo1HudRect Bounds,
    Fo1HudPoint Single,
    Fo1HudPoint MovePoints,
    Fo1HudPoint MoveNumber,
    Fo1HudPoint Weapon,
    int WeaponSlotWidth,
    int WeaponSlotHeight,
    int MoveDigitWidth);

internal sealed record Fo1CreatorNumberLayout(
    IReadOnlyList<Fo1HudPoint> Special,
    IReadOnlyList<Fo1HudRect> SpecialIncrease,
    IReadOnlyList<Fo1HudRect> SpecialDecrease,
    IReadOnlyList<Fo1HudPoint> CharacterPoints);

internal sealed record Fo1CreatorNumberAssets(
    Fo1OwnedUiTexture Atlas,
    int DigitWidth,
    int SpecialDigitStride,
    int WhiteOffsetX,
    Fo1CreatorNumberLayout Layout)
{
    internal object Report() => new
    {
        source = "owned Fallout 1 ART/INTRFACE/BIGNUM.FRM",
        atlas = Atlas.Report(),
        digitWidth = DigitWidth,
        specialDigitStride = SpecialDigitStride,
        whiteOffsetX = WhiteOffsetX,
        layout = Layout,
    };
}

internal sealed record Fo1HudCombatLayout(
    Fo1HudRect Window,
    Fo1HudPoint EndTurn,
    Fo1HudPoint EndCombat);

internal sealed record Fo1HudButtonLayout(
    Fo1HudPoint SwapHands,
    Fo1HudPoint Inventory,
    Fo1HudPoint Options,
    Fo1HudPoint Skilldex,
    Fo1HudPoint Automap,
    Fo1HudPoint Character,
    Fo1HudRect PipBoy);

internal sealed record Fo1ClassicHudLayout(
    int Width,
    int Height,
    Fo1HudMessageLayout Message,
    Fo1HudPoint HitPoints,
    Fo1HudPoint ArmorClass,
    Fo1HudNumberLayout Numbers,
    Fo1HudActionPointLayout ActionPoints,
    Fo1HudItemLayout Item,
    Fo1HudCombatLayout Combat,
    Fo1HudButtonLayout Buttons);

internal sealed record Fo1OwnedBitmapFont(
    string AtlasPath,
    string AtlasSha256,
    string SourceAafSha256,
    int AtlasWidth,
    int AtlasHeight,
    int CellWidth,
    int MaximumHeight,
    int LetterSpacing,
    int WordSpacing,
    int LineSpacing,
    IReadOnlyList<int> GlyphWidths,
    IReadOnlyList<int> GlyphHeights,
    Color Tint,
    int ColorTableIndex)
{
    internal Image LoadImage()
    {
        var image = Image.LoadFromFile(AtlasPath);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != AtlasWidth || image.GetHeight() != AtlasHeight)
            throw new InvalidOperationException("Prepared Fallout AAFF atlas failed validation.");
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    internal object Report() => new
    {
        atlas = AtlasPath,
        atlasSha256 = AtlasSha256,
        sourceAafSha256 = SourceAafSha256,
        atlasWidth = AtlasWidth,
        atlasHeight = AtlasHeight,
        cellWidth = CellWidth,
        maximumHeight = MaximumHeight,
        letterSpacing = LetterSpacing,
        wordSpacing = WordSpacing,
        lineSpacing = LineSpacing,
        colorTableIndex = ColorTableIndex,
        tint = new[] { Tint.R, Tint.G, Tint.B },
    };
}

internal sealed record Fo1PremadeCharacter(
    string Id,
    string Role,
    Fo1CharacterProfile Profile,
    string Biography,
    string GcdPath,
    string GcdSha256,
    string BioPath,
    string BioSha256,
    Fo1OwnedUiTexture Portrait)
{
    internal object Report() => new
    {
        id = Id,
        role = Role,
        profile = Profile.Report(),
        biography = Biography,
        gcd = GcdPath,
        gcdSha256 = GcdSha256,
        bio = BioPath,
        bioSha256 = BioSha256,
        portrait = Portrait.Report(),
    };
}

internal sealed record Fo1PipBoyAssets(
    string Model,
    Fo1OwnedUiTexture Main,
    Fo1OwnedUiTexture SidePanel,
    Fo1OwnedUiTexture UpButton,
    Fo1OwnedUiTexture DownButton,
    Fo1OwnedUiTexture Screensaver,
    string MessagesPath,
    string MessagesSha256,
    IReadOnlyList<string> Pages)
{
    internal object Report() => new
    {
        model = Model,
        main = Main.Report(),
        sidePanel = SidePanel.Report(),
        upButton = UpButton.Report(),
        downButton = DownButton.Report(),
        screensaver = Screensaver.Report(),
        messages = MessagesPath,
        messagesSha256 = MessagesSha256,
        pages = Pages,
    };
}

internal sealed record Fo1ClassicInterfaceAssets(
    IReadOnlyDictionary<string, Fo1OwnedUiTexture> Textures,
    IReadOnlyDictionary<string, Fo1OwnedUiTexture> WeaponInventoryBySymbol,
    Fo1OwnedBitmapFont MessageFont,
    Fo1ClassicHudLayout Layout)
{
    internal Fo1OwnedUiTexture Main => Texture("main");
    internal Fo1OwnedUiTexture Numbers => Texture("numbers");
    internal Fo1OwnedUiTexture ActionPointGreen => Texture("actionPointGreen");
    internal Fo1OwnedUiTexture ActionPointYellow => Texture("actionPointYellow");
    internal Fo1OwnedUiTexture ActionPointRed => Texture("actionPointRed");
    internal Fo1OwnedUiTexture EndWindow => Texture("endWindow");
    internal Fo1OwnedUiTexture EndTurn => Texture("endTurn");
    internal Fo1OwnedUiTexture EndCombat => Texture("endCombat");
    internal Fo1OwnedUiTexture EndLightGreen => Texture("endLightGreen");
    internal Fo1OwnedUiTexture EndLightRed => Texture("endLightRed");
    internal Fo1OwnedUiTexture ItemPanel => Texture("itemPanel");
    internal Fo1OwnedUiTexture SingleAttack => Texture("singleAttack");
    internal Fo1OwnedUiTexture MovePoints => Texture("movePoints");
    internal Fo1OwnedUiTexture MoveNumbers => Texture("moveNumbers");
    internal Fo1OwnedUiTexture InventoryButton => Texture("inventoryButton");
    internal Fo1OwnedUiTexture OptionsButton => Texture("optionsButton");
    internal Fo1OwnedUiTexture RedButton => Texture("redButton");
    internal Fo1OwnedUiTexture AutomapButton => Texture("automapButton");
    internal Fo1OwnedUiTexture CharacterButton => Texture("characterButton");
    internal Fo1OwnedUiTexture PipBoyButton => Texture("pipBoyButton");

    internal object Report() => new
    {
        source = "owned Fallout 1 ART/INTRFACE FRMs and FONT1.AAF",
        compositor = "single 640x100 source-pixel buffer",
        textures = Textures.OrderBy(row => row.Key)
            .ToDictionary(row => row.Key, row => row.Value.Report()),
        weaponInventoryBySymbol = WeaponInventoryBySymbol.OrderBy(row => row.Key)
            .ToDictionary(row => row.Key, row => row.Value.Report()),
        messageFont = MessageFont.Report(),
        layout = Layout,
        pipBoyAccess = "P key or PIP control",
    };

    private Fo1OwnedUiTexture Texture(string id)
    {
        if (!Textures.TryGetValue(id, out var texture))
            throw new InvalidOperationException($"Prepared Fallout HUD texture is missing: {id}.");
        return texture;
    }

    internal Fo1OwnedUiTexture WeaponInventory(string symbol)
    {
        if (!WeaponInventoryBySymbol.TryGetValue(symbol, out var texture))
            throw new InvalidOperationException(
                $"Prepared Fallout HUD has no inventory art for equipped symbol: {symbol}.");
        return texture;
    }
}

internal sealed record Fo1CharacterStartContract(
    string ManifestPath,
    string ManifestSha256,
    string ChromePath,
    string ChromeSha256,
    Fo1CreatorNumberAssets CreatorNumbers,
    Fo1OwnedUiTexture CharacterPicker,
    IReadOnlyList<Fo1PremadeCharacter> PremadeCharacters,
    Fo1ClassicInterfaceAssets InterfaceHud,
    Fo1PipBoyAssets PipBoy,
    string OpeningFramesPath,
    string OpeningFramesSha256,
    string OpeningAudioPath,
    string OpeningAudioSha256,
    int OpeningWidth,
    int OpeningHeight,
    int OpeningFramesPerSecond,
    int OpeningFrameCount,
    double OpeningDurationSeconds,
    string SourceMveSha256,
    string SourceTextSha256,
    string SourceTimingSha256,
    int EntryTile,
    int EntryElevation,
    int EntryRotation,
    int TimingRows)
{
    private const string Schema = "opennv-fo1-character-start/v1";

    internal static Fo1CharacterStartContract Load(string path, string expectedSha256)
    {
        var manifestPath = VerifiedGltfLoader.ResolvePath(path);
        var bytes = File.ReadAllBytes(manifestPath);
        var manifestSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(manifestSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Fallout character-start manifest hash mismatch: {manifestSha256}");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != Schema ||
            root.GetProperty("status").GetString() != "prepared-owned-data")
            throw new InvalidOperationException("Unexpected Fallout character-start manifest.");

        var creator = root.GetProperty("creator");
        if (creator.GetProperty("width").GetInt32() != Fo1CharacterStartContractNumericContracts.SourcePresentationInt640 ||
            creator.GetProperty("height").GetInt32() != Fo1CharacterStartContractNumericContracts.SourcePresentationInt480)
            throw new InvalidOperationException(
                "Fallout character creator must use the original 640x480 chrome.");
        var chromePath = VerifiedGltfLoader.ResolvePath(creator.GetProperty("chromePng").GetString()!);
        var chromeSha256 = creator.GetProperty("chromePngSha256").GetString()!;
        VerifiedGltfLoader.VerifyHash(chromePath, chromeSha256);
        var creatorNumbersRow = creator.GetProperty("dynamicNumbers");
        var creatorNumberAtlas = ReadTexture(
            "creator-bignum",
            creatorNumbersRow,
            "atlasPng",
            "atlasPngSha256",
            "width",
            "height");
        var creatorNumberLayoutRow = creatorNumbersRow.GetProperty("layout");
        var creatorNumbers = new Fo1CreatorNumberAssets(
            creatorNumberAtlas,
            creatorNumbersRow.GetProperty("digitWidth").GetInt32(),
            creatorNumbersRow.GetProperty("specialDigitStride").GetInt32(),
            creatorNumbersRow.GetProperty("whiteOffsetX").GetInt32(),
            new Fo1CreatorNumberLayout(
                creatorNumberLayoutRow.GetProperty("special").EnumerateArray()
                    .Select(row => ReadPoint(row)).ToArray(),
                creatorNumberLayoutRow.GetProperty("specialIncrease").EnumerateArray()
                    .Select(ReadRect).ToArray(),
                creatorNumberLayoutRow.GetProperty("specialDecrease").EnumerateArray()
                    .Select(ReadRect).ToArray(),
                creatorNumberLayoutRow.GetProperty("characterPoints").EnumerateArray()
                    .Select(row => ReadPoint(row)).ToArray()));
        if (creatorNumbers.Atlas.Height != Fo1CharacterStartContractNumericContracts.SourcePresentationInt24 || creatorNumbers.Atlas.Width != Fo1CharacterStartContractNumericContracts.SourcePresentationInt336 ||
            creatorNumbers.DigitWidth != Fo1CharacterStartContractNumericContracts.SourcePresentationInt14 || creatorNumbers.SpecialDigitStride != Fo1CharacterStartContractNumericContracts.SourcePresentationInt18 ||
            creatorNumbers.WhiteOffsetX < 0 ||
            creatorNumbers.WhiteOffsetX + creatorNumbers.DigitWidth * Fo1CharacterStartContractNumericContracts.SourcePresentationInt10 >
                creatorNumbers.Atlas.Width || creatorNumbers.Layout.Special.Count != Fo1CharacterStartContractNumericContracts.SourcePresentationInt7 ||
            creatorNumbers.Layout.SpecialIncrease.Count != Fo1CharacterStartContractNumericContracts.SourcePresentationInt7 ||
            creatorNumbers.Layout.SpecialDecrease.Count != Fo1CharacterStartContractNumericContracts.SourcePresentationInt7 ||
            creatorNumbers.Layout.CharacterPoints.Count != 2 ||
            creatorNumbers.Layout.Special.Any(point =>
                !new Fo1HudRect(point.X, point.Y,
                    creatorNumbers.SpecialDigitStride + creatorNumbers.DigitWidth,
                    creatorNumbers.Atlas.Height).Fits(Fo1CharacterStartContractNumericContracts.SourcePresentationInt640, Fo1CharacterStartContractNumericContracts.SourcePresentationInt480)) ||
            creatorNumbers.Layout.CharacterPoints.Any(point =>
                !new Fo1HudRect(point.X, point.Y, creatorNumbers.DigitWidth,
                    creatorNumbers.Atlas.Height).Fits(Fo1CharacterStartContractNumericContracts.SourcePresentationInt640, Fo1CharacterStartContractNumericContracts.SourcePresentationInt480)) ||
            creatorNumbers.Layout.SpecialIncrease.Any(rect => !rect.Fits(Fo1CharacterStartContractNumericContracts.SourcePresentationInt640, Fo1CharacterStartContractNumericContracts.SourcePresentationInt480)) ||
            creatorNumbers.Layout.SpecialDecrease.Any(rect => !rect.Fits(Fo1CharacterStartContractNumericContracts.SourcePresentationInt640, Fo1CharacterStartContractNumericContracts.SourcePresentationInt480)))
            throw new InvalidOperationException(
                "Fallout creator BIGNUM source-layout contract drifted.");

        var picker = ReadTexture(
            "character-picker",
            root.GetProperty("characterPicker"),
            "chromePng",
            "chromePngSha256",
            "width",
            "height",
            root.GetProperty("source").GetProperty("characterPickerFrmSha256").GetString()!);
        if (picker.Width != Fo1CharacterStartContractNumericContracts.SourcePresentationInt640 || picker.Height != Fo1CharacterStartContractNumericContracts.SourcePresentationInt480)
            throw new InvalidOperationException("Fallout character picker must be the owned 640x480 screen.");

        var premades = new List<Fo1PremadeCharacter>();
        foreach (var row in root.GetProperty("characterPicker")
                     .GetProperty("premadeCharacters").EnumerateArray())
        {
            var profileRow = row.GetProperty("profile");
            var special = profileRow.GetProperty("allocatedSpecial")
                .EnumerateArray().Select(value => value.GetInt32()).ToArray();
            if (special.Length != Fo1CharacterStartContractNumericContracts.SourcePresentationInt7)
                throw new InvalidOperationException("Fallout premade SPECIAL coverage is invalid.");
            var profile = new Fo1CharacterProfile(
                profileRow.GetProperty("name").GetString()!,
                profileRow.GetProperty("age").GetInt32(),
                profileRow.GetProperty("sex").GetString()!,
                special[0],
                special[1],
                special[2],
                special[3],
                special[4],
                special[Fo1CharacterStartContractNumericContracts.SourcePresentationInt5],
                special[Fo1CharacterStartContractNumericContracts.SourcePresentationInt6],
                profileRow.GetProperty("taggedSkills").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray(),
                profileRow.GetProperty("traits").EnumerateArray()
                    .Select(value => value.GetString()!).ToArray());
            profile.Validate();

            var gcdPath = VerifiedGltfLoader.ResolvePath(row.GetProperty("gcd").GetString()!);
            var gcdSha256 = row.GetProperty("gcdSha256").GetString()!;
            VerifiedGltfLoader.VerifyHash(gcdPath, gcdSha256);
            if (new FileInfo(gcdPath).Length != Fo1CharacterStartContractNumericContracts.SourcePresentationInt428)
                throw new InvalidOperationException($"Fallout premade GCD size drifted: {gcdPath}");
            var bioPath = VerifiedGltfLoader.ResolvePath(row.GetProperty("bio").GetString()!);
            var bioSha256 = row.GetProperty("bioSha256").GetString()!;
            VerifiedGltfLoader.VerifyHash(bioPath, bioSha256);
            var biography = row.GetProperty("bioText").GetString()!;
            if (biography.Length < Fo1CharacterStartContractNumericContracts.SourcePresentationInt80)
                throw new InvalidOperationException("Fallout premade biography coverage is invalid.");
            var portrait = ReadTexture(
                $"premade-{row.GetProperty("id").GetString()}",
                row,
                "portraitPng",
                "portraitPngSha256",
                "portraitWidth",
                "portraitHeight",
                row.GetProperty("sourcePortraitFrmSha256").GetString()!);
            if (portrait.Width != Fo1CharacterStartContractNumericContracts.SourcePresentationInt212 || portrait.Height != Fo1CharacterStartContractNumericContracts.SourcePresentationInt187)
                throw new InvalidOperationException("Fallout premade portrait dimensions drifted.");
            premades.Add(new Fo1PremadeCharacter(
                row.GetProperty("id").GetString()!,
                row.GetProperty("role").GetString()!,
                profile,
                biography,
                gcdPath,
                gcdSha256,
                bioPath,
                bioSha256,
                portrait));
        }
        var expectedPremades = new[] { "Max Stone", "Natalia", "Albert" };
        if (!premades.Select(row => row.Profile.Name).SequenceEqual(expectedPremades))
            throw new InvalidOperationException(
                "Fallout picker must expose Max Stone, Natalia, Albert, and the Custom route.");

        var interfaceRow = root.GetProperty("interfaceHud");
        var interfaceAssets = interfaceRow.GetProperty("assets");
        var expectedInterfaceDimensions = new Dictionary<string, (int Width, int Height)>
        {
            ["main"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt640, Fo1CharacterStartContractNumericContracts.SourcePresentationInt100),
            ["numbers"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt360, Fo1CharacterStartContractNumericContracts.SourcePresentationInt17),
            ["actionPointGreen"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt5, Fo1CharacterStartContractNumericContracts.SourcePresentationInt5),
            ["actionPointYellow"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt5, Fo1CharacterStartContractNumericContracts.SourcePresentationInt5),
            ["actionPointRed"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt5, Fo1CharacterStartContractNumericContracts.SourcePresentationInt5),
            ["endWindow"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt57, Fo1CharacterStartContractNumericContracts.SourcePresentationInt58),
            ["endTurn"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt38, Fo1CharacterStartContractNumericContracts.SourcePresentationInt22),
            ["endCombat"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt38, Fo1CharacterStartContractNumericContracts.SourcePresentationInt22),
            ["endLightGreen"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt57, Fo1CharacterStartContractNumericContracts.SourcePresentationInt58),
            ["endLightRed"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt57, Fo1CharacterStartContractNumericContracts.SourcePresentationInt58),
            ["itemPanel"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt188, Fo1CharacterStartContractNumericContracts.SourcePresentationInt67),
            ["singleAttack"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt52, Fo1CharacterStartContractNumericContracts.SourcePresentationInt11),
            ["movePoints"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt17, Fo1CharacterStartContractNumericContracts.SourcePresentationInt12),
            ["moveNumbers"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt99, Fo1CharacterStartContractNumericContracts.SourcePresentationInt12),
            ["inventoryButton"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt32, Fo1CharacterStartContractNumericContracts.SourcePresentationInt21),
            ["optionsButton"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt34, Fo1CharacterStartContractNumericContracts.SourcePresentationInt34),
            ["redButton"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt22, Fo1CharacterStartContractNumericContracts.SourcePresentationInt21),
            ["automapButton"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt41, Fo1CharacterStartContractNumericContracts.SourcePresentationInt19),
            ["characterButton"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt41, Fo1CharacterStartContractNumericContracts.SourcePresentationInt19),
            ["pipBoyButton"] = (Fo1CharacterStartContractNumericContracts.SourcePresentationInt41, Fo1CharacterStartContractNumericContracts.SourcePresentationInt19),
        };
        if (interfaceAssets.EnumerateObject().Count() != expectedInterfaceDimensions.Count)
            throw new InvalidOperationException("Fallout gameplay HUD asset coverage drifted.");
        var interfaceTextures = new Dictionary<string, Fo1OwnedUiTexture>();
        foreach (var (id, dimensions) in expectedInterfaceDimensions)
        {
            var texture = ReadTexture($"interface-{id}", interfaceAssets.GetProperty(id));
            if (texture.Width != dimensions.Width || texture.Height != dimensions.Height)
                throw new InvalidOperationException(
                    $"Fallout gameplay HUD texture dimensions drifted: {id}.");
            interfaceTextures.Add(id, texture);
        }
        var weaponInventoryRows = interfaceRow.GetProperty("weaponInventoryBySymbol");
        var weaponInventoryTextures = new Dictionary<string, Fo1OwnedUiTexture>(
            StringComparer.Ordinal);
        foreach (var row in weaponInventoryRows.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(row.Name) ||
                !weaponInventoryTextures.TryAdd(
                    row.Name,
                    ReadTexture($"interface-weapon-{row.Name}", row.Value)))
                throw new InvalidOperationException(
                    "Fallout gameplay HUD weapon-art symbols drifted.");
        }
        if (weaponInventoryTextures.Count < 2 ||
            !weaponInventoryTextures.ContainsKey("PID_10MM_PISTOL") ||
            !weaponInventoryTextures.ContainsKey("PID_KNIFE"))
            throw new InvalidOperationException(
                "Fallout gameplay HUD must contain owned starting weapon art.");
        var interfaceFont = ReadBitmapFont(interfaceRow.GetProperty("messageFont"));
        var interfaceLayout = ReadHudLayout(interfaceRow.GetProperty("layout"));
        ValidateHudLayout(
            interfaceLayout,
            interfaceTextures,
            weaponInventoryTextures,
            interfaceFont);
        var interfaceHud = new Fo1ClassicInterfaceAssets(
            interfaceTextures,
            weaponInventoryTextures,
            interfaceFont,
            interfaceLayout);

        var pipBoyRow = root.GetProperty("pipBoy");
        if (pipBoyRow.GetProperty("model").GetString() != "Pip-Boy 2000")
            throw new InvalidOperationException("Fallout UI contract is not the Pip-Boy 2000.");
        var assets = pipBoyRow.GetProperty("assets");
        var pipBoy = new Fo1PipBoyAssets(
            "Pip-Boy 2000",
            ReadTexture("pip-main", assets.GetProperty("main")),
            ReadTexture("pip-side-panel", assets.GetProperty("sidePanel")),
            ReadTexture("pip-up", assets.GetProperty("upButton")),
            ReadTexture("pip-down", assets.GetProperty("downButton")),
            ReadTexture("pip-screensaver", assets.GetProperty("screensaver")),
            VerifyFile(pipBoyRow, "messages", "messagesSha256"),
            pipBoyRow.GetProperty("messagesSha256").GetString()!,
            pipBoyRow.GetProperty("pages").EnumerateArray()
                .Select(value => value.GetString()!).ToArray());
        if (!pipBoy.Pages.SequenceEqual(new[] { "STATUS", "AUTOMAPS", "ARCHIVES" }))
            throw new InvalidOperationException("Fallout Pip-Boy 2000 page coverage drifted.");

        var opening = root.GetProperty("opening");
        var openingFramesPath = VerifyFile(opening, "playbackFrames", "playbackFramesSha256");
        var openingFramesSha256 = opening.GetProperty("playbackFramesSha256").GetString()!;
        var openingAudioPath = VerifyFile(opening, "playbackAudio", "playbackAudioSha256");
        var openingAudioSha256 = opening.GetProperty("playbackAudioSha256").GetString()!;
        var openingWidth = opening.GetProperty("width").GetInt32();
        var openingHeight = opening.GetProperty("height").GetInt32();
        var openingFramesPerSecond = opening.GetProperty("framesPerSecond").GetInt32();
        var openingFrameCount = opening.GetProperty("frameCount").GetInt32();
        var openingDurationSeconds = opening.GetProperty("playbackDurationSeconds").GetDouble();
        if (openingWidth != Fo1CharacterStartContractNumericContracts.SourcePresentationInt432 || openingHeight != Fo1CharacterStartContractNumericContracts.SourcePresentationInt320 || openingFramesPerSecond != Fo1CharacterStartContractNumericContracts.SourcePresentationInt15 ||
            openingFrameCount < Fo1CharacterStartContractNumericContracts.SourcePresentationInt1500 || openingDurationSeconds is < Fo1CharacterStartContractNumericContracts.SourcePresentationDouble100Point0 or > Fo1CharacterStartContractNumericContracts.SourcePresentationDouble130Point0)
            throw new InvalidOperationException("Fallout Overseer deterministic playback contract is invalid.");
        var rows = opening.GetProperty("timingRows");
        if (rows.GetArrayLength() < Fo1CharacterStartContractNumericContracts.SourcePresentationInt10 ||
            rows.EnumerateArray().Any(row => row.GetProperty("seconds").GetDouble() < 0.0))
            throw new InvalidOperationException("Fallout Overseer timing contract is invalid.");

        var handoff = root.GetProperty("handoff");
        if (handoff.GetProperty("map").GetString() != "V13ENT")
            throw new InvalidOperationException("Fallout opening must hand off to V13ENT.");
        return new Fo1CharacterStartContract(
            manifestPath,
            manifestSha256,
            chromePath,
            chromeSha256,
            creatorNumbers,
            picker,
            premades,
            interfaceHud,
            pipBoy,
            openingFramesPath,
            openingFramesSha256,
            openingAudioPath,
            openingAudioSha256,
            openingWidth,
            openingHeight,
            openingFramesPerSecond,
            openingFrameCount,
            openingDurationSeconds,
            opening.GetProperty("sourceMveSha256").GetString()!,
            opening.GetProperty("transcriptSha256").GetString()!,
            opening.GetProperty("timingSha256").GetString()!,
            handoff.GetProperty("tile").GetInt32(),
            handoff.GetProperty("elevation").GetInt32(),
            handoff.GetProperty("rotation").GetInt32(),
            rows.GetArrayLength());
    }

    internal Texture2D LoadChrome()
    {
        var image = Image.LoadFromFile(ChromePath);
        if (image is null || image.IsEmpty() || image.GetWidth() != Fo1CharacterStartContractNumericContracts.SourcePresentationInt640 || image.GetHeight() != Fo1CharacterStartContractNumericContracts.SourcePresentationInt480)
            throw new InvalidOperationException("Prepared Fallout creator chrome failed image validation.");
        return ImageTexture.CreateFromImage(image);
    }

    internal object Report() => new
    {
        schema = Schema,
        manifest = ManifestPath,
        manifestSha256 = ManifestSha256,
        creatorChromeSha256 = ChromeSha256,
        creatorNumbers = CreatorNumbers.Report(),
        characterPicker = CharacterPicker.Report(),
        premadeCharacters = PremadeCharacters.Select(row => row.Report()).ToArray(),
        customCharacter = true,
        interfaceHud = InterfaceHud.Report(),
        pipBoy = PipBoy.Report(),
        originalOverseerMveSha256 = SourceMveSha256,
        originalOverseerTextSha256 = SourceTextSha256,
        originalOverseerTimingSha256 = SourceTimingSha256,
        playbackFramesSha256 = OpeningFramesSha256,
        playbackAudioSha256 = OpeningAudioSha256,
        playback = new
        {
            width = OpeningWidth,
            height = OpeningHeight,
            framesPerSecond = OpeningFramesPerSecond,
            frameCount = OpeningFrameCount,
            durationSeconds = OpeningDurationSeconds,
        },
        timingRows = TimingRows,
        handoff = new
        {
            map = "V13ENT",
            tile = EntryTile,
            elevation = EntryElevation,
            rotation = EntryRotation,
        },
    };

    private static Fo1OwnedBitmapFont ReadBitmapFont(JsonElement row)
    {
        var atlasPath = VerifyFile(row, "atlasPng", "atlasPngSha256");
        var widths = row.GetProperty("glyphWidths").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var heights = row.GetProperty("glyphHeights").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var tint = row.GetProperty("tintRgb").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var atlasWidth = row.GetProperty("atlasWidth").GetInt32();
        var atlasHeight = row.GetProperty("atlasHeight").GetInt32();
        var cellWidth = row.GetProperty("cellWidth").GetInt32();
        var maximumHeight = row.GetProperty("maximumHeight").GetInt32();
        var sourceSha256 = row.GetProperty("sourceAafSha256").GetString()!;
        if (widths.Length != Fo1CharacterStartContractNumericContracts.SourcePresentationInt256 || heights.Length != Fo1CharacterStartContractNumericContracts.SourcePresentationInt256 || tint.Length != 3 ||
            widths.Any(value => value < 0 || value > cellWidth) ||
            heights.Any(value => value < 0 || value > maximumHeight) ||
            cellWidth <= 0 || maximumHeight != Fo1CharacterStartContractNumericContracts.SourcePresentationInt9 || atlasWidth != cellWidth * Fo1CharacterStartContractNumericContracts.SourcePresentationInt16 ||
            atlasHeight != maximumHeight * Fo1CharacterStartContractNumericContracts.SourcePresentationInt16 ||
            row.GetProperty("letterSpacing").GetInt32() != 1 ||
            row.GetProperty("wordSpacing").GetInt32() != 4 ||
            row.GetProperty("lineSpacing").GetInt32() != 1 ||
            row.GetProperty("colorTableIndex").GetInt32() != Fo1CharacterStartContractNumericContracts.SourcePresentationInt992 ||
            !tint.SequenceEqual(new[] { Fo1CharacterStartContractNumericContracts.SourcePresentationInt60, Fo1CharacterStartContractNumericContracts.SourcePresentationInt248, 0 }) ||
            sourceSha256.Length != Fo1CharacterStartContractNumericContracts.SourcePresentationInt64)
            throw new InvalidOperationException("Fallout FONT1.AAF atlas contract drifted.");
        return new Fo1OwnedBitmapFont(
            atlasPath,
            row.GetProperty("atlasPngSha256").GetString()!,
            sourceSha256,
            atlasWidth,
            atlasHeight,
            cellWidth,
            maximumHeight,
            1,
            4,
            1,
            widths,
            heights,
            new Color(tint[0] / Fo1CharacterStartContractNumericContracts.SourcePresentationFloat255Point0f, tint[1] / Fo1CharacterStartContractNumericContracts.SourcePresentationFloat255Point0f, tint[2] / Fo1CharacterStartContractNumericContracts.SourcePresentationFloat255Point0f),
            Fo1CharacterStartContractNumericContracts.SourcePresentationInt992);
    }

    private static Fo1ClassicHudLayout ReadHudLayout(JsonElement row)
    {
        var canvas = row.GetProperty("canvas");
        var message = row.GetProperty("message");
        var numbers = row.GetProperty("numbers");
        var actionPoints = row.GetProperty("actionPoints");
        var item = row.GetProperty("itemPanel");
        var combat = row.GetProperty("combat");
        var buttons = row.GetProperty("buttons");
        return new Fo1ClassicHudLayout(
            canvas.GetProperty("width").GetInt32(),
            canvas.GetProperty("height").GetInt32(),
            new Fo1HudMessageLayout(
                ReadRect(message),
                message.GetProperty("maximumLines").GetInt32(),
                message.GetProperty("lineIndent").GetInt32(),
                message.GetProperty("prefixCodePoint").GetInt32()),
            ReadPoint(row.GetProperty("hitPoints")),
            ReadPoint(row.GetProperty("armorClass")),
            new Fo1HudNumberLayout(
                numbers.GetProperty("digitWidth").GetInt32(),
                numbers.GetProperty("signWidth").GetInt32(),
                numbers.GetProperty("height").GetInt32(),
                numbers.GetProperty("minusX").GetInt32(),
                numbers.GetProperty("plusX").GetInt32(),
                numbers.GetProperty("whiteOffset").GetInt32(),
                numbers.GetProperty("yellowOffset").GetInt32(),
                numbers.GetProperty("redOffset").GetInt32()),
            new Fo1HudActionPointLayout(
                ReadRect(actionPoints),
                actionPoints.GetProperty("slots").GetInt32(),
                actionPoints.GetProperty("stride").GetInt32()),
            new Fo1HudItemLayout(
                ReadRect(item),
                ReadPoint(item, "singleX", "singleY"),
                ReadPoint(item, "movePointsX", "movePointsY"),
                ReadPoint(item, "moveNumberX", "moveNumberY"),
                ReadPoint(item, "weaponX", "weaponY"),
                item.GetProperty("weaponSlotWidth").GetInt32(),
                item.GetProperty("weaponSlotHeight").GetInt32(),
                item.GetProperty("moveDigitWidth").GetInt32()),
            new Fo1HudCombatLayout(
                ReadRect(combat.GetProperty("window")),
                ReadPoint(combat.GetProperty("endTurn")),
                ReadPoint(combat.GetProperty("endCombat"))),
            new Fo1HudButtonLayout(
                ReadPoint(buttons.GetProperty("swapHands")),
                ReadPoint(buttons.GetProperty("inventory")),
                ReadPoint(buttons.GetProperty("options")),
                ReadPoint(buttons.GetProperty("skilldex")),
                ReadPoint(buttons.GetProperty("automap")),
                ReadPoint(buttons.GetProperty("character")),
                ReadRect(buttons.GetProperty("pipBoy"))));
    }

    private static Fo1HudPoint ReadPoint(
        JsonElement row,
        string xProperty = "x",
        string yProperty = "y") =>
        new(row.GetProperty(xProperty).GetInt32(), row.GetProperty(yProperty).GetInt32());

    private static Fo1HudRect ReadRect(JsonElement row) =>
        new(
            row.GetProperty("x").GetInt32(),
            row.GetProperty("y").GetInt32(),
            row.GetProperty("width").GetInt32(),
            row.GetProperty("height").GetInt32());

    private static void ValidateHudLayout(
        Fo1ClassicHudLayout layout,
        IReadOnlyDictionary<string, Fo1OwnedUiTexture> textures,
        IReadOnlyDictionary<string, Fo1OwnedUiTexture> weaponInventoryTextures,
        Fo1OwnedBitmapFont font)
    {
        if (layout.Width != Fo1CharacterStartContractNumericContracts.SourcePresentationInt640 || layout.Height != Fo1CharacterStartContractNumericContracts.SourcePresentationInt100 ||
            !layout.Message.Bounds.Fits(layout.Width, layout.Height) ||
            layout.Message.MaximumLines != Fo1CharacterStartContractNumericContracts.SourcePresentationInt6 || layout.Message.LineIndent != 1 ||
            layout.Message.PrefixCodePoint is < 0 or > Fo1CharacterStartContractNumericContracts.SourcePresentationInt255 ||
            layout.Message.MaximumLines * (font.MaximumHeight + font.LineSpacing) >
                layout.Message.Bounds.Height ||
            !layout.ActionPoints.Bounds.Fits(layout.Width, layout.Height) ||
            layout.ActionPoints.Slots != Fo1CharacterStartContractNumericContracts.SourcePresentationInt10 || layout.ActionPoints.Stride <= 0 ||
            layout.ActionPoints.Bounds.Width <
                (layout.ActionPoints.Slots - 1) * layout.ActionPoints.Stride +
                textures["actionPointGreen"].Width ||
            layout.Item.Bounds.Width != textures["itemPanel"].Width ||
            layout.Item.Bounds.Height != textures["itemPanel"].Height ||
            !layout.Item.Bounds.Fits(layout.Width, layout.Height) ||
            layout.Combat.Window.Width != textures["endWindow"].Width ||
            layout.Combat.Window.Height != textures["endWindow"].Height ||
            !layout.Combat.Window.Fits(layout.Width, layout.Height) ||
            layout.Buttons.PipBoy.Width != textures["pipBoyButton"].Width ||
            layout.Buttons.PipBoy.Height != textures["pipBoyButton"].Height ||
            !layout.Buttons.PipBoy.Fits(layout.Width, layout.Height) ||
            layout.Numbers.DigitWidth <= 0 || layout.Numbers.SignWidth <= 0 ||
            layout.Numbers.Height != textures["numbers"].Height ||
            layout.Numbers.RedOffset + layout.Numbers.PlusX + layout.Numbers.SignWidth >
                textures["numbers"].Width ||
            layout.Item.MoveDigitWidth <= 0 || layout.Item.WeaponSlotWidth <= 0 ||
            layout.Item.WeaponSlotHeight <= 0)
            throw new InvalidOperationException("Fallout gameplay HUD source layout drifted.");

        var numberWidth = layout.Numbers.SignWidth + layout.Numbers.DigitWidth * 3;
        RequireFits(layout.HitPoints, numberWidth, layout.Numbers.Height, layout, "hit points");
        RequireFits(layout.ArmorClass, numberWidth, layout.Numbers.Height, layout, "armor class");
        RequireFits(layout.Item.Single, textures["singleAttack"], layout.Item.Bounds, "single attack");
        RequireFits(layout.Item.MovePoints, textures["movePoints"], layout.Item.Bounds, "move points");
        RequireFits(
            layout.Item.Weapon,
            layout.Item.WeaponSlotWidth,
            layout.Item.WeaponSlotHeight,
            layout.Item.Bounds,
            "weapon slot");
        foreach (var (symbol, weaponTexture) in weaponInventoryTextures)
        {
            var centered = new Fo1HudPoint(
                layout.Item.Weapon.X +
                    (layout.Item.WeaponSlotWidth - weaponTexture.Width) / 2,
                layout.Item.Weapon.Y +
                    (layout.Item.WeaponSlotHeight - weaponTexture.Height) / 2);
            RequireFits(centered, weaponTexture, layout.Item.Bounds, $"weapon {symbol}");
        }
        RequireFits(layout.Buttons.Inventory, textures["inventoryButton"], layout, "inventory button");
        RequireFits(layout.Buttons.Options, textures["optionsButton"], layout, "options button");
        RequireFits(layout.Buttons.SwapHands, textures["redButton"], layout, "swap button");
        RequireFits(layout.Buttons.Skilldex, textures["redButton"], layout, "skilldex button");
        RequireFits(layout.Buttons.Automap, textures["automapButton"], layout, "automap button");
        RequireFits(layout.Buttons.Character, textures["characterButton"], layout, "character button");
        RequireFits(layout.Combat.EndTurn, textures["endTurn"], layout, "end-turn button");
        RequireFits(layout.Combat.EndCombat, textures["endCombat"], layout, "end-combat button");
    }

    private static void RequireFits(
        Fo1HudPoint point,
        Fo1OwnedUiTexture texture,
        Fo1ClassicHudLayout layout,
        string label) =>
        RequireFits(point, texture.Width, texture.Height, layout, label);

    private static void RequireFits(
        Fo1HudPoint point,
        int width,
        int height,
        Fo1ClassicHudLayout layout,
        string label)
    {
        if (!new Fo1HudRect(point.X, point.Y, width, height).Fits(layout.Width, layout.Height))
            throw new InvalidOperationException($"Fallout HUD {label} escapes the canvas.");
    }

    private static void RequireFits(
        Fo1HudPoint point,
        Fo1OwnedUiTexture texture,
        Fo1HudRect parent,
        string label)
    {
        if (point.X < 0 || point.Y < 0 || point.X + texture.Width > parent.Width ||
            point.Y + texture.Height > parent.Height)
            throw new InvalidOperationException($"Fallout HUD {label} escapes its source panel.");
    }

    private static void RequireFits(
        Fo1HudPoint point,
        int width,
        int height,
        Fo1HudRect parent,
        string label)
    {
        if (point.X < 0 || point.Y < 0 || point.X + width > parent.Width ||
            point.Y + height > parent.Height)
            throw new InvalidOperationException($"Fallout HUD {label} escapes its source panel.");
    }

    private static Fo1OwnedUiTexture ReadTexture(
        string id,
        JsonElement row,
        string pathProperty = "png",
        string hashProperty = "pngSha256",
        string widthProperty = "width",
        string heightProperty = "height",
        string? sourceFrmSha256 = null)
    {
        var texturePath = VerifyFile(row, pathProperty, hashProperty);
        var width = row.GetProperty(widthProperty).GetInt32();
        var height = row.GetProperty(heightProperty).GetInt32();
        if (width <= 0 || height <= 0 || width > Fo1CharacterStartContractNumericContracts.SourcePresentationInt2048 || height > Fo1CharacterStartContractNumericContracts.SourcePresentationInt2048)
            throw new InvalidOperationException($"Fallout UI texture dimensions are invalid: {id}.");
        return new Fo1OwnedUiTexture(
            id,
            texturePath,
            row.GetProperty(hashProperty).GetString()!,
            width,
            height,
            sourceFrmSha256 ?? row.GetProperty("sourceFrmSha256").GetString()!);
    }

    private static string VerifyFile(JsonElement row, string pathProperty, string hashProperty)
    {
        var filePath = VerifiedGltfLoader.ResolvePath(row.GetProperty(pathProperty).GetString()!);
        VerifiedGltfLoader.VerifyHash(filePath, row.GetProperty(hashProperty).GetString()!);
        return filePath;
    }
}
