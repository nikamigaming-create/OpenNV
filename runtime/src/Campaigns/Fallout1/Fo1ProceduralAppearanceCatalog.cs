using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal sealed record Fo1FaceShape(
    string Id,
    float HalfWidth,
    float HalfHeight,
    float Taper,
    Vector3 HeadScale);

internal sealed record Fo1HairStyle(
    string Id,
    int HairLineY,
    int BottomY,
    string SideMode,
    float SideLength);

internal sealed record Fo1AppearanceColor(
    string Id,
    Color PortraitColor,
    Color HeadAlbedo);

internal sealed record Fo1SkinTone(
    string Id,
    Color PortraitShadow,
    Color PortraitHighlight,
    Color HeadAlbedo);

internal sealed class Fo1ProceduralAppearanceCatalog
{
    internal const string ResourcePath = "res://config/fo1-procedural-appearance-v1.json";
    internal const string ExpectedSchema = "opennv-fo1-procedural-appearance/v1";
    internal const string ExpectedId = "fo1-local-classic-green-appearance-v1";
    internal const string NoSideHair = "none";
    internal const string RightSideHair = "right";
    internal const string BothSideHair = "both";
    private const int ColorComponents = 4;
    private const int VectorComponents = 3;
    private const int Vector2Components = 2;
    private static readonly Lazy<Fo1ProceduralAppearanceCatalog> Shared = new(LoadCore);

    private Fo1ProceduralAppearanceCatalog(
        string sha256,
        int portraitWidth,
        int portraitHeight,
        Color background,
        Color outline,
        Color feature,
        string defaultFaceShapeId,
        string defaultHairStyleId,
        string defaultSkinToneId,
        string defaultHairColorId,
        string defaultEyeColorId,
        IReadOnlyList<Fo1FaceShape> faceShapes,
        IReadOnlyList<Fo1HairStyle> hairStyles,
        IReadOnlyList<Fo1SkinTone> skinTones,
        IReadOnlyList<Fo1AppearanceColor> hairColors,
        IReadOnlyList<Fo1AppearanceColor> eyeColors,
        Vector2I liveViewport,
        float liveHeadRadius,
        float liveHeadHeight,
        float liveYawAmplitude,
        float liveYawCycles)
    {
        Sha256 = sha256;
        PortraitWidth = portraitWidth;
        PortraitHeight = portraitHeight;
        Background = background;
        Outline = outline;
        Feature = feature;
        DefaultFaceShapeId = defaultFaceShapeId;
        DefaultHairStyleId = defaultHairStyleId;
        DefaultSkinToneId = defaultSkinToneId;
        DefaultHairColorId = defaultHairColorId;
        DefaultEyeColorId = defaultEyeColorId;
        FaceShapes = faceShapes;
        HairStyles = hairStyles;
        SkinTones = skinTones;
        HairColors = hairColors;
        EyeColors = eyeColors;
        LiveViewport = liveViewport;
        LiveHeadRadius = liveHeadRadius;
        LiveHeadHeight = liveHeadHeight;
        LiveYawAmplitude = liveYawAmplitude;
        LiveYawCycles = liveYawCycles;
    }

    internal string Sha256 { get; }
    internal int PortraitWidth { get; }
    internal int PortraitHeight { get; }
    internal Color Background { get; }
    internal Color Outline { get; }
    internal Color Feature { get; }
    internal string DefaultFaceShapeId { get; }
    internal string DefaultHairStyleId { get; }
    internal string DefaultSkinToneId { get; }
    internal string DefaultHairColorId { get; }
    internal string DefaultEyeColorId { get; }
    internal IReadOnlyList<Fo1FaceShape> FaceShapes { get; }
    internal IReadOnlyList<Fo1HairStyle> HairStyles { get; }
    internal IReadOnlyList<Fo1SkinTone> SkinTones { get; }
    internal IReadOnlyList<Fo1AppearanceColor> HairColors { get; }
    internal IReadOnlyList<Fo1AppearanceColor> EyeColors { get; }
    internal Vector2I LiveViewport { get; }
    internal float LiveHeadRadius { get; }
    internal float LiveHeadHeight { get; }
    internal float LiveYawAmplitude { get; }
    internal float LiveYawCycles { get; }

    internal static Fo1ProceduralAppearanceCatalog Load() => Shared.Value;
    internal Fo1FaceShape Face(string id) =>
        FaceShapes.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 1 face: {id}");
    internal Fo1HairStyle Hair(string id) =>
        HairStyles.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 1 hair style: {id}");
    internal Fo1SkinTone Skin(string id) =>
        SkinTones.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 1 skin tone: {id}");
    internal Fo1AppearanceColor HairColor(string id) =>
        HairColors.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 1 hair color: {id}");
    internal Fo1AppearanceColor EyeColor(string id) =>
        EyeColors.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 1 eye color: {id}");

    private static Fo1ProceduralAppearanceCatalog LoadCore()
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(ResourcePath);
        if (bytes.Length == 0)
            throw new FileNotFoundException("Fallout 1 procedural appearance recipe is missing.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (Text(root, "schema") != ExpectedSchema || Text(root, "id") != ExpectedId ||
            Text(root, "campaign") != "Fallout1" ||
            Text(root, "boundary") !=
                "asset-free-local-procedural-hex-extension-not-retail-face-geometry" ||
            !root.GetProperty("unsupported").EnumerateArray().Any())
            throw new InvalidOperationException("Unexpected Fallout 1 appearance recipe.");
        var defaults = root.GetProperty("defaults");
        var portrait = root.GetProperty("portrait");
        var live = root.GetProperty("live3d");
        var result = new Fo1ProceduralAppearanceCatalog(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            portrait.GetProperty("width").GetInt32(),
            portrait.GetProperty("height").GetInt32(),
            Html(portrait, "background"),
            Html(portrait, "outline"),
            Html(portrait, "feature"),
            Text(defaults, "faceShapeId"),
            Text(defaults, "hairStyleId"),
            Text(defaults, "skinToneId"),
            Text(defaults, "hairColorId"),
            Text(defaults, "eyeColorId"),
            root.GetProperty("faceShapes").EnumerateArray().Select(row =>
                new Fo1FaceShape(
                    Text(row, "id"), Positive(row, "halfWidth"),
                    Positive(row, "halfHeight"), Finite(row, "taper"),
                    Vector3Value(row.GetProperty("headScale")))).ToArray(),
            root.GetProperty("hairStyles").EnumerateArray().Select(row =>
                new Fo1HairStyle(
                    Text(row, "id"), row.GetProperty("hairLineY").GetInt32(),
                    row.GetProperty("bottomY").GetInt32(), Text(row, "sideMode"),
                    Positive(row, "sideLength"))).ToArray(),
            root.GetProperty("skinTones").EnumerateArray().Select(row =>
                new Fo1SkinTone(
                    Text(row, "id"), Html(row, "portraitShadow"),
                    Html(row, "portraitHighlight"), ColorValue(row.GetProperty("headAlbedo")))).ToArray(),
            Colors(root, "hairColors"),
            Colors(root, "eyeColors"),
            Vector2Value(live.GetProperty("viewport")),
            Positive(live, "headRadius"),
            Positive(live, "headHeight"),
            Positive(live, "yawAmplitudeRadians"),
            Positive(live, "yawCyclesPerSecond"));
        result.Validate();
        return result;
    }

    private void Validate()
    {
        if (PortraitWidth != Fo1ProceduralPortrait.Width ||
            PortraitHeight != Fo1ProceduralPortrait.Height ||
            FaceShapes.Count < 2 || HairStyles.Count < 2 || SkinTones.Count < 2 ||
            HairColors.Count < 2 || EyeColors.Count < 2 ||
            !FaceShapes.Any(row => row.Id == DefaultFaceShapeId) ||
            !HairStyles.Any(row => row.Id == DefaultHairStyleId) ||
            !SkinTones.Any(row => row.Id == DefaultSkinToneId) ||
            !HairColors.Any(row => row.Id == DefaultHairColorId) ||
            !EyeColors.Any(row => row.Id == DefaultEyeColorId) ||
            HairStyles.Any(row => row.SideMode is not NoSideHair and not RightSideHair and
                not BothSideHair || row.HairLineY < 0 || row.BottomY < row.HairLineY ||
                row.BottomY >= PortraitHeight))
            throw new InvalidOperationException("Fallout 1 appearance recipe identities drifted.");
    }

    private static Fo1AppearanceColor[] Colors(JsonElement root, string name) =>
        root.GetProperty(name).EnumerateArray().Select(row =>
            new Fo1AppearanceColor(
                Text(row, "id"), Html(row, "portraitColor"),
                ColorValue(row.GetProperty("headAlbedo")))).ToArray();

    private static string Text(JsonElement row, string name) =>
        row.GetProperty(name).GetString() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Fallout 1 appearance field is empty: {name}");

    private static float Finite(JsonElement row, string name)
    {
        var value = row.GetProperty(name).GetSingle();
        return float.IsFinite(value)
            ? value
            : throw new InvalidOperationException($"Fallout 1 appearance number is invalid: {name}");
    }

    private static float Positive(JsonElement row, string name)
    {
        var value = Finite(row, name);
        return value > 0.0f
            ? value
            : throw new InvalidOperationException($"Fallout 1 appearance number is not positive: {name}");
    }

    private static Color Html(JsonElement row, string name) => new(Text(row, name));

    private static Color ColorValue(JsonElement row)
    {
        var values = row.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != ColorComponents ||
            values.Any(value => !float.IsFinite(value) || value is < 0.0f or > 1.0f))
            throw new InvalidOperationException("Fallout 1 appearance color is invalid.");
        return new Color(values[0], values[1], values[2], values[3]);
    }

    private static Vector3 Vector3Value(JsonElement row)
    {
        var values = row.EnumerateArray().Select(value => value.GetSingle()).ToArray();
        if (values.Length != VectorComponents || values.Any(value => value <= 0.0f))
            throw new InvalidOperationException("Fallout 1 appearance vector is invalid.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector2I Vector2Value(JsonElement row)
    {
        var values = row.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (values.Length != Vector2Components || values.Any(value => value <= 0))
            throw new InvalidOperationException("Fallout 1 appearance viewport is invalid.");
        return new Vector2I(values[0], values[1]);
    }
}
