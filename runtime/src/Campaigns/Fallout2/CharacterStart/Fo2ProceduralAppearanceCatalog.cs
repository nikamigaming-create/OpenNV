using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2FaceShapePreset(
    string Id,
    float HalfWidth,
    float HalfHeight,
    float Taper,
    Vector3 HeadScale);

internal sealed record Fo2HairStylePreset(
    string Id,
    int HairLineY,
    int BottomY,
    string SideMode,
    float SideInset,
    float SideLength,
    string HeadGeometry);

internal sealed record Fo2SkinTonePreset(
    string Id,
    Color PortraitShadow,
    Color PortraitHighlight,
    Color HeadAlbedo);

internal sealed record Fo2AppearanceColorPreset(
    string Id,
    Color PortraitColor,
    Color HeadAlbedo);

internal sealed record Fo2LiveHeadProfile(
    Vector2I Viewport,
    float HeadRadius,
    float HeadHeight,
    float YawAmplitudeRadians,
    float YawCyclesPerSecond);

internal sealed class Fo2ProceduralAppearanceCatalog
{
    internal const string ResourcePath = "res://config/fo2-procedural-appearance-v2.json";
    internal const string ExpectedSchema = "opennv-fo2-procedural-appearance/v2";
    internal const string ExpectedId = "fo2-local-classic-green-appearance-v2";
    internal const string NoSideHair = "none";
    internal const string RightSideHair = "right";
    internal const string BothSideHair = "both";
    private const int ColorComponents = 4;
    private const int VectorComponents = 3;
    private const int Vector2Components = 2;
    private const int IndexX = 0;
    private const int IndexY = 1;
    private const int IndexZ = 2;
    private const int IndexW = 3;
    private const int ZeroInteger = 0;
    private const float MinimumPositive = 0.0f;
    private static readonly Lazy<Fo2ProceduralAppearanceCatalog> SharedCatalog =
        new(LoadCore);

    private Fo2ProceduralAppearanceCatalog(
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
        IReadOnlyList<Fo2FaceShapePreset> faceShapes,
        IReadOnlyList<Fo2HairStylePreset> hairStyles,
        IReadOnlyList<Fo2SkinTonePreset> skinTones,
        IReadOnlyList<Fo2AppearanceColorPreset> hairColors,
        IReadOnlyList<Fo2AppearanceColorPreset> eyeColors,
        Fo2LiveHeadProfile liveHead)
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
        FaceShapeIds = faceShapes.Select(row => row.Id).ToArray();
        HairStyleIds = hairStyles.Select(row => row.Id).ToArray();
        SkinToneIds = skinTones.Select(row => row.Id).ToArray();
        HairColorIds = hairColors.Select(row => row.Id).ToArray();
        EyeColorIds = eyeColors.Select(row => row.Id).ToArray();
        LiveHead = liveHead;
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
    internal IReadOnlyList<Fo2FaceShapePreset> FaceShapes { get; }
    internal IReadOnlyList<Fo2HairStylePreset> HairStyles { get; }
    internal IReadOnlyList<Fo2SkinTonePreset> SkinTones { get; }
    internal IReadOnlyList<Fo2AppearanceColorPreset> HairColors { get; }
    internal IReadOnlyList<Fo2AppearanceColorPreset> EyeColors { get; }
    internal IReadOnlyList<string> FaceShapeIds { get; }
    internal IReadOnlyList<string> HairStyleIds { get; }
    internal IReadOnlyList<string> SkinToneIds { get; }
    internal IReadOnlyList<string> HairColorIds { get; }
    internal IReadOnlyList<string> EyeColorIds { get; }
    internal Fo2LiveHeadProfile LiveHead { get; }

    internal static Fo2ProceduralAppearanceCatalog Load() => SharedCatalog.Value;

    internal Fo2FaceShapePreset Face(string id) => FaceShapes.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 face shape: {id}");

    internal Fo2HairStylePreset HairStyle(string id) =>
        HairStyles.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 hair style: {id}");

    internal Fo2SkinTonePreset SkinTone(string id) =>
        SkinTones.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 skin tone: {id}");

    internal Fo2AppearanceColorPreset HairColor(string id) =>
        HairColors.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 hair color: {id}");

    internal Fo2AppearanceColorPreset EyeColor(string id) =>
        EyeColors.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 eye color: {id}");

    private static Fo2ProceduralAppearanceCatalog LoadCore()
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(ResourcePath);
        if (bytes.Length == ZeroInteger)
            throw new FileNotFoundException(
                "Fallout 2 procedural appearance recipe is missing.",
                ResourcePath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var portrait = root.GetProperty("portrait");
        var defaults = root.GetProperty("defaults");
        var live = root.GetProperty("live3d");
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "id") != ExpectedId ||
            RequiredString(root, "campaign") != "Fallout2" ||
            RequiredString(root, "boundary") !=
                "asset-free-local-procedural-extension-not-retail-face-geometry" ||
            !root.GetProperty("unsupported").EnumerateArray().Any())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 procedural appearance recipe.");
        var faces = root.GetProperty("faceShapes").EnumerateArray().Select(row =>
            new Fo2FaceShapePreset(
                RequiredString(row, "id"),
                Positive(row, "halfWidth"),
                Positive(row, "halfHeight"),
                Finite(row, "taper"),
                ReadVector(row.GetProperty("headScale")))).ToArray();
        var hair = root.GetProperty("hairStyles").EnumerateArray().Select(row =>
            new Fo2HairStylePreset(
                RequiredString(row, "id"),
                row.GetProperty("hairLineY").GetInt32(),
                row.GetProperty("bottomY").GetInt32(),
                RequiredString(row, "sideMode"),
                Finite(row, "sideInset"),
                Finite(row, "sideLength"),
                RequiredString(row, "headGeometry"))).ToArray();
        var skin = root.GetProperty("skinTones").EnumerateArray().Select(row =>
            new Fo2SkinTonePreset(
                RequiredString(row, "id"),
                ReadHtmlColor(row, "portraitShadow"),
                ReadHtmlColor(row, "portraitHighlight"),
                ReadColor(row.GetProperty("headAlbedo")))).ToArray();
        var hairColors = ReadColors(root, "hairColors");
        var eyeColors = ReadColors(root, "eyeColors");
        var profile = new Fo2ProceduralAppearanceCatalog(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            portrait.GetProperty("width").GetInt32(),
            portrait.GetProperty("height").GetInt32(),
            ReadHtmlColor(portrait, "background"),
            ReadHtmlColor(portrait, "outline"),
            ReadHtmlColor(portrait, "feature"),
            RequiredString(defaults, "faceShapeId"),
            RequiredString(defaults, "hairStyleId"),
            RequiredString(defaults, "skinToneId"),
            RequiredString(defaults, "hairColorId"),
            RequiredString(defaults, "eyeColorId"),
            faces,
            hair,
            skin,
            hairColors,
            eyeColors,
            new Fo2LiveHeadProfile(
                ReadVector2I(live.GetProperty("viewport")),
                Positive(live, "headRadius"),
                Positive(live, "headHeight"),
                Positive(live, "yawAmplitudeRadians"),
                Positive(live, "yawCyclesPerSecond")));
        if (profile.PortraitWidth != Fo2ProceduralPortrait.Width ||
            profile.PortraitHeight != Fo2ProceduralPortrait.Height ||
            !faces.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.RoundFace, Fo2ProceduralPortrait.OvalFace,
                    Fo2ProceduralPortrait.AngularFace]) ||
            !hair.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.CroppedHair, Fo2ProceduralPortrait.SweptHair,
                    Fo2ProceduralPortrait.LongHair]) ||
            !skin.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.LightSkin, Fo2ProceduralPortrait.MediumSkin,
                    Fo2ProceduralPortrait.DeepSkin]) ||
            !hairColors.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.BlackHairColor,
                    Fo2ProceduralPortrait.BrownHairColor,
                    Fo2ProceduralPortrait.AuburnHairColor]) ||
            !eyeColors.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.HazelEyeColor,
                    Fo2ProceduralPortrait.BlueEyeColor,
                    Fo2ProceduralPortrait.GreenEyeColor]) ||
            profile.DefaultFaceShapeId != Fo2ProceduralPortrait.OvalFace ||
            profile.DefaultHairStyleId != Fo2ProceduralPortrait.CroppedHair ||
            profile.DefaultSkinToneId != Fo2ProceduralPortrait.MediumSkin ||
            profile.DefaultHairColorId != Fo2ProceduralPortrait.BrownHairColor ||
            profile.DefaultEyeColorId != Fo2ProceduralPortrait.HazelEyeColor ||
            hair.Any(row => row.HairLineY < ZeroInteger || row.BottomY < row.HairLineY ||
                row.BottomY >= profile.PortraitHeight || row.SideInset < MinimumPositive ||
                row.SideLength < MinimumPositive ||
                row.SideMode is not NoSideHair and not RightSideHair and not BothSideHair))
            throw new InvalidOperationException(
                "Fallout 2 procedural appearance identities or dimensions drifted.");
        return profile;
    }

    private static Fo2AppearanceColorPreset[] ReadColors(
        JsonElement root,
        string property) => root.GetProperty(property).EnumerateArray().Select(row =>
            new Fo2AppearanceColorPreset(
                RequiredString(row, "id"),
                ReadHtmlColor(row, "portraitColor"),
                ReadColor(row.GetProperty("headAlbedo")))).ToArray();

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 appearance recipe string is empty: {property}");
    }

    private static float Finite(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetSingle();
        return float.IsFinite(value)
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 appearance recipe number is invalid: {property}");
    }

    private static float Positive(JsonElement source, string property)
    {
        var value = Finite(source, property);
        return value > MinimumPositive
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 appearance recipe number is not positive: {property}");
    }

    private static Color ReadHtmlColor(JsonElement source, string property) =>
        new(RequiredString(source, property));

    private static Color ReadColor(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != ColorComponents ||
            values.Any(value => !float.IsFinite(value) || value is < 0.0f or > 1.0f))
            throw new InvalidOperationException(
                "Fallout 2 appearance recipe color is invalid.");
        return new Color(values[IndexX], values[IndexY], values[IndexZ], values[IndexW]);
    }

    private static Vector3 ReadVector(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != VectorComponents ||
            values.Any(value => !float.IsFinite(value) || value <= MinimumPositive))
            throw new InvalidOperationException(
                "Fallout 2 appearance recipe vector is invalid.");
        return new Vector3(values[IndexX], values[IndexY], values[IndexZ]);
    }

    private static Vector2I ReadVector2I(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetInt32()).ToArray();
        if (values.Length != Vector2Components ||
            values.Any(value => value <= ZeroInteger))
            throw new InvalidOperationException(
                "Fallout 2 appearance recipe viewport is invalid.");
        return new Vector2I(values[IndexX], values[IndexY]);
    }
}
