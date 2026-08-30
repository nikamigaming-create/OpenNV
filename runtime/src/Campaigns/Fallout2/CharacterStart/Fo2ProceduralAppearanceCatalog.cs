using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2FaceShapePreset(
    string Id,
    float HalfWidth,
    float HalfHeight,
    float Taper,
    Vector3 HeadScale,
    IReadOnlyDictionary<string, float> NativeFaceGenControls);

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

internal sealed record Fo2BrowStylePreset(
    string Id,
    int PortraitY,
    int PortraitOuterOffset,
    int PortraitThickness,
    float LiveY,
    float LiveRotationRadians,
    float LiveWidth,
    float LiveThickness,
    IReadOnlyDictionary<string, float> NativeFaceGenControls);

internal sealed record Fo2NoseStylePreset(
    string Id,
    int PortraitWidth,
    int PortraitHeight,
    Vector3 HeadScale,
    IReadOnlyDictionary<string, float> NativeFaceGenControls);

internal sealed record Fo2MouthStylePreset(
    string Id,
    int PortraitWidth,
    int PortraitThickness,
    float LiveWidth,
    float LiveHeight,
    IReadOnlyDictionary<string, float> NativeFaceGenControls);

internal sealed record Fo2LiveHeadProfile(
    Vector2I Viewport,
    float HeadRadius,
    float HeadHeight,
    float BrowX,
    float BrowZ,
    float NoseY,
    float NoseZ,
    float MouthY,
    float MouthZ,
    float FeatureDepth,
    float YawAmplitudeRadians,
    float YawCyclesPerSecond,
    float NativeMorphWeightScale);

internal sealed class Fo2ProceduralAppearanceCatalog
{
    internal const string ResourcePath = "res://config/fo2-procedural-appearance-v3.json";
    internal const string ExpectedSchema = "opennv-fo2-procedural-appearance/v3";
    internal const string ExpectedId = "fo2-local-classic-green-appearance-v3";
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
        int portraitBrowLeftX,
        int portraitBrowRightX,
        int portraitBrowWidth,
        int portraitNoseY,
        int portraitMouthY,
        string defaultFaceShapeId,
        string defaultHairStyleId,
        string defaultSkinToneId,
        string defaultHairColorId,
        string defaultEyeColorId,
        string defaultBrowStyleId,
        string defaultNoseStyleId,
        string defaultMouthStyleId,
        IReadOnlyList<Fo2FaceShapePreset> faceShapes,
        IReadOnlyList<Fo2HairStylePreset> hairStyles,
        IReadOnlyList<Fo2SkinTonePreset> skinTones,
        IReadOnlyList<Fo2AppearanceColorPreset> hairColors,
        IReadOnlyList<Fo2AppearanceColorPreset> eyeColors,
        IReadOnlyList<Fo2BrowStylePreset> browStyles,
        IReadOnlyList<Fo2NoseStylePreset> noseStyles,
        IReadOnlyList<Fo2MouthStylePreset> mouthStyles,
        Fo2LiveHeadProfile liveHead)
    {
        Sha256 = sha256;
        PortraitWidth = portraitWidth;
        PortraitHeight = portraitHeight;
        Background = background;
        Outline = outline;
        Feature = feature;
        PortraitBrowLeftX = portraitBrowLeftX;
        PortraitBrowRightX = portraitBrowRightX;
        PortraitBrowWidth = portraitBrowWidth;
        PortraitNoseY = portraitNoseY;
        PortraitMouthY = portraitMouthY;
        DefaultFaceShapeId = defaultFaceShapeId;
        DefaultHairStyleId = defaultHairStyleId;
        DefaultSkinToneId = defaultSkinToneId;
        DefaultHairColorId = defaultHairColorId;
        DefaultEyeColorId = defaultEyeColorId;
        DefaultBrowStyleId = defaultBrowStyleId;
        DefaultNoseStyleId = defaultNoseStyleId;
        DefaultMouthStyleId = defaultMouthStyleId;
        FaceShapes = faceShapes;
        HairStyles = hairStyles;
        SkinTones = skinTones;
        HairColors = hairColors;
        EyeColors = eyeColors;
        BrowStyles = browStyles;
        NoseStyles = noseStyles;
        MouthStyles = mouthStyles;
        FaceShapeIds = faceShapes.Select(row => row.Id).ToArray();
        HairStyleIds = hairStyles.Select(row => row.Id).ToArray();
        SkinToneIds = skinTones.Select(row => row.Id).ToArray();
        HairColorIds = hairColors.Select(row => row.Id).ToArray();
        EyeColorIds = eyeColors.Select(row => row.Id).ToArray();
        BrowStyleIds = browStyles.Select(row => row.Id).ToArray();
        NoseStyleIds = noseStyles.Select(row => row.Id).ToArray();
        MouthStyleIds = mouthStyles.Select(row => row.Id).ToArray();
        LiveHead = liveHead;
    }

    internal string Sha256 { get; }
    internal int PortraitWidth { get; }
    internal int PortraitHeight { get; }
    internal Color Background { get; }
    internal Color Outline { get; }
    internal Color Feature { get; }
    internal int PortraitBrowLeftX { get; }
    internal int PortraitBrowRightX { get; }
    internal int PortraitBrowWidth { get; }
    internal int PortraitNoseY { get; }
    internal int PortraitMouthY { get; }
    internal string DefaultFaceShapeId { get; }
    internal string DefaultHairStyleId { get; }
    internal string DefaultSkinToneId { get; }
    internal string DefaultHairColorId { get; }
    internal string DefaultEyeColorId { get; }
    internal string DefaultBrowStyleId { get; }
    internal string DefaultNoseStyleId { get; }
    internal string DefaultMouthStyleId { get; }
    internal IReadOnlyList<Fo2FaceShapePreset> FaceShapes { get; }
    internal IReadOnlyList<Fo2HairStylePreset> HairStyles { get; }
    internal IReadOnlyList<Fo2SkinTonePreset> SkinTones { get; }
    internal IReadOnlyList<Fo2AppearanceColorPreset> HairColors { get; }
    internal IReadOnlyList<Fo2AppearanceColorPreset> EyeColors { get; }
    internal IReadOnlyList<Fo2BrowStylePreset> BrowStyles { get; }
    internal IReadOnlyList<Fo2NoseStylePreset> NoseStyles { get; }
    internal IReadOnlyList<Fo2MouthStylePreset> MouthStyles { get; }
    internal IReadOnlyList<string> FaceShapeIds { get; }
    internal IReadOnlyList<string> HairStyleIds { get; }
    internal IReadOnlyList<string> SkinToneIds { get; }
    internal IReadOnlyList<string> HairColorIds { get; }
    internal IReadOnlyList<string> EyeColorIds { get; }
    internal IReadOnlyList<string> BrowStyleIds { get; }
    internal IReadOnlyList<string> NoseStyleIds { get; }
    internal IReadOnlyList<string> MouthStyleIds { get; }
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

    internal Fo2BrowStylePreset BrowStyle(string id) =>
        BrowStyles.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 brow style: {id}");

    internal Fo2NoseStylePreset NoseStyle(string id) =>
        NoseStyles.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 nose style: {id}");

    internal Fo2MouthStylePreset MouthStyle(string id) =>
        MouthStyles.SingleOrDefault(row => row.Id == id) ??
        throw new InvalidOperationException($"Unsupported Fallout 2 mouth style: {id}");

    internal IReadOnlyDictionary<string, float> NativeFaceGenControls(
        string faceShapeId,
        string browStyleId,
        string noseStyleId,
        string mouthStyleId)
    {
        var controls = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var source in new[]
                 {
                     Face(faceShapeId).NativeFaceGenControls,
                     BrowStyle(browStyleId).NativeFaceGenControls,
                     NoseStyle(noseStyleId).NativeFaceGenControls,
                     MouthStyle(mouthStyleId).NativeFaceGenControls,
                 })
        {
            foreach (var control in source)
            {
                if (!controls.TryAdd(control.Key, control.Value))
                    throw new InvalidOperationException(
                        $"Fallout 2 appearance presets overlap native FaceGen control " +
                        $"{control.Key}.");
            }
        }
        return controls;
    }

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
                "asset-free-local-semantics-projected-through-owned-fnv-facegen-non-parity" ||
            !root.GetProperty("unsupported").EnumerateArray().Any())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 procedural appearance recipe.");
        var faces = root.GetProperty("faceShapes").EnumerateArray().Select(row =>
            new Fo2FaceShapePreset(
                RequiredString(row, "id"),
                Positive(row, "halfWidth"),
                Positive(row, "halfHeight"),
                Finite(row, "taper"),
                ReadVector(row.GetProperty("headScale")),
                ReadNativeFaceGenControls(row))).ToArray();
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
        var browStyles = root.GetProperty("browStyles").EnumerateArray().Select(row =>
            new Fo2BrowStylePreset(
                RequiredString(row, "id"),
                NonNegativeInt(row, "portraitY"),
                NonNegativeInt(row, "portraitOuterOffset"),
                PositiveInt(row, "portraitThickness"),
                Finite(row, "liveY"),
                Finite(row, "liveRotationRadians"),
                Positive(row, "liveWidth"),
                Positive(row, "liveThickness"),
                ReadNativeFaceGenControls(row))).ToArray();
        var noseStyles = root.GetProperty("noseStyles").EnumerateArray().Select(row =>
            new Fo2NoseStylePreset(
                RequiredString(row, "id"),
                PositiveInt(row, "portraitWidth"),
                PositiveInt(row, "portraitHeight"),
                ReadVector(row.GetProperty("headScale")),
                ReadNativeFaceGenControls(row))).ToArray();
        var mouthStyles = root.GetProperty("mouthStyles").EnumerateArray().Select(row =>
            new Fo2MouthStylePreset(
                RequiredString(row, "id"),
                PositiveInt(row, "portraitWidth"),
                PositiveInt(row, "portraitThickness"),
                Positive(row, "liveWidth"),
                Positive(row, "liveHeight"),
                ReadNativeFaceGenControls(row))).ToArray();
        var profile = new Fo2ProceduralAppearanceCatalog(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            portrait.GetProperty("width").GetInt32(),
            portrait.GetProperty("height").GetInt32(),
            ReadHtmlColor(portrait, "background"),
            ReadHtmlColor(portrait, "outline"),
            ReadHtmlColor(portrait, "feature"),
            NonNegativeInt(portrait, "browLeftX"),
            NonNegativeInt(portrait, "browRightX"),
            PositiveInt(portrait, "browWidth"),
            NonNegativeInt(portrait, "noseY"),
            NonNegativeInt(portrait, "mouthY"),
            RequiredString(defaults, "faceShapeId"),
            RequiredString(defaults, "hairStyleId"),
            RequiredString(defaults, "skinToneId"),
            RequiredString(defaults, "hairColorId"),
            RequiredString(defaults, "eyeColorId"),
            RequiredString(defaults, "browStyleId"),
            RequiredString(defaults, "noseStyleId"),
            RequiredString(defaults, "mouthStyleId"),
            faces,
            hair,
            skin,
            hairColors,
            eyeColors,
            browStyles,
            noseStyles,
            mouthStyles,
            new Fo2LiveHeadProfile(
                ReadVector2I(live.GetProperty("viewport")),
                Positive(live, "headRadius"),
                Positive(live, "headHeight"),
                Positive(live, "browX"),
                Positive(live, "browZ"),
                Finite(live, "noseY"),
                Positive(live, "noseZ"),
                Finite(live, "mouthY"),
                Positive(live, "mouthZ"),
                Positive(live, "featureDepth"),
                Positive(live, "yawAmplitudeRadians"),
                Positive(live, "yawCyclesPerSecond"),
                Positive(live, "nativeMorphWeightScale")));
        if (profile.PortraitWidth != Fo2ProceduralPortrait.Width ||
            profile.PortraitHeight != Fo2ProceduralPortrait.Height ||
            profile.PortraitBrowLeftX + profile.PortraitBrowWidth >= profile.PortraitWidth ||
            profile.PortraitBrowRightX + profile.PortraitBrowWidth >= profile.PortraitWidth ||
            profile.PortraitNoseY >= profile.PortraitHeight ||
            profile.PortraitMouthY >= profile.PortraitHeight ||
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
            !browStyles.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.StraightBrow,
                    Fo2ProceduralPortrait.ArchedBrow,
                    Fo2ProceduralPortrait.HeavyBrow]) ||
            !noseStyles.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.NarrowNose,
                    Fo2ProceduralPortrait.StandardNose,
                    Fo2ProceduralPortrait.BroadNose]) ||
            !mouthStyles.Select(row => row.Id).SequenceEqual(
                [Fo2ProceduralPortrait.SmallMouth,
                    Fo2ProceduralPortrait.NeutralMouth,
                    Fo2ProceduralPortrait.WideMouth]) ||
            profile.DefaultFaceShapeId != Fo2ProceduralPortrait.OvalFace ||
            profile.DefaultHairStyleId != Fo2ProceduralPortrait.CroppedHair ||
            profile.DefaultSkinToneId != Fo2ProceduralPortrait.MediumSkin ||
            profile.DefaultHairColorId != Fo2ProceduralPortrait.BrownHairColor ||
            profile.DefaultEyeColorId != Fo2ProceduralPortrait.HazelEyeColor ||
            profile.DefaultBrowStyleId != Fo2ProceduralPortrait.StraightBrow ||
            profile.DefaultNoseStyleId != Fo2ProceduralPortrait.StandardNose ||
            profile.DefaultMouthStyleId != Fo2ProceduralPortrait.NeutralMouth ||
            !Mathf.IsEqualApprox(profile.LiveHead.NativeMorphWeightScale, 0.1f) ||
            profile.NativeFaceGenControls(
                profile.DefaultFaceShapeId,
                profile.DefaultBrowStyleId,
                profile.DefaultNoseStyleId,
                profile.DefaultMouthStyleId).Count != 0 ||
            faces.Sum(row => row.NativeFaceGenControls.Count) == 0 ||
            browStyles.Sum(row => row.NativeFaceGenControls.Count) == 0 ||
            noseStyles.Sum(row => row.NativeFaceGenControls.Count) == 0 ||
            mouthStyles.Sum(row => row.NativeFaceGenControls.Count) == 0 ||
            hair.Any(row => row.HairLineY < ZeroInteger || row.BottomY < row.HairLineY ||
                row.BottomY >= profile.PortraitHeight || row.SideInset < MinimumPositive ||
                row.SideLength < MinimumPositive ||
                row.SideMode is not NoSideHair and not RightSideHair and not BothSideHair) ||
            browStyles.Any(row => row.PortraitY + row.PortraitOuterOffset +
                    row.PortraitThickness >= profile.PortraitHeight) ||
            noseStyles.Any(row => row.PortraitWidth >= profile.PortraitWidth ||
                row.PortraitHeight >= profile.PortraitHeight) ||
            mouthStyles.Any(row => row.PortraitWidth >= profile.PortraitWidth ||
                row.PortraitThickness >= profile.PortraitHeight))
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

    private static IReadOnlyDictionary<string, float> ReadNativeFaceGenControls(
        JsonElement source)
    {
        var controls = source.GetProperty("nativeFaceGenControls")
            .EnumerateObject()
            .ToDictionary(
                row => row.Name,
                row => row.Value.GetSingle(),
                StringComparer.Ordinal);
        if (controls.Any(row =>
                !row.Key.StartsWith("sRSMShapeOption", StringComparison.Ordinal) ||
                row.Key.Length != "sRSMShapeOption00".Length ||
                !int.TryParse(row.Key[^2..], out var index) ||
                index is < 1 or > 55 ||
                !float.IsFinite(row.Value) ||
                row.Value is < -50.0f or > 50.0f))
            throw new InvalidOperationException(
                "Fallout 2 native FaceGen appearance projection is invalid.");
        return controls;
    }

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

    private static int NonNegativeInt(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetInt32();
        return value >= ZeroInteger
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 appearance recipe integer is negative: {property}");
    }

    private static int PositiveInt(JsonElement source, string property)
    {
        var value = NonNegativeInt(source, property);
        return value > ZeroInteger
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 appearance recipe integer is not positive: {property}");
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
