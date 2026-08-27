using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

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
    OpeningBitmapFont Font,
    OpeningMenuStyle Style,
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
            textures);
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
            ReadRgb(presentation.GetProperty("mainMenuColorRgb")),
            font,
            style,
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

    private static OpeningTexture ParseTexture(JsonElement source)
    {
        var path = source.GetProperty("png").GetString()!;
        VerifyHash(path, source.GetProperty("pngSha256").GetString()!);
        var result = new OpeningTexture(
            System.IO.Path.GetFullPath(path),
            new Vector2I(
                source.GetProperty("width").GetInt32(),
                source.GetProperty("height").GetInt32()));
        if (result.Size.X <= 0 || result.Size.Y <= 0)
            throw new InvalidOperationException("Owned opening texture has invalid dimensions.");
        return result;
    }

    private static OpeningBitmapFont ParseFont(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != "opennv-owned-gamebryo-bitmap-font/v1")
            throw new InvalidOperationException("Owned opening font has an unexpected contract.");
        VerifyHash(
            source.GetProperty("source").GetString()!,
            source.GetProperty("sha256").GetString()!);
        var glyphs = source.GetProperty("glyphs")
            .EnumerateArray()
            .Select(value => new OpeningGlyph(
                value.GetProperty("codepoint").GetInt32(),
                ReadRect(value.GetProperty("uvRectPixels")),
                ReadVector(value.GetProperty("sizePixels")),
                value.GetProperty("horizontalOffsetPixels").GetSingle(),
                value.GetProperty("verticalBearingPixels").GetSingle(),
                value.GetProperty("advancePixels").GetSingle()))
            .ToArray();
        var result = new OpeningBitmapFont(
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

    private static OpeningMenuStyle ParseStyle(JsonElement presentation)
    {
        var button = presentation.GetProperty("buttonStyle");
        var globals = presentation.GetProperty("globalStyleTraits");
        var result = new OpeningMenuStyle(
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

    internal static void VerifyHash(string path, string expected)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Owned opening cache file is unavailable.", path);
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned opening runtime video hash differs from its manifest.");
    }
}

internal sealed record OpeningMenuButton(
    int EngineId,
    string Tile,
    string Action,
    string Label,
    Rect2 Rect);

internal sealed record OpeningTexture(string Path, Vector2I Size);

internal sealed record OpeningBitmapFont(
    string LogicalPath,
    float LineHeightPixels,
    float AscentPixels,
    float DescentPixels,
    OpeningTexture Atlas,
    IReadOnlyList<OpeningGlyph> Glyphs);

internal sealed record OpeningGlyph(
    int Codepoint,
    Rect2 UvRect,
    Vector2 Size,
    float HorizontalOffsetPixels,
    float VerticalBearingPixels,
    float AdvancePixels);

internal sealed record OpeningMenuStyle(
    float HorizontalPaddingPixels,
    float VerticalPaddingPixels,
    float TextOffsetYPixels,
    float LineThicknessPixels,
    float LineBrightness,
    float DisabledLineBrightness,
    float TextBrightness,
    float DisabledTextBrightness,
    float BackgroundFillAlpha,
    float BackgroundFillBrightness);
