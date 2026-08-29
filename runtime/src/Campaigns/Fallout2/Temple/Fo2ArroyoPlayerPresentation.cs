using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoPlayerFrame(
    string Id,
    string LogicalPath,
    string Path,
    string SourceSha256,
    string PngSha256,
    long PngBytes,
    int Width,
    int Height,
    int Direction,
    int Frame,
    Vector2I DirectionOffset,
    Vector2I FrameOffset);

internal sealed record Fo2ArroyoPlayerPresentationSource(
    string SourceProfileId,
    string NodeName,
    string Fid,
    string LogicalPath,
    string SourceSha256,
    IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> Directions);

internal sealed class Fo2ArroyoPlayerPresentationCatalog
{
    private const string CacheSchema = "opennv-fo2-player-presentation-cache/v1";
    private const string RecipeSchema = "opennv-fo2-player-presentation-recipe/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    internal const string ExpectedRecipeId = "fo2-arroyo-player-presentation-v1";
    internal const string ExpectedFid = "0100003e";
    internal const string ExpectedLogicalPath = "art\\critters\\hmwarraa.frm";
    internal const int ExpectedArtIndex = 62;
    internal const int IdleFrame = 0;

    private Fo2ArroyoPlayerPresentationCatalog(
        string manifestPath,
        string manifestSha256,
        string sourceProfileId,
        string recipeSha256,
        string critterListSha256,
        string sourceSha256,
        int storedFps,
        int framesPerDirection,
        IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> directions,
        int verifiedResources)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceProfileId = sourceProfileId;
        RecipeSha256 = recipeSha256;
        CritterListSha256 = critterListSha256;
        SourceSha256 = sourceSha256;
        StoredFps = storedFps;
        FramesPerDirection = framesPerDirection;
        Directions = directions;
        VerifiedResources = verifiedResources;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string RecipeSha256 { get; }
    internal string CritterListSha256 { get; }
    internal string SourceSha256 { get; }
    internal int StoredFps { get; }
    internal int FramesPerDirection { get; }
    internal IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> Directions { get; }
    internal int VerifiedResources { get; }
    internal Fo2ArroyoPlayerPresentationSource Source => new(
        SourceProfileId,
        "CHOSEN_ONE_OWNED_HMWARR_IDLE_FRAME_ZERO",
        ExpectedFid,
        ExpectedLogicalPath,
        SourceSha256,
        Directions);

    internal static Fo2ArroyoPlayerPresentationCatalog Load(
        string cacheManifestPath,
        string expectedSourceProfileId)
    {
        var manifestPath = Fo2TemplePresentationCatalog.ResolvePath(
            cacheManifestPath,
            Directory.GetCurrentDirectory());
        var cacheBytes = File.ReadAllBytes(manifestPath);
        using var cacheDocument = JsonDocument.Parse(cacheBytes);
        var cache = cacheDocument.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(cache, "schema") != CacheSchema ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "status") !=
                "decoded-disposable-local-cache" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "campaign") != "Fallout2" ||
            Fo2TemplePresentationCatalog.RequiredString(cache, "slice") !=
                "ArroyoCavesPlayer" ||
            cache.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
            cache.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
            cache.GetProperty("cachePolicy").GetProperty("distributionAllowed").GetBoolean() ||
            !cache.GetProperty("cachePolicy").GetProperty("containsDerivedOwnedPixels")
                .GetBoolean() ||
            !cache.GetProperty("promotion").GetProperty("transported").GetBoolean() ||
            !cache.GetProperty("promotion").GetProperty("decodedPresentationAssets")
                .GetBoolean() ||
            cache.GetProperty("promotion").GetProperty("rendered").GetBoolean() ||
            cache.GetProperty("promotion").GetProperty("interactive").GetBoolean())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 Arroyo player presentation cache.");
        var cacheRoot = Path.GetDirectoryName(manifestPath)!;

        var profileDescriptor = cache.GetProperty("sourceProfile");
        var profilePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(profileDescriptor, "file"),
            cacheRoot);
        var profileBytes = Fo2TemplePresentationCatalog.VerifyFile(
            profilePath,
            Fo2TemplePresentationCatalog.RequiredHash(profileDescriptor, "sha256"),
            null,
            "Fallout 2 player owned profile");
        using (var profileDocument = JsonDocument.Parse(profileBytes))
        {
            var profile = profileDocument.RootElement;
            if (Fo2TemplePresentationCatalog.RequiredString(profileDescriptor, "schema") !=
                    ProfileSchema ||
                Fo2TemplePresentationCatalog.RequiredString(profile, "schema") != ProfileSchema ||
                Fo2TemplePresentationCatalog.RequiredString(profile, "status") !=
                    "registered-owned-install" ||
                Fo2TemplePresentationCatalog.RequiredString(profile, "campaign") != "Fallout2" ||
                profile.GetProperty("runtimeCompatibility").GetProperty("ready").GetBoolean() ||
                profile.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean() ||
                Fo2TemplePresentationCatalog.RequiredString(profile, "sourceProfileId") !=
                    expectedSourceProfileId ||
                Fo2TemplePresentationCatalog.RequiredString(
                    profileDescriptor,
                    "sourceProfileId") != expectedSourceProfileId)
                throw new InvalidOperationException(
                    "Fallout 2 player owned-profile binding drifted.");
        }

        var recipeDescriptor = cache.GetProperty("recipe");
        var recipePath = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "file"),
            cacheRoot);
        var recipeSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            recipeDescriptor,
            "sha256");
        var recipeBytes = Fo2TemplePresentationCatalog.VerifyFile(
            recipePath,
            recipeSha256,
            null,
            "Fallout 2 player presentation recipe");
        using (var recipeDocument = JsonDocument.Parse(recipeBytes))
        {
            var recipe = recipeDocument.RootElement;
            var player = recipe.GetProperty("player");
            var directions = player.GetProperty("directions")
                .EnumerateArray().Select(row => row.GetInt32()).ToArray();
            if (Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "schema") !=
                    RecipeSchema ||
                Fo2TemplePresentationCatalog.RequiredString(recipeDescriptor, "id") !=
                    ExpectedRecipeId ||
                Fo2TemplePresentationCatalog.RequiredString(recipe, "schema") != RecipeSchema ||
                Fo2TemplePresentationCatalog.RequiredString(recipe, "id") != ExpectedRecipeId ||
                Fo2TemplePresentationCatalog.RequiredString(recipe, "campaign") != "Fallout2" ||
                Fo2TemplePresentationCatalog.RequiredString(recipe, "sourceProfileSchema") !=
                    ProfileSchema ||
                Fo2TemplePresentationCatalog.RequiredString(
                    player,
                    "critterListLogicalPath") != "art\\critters\\critters.lst" ||
                player.GetProperty("artIndex").GetInt32() != ExpectedArtIndex ||
                Fo2TemplePresentationCatalog.RequiredString(player, "artListEntry") !=
                    "hmwarr,11,1" ||
                player.GetProperty("objectType").GetInt32() != 1 ||
                Fo2TemplePresentationCatalog.RequiredString(player, "fid") != ExpectedFid ||
                Fo2TemplePresentationCatalog.RequiredString(player, "idleFrmLogicalPath") !=
                    ExpectedLogicalPath ||
                player.GetProperty("frame").GetInt32() != IdleFrame ||
                !directions.SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)))
                throw new InvalidOperationException(
                    "Fallout 2 player presentation recipe binding drifted.");
        }

        var critterList = cache.GetProperty("critterList");
        var critterListSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            critterList,
            "sha256");
        if (Fo2TemplePresentationCatalog.RequiredString(critterList, "logicalPath") !=
                "art\\critters\\critters.lst" ||
            critterList.GetProperty("entries").GetInt32() <= ExpectedArtIndex ||
            critterList.GetProperty("artIndex").GetInt32() != ExpectedArtIndex ||
            Fo2TemplePresentationCatalog.RequiredString(critterList, "entry") !=
                "hmwarr,11,1")
            throw new InvalidOperationException("Fallout 2 player critter-list binding drifted.");

        var idle = cache.GetProperty("idleArt");
        var sourceSha256 = Fo2TemplePresentationCatalog.RequiredHash(idle, "sha256");
        var admittedDirections = idle.GetProperty("admittedDirections")
            .EnumerateArray().Select(row => row.GetInt32()).ToArray();
        if (Fo2TemplePresentationCatalog.RequiredString(idle, "fid") != ExpectedFid ||
            Fo2TemplePresentationCatalog.RequiredString(idle, "logicalPath") !=
                ExpectedLogicalPath ||
            idle.GetProperty("bytes").GetInt64() <= 0 ||
            idle.GetProperty("fps").GetInt32() <= 0 ||
            idle.GetProperty("framesPerDirection").GetInt32() <= IdleFrame ||
            idle.GetProperty("decodedDirections").GetInt32() != Fo1HexMath.DirectionCount ||
            idle.GetProperty("admittedFrame").GetInt32() != IdleFrame ||
            !admittedDirections.SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            idle.GetProperty("animationPlayback").GetBoolean())
            throw new InvalidOperationException("Fallout 2 player idle-art binding drifted.");

        var frames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
        foreach (var row in cache.GetProperty("artifacts").EnumerateArray())
        {
            var id = Fo2TemplePresentationCatalog.RequiredString(row, "id");
            var direction = row.GetProperty("rotation").GetInt32();
            var relativePath = Fo2TemplePresentationCatalog.RequiredString(row, "png");
            if (Path.IsPathRooted(relativePath))
                throw new InvalidOperationException("Fallout 2 player PNG path must be cache-relative.");
            var path = Path.GetFullPath(Path.Combine(
                cacheRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(
                    cacheRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fallout 2 player PNG path escapes its cache.");
            var pngBytes = row.GetProperty("pngBytes").GetInt64();
            var pngSha256 = Fo2TemplePresentationCatalog.RequiredHash(row, "pngSha256");
            Fo2TemplePresentationCatalog.VerifyFile(
                path,
                pngSha256,
                pngBytes,
                $"Fallout 2 player PNG direction {direction}");
            var frame = new Fo2ArroyoPlayerFrame(
                id,
                Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath"),
                path,
                Fo2TemplePresentationCatalog.RequiredHash(row, "sourceSha256"),
                pngSha256,
                pngBytes,
                row.GetProperty("width").GetInt32(),
                row.GetProperty("height").GetInt32(),
                direction,
                row.GetProperty("frame").GetInt32(),
                Fo2TemplePresentationCatalog.ReadVector2I(row.GetProperty("directionOffset")),
                Fo2TemplePresentationCatalog.ReadVector2I(row.GetProperty("frameOffset")));
            if (Fo2TemplePresentationCatalog.RequiredString(row, "kind") != "player" ||
                frame.LogicalPath != ExpectedLogicalPath ||
                frame.SourceSha256 != sourceSha256 ||
                frame.Frame != IdleFrame ||
                frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                frame.Width <= 0 || frame.Height <= 0 ||
                !frames.TryAdd(frame.Direction, frame))
                throw new InvalidOperationException(
                    $"Fallout 2 player direction artifact is invalid: {id}");
        }
        if (!frames.Keys.Order().SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)))
            throw new InvalidOperationException(
                "Fallout 2 player cache does not contain exactly six idle directions.");

        var resources = cache.GetProperty("resources").EnumerateArray().ToArray();
        var resourceIdentities = resources.Select(row =>
            $"{Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath")}|" +
            Fo2TemplePresentationCatalog.RequiredHash(row, "sha256"))
            .ToHashSet(StringComparer.Ordinal);
        if (resourceIdentities.Count != resources.Length ||
            !resourceIdentities.Contains($"art\\critters\\critters.lst|{critterListSha256}") ||
            !resourceIdentities.Contains($"{ExpectedLogicalPath}|{sourceSha256}") ||
            !resourceIdentities.Any(row => row.StartsWith("color.pal|", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Fallout 2 player cache resource identity closure failed.");
        var counts = cache.GetProperty("counts");
        if (counts.GetProperty("sourceResources").GetInt32() != resources.Length ||
            counts.GetProperty("idleDirectionArtifacts").GetInt32() != frames.Count)
            throw new InvalidOperationException("Fallout 2 player cache counts drifted.");

        return new Fo2ArroyoPlayerPresentationCatalog(
            manifestPath,
            Fo2TemplePresentationCatalog.Sha256(cacheBytes),
            expectedSourceProfileId,
            recipeSha256,
            critterListSha256,
            sourceSha256,
            idle.GetProperty("fps").GetInt32(),
            idle.GetProperty("framesPerDirection").GetInt32(),
            frames,
            resources.Length);
    }
}

internal sealed partial class Fo2ArroyoPlayerPresentation : Sprite3D
{
    private readonly IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> _frames;
    private readonly IReadOnlyDictionary<int, Texture2D> _textures;

    internal Fo2ArroyoPlayerPresentation(
        Fo2ArroyoPlayerPresentationSource source,
        float sourcePixelsPerMeter,
        float spawnCenterHeightMeters,
        int initialDirection)
    {
        if (!float.IsFinite(sourcePixelsPerMeter) || sourcePixelsPerMeter <= 0.0f ||
            !float.IsFinite(spawnCenterHeightMeters) || spawnCenterHeightMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 2 player source scale or floor anchor is invalid.");
        _frames = source.Directions;
        _textures = source.Directions.ToDictionary(
            row => row.Key,
            row => LoadTexture(row.Value));
        Name = source.NodeName;
        PixelSize = 1.0f / sourcePixelsPerMeter;
        Position = Vector3.Down * spawnCenterHeightMeters;
        Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
        Shaded = false;
        DoubleSided = true;
        AlphaCut = AlphaCutMode.OpaquePrepass;
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        SetMeta("fid", source.Fid);
        SetMeta("logical_path", source.LogicalPath);
        SetMeta("source_sha256", source.SourceSha256);
        SetMeta("animation_playback", false);
        SetDirection(initialDirection);
    }

    internal int Direction { get; private set; }
    internal Fo2ArroyoPlayerFrame CurrentFrame => _frames[Direction];

    internal void SetDirection(int direction)
    {
        if (!_frames.TryGetValue(direction, out var frame) ||
            !_textures.TryGetValue(direction, out var texture))
            throw new InvalidOperationException(
                $"Fallout 2 player source direction is unavailable: {direction}");
        Direction = direction;
        Texture = texture;
        Offset = new Vector2(
            frame.FrameOffset.X,
            -frame.FrameOffset.Y + frame.Height / 2.0f);
        SetMeta("source_direction", direction);
        SetMeta("png_sha256", frame.PngSha256);
    }

    private static Texture2D LoadTexture(Fo2ArroyoPlayerFrame frame)
    {
        var image = Image.LoadFromFile(frame.Path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != frame.Width || image.GetHeight() != frame.Height)
            throw new InvalidOperationException(
                $"Fallout 2 player PNG dimensions drifted: {frame.Path}");
        return ImageTexture.CreateFromImage(image);
    }
}
