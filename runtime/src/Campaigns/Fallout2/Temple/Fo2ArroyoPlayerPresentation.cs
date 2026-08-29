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

internal sealed record Fo2ArroyoPlayerAnimation(
    string Code,
    string LogicalPath,
    string SourceSha256,
    int FramesPerSecond,
    int ActionFrame,
    IReadOnlyDictionary<int, IReadOnlyList<Fo2ArroyoPlayerFrame>> Directions);

internal sealed record Fo2ArroyoPlayerPresentationSource(
    string SourceProfileId,
    string NodeName,
    string Fid,
    string PrototypePid,
    string PrototypeLogicalPath,
    string PrototypeSha256,
    string LogicalPath,
    string SourceSha256,
    IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> Directions,
    Fo2ArroyoPlayerAnimation Walk);

internal sealed class Fo2ArroyoPlayerPresentationCatalog
{
    private const string CacheSchema = "opennv-fo2-player-presentation-cache/v1";
    private const string RecipeSchema = "opennv-fo2-player-presentation-recipe/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    internal const string ExpectedRecipeId = "fo2-arroyo-player-presentation-v1";
    internal const string ExpectedFid = "0100003e";
    internal const string ExpectedLogicalPath = "art\\critters\\hmwarraa.frm";
    internal const string ExpectedWalkLogicalPath = "art\\critters\\hmwarrab.frm";
    internal const string ExpectedPrototypeLogicalPath = "proto\\critters\\00000001.pro";
    internal const string ExpectedPrototypePid = "01000001";
    internal const int ExpectedArtIndex = 62;
    internal const int IdleFrame = 0;
    internal const int WalkFramesPerDirection = 8;
    internal const int WalkFramesPerSecond = 10;

    private Fo2ArroyoPlayerPresentationCatalog(
        string manifestPath,
        string manifestSha256,
        string sourceProfileId,
        string recipeSha256,
        string critterListSha256,
        string prototypeSha256,
        string sourceSha256,
        string walkSourceSha256,
        int storedFps,
        int framesPerDirection,
        IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> directions,
        Fo2ArroyoPlayerAnimation walk,
        int verifiedResources)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceProfileId = sourceProfileId;
        RecipeSha256 = recipeSha256;
        CritterListSha256 = critterListSha256;
        PrototypeSha256 = prototypeSha256;
        SourceSha256 = sourceSha256;
        WalkSourceSha256 = walkSourceSha256;
        StoredFps = storedFps;
        FramesPerDirection = framesPerDirection;
        Directions = directions;
        Walk = walk;
        VerifiedResources = verifiedResources;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string RecipeSha256 { get; }
    internal string CritterListSha256 { get; }
    internal string PrototypeSha256 { get; }
    internal string SourceSha256 { get; }
    internal string WalkSourceSha256 { get; }
    internal int StoredFps { get; }
    internal int FramesPerDirection { get; }
    internal IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> Directions { get; }
    internal Fo2ArroyoPlayerAnimation Walk { get; }
    internal int VerifiedResources { get; }
    internal Fo2ArroyoPlayerPresentationSource Source => new(
        SourceProfileId,
        "CHOSEN_ONE_OWNED_HMWARR_DIRECTIONAL_FRM",
        ExpectedFid,
        ExpectedPrototypePid,
        ExpectedPrototypeLogicalPath,
        PrototypeSha256,
        ExpectedLogicalPath,
        SourceSha256,
        Directions,
        Walk);

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
                Fo2TemplePresentationCatalog.RequiredString(
                    player,
                    "prototypeListLogicalPath") != "proto\\critters\\critters.lst" ||
                player.GetProperty("prototypeListIndex").GetInt32() != 1 ||
                Fo2TemplePresentationCatalog.RequiredString(
                    player,
                    "prototypeListEntry") != "00000001.pro" ||
                Fo2TemplePresentationCatalog.RequiredString(
                    player,
                    "prototypeLogicalPath") != ExpectedPrototypeLogicalPath ||
                Fo2TemplePresentationCatalog.RequiredString(
                    player,
                    "prototypePid") != ExpectedPrototypePid ||
                Fo2TemplePresentationCatalog.RequiredString(player, "idleFrmLogicalPath") !=
                    ExpectedLogicalPath ||
                player.GetProperty("frame").GetInt32() != IdleFrame ||
                !directions.SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
                Fo2TemplePresentationCatalog.RequiredString(
                    player,
                    "walkAnimationCode") != "AB" ||
                Fo2TemplePresentationCatalog.RequiredString(
                    player,
                    "walkFrmLogicalPath") != ExpectedWalkLogicalPath ||
                !player.GetProperty("walkFrames").EnumerateArray()
                    .Select(row => row.GetInt32())
                    .SequenceEqual(Enumerable.Range(0, WalkFramesPerDirection)) ||
                player.GetProperty("walkFps").GetInt32() != WalkFramesPerSecond)
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

        var prototype = cache.GetProperty("prototype");
        var prototypeSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            prototype,
            "sha256");
        var prototypeListSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            prototype,
            "listSha256");
        if (Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "listLogicalPath") != "proto\\critters\\critters.lst" ||
            prototype.GetProperty("listIndex").GetInt32() != 1 ||
            Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "listEntry") != "00000001.pro" ||
            Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "logicalPath") != ExpectedPrototypeLogicalPath ||
            Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "pid") != ExpectedPrototypePid ||
            Fo2TemplePresentationCatalog.RequiredString(prototype, "fid") != ExpectedFid ||
            prototype.GetProperty("bytes").GetInt64() <= 0)
            throw new InvalidOperationException("Fallout 2 player PRO/FID binding drifted.");

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

        var walkArt = cache.GetProperty("walkArt");
        var walkSourceSha256 = Fo2TemplePresentationCatalog.RequiredHash(walkArt, "sha256");
        if (Fo2TemplePresentationCatalog.RequiredString(walkArt, "fid") != ExpectedFid ||
            Fo2TemplePresentationCatalog.RequiredString(
                walkArt,
                "animationCode") != "AB" ||
            Fo2TemplePresentationCatalog.RequiredString(
                walkArt,
                "logicalPath") != ExpectedWalkLogicalPath ||
            walkArt.GetProperty("bytes").GetInt64() <= 0 ||
            walkArt.GetProperty("fps").GetInt32() != WalkFramesPerSecond ||
            walkArt.GetProperty("actionFrame").GetInt32() != 0 ||
            walkArt.GetProperty("framesPerDirection").GetInt32() !=
                WalkFramesPerDirection ||
            walkArt.GetProperty("decodedDirections").GetInt32() !=
                Fo1HexMath.DirectionCount ||
            !walkArt.GetProperty("admittedFrames").EnumerateArray()
                .Select(row => row.GetInt32())
                .SequenceEqual(Enumerable.Range(0, WalkFramesPerDirection)) ||
            !walkArt.GetProperty("admittedDirections").EnumerateArray()
                .Select(row => row.GetInt32())
                .SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            !walkArt.GetProperty("animationPlayback").GetBoolean())
            throw new InvalidOperationException("Fallout 2 player walk-art binding drifted.");

        var frames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
        var walkFrames = new Dictionary<int, Dictionary<int, Fo2ArroyoPlayerFrame>>();
        var artifactRows = cache.GetProperty("artifacts").EnumerateArray().ToArray();
        foreach (var row in artifactRows)
        {
            var id = Fo2TemplePresentationCatalog.RequiredString(row, "id");
            var kind = Fo2TemplePresentationCatalog.RequiredString(row, "kind");
            var frame = LoadFrame(row, cacheRoot, $"Fallout 2 player PNG {id}");
            if (kind == "player")
            {
                if (frame.LogicalPath != ExpectedLogicalPath ||
                    frame.SourceSha256 != sourceSha256 ||
                    frame.Frame != IdleFrame ||
                    frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                    frame.Width <= 0 || frame.Height <= 0 ||
                    !frames.TryAdd(frame.Direction, frame))
                    throw new InvalidOperationException(
                        $"Fallout 2 player direction artifact is invalid: {id}");
                continue;
            }
            if (kind != "player-walk" ||
                frame.LogicalPath != ExpectedWalkLogicalPath ||
                frame.SourceSha256 != walkSourceSha256 ||
                frame.Frame is < 0 or >= WalkFramesPerDirection ||
                frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                frame.Width <= 0 || frame.Height <= 0)
                throw new InvalidOperationException(
                    $"Fallout 2 player walk artifact is invalid: {id}");
            if (!walkFrames.TryGetValue(frame.Direction, out var directionFrames))
            {
                directionFrames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
                walkFrames.Add(frame.Direction, directionFrames);
            }
            if (!directionFrames.TryAdd(frame.Frame, frame))
                throw new InvalidOperationException(
                    $"Duplicate Fallout 2 player walk artifact: {id}");
        }
        if (!frames.Keys.Order().SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)))
            throw new InvalidOperationException(
                "Fallout 2 player cache does not contain exactly six idle directions.");
        var walkDirections = walkFrames.ToDictionary(
            row => row.Key,
            row => (IReadOnlyList<Fo2ArroyoPlayerFrame>)row.Value
                .OrderBy(frame => frame.Key).Select(frame => frame.Value).ToArray());
        if (!walkDirections.Keys.Order().SequenceEqual(
                Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            walkDirections.Values.Any(direction =>
                direction.Count != WalkFramesPerDirection ||
                !direction.Select(frame => frame.Frame).SequenceEqual(
                    Enumerable.Range(0, WalkFramesPerDirection))))
            throw new InvalidOperationException(
                "Fallout 2 player cache does not contain the exact 6x8 AB walk cycle.");
        var walk = new Fo2ArroyoPlayerAnimation(
            "AB",
            ExpectedWalkLogicalPath,
            walkSourceSha256,
            walkArt.GetProperty("fps").GetInt32(),
            walkArt.GetProperty("actionFrame").GetInt32(),
            walkDirections);

        var resources = cache.GetProperty("resources").EnumerateArray().ToArray();
        var resourceIdentities = resources.Select(row =>
            $"{Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath")}|" +
            Fo2TemplePresentationCatalog.RequiredHash(row, "sha256"))
            .ToHashSet(StringComparer.Ordinal);
        if (resourceIdentities.Count != resources.Length ||
            !resourceIdentities.Contains($"art\\critters\\critters.lst|{critterListSha256}") ||
            !resourceIdentities.Contains(
                $"proto\\critters\\critters.lst|{prototypeListSha256}") ||
            !resourceIdentities.Contains(
                $"{ExpectedPrototypeLogicalPath}|{prototypeSha256}") ||
            !resourceIdentities.Contains($"{ExpectedLogicalPath}|{sourceSha256}") ||
            !resourceIdentities.Contains($"{ExpectedWalkLogicalPath}|{walkSourceSha256}") ||
            !resourceIdentities.Any(row => row.StartsWith("color.pal|", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Fallout 2 player cache resource identity closure failed.");
        var counts = cache.GetProperty("counts");
        if (counts.GetProperty("sourceResources").GetInt32() != resources.Length ||
            counts.GetProperty("idleDirectionArtifacts").GetInt32() != frames.Count ||
            counts.GetProperty("walkFrameArtifacts").GetInt32() !=
                WalkFramesPerDirection * Fo1HexMath.DirectionCount ||
            artifactRows.Length != frames.Count + walkDirections.Values.Sum(row => row.Count))
            throw new InvalidOperationException("Fallout 2 player cache counts drifted.");

        return new Fo2ArroyoPlayerPresentationCatalog(
            manifestPath,
            Fo2TemplePresentationCatalog.Sha256(cacheBytes),
            expectedSourceProfileId,
            recipeSha256,
            critterListSha256,
            prototypeSha256,
            sourceSha256,
            walkSourceSha256,
            idle.GetProperty("fps").GetInt32(),
            idle.GetProperty("framesPerDirection").GetInt32(),
            frames,
            walk,
            resources.Length);
    }

    internal static Fo2ArroyoPlayerFrame LoadFrame(
        JsonElement row,
        string cacheRoot,
        string label)
    {
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
        Fo2TemplePresentationCatalog.VerifyFile(path, pngSha256, pngBytes, label);
        return new Fo2ArroyoPlayerFrame(
            Fo2TemplePresentationCatalog.RequiredString(row, "id"),
            Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath"),
            path,
            Fo2TemplePresentationCatalog.RequiredHash(row, "sourceSha256"),
            pngSha256,
            pngBytes,
            row.GetProperty("width").GetInt32(),
            row.GetProperty("height").GetInt32(),
            row.GetProperty("rotation").GetInt32(),
            row.GetProperty("frame").GetInt32(),
            Fo2TemplePresentationCatalog.ReadVector2I(row.GetProperty("directionOffset")),
            Fo2TemplePresentationCatalog.ReadVector2I(row.GetProperty("frameOffset")));
    }
}

internal sealed partial class Fo2ArroyoPlayerPresentation : Sprite3D
{
    private readonly IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> _idleFrames;
    private readonly IReadOnlyDictionary<int, Texture2D> _idleTextures;
    private readonly Fo2ArroyoPlayerAnimation _walk;
    private readonly IReadOnlyDictionary<(int Direction, int Frame), Texture2D> _walkTextures;
    private Fo2ArroyoPlayerFrame _currentFrame = null!;
    private double _walkFrameAccumulator;
    private int _walkFrameIndex;

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
        _idleFrames = source.Directions;
        _idleTextures = source.Directions.ToDictionary(
            row => row.Key,
            row => LoadTexture(row.Value));
        _walk = source.Walk;
        if (!_walk.Directions.Keys.Order().SequenceEqual(
                Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            _walk.Directions.Values.Any(frames =>
                frames.Count != Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection))
            throw new InvalidOperationException(
                "Fallout 2 player AB walk presentation coverage drifted.");
        _walkTextures = _walk.Directions.SelectMany(direction =>
                direction.Value.Select(frame =>
                    new KeyValuePair<(int Direction, int Frame), Texture2D>(
                        (direction.Key, frame.Frame),
                        LoadTexture(frame))))
            .ToDictionary(row => row.Key, row => row.Value);
        Name = source.NodeName;
        PixelSize = 1.0f / sourcePixelsPerMeter;
        Position = Vector3.Down * spawnCenterHeightMeters;
        Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
        Shaded = false;
        DoubleSided = true;
        AlphaCut = AlphaCutMode.OpaquePrepass;
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        SetMeta("fid", source.Fid);
        SetMeta("prototype_pid", source.PrototypePid);
        SetMeta("prototype_logical_path", source.PrototypeLogicalPath);
        SetMeta("prototype_sha256", source.PrototypeSha256);
        SetMeta("logical_path", source.LogicalPath);
        SetMeta("source_sha256", source.SourceSha256);
        SetMeta("walk_logical_path", source.Walk.LogicalPath);
        SetMeta("walk_source_sha256", source.Walk.SourceSha256);
        SetMeta("walk_fps", source.Walk.FramesPerSecond);
        SetMeta("animation_playback", false);
        SetDirection(initialDirection);
    }

    internal int Direction { get; private set; }
    internal bool IsWalking { get; private set; }
    internal int WalkFrameAdvances { get; private set; }
    internal int CompletedWalkCycles { get; private set; }
    internal int AnimationFrame => _currentFrame.Frame;
    internal string AnimationCode => IsWalking ? _walk.Code : "AA";
    internal Fo2ArroyoPlayerFrame CurrentFrame => _currentFrame;

    internal void SetDirection(int direction)
    {
        if (!_idleFrames.TryGetValue(direction, out var frame) ||
            !_idleTextures.TryGetValue(direction, out var texture))
            throw new InvalidOperationException(
                $"Fallout 2 player source direction is unavailable: {direction}");
        Direction = direction;
        IsWalking = false;
        _walkFrameAccumulator = 0.0;
        _walkFrameIndex = 0;
        ApplyFrame(frame, texture);
    }

    internal void StartWalking(int direction)
    {
        if (!_walk.Directions.TryGetValue(direction, out var frames) || frames.Count == 0)
            throw new InvalidOperationException(
                $"Fallout 2 player walk direction is unavailable: {direction}");
        if (IsWalking && Direction == direction)
            return;
        Direction = direction;
        IsWalking = true;
        _walkFrameAccumulator = 0.0;
        _walkFrameIndex = 0;
        var frame = frames[0];
        ApplyFrame(frame, _walkTextures[(direction, frame.Frame)]);
    }

    internal void StopWalking()
    {
        if (IsWalking)
            SetDirection(Direction);
    }

    public override void _Process(double delta)
    {
        if (!IsWalking)
            return;
        _walkFrameAccumulator += delta;
        var frameDuration = 1.0 / _walk.FramesPerSecond;
        var frames = _walk.Directions[Direction];
        while (_walkFrameAccumulator >= frameDuration)
        {
            _walkFrameAccumulator -= frameDuration;
            _walkFrameIndex = (_walkFrameIndex + 1) % frames.Count;
            WalkFrameAdvances++;
            if (_walkFrameIndex == 0)
                CompletedWalkCycles++;
            var frame = frames[_walkFrameIndex];
            ApplyFrame(frame, _walkTextures[(Direction, frame.Frame)]);
        }
    }

    private void ApplyFrame(Fo2ArroyoPlayerFrame frame, Texture2D texture)
    {
        _currentFrame = frame;
        Texture = texture;
        Offset = new Vector2(
            frame.FrameOffset.X,
            -frame.FrameOffset.Y + frame.Height / 2.0f);
        SetMeta("source_direction", Direction);
        SetMeta("animation_code", AnimationCode);
        SetMeta("animation_frame", frame.Frame);
        SetMeta("animation_playback", IsWalking);
        SetMeta("frame_logical_path", frame.LogicalPath);
        SetMeta("frame_source_sha256", frame.SourceSha256);
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
