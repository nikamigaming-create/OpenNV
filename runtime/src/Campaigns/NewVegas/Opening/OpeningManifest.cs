using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed record OpeningManifest(
    string Path,
    string Campaign,
    string EntryQuestEditorId,
    int EntryStage,
    Vector2 CanvasSize,
    Rect2 TitleRect,
    string TitleTexturePath,
    string BackgroundTexturePath,
    string MainMenuMusicPath,
    float MainMenuMusicVolume,
    Color MainMenuColor,
    OwnedBitmapFont Font,
    OwnedUiStyle Style,
    OwnedGameplayUiPresentation GameplayUi,
    string IntroVideoPath,
    IReadOnlyList<OpeningMenuButton> Buttons,
    OpeningNewGameFlow NewGameFlow)
{
    private const string ExpectedSchema = "opennv-owned-opening-manifest/v1";
    private const string ExpectedStatus = "compiled-owned-opening-graph";
    private const int VectorComponents = 2;
    private const int RectComponents = 4;
    private const int RgbComponents = 3;
    private const float ByteChannelMaximum = 255.0f;

    internal static OpeningManifest Load(
        string path,
        RuntimeConfiguration configuration)
    {
        var resolved = System.IO.Path.GetFullPath(path);
        using var document = JsonDocument.Parse(File.ReadAllText(resolved));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ExpectedSchema ||
            root.GetProperty("status").GetString() != ExpectedStatus)
            throw new InvalidOperationException("Owned opening manifest has an unexpected contract.");
        configuration.VerifyCompiledConfigurationDescriptor(root.GetProperty("configuration"));
        if (root.GetProperty("blockers").GetArrayLength() != 0)
            throw new InvalidOperationException("Owned opening manifest contains entry blockers.");

        var entry = root.GetProperty("entryPoint");
        var ui = root.GetProperty("ui");
        var boot = ui.GetProperty("boot");
        var layout = boot.GetProperty("layout");
        var textures = ui.GetProperty("preparedTextures")
            .EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("requestedPath").GetString()!,
                ParseTexture,
                StringComparer.OrdinalIgnoreCase);
        var titleAssets = boot.GetProperty("titleAssets").EnumerateArray().ToArray();
        if (titleAssets.Length != 1)
            throw new InvalidOperationException("Owned opening title texture does not resolve uniquely.");
        var titleAsset = titleAssets[0].GetString()!;
        if (!textures.TryGetValue(titleAsset, out var titleTexture))
            throw new FileNotFoundException("Owned opening title texture is unavailable.", titleAsset);

        var presentation = ui.GetProperty("enginePresentation");
        var background = ParseTexture(presentation.GetProperty("background"));
        var music = presentation.GetProperty("music");
        var musicPath = music.GetProperty("source").GetString()!;
        VerifyHash(musicPath, music.GetProperty("sha256").GetString()!);
        var font = ParseFont(presentation.GetProperty("font"));
        var style = ParseStyle(presentation);
        var mainMenuColor = ReadRgb(presentation.GetProperty("mainMenuColorRgb"));
        var uiDocuments = ui.GetProperty("documents")
            .EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("path").GetString()!,
                value => value,
                StringComparer.OrdinalIgnoreCase);
        var gameplayUi = ParseGameplayUi(
            ui.GetProperty("gameplayPresentation"),
            style,
            uiDocuments);

        var buttons = layout.GetProperty("buttons")
            .EnumerateArray()
            .Select(value => new OpeningMenuButton(
                value.GetProperty("engineId").GetInt32(),
                value.GetProperty("tile").GetString()!,
                value.GetProperty("action").GetString()!,
                value.GetProperty("label").GetString()!,
                ReadRect(value.GetProperty("rect"))))
            .ToArray();
        if (buttons.Length < 1 ||
            buttons.Any(value =>
                string.IsNullOrWhiteSpace(value.Tile) ||
                string.IsNullOrWhiteSpace(value.Action) ||
                string.IsNullOrWhiteSpace(value.Label) ||
                value.Rect.Size.X <= 0.0f ||
                value.Rect.Size.Y <= 0.0f) ||
            buttons.Select(value => value.EngineId).Distinct().Count() != buttons.Length ||
            buttons.Select(value => value.Action).Distinct(StringComparer.OrdinalIgnoreCase).Count() != buttons.Length)
            throw new InvalidOperationException("Owned opening menu buttons are incomplete or ambiguous.");

        var entryVideos = root.GetProperty("videos")
            .EnumerateArray()
            .Where(value => value.GetProperty("requiredAtEntry").GetBoolean())
            .ToArray();
        if (entryVideos.Length != 1 || entryVideos[0].GetProperty("runtime").ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Owned opening entry video does not resolve uniquely.");
        var runtimeVideo = entryVideos[0].GetProperty("runtime");
        var introVideoPath = runtimeVideo.GetProperty("output").GetString()!;
        VerifyHash(introVideoPath, runtimeVideo.GetProperty("outputSha256").GetString()!);

        var newGameFlow = OpeningNewGameFlow.Load(
            root.GetProperty("newGameFlow"),
            ui.GetProperty("flow"),
            textures,
            uiDocuments);
        var result = new OpeningManifest(
            resolved,
            root.GetProperty("campaign").GetString()!,
            entry.GetProperty("questEditorId").GetString()!,
            entry.GetProperty("stage").GetInt32(),
            ReadVector(layout.GetProperty("canvasSize")),
            ReadRect(layout.GetProperty("titleRect")),
            titleTexture.Path,
            background.Path,
            System.IO.Path.GetFullPath(musicPath),
            music.GetProperty("volume").GetSingle(),
            mainMenuColor,
            font,
            style,
            gameplayUi,
            System.IO.Path.GetFullPath(introVideoPath),
            buttons,
            newGameFlow);
        if (string.IsNullOrWhiteSpace(result.Campaign) ||
            string.IsNullOrWhiteSpace(result.EntryQuestEditorId) ||
            result.EntryStage < 0 ||
            result.MainMenuMusicVolume < 0.0f ||
            result.MainMenuMusicVolume > 1.0f ||
            result.CanvasSize.X <= 0.0f ||
            result.CanvasSize.Y <= 0.0f ||
            result.TitleRect.Size.X <= 0.0f ||
            result.TitleRect.Size.Y <= 0.0f)
            throw new InvalidOperationException("Owned opening manifest presentation is incomplete.");
        return result;
    }

    private static OwnedGameplayUiPresentation ParseGameplayUi(
        JsonElement source,
        OwnedUiStyle style,
        IReadOnlyDictionary<string, JsonElement> documents)
    {
        if (source.GetProperty("schema").GetString() != "opennv-owned-gameplay-ui/v1")
            throw new InvalidOperationException("Owned gameplay UI has an unexpected contract.");
        var fonts = source.GetProperty("fonts")
            .EnumerateArray()
            .ToDictionary(
                value => value.GetProperty("fontId").GetInt32(),
                ParseFont);
        var roles = source.GetProperty("roles")
            .EnumerateArray()
            .Select(value =>
            {
                var documentPath = value.GetProperty("source").GetString()!;
                VerifyHash(documentPath, value.GetProperty("sha256").GetString()!);
                var document = value.GetProperty("document").GetString()!;
                var closure = value.GetProperty("documentClosure")
                    .EnumerateArray()
                    .Select(member => member.GetString()!)
                    .ToArray();
                if (!closure.Contains(document, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Owned gameplay UI document closure excludes its root.");
                foreach (var member in closure)
                {
                    if (!documents.TryGetValue(member, out var ownedDocument))
                        throw new InvalidOperationException(
                            $"Owned gameplay UI closure document is absent: {member}");
                    VerifyHash(
                        ownedDocument.GetProperty("source").GetString()!,
                        ownedDocument.GetProperty("sha256").GetString()!);
                }
                var layout = value.GetProperty("layout")
                    .EnumerateArray()
                    .ToDictionary(
                        tile => tile.GetProperty("tile").GetString()!,
                        tile => ReadRect(tile.GetProperty("rect")),
                        StringComparer.OrdinalIgnoreCase);
                return new OwnedGameplayUiRole(
                    value.GetProperty("role").GetString()!,
                    document,
                    value.GetProperty("menuName").GetString()!,
                    value.GetProperty("bodyFontId").GetInt32(),
                    value.GetProperty("titleFontId").GetInt32(),
                    layout);
            })
            .ToDictionary(value => value.Role, StringComparer.OrdinalIgnoreCase);
        var result = new OwnedGameplayUiPresentation(
            ReadVector(source.GetProperty("referenceCanvasSize")),
            ParseTexture(source.GetProperty("background")),
            ParsePhysicalPipBoy(source.GetProperty("physicalDevice")),
            ParsePipBoyStatusPresentation(source.GetProperty("statusPresentation")),
            ReadRgba(source.GetProperty("systemColor").GetProperty("rgba")),
            style,
            roles,
            fonts);
        var requiredRoles = new[] { "hud", "status", "items", "data" };
        var requiredLayoutTiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["hud"] = ["QuestReminder", "Messages", "Info", "ReticleCenter"],
            ["status"] = [],
            ["items"] = ["IM_MainRect"],
            ["data"] = ["MM_MainRect"],
        };
        if (result.CanvasSize.X <= 0.0f ||
            result.CanvasSize.Y <= 0.0f ||
            result.Roles.Count != requiredRoles.Length ||
            requiredRoles.Any(role => !result.Roles.ContainsKey(role)) ||
            requiredLayoutTiles.Any(required =>
                !result.Roles.TryGetValue(required.Key, out var role) ||
                required.Value.Any(tile => !role.Layout.ContainsKey(tile))) ||
            result.Roles.Values.Any(role =>
                string.IsNullOrWhiteSpace(role.Document) ||
                string.IsNullOrWhiteSpace(role.MenuName) ||
                !result.Fonts.ContainsKey(role.BodyFontId) ||
                !result.Fonts.ContainsKey(role.TitleFontId)))
            throw new InvalidOperationException("Owned gameplay UI presentation is incomplete.");
        return result;
    }

    private static OwnedPipBoyStatusPresentation ParsePipBoyStatusPresentation(
        JsonElement source)
    {
        var statusContainer = source.GetProperty("statusContainer");
        var result = new OwnedPipBoyStatusPresentation(
            ReadRect(statusContainer.GetProperty("rect")),
            source.GetProperty("rules")
                .EnumerateArray()
                .Select(value => new OwnedPipBoyRule(
                    value.GetProperty("tile").GetString()!,
                    ReadRect(value.GetProperty("rect"))))
                .ToArray(),
            ParsePipBoyStrings(source.GetProperty("headline")),
            ParsePipBoyStrings(source.GetProperty("conditionTabs")),
            ParsePipBoyStrings(source.GetProperty("navigation")),
            source.GetProperty("bodyImages")
                .EnumerateArray()
                .Select(value => new OwnedPipBoyBodyImage(
                    value.GetProperty("tile").GetString()!,
                    value.GetProperty("parentTile").GetString()!,
                    value.GetProperty("engineId").GetInt32(),
                    ReadRect(value.GetProperty("rect")),
                    ParseTexture(value.GetProperty("texture"))))
                .ToArray());
        var strings = result.Headline
            .Concat(result.ConditionTabs)
            .Concat(result.Navigation)
            .ToArray();
        if (result.StatusContainerRect.Size.X <= 0.0f ||
            result.StatusContainerRect.Size.Y <= 0.0f ||
            result.Rules.Count != 4 ||
            result.Rules.Any(value =>
                string.IsNullOrWhiteSpace(value.Tile) ||
                value.Rect.Size.X <= 0.0f ||
                value.Rect.Size.Y <= 0.0f) ||
            result.Headline.Count != 5 ||
            result.ConditionTabs.Count != 3 ||
            result.Navigation.Count != 5 ||
            result.BodyImages.Count != 7 ||
            strings.Any(value =>
                string.IsNullOrWhiteSpace(value.Tile) ||
                string.IsNullOrWhiteSpace(value.Entity) ||
                string.IsNullOrWhiteSpace(value.Text) ||
                value.TextProvenance !=
                    "recipe-fallback-after-owned-entity-validation" ||
                value.FontId <= 0 ||
                value.Rect.Size.X <= 0.0f ||
                value.Rect.Size.Y <= 0.0f) ||
            strings.Select(value => value.EngineId).Distinct().Count() != strings.Length ||
            result.BodyImages.Any(value =>
                string.IsNullOrWhiteSpace(value.Tile) ||
                string.IsNullOrWhiteSpace(value.ParentTile) ||
                value.Rect.Size.X <= 0.0f ||
                value.Rect.Size.Y <= 0.0f) ||
            result.BodyImages.Select(value => value.EngineId).Distinct().Count() !=
                result.BodyImages.Count)
            throw new InvalidOperationException(
                "Owned Pip-Boy STATS presentation is incomplete.");
        return result;
    }

    private static IReadOnlyList<OwnedPipBoyStringSource> ParsePipBoyStrings(
        JsonElement source) => source
        .EnumerateArray()
        .Select(value =>
        {
            var provenance = value.GetProperty("textProvenance");
            if (provenance.GetProperty("entity").GetString() !=
                    value.GetProperty("entity").GetString())
                throw new InvalidOperationException(
                    "Owned Pip-Boy STATS text provenance differs from its entity.");
            return new OwnedPipBoyStringSource(
                value.GetProperty("tile").GetString()!,
                value.GetProperty("engineId").GetInt32(),
                value.GetProperty("entity").GetString()!,
                value.GetProperty("fontId").GetInt32(),
                value.GetProperty("text").GetString()!,
                provenance.GetProperty("kind").GetString()!,
                ReadRect(value.GetProperty("rect")),
                value.TryGetProperty("selected", out var selected) &&
                    selected.GetBoolean());
        })
        .ToArray();

    private static OwnedPhysicalPipBoy ParsePhysicalPipBoy(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != "opennv-owned-physical-pipboy/v1")
            throw new InvalidOperationException("Owned physical Pip-Boy has an unexpected contract.");
        var sourcePath = source.GetProperty("source").GetString()!;
        var sourceSha256 = source.GetProperty("sourceSha256").GetString()!;
        VerifyHash(sourcePath, sourceSha256);
        var materialManifestPath = source.GetProperty("materialManifest").GetString()!;
        var materialManifestSha256 = source.GetProperty("materialManifestSha256").GetString()!;
        VerifyHash(materialManifestPath, materialManifestSha256);
        using var materialDocument = JsonDocument.Parse(File.ReadAllText(materialManifestPath));
        if (materialDocument.RootElement.GetProperty("schema").GetString() !=
                "opennv-static-material-manifest/v1")
            throw new InvalidOperationException(
                "Owned physical Pip-Boy material manifest has an unexpected contract.");
        var modelPath = source.GetProperty("model").GetString()!;
        var modelSha256 = source.GetProperty("modelSha256").GetString()!;
        var sidecarPath = source.GetProperty("sidecar").GetString()!;
        var sidecarSha256 = source.GetProperty("sidecarSha256").GetString()!;
        var bufferPath = source.GetProperty("buffer").GetString()!;
        var bufferSha256 = source.GetProperty("bufferSha256").GetString()!;
        VerifyHash(modelPath, modelSha256);
        VerifyHash(sidecarPath, sidecarSha256);
        VerifyHash(bufferPath, bufferSha256);
        using var sidecarDocument = JsonDocument.Parse(File.ReadAllText(sidecarPath));
        var sidecar = sidecarDocument.RootElement;
        var sidecarSourceSha256 = sidecar.GetProperty("source").GetProperty("sha256").GetString();
        var outputs = sidecar.GetProperty("outputs");
        var expectedBufferPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(modelPath)!,
            outputs.GetProperty("buffer").GetProperty("file").GetString()!));
        if (!sourceSha256.Equals(sidecarSourceSha256, StringComparison.OrdinalIgnoreCase) ||
            !modelSha256.Equals(
                outputs.GetProperty("gltf").GetProperty("sha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !bufferSha256.Equals(
                outputs.GetProperty("buffer").GetProperty("sha256").GetString(),
                StringComparison.OrdinalIgnoreCase) ||
            !System.IO.Path.GetFullPath(bufferPath).Equals(
                expectedBufferPath,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned physical Pip-Boy model/sidecar/buffer identities disagree.");
        var result = new OwnedPhysicalPipBoy(
            source.GetProperty("logicalPath").GetString()!,
            sourceSha256,
            System.IO.Path.GetFullPath(modelPath),
            modelSha256,
            System.IO.Path.GetFullPath(sidecarPath),
            sidecarSha256,
            System.IO.Path.GetFullPath(bufferPath),
            bufferSha256,
            System.IO.Path.GetFullPath(materialManifestPath),
            materialManifestSha256,
            source.GetProperty("screenSurface").GetString()!,
            source.GetProperty("buttonGlowSurfaces")
                .EnumerateObject()
                .ToDictionary(
                    value => value.Name,
                    value => value.Value.GetString()!,
                    StringComparer.OrdinalIgnoreCase),
            source.GetProperty("surfaces").GetInt32(),
            source.GetProperty("vertices").GetInt32(),
            source.GetProperty("textures").GetInt32());
        if (!result.LogicalPath.Equals(
                "meshes\\pipboy3000\\pipboyarm.nif",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.SourceSha256) ||
            string.IsNullOrWhiteSpace(result.ScreenSurface) ||
            result.ButtonGlowSurfaces.Count != 3 ||
            new[] { "status", "items", "data" }.Any(role =>
                !result.ButtonGlowSurfaces.TryGetValue(role, out var surface) ||
                string.IsNullOrWhiteSpace(surface)) ||
            result.Surfaces < 1 ||
            result.Vertices < 1 ||
            result.Textures < 1)
            throw new InvalidOperationException("Owned physical Pip-Boy presentation is incomplete.");
        return result;
    }

    private static OwnedUiTexture ParseTexture(JsonElement source)
    {
        var path = source.GetProperty("png").GetString()!;
        VerifyHash(path, source.GetProperty("pngSha256").GetString()!);
        var result = new OwnedUiTexture(
            System.IO.Path.GetFullPath(path),
            new Vector2I(
                source.GetProperty("width").GetInt32(),
                source.GetProperty("height").GetInt32()));
        if (result.Size.X <= 0 || result.Size.Y <= 0)
            throw new InvalidOperationException("Owned opening texture has invalid dimensions.");
        return result;
    }

    private static OwnedBitmapFont ParseFont(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != "opennv-owned-gamebryo-bitmap-font/v1")
            throw new InvalidOperationException("Owned opening font has an unexpected contract.");
        VerifyHash(
            source.GetProperty("source").GetString()!,
            source.GetProperty("sha256").GetString()!);
        var glyphs = source.GetProperty("glyphs")
            .EnumerateArray()
            .Select(value => new OwnedUiGlyph(
                value.GetProperty("codepoint").GetInt32(),
                ReadRect(value.GetProperty("uvRectPixels")),
                ReadVector(value.GetProperty("sizePixels")),
                value.GetProperty("horizontalOffsetPixels").GetSingle(),
                value.GetProperty("verticalBearingPixels").GetSingle(),
                value.GetProperty("advancePixels").GetSingle()))
            .ToArray();
        var result = new OwnedBitmapFont(
            source.GetProperty("logicalPath").GetString()!,
            source.GetProperty("lineHeightPixels").GetSingle(),
            source.GetProperty("ascentPixels").GetSingle(),
            source.GetProperty("descentPixels").GetSingle(),
            ParseTexture(source.GetProperty("atlas")),
            glyphs);
        if (string.IsNullOrWhiteSpace(result.LogicalPath) ||
            result.LineHeightPixels <= 0.0f ||
            result.AscentPixels <= 0.0f ||
            result.DescentPixels < 0.0f ||
            glyphs.Length < 1 ||
            glyphs.Select(value => value.Codepoint).Distinct().Count() != glyphs.Length ||
            glyphs.Any(value =>
                value.Codepoint < 0 ||
                value.UvRect.Size.X < 0.0f ||
                value.UvRect.Size.Y < 0.0f ||
                value.Size.X < 0.0f ||
                value.Size.Y < 0.0f ||
                value.AdvancePixels <= 0.0f))
            throw new InvalidOperationException("Owned opening font metrics are incomplete.");
        return result;
    }

    private static OwnedUiStyle ParseStyle(JsonElement presentation)
    {
        var button = presentation.GetProperty("buttonStyle");
        var globals = presentation.GetProperty("globalStyleTraits");
        var result = new OwnedUiStyle(
            button.GetProperty("horizontalPaddingPixels").GetSingle(),
            button.GetProperty("verticalPaddingPixels").GetSingle(),
            button.GetProperty("textOffsetYPixels").GetSingle(),
            globals.GetProperty("_line_thickness").GetSingle(),
            globals.GetProperty("_line_brightness").GetSingle(),
            globals.GetProperty("_line_brightness_disabled").GetSingle(),
            globals.GetProperty("_text_brightness").GetSingle(),
            globals.GetProperty("_text_brightness_disabled").GetSingle(),
            globals.GetProperty("_background_fill_alpha").GetSingle(),
            globals.GetProperty("_background_fill_brightness").GetSingle());
        if (result.HorizontalPaddingPixels < 0.0f ||
            result.VerticalPaddingPixels < 0.0f ||
            result.LineThicknessPixels <= 0.0f)
            throw new InvalidOperationException("Owned opening menu style is invalid.");
        return result;
    }

    internal static Vector2 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != VectorComponents)
            throw new InvalidOperationException("Owned opening vector has an unexpected dimension.");
        return new Vector2(values[0], values[1]);
    }

    internal static Rect2 ReadRect(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != RectComponents)
            throw new InvalidOperationException("Owned opening rectangle has an unexpected dimension.");
        return new Rect2(values[0], values[1], values[2], values[3]);
    }

    private static Color ReadRgb(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != RgbComponents ||
            values.Any(value => value < 0.0f || value > ByteChannelMaximum))
            throw new InvalidOperationException("Owned opening RGB color is invalid.");
        return new Color(
            values[0] / ByteChannelMaximum,
            values[1] / ByteChannelMaximum,
            values[2] / ByteChannelMaximum,
            1.0f);
    }

    private static Color ReadRgba(JsonElement source)
    {
        var values = source.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != 4 ||
            values.Any(value => value < 0.0f || value > ByteChannelMaximum))
            throw new InvalidOperationException("Owned opening RGBA color is invalid.");
        return new Color(
            values[0] / ByteChannelMaximum,
            values[1] / ByteChannelMaximum,
            values[2] / ByteChannelMaximum,
            values[3] / ByteChannelMaximum);
    }

    internal static void VerifyHash(string path, string expected)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Owned opening cache file is unavailable.", path);
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Owned opening cache-file hash differs from its manifest.");
    }
}

internal sealed record OpeningMenuButton(
    int EngineId,
    string Tile,
    string Action,
    string Label,
    Rect2 Rect);
