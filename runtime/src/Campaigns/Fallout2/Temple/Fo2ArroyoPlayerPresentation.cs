using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout1;

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
    Vector2I FrameOffset,
    Fo2FrmReliefArtifact Relief);

internal sealed record Fo2ArroyoPlayerAnimation(
    string Code,
    string LogicalPath,
    string SourceSha256,
    int FramesPerSecond,
    int ActionFrame,
    IReadOnlyDictionary<int, IReadOnlyList<Fo2ArroyoPlayerFrame>> Directions);

internal sealed record Fo2ArroyoEquippedWeaponPresentation(
    string ItemFid,
    string ItemPid,
    int WeaponAnimationCode,
    string WeaponArtSuffix,
    string GeometryDisposition,
    IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> IdleDirections,
    Fo2ArroyoPlayerAnimation Walk);

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
    Fo2ArroyoPlayerAnimation Walk,
    Fo2ArroyoEquippedWeaponPresentation EquippedWeapon,
    float ReliefDepthMeters,
    float ReliefSideRoughness);

internal sealed class Fo2ArroyoPlayerPresentationCatalog
{
    private const string CacheSchema = "opennv-fo2-player-presentation-cache/v1";
    private const string RecipeSchema = "opennv-fo2-player-presentation-recipe/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    internal const string ExpectedRecipeId = "fo2-arroyo-player-presentation-v1";
    internal const string ExpectedFid = "0100003e";
    internal const string ExpectedLogicalPath = "art\\critters\\hmwarraa.frm";
    internal const string ExpectedWalkLogicalPath = "art\\critters\\hmwarrab.frm";
    internal const string ExpectedEquippedIdleLogicalPath = "art\\critters\\hmwarrga.frm";
    internal const string ExpectedEquippedWalkLogicalPath = "art\\critters\\hmwarrgb.frm";
    internal const string ExpectedEquippedItemFid = "0000002a";
    internal const string ExpectedEquippedItemPid = "00000007";
    internal const string EquippedGeometryDisposition =
        "owned-critter-frm-composites-player-and-spear-no-separable-3d-weapon-transform";
    internal const int ExpectedWeaponAnimationCode = 4;
    internal const string ExpectedPrototypeLogicalPath = "proto\\critters\\00000001.pro";
    internal const string ExpectedPrototypePid = "01000001";
    internal const string Live3DPresentationSchema =
        "opennv-classic-humanoid-role-donor/v1";
    internal const string Live3DPresentationAuthority =
        "fo2-source-role-to-owned-fnv-presentation-donor";
    internal const string ExpectedLive3DPresentationOutfitFormId = "0003307c";
    internal const int ExpectedArtIndex = 62;
    internal const int IdleFrame = 0;
    internal const int WalkFramesPerDirection = 8;
    internal const int WalkFramesPerSecond = 10;
    internal const float ExpectedReliefDepthMeters = 0.12f;
    internal const float ExpectedReliefSideRoughness = 0.86f;
    private const string ReliefSchema = "opennv-fo2-frm-alpha-relief/v3";
    private const string ReliefMode = "exact-frm-alpha-island-molded-relief-v2";

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
        Fo2ArroyoEquippedWeaponPresentation equippedWeapon,
        string live3DPresentationOutfitFormId,
        float reliefDepthMeters,
        float reliefSideRoughness,
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
        EquippedWeapon = equippedWeapon;
        Live3DPresentationOutfitFormId = live3DPresentationOutfitFormId;
        ReliefDepthMeters = reliefDepthMeters;
        ReliefSideRoughness = reliefSideRoughness;
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
    internal Fo2ArroyoEquippedWeaponPresentation EquippedWeapon { get; }
    internal string Live3DPresentationOutfitFormId { get; }
    internal float ReliefDepthMeters { get; }
    internal float ReliefSideRoughness { get; }
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
        Walk,
        EquippedWeapon,
        ReliefDepthMeters,
        ReliefSideRoughness);

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
        float reliefDepthMeters;
        float reliefSideRoughness;
        using (var recipeDocument = JsonDocument.Parse(recipeBytes))
        {
            var recipe = recipeDocument.RootElement;
            var player = recipe.GetProperty("player");
            var equipped = player.GetProperty("equippedWeapon");
            var live3D = player.GetProperty("live3dPresentation");
            var relief = player.GetProperty("relief3d");
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
                player.GetProperty("walkFps").GetInt32() != WalkFramesPerSecond ||
                Fo2TemplePresentationCatalog.RequiredString(live3D, "schema") !=
                    Live3DPresentationSchema ||
                Fo2TemplePresentationCatalog.RequiredString(live3D, "authority") !=
                    Live3DPresentationAuthority ||
                Fo2TemplePresentationCatalog.RequiredString(live3D, "donorGame") !=
                    "FalloutNV" ||
                Fo2TemplePresentationCatalog.RequiredString(live3D, "outfitFormId") !=
                    ExpectedLive3DPresentationOutfitFormId ||
                Fo2TemplePresentationCatalog.RequiredString(live3D, "role") !=
                    "Chosen One tribal silhouette donor" ||
                !live3D.GetProperty("fullBody").GetBoolean() ||
                !live3D.GetProperty("requiredBodyRoles").EnumerateArray()
                    .Select(row => row.GetString())
                    .SequenceEqual(["body", "left-hand", "right-hand"]) ||
                live3D.GetProperty("retailParity").GetBoolean() ||
                Fo2TemplePresentationCatalog.RequiredString(equipped, "itemFid") !=
                    ExpectedEquippedItemFid ||
                Fo2TemplePresentationCatalog.RequiredString(equipped, "itemPid") !=
                    ExpectedEquippedItemPid ||
                equipped.GetProperty("weaponAnimationCode").GetInt32() !=
                    ExpectedWeaponAnimationCode ||
                Fo2TemplePresentationCatalog.RequiredString(equipped, "weaponArtSuffix") != "g" ||
                Fo2TemplePresentationCatalog.RequiredString(
                    equipped,
                    "idleAnimationCode") != "GA" ||
                Fo2TemplePresentationCatalog.RequiredString(
                    equipped,
                    "idleFrmLogicalPath") != ExpectedEquippedIdleLogicalPath ||
                equipped.GetProperty("idleFrame").GetInt32() != IdleFrame ||
                Fo2TemplePresentationCatalog.RequiredString(
                    equipped,
                    "walkAnimationCode") != "GB" ||
                Fo2TemplePresentationCatalog.RequiredString(
                    equipped,
                    "walkFrmLogicalPath") != ExpectedEquippedWalkLogicalPath ||
                !equipped.GetProperty("walkFrames").EnumerateArray()
                    .Select(row => row.GetInt32())
                    .SequenceEqual(Enumerable.Range(0, WalkFramesPerDirection)) ||
                equipped.GetProperty("walkFps").GetInt32() != WalkFramesPerSecond ||
                Fo2TemplePresentationCatalog.RequiredString(
                    equipped,
                    "geometryDisposition") != EquippedGeometryDisposition)
                throw new InvalidOperationException(
                    "Fallout 2 player presentation recipe binding drifted.");
            if (Fo2TemplePresentationCatalog.RequiredString(relief, "schema") !=
                    ReliefSchema ||
                Fo2TemplePresentationCatalog.RequiredString(relief, "mode") != ReliefMode)
                throw new InvalidOperationException(
                    "Fallout 2 player closed-relief recipe binding drifted.");
            reliefDepthMeters = relief.GetProperty("depthMeters").GetSingle();
            reliefSideRoughness = relief.GetProperty("sideRoughness").GetSingle();
            if (!Mathf.IsEqualApprox(reliefDepthMeters, ExpectedReliefDepthMeters) ||
                !Mathf.IsEqualApprox(
                    reliefSideRoughness,
                    ExpectedReliefSideRoughness))
                throw new InvalidOperationException(
                    "Fallout 2 player closed-relief recipe dimensions drifted.");
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

        var equippedArt = cache.GetProperty("equippedWeaponArt");
        var live3DPresentation = cache.GetProperty("live3dPresentation");
        var live3DPresentationOutfitFormId =
            Fo2TemplePresentationCatalog.RequiredString(
                live3DPresentation,
                "outfitFormId");
        if (Fo2TemplePresentationCatalog.RequiredString(
                live3DPresentation,
                "schema") != Live3DPresentationSchema ||
            Fo2TemplePresentationCatalog.RequiredString(
                live3DPresentation,
                "authority") != Live3DPresentationAuthority ||
            Fo2TemplePresentationCatalog.RequiredString(
                live3DPresentation,
                "donorGame") != "FalloutNV" ||
            live3DPresentationOutfitFormId != ExpectedLive3DPresentationOutfitFormId ||
            !live3DPresentation.GetProperty("fullBody").GetBoolean() ||
            live3DPresentation.GetProperty("retailParity").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 live 3D role-donor binding drifted.");
        var equippedIdleArt = equippedArt.GetProperty("idle");
        var equippedWalkArt = equippedArt.GetProperty("walk");
        var equippedIdleSourceSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            equippedIdleArt,
            "sha256");
        var equippedWalkSourceSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            equippedWalkArt,
            "sha256");
        if (Fo2TemplePresentationCatalog.RequiredString(equippedArt, "itemFid") !=
                ExpectedEquippedItemFid ||
            Fo2TemplePresentationCatalog.RequiredString(equippedArt, "itemPid") !=
                ExpectedEquippedItemPid ||
            equippedArt.GetProperty("weaponAnimationCode").GetInt32() !=
                ExpectedWeaponAnimationCode ||
            Fo2TemplePresentationCatalog.RequiredString(equippedArt, "weaponArtSuffix") != "g" ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedArt,
                "geometryDisposition") != EquippedGeometryDisposition ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedIdleArt,
                "animationCode") != "GA" ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedIdleArt,
                "logicalPath") != ExpectedEquippedIdleLogicalPath ||
            equippedIdleArt.GetProperty("bytes").GetInt64() <= 0 ||
            equippedIdleArt.GetProperty("framesPerDirection").GetInt32() <= IdleFrame ||
            equippedIdleArt.GetProperty("decodedDirections").GetInt32() !=
                Fo1HexMath.DirectionCount ||
            equippedIdleArt.GetProperty("admittedFrame").GetInt32() != IdleFrame ||
            !equippedIdleArt.GetProperty("admittedDirections").EnumerateArray()
                .Select(row => row.GetInt32())
                .SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            equippedIdleArt.GetProperty("animationPlayback").GetBoolean() ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedWalkArt,
                "animationCode") != "GB" ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedWalkArt,
                "logicalPath") != ExpectedEquippedWalkLogicalPath ||
            equippedWalkArt.GetProperty("bytes").GetInt64() <= 0 ||
            equippedWalkArt.GetProperty("fps").GetInt32() != WalkFramesPerSecond ||
            equippedWalkArt.GetProperty("actionFrame").GetInt32() != 0 ||
            equippedWalkArt.GetProperty("framesPerDirection").GetInt32() !=
                WalkFramesPerDirection ||
            equippedWalkArt.GetProperty("decodedDirections").GetInt32() !=
                Fo1HexMath.DirectionCount ||
            !equippedWalkArt.GetProperty("admittedFrames").EnumerateArray()
                .Select(row => row.GetInt32())
                .SequenceEqual(Enumerable.Range(0, WalkFramesPerDirection)) ||
            !equippedWalkArt.GetProperty("admittedDirections").EnumerateArray()
                .Select(row => row.GetInt32())
                .SequenceEqual(Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            !equippedWalkArt.GetProperty("animationPlayback").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 Spear-equipped GA/GB art binding drifted.");

        var frames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
        var walkFrames = new Dictionary<int, Dictionary<int, Fo2ArroyoPlayerFrame>>();
        var equippedFrames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
        var equippedWalkFrames = new Dictionary<int, Dictionary<int, Fo2ArroyoPlayerFrame>>();
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
            if (kind == "player-equipped")
            {
                if (frame.LogicalPath != ExpectedEquippedIdleLogicalPath ||
                    frame.SourceSha256 != equippedIdleSourceSha256 ||
                    frame.Frame != IdleFrame ||
                    frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                    frame.Width <= 0 || frame.Height <= 0 ||
                    !equippedFrames.TryAdd(frame.Direction, frame))
                    throw new InvalidOperationException(
                        $"Fallout 2 Spear-equipped player direction artifact is invalid: {id}");
                continue;
            }
            if (kind == "player-equipped-walk")
            {
                if (frame.LogicalPath != ExpectedEquippedWalkLogicalPath ||
                    frame.SourceSha256 != equippedWalkSourceSha256 ||
                    frame.Frame is < 0 or >= WalkFramesPerDirection ||
                    frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                    frame.Width <= 0 || frame.Height <= 0)
                    throw new InvalidOperationException(
                        $"Fallout 2 Spear-equipped walk artifact is invalid: {id}");
                if (!equippedWalkFrames.TryGetValue(
                        frame.Direction,
                        out var equippedDirectionFrames))
                {
                    equippedDirectionFrames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
                    equippedWalkFrames.Add(frame.Direction, equippedDirectionFrames);
                }
                if (!equippedDirectionFrames.TryAdd(frame.Frame, frame))
                    throw new InvalidOperationException(
                        $"Duplicate Fallout 2 Spear-equipped walk artifact: {id}");
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
        if (!equippedFrames.Keys.Order().SequenceEqual(
                Enumerable.Range(0, Fo1HexMath.DirectionCount)))
            throw new InvalidOperationException(
                "Fallout 2 player cache does not contain six Spear-equipped idle directions.");
        var equippedWalkDirections = equippedWalkFrames.ToDictionary(
            row => row.Key,
            row => (IReadOnlyList<Fo2ArroyoPlayerFrame>)row.Value
                .OrderBy(frame => frame.Key).Select(frame => frame.Value).ToArray());
        if (!equippedWalkDirections.Keys.Order().SequenceEqual(
                Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            equippedWalkDirections.Values.Any(direction =>
                direction.Count != WalkFramesPerDirection ||
                !direction.Select(frame => frame.Frame).SequenceEqual(
                    Enumerable.Range(0, WalkFramesPerDirection))))
            throw new InvalidOperationException(
                "Fallout 2 player cache does not contain the exact 6x8 GB walk cycle.");
        var equippedWeapon = new Fo2ArroyoEquippedWeaponPresentation(
            ExpectedEquippedItemFid,
            ExpectedEquippedItemPid,
            ExpectedWeaponAnimationCode,
            "g",
            EquippedGeometryDisposition,
            equippedFrames,
            new Fo2ArroyoPlayerAnimation(
                "GB",
                ExpectedEquippedWalkLogicalPath,
                equippedWalkSourceSha256,
                equippedWalkArt.GetProperty("fps").GetInt32(),
                equippedWalkArt.GetProperty("actionFrame").GetInt32(),
                equippedWalkDirections));

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
            !resourceIdentities.Contains(
                $"{ExpectedEquippedIdleLogicalPath}|{equippedIdleSourceSha256}") ||
            !resourceIdentities.Contains(
                $"{ExpectedEquippedWalkLogicalPath}|{equippedWalkSourceSha256}") ||
            !resourceIdentities.Any(row => row.StartsWith("color.pal|", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Fallout 2 player cache resource identity closure failed.");
        var counts = cache.GetProperty("counts");
        if (counts.GetProperty("sourceResources").GetInt32() != resources.Length ||
            counts.GetProperty("idleDirectionArtifacts").GetInt32() != frames.Count ||
            counts.GetProperty("walkFrameArtifacts").GetInt32() !=
                WalkFramesPerDirection * Fo1HexMath.DirectionCount ||
            counts.GetProperty("equippedIdleDirectionArtifacts").GetInt32() !=
                equippedFrames.Count ||
            counts.GetProperty("equippedWalkFrameArtifacts").GetInt32() !=
                WalkFramesPerDirection * Fo1HexMath.DirectionCount ||
            counts.GetProperty("closedReliefArtifacts").GetInt32() !=
                artifactRows.Length ||
            artifactRows.Length != frames.Count + walkDirections.Values.Sum(row => row.Count) +
                equippedFrames.Count + equippedWalkDirections.Values.Sum(row => row.Count))
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
            equippedWeapon,
            live3DPresentationOutfitFormId,
            reliefDepthMeters,
            reliefSideRoughness,
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
            Fo2TemplePresentationCatalog.ReadVector2I(row.GetProperty("frameOffset")),
            Fo2FrmReliefArtifact.Load(
                row.GetProperty("relief3d"),
                cacheRoot,
                pngSha256,
                label));
    }
}

internal sealed partial class Fo2ArroyoPlayerPresentation : Node3D
{
    internal const string OwnedFrmReliefMode =
        "exact-owned-fo2-frm-alpha-island-molded-relief-v2";

    private readonly IReadOnlyDictionary<int, Fo2ArroyoPlayerFrame> _idleFrames;
    private readonly IReadOnlyDictionary<int, Texture2D> _idleTextures;
    private readonly Fo2ArroyoPlayerAnimation _walk;
    private readonly IReadOnlyDictionary<(int Direction, int Frame), Texture2D>
        _walkTextures;
    private readonly Fo2ArroyoEquippedWeaponPresentation _equippedWeapon;
    private readonly IReadOnlyDictionary<int, Texture2D> _equippedIdleTextures;
    private readonly IReadOnlyDictionary<(int Direction, int Frame), Texture2D>
        _equippedWalkTextures;
    private readonly Dictionary<string, Fo2FrmReliefMeshSet> _reliefMeshes =
        new(StringComparer.Ordinal);
    private readonly float _sourcePixelsPerMeter;
    private readonly float _reliefDepthMeters;
    private readonly float _reliefSideRoughness;
    private Fo2ArroyoPlayerFrame _currentFrame = null!;
    private Texture2D? _currentTexture;
    private Fo2FrmReliefMeshSet? _currentRelief;
    private Node3D? _visibleRelief;
    private double _walkFrameAccumulator;
    private int _walkFrameIndex;
    private bool _spearEquipped;

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
        _sourcePixelsPerMeter = sourcePixelsPerMeter;
        _reliefDepthMeters = source.ReliefDepthMeters;
        _reliefSideRoughness = source.ReliefSideRoughness;
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
        _equippedWeapon = source.EquippedWeapon;
        _equippedIdleTextures = _equippedWeapon.IdleDirections.ToDictionary(
            row => row.Key,
            row => LoadTexture(row.Value));
        _equippedWalkTextures = _equippedWeapon.Walk.Directions.SelectMany(direction =>
                direction.Value.Select(frame =>
                    new KeyValuePair<(int Direction, int Frame), Texture2D>(
                        (direction.Key, frame.Frame),
                        LoadTexture(frame))))
            .ToDictionary(row => row.Key, row => row.Value);
        Name = source.NodeName;
        Position = Vector3.Down * spawnCenterHeightMeters;
        SetMeta("fid", source.Fid);
        SetMeta("prototype_pid", source.PrototypePid);
        SetMeta("prototype_logical_path", source.PrototypeLogicalPath);
        SetMeta("prototype_sha256", source.PrototypeSha256);
        SetMeta("logical_path", source.LogicalPath);
        SetMeta("source_sha256", source.SourceSha256);
        SetMeta("walk_logical_path", source.Walk.LogicalPath);
        SetMeta("walk_source_sha256", source.Walk.SourceSha256);
        SetMeta("walk_fps", source.Walk.FramesPerSecond);
        SetMeta("equipped_item_fid", _equippedWeapon.ItemFid);
        SetMeta("equipped_item_pid", _equippedWeapon.ItemPid);
        SetMeta("weapon_animation_code", _equippedWeapon.WeaponAnimationCode);
        SetMeta("weapon_art_suffix", _equippedWeapon.WeaponArtSuffix);
        SetMeta("equipped_geometry_disposition", _equippedWeapon.GeometryDisposition);
        SetMeta("geometry_mode", OwnedFrmReliefMode);
        SetMeta("visible_sprite3d_cards", 0);
        SetMeta(
            "source_frm_role",
            "exact-selected-character-and-equipment-composite-molded-3d-presentation");
        SetMeta("source_composite_weapon_policy", _equippedWeapon.GeometryDisposition);
        SetMeta("visual_parity", false);
        SetMeta("animation_playback", false);
        SetDirection(initialDirection);
    }

    internal int Direction { get; private set; }
    internal bool IsWalking { get; private set; }
    internal int WalkFrameAdvances { get; private set; }
    internal int CompletedWalkCycles { get; private set; }
    internal int AnimationFrame => _currentFrame.Frame;
    internal string AnimationCode => _spearEquipped
        ? IsWalking ? _equippedWeapon.Walk.Code : "GA"
        : IsWalking ? _walk.Code : "AA";
    internal Fo2ArroyoPlayerFrame CurrentFrame => _currentFrame;
    internal Texture2D? Texture => _currentTexture;
    internal bool VisibleInWorld => _visibleRelief is not null &&
        _visibleRelief.IsVisibleInTree();
    internal string GeometryMode => OwnedFrmReliefMode;
    internal string PresentationLabel =>
        "Owned FO2 composite FRM alpha-island molded relief (non-parity)";
    internal bool UsesOwnedDonor => false;
    internal bool UsesOwnedFrmRelief => true;
    internal int MeshInstances => _currentRelief is null
        ? 0
        : 1 + (_currentRelief.Sides is null ? 0 : 1);
    internal int MoldedFaceTriangles => _currentRelief?.FaceTriangles ?? 0;
    internal int MoldedSideTriangles => _currentRelief?.SideTriangles ?? 0;
    internal int ReliefIslands => _currentFrame.Relief.IslandCount;
    internal bool SpearEquipped => _spearEquipped;
    internal bool EquipmentSocketResolved => false;
    internal string EquipmentSocketName => "none-authored-composite-frm";
    internal bool EquippedCompositeVisible => _spearEquipped && VisibleInWorld &&
        AnimationCode is "GA" or "GB";
    internal bool EquippedWeaponGeometryVisible => false;

    public override void _Ready()
    {
        SetMeta("geometry_mode", GeometryMode);
        SetMeta("presentation_label", PresentationLabel);
        SetMeta("uses_owned_donor", UsesOwnedDonor);
        SetMeta("uses_owned_fo2_frm_relief", UsesOwnedFrmRelief);
    }

    internal void SetDirection(int direction)
    {
        var frames = _spearEquipped ? _equippedWeapon.IdleDirections : _idleFrames;
        var textures = _spearEquipped ? _equippedIdleTextures : _idleTextures;
        if (!frames.TryGetValue(direction, out var frame) ||
            !textures.TryGetValue(direction, out var texture))
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
        var animation = _spearEquipped ? _equippedWeapon.Walk : _walk;
        var textures = _spearEquipped ? _equippedWalkTextures : _walkTextures;
        if (!animation.Directions.TryGetValue(direction, out var frames) || frames.Count == 0)
            throw new InvalidOperationException(
                $"Fallout 2 player walk direction is unavailable: {direction}");
        if (IsWalking && Direction == direction)
            return;
        Direction = direction;
        IsWalking = true;
        _walkFrameAccumulator = 0.0;
        _walkFrameIndex = 0;
        var frame = frames[0];
        ApplyFrame(frame, textures[(direction, frame.Frame)]);
    }

    internal void SetSpearEquipped(Fo2TempleConfrontationLoot loot, bool equipped)
    {
        if (loot.Fid != _equippedWeapon.ItemFid ||
            loot.Pid != _equippedWeapon.ItemPid ||
            loot.Weapon.AnimationCode != _equippedWeapon.WeaponAnimationCode)
            throw new InvalidOperationException(
                "Fallout 2 Temple Spear/equipped-player source join drifted.");
        if (_spearEquipped == equipped)
            return;
        _spearEquipped = equipped;
        SetMeta("spear_equipped", equipped);
        SetMeta("source_composite_includes_spear", equipped);
        SetMeta("separable_weapon_geometry", false);
        SetMeta("equipment_visual_geometry", _equippedWeapon.GeometryDisposition);
        var wasWalking = IsWalking;
        IsWalking = false;
        if (wasWalking)
            StartWalking(Direction);
        else
            SetDirection(Direction);
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
        var animation = _spearEquipped ? _equippedWeapon.Walk : _walk;
        var textures = _spearEquipped ? _equippedWalkTextures : _walkTextures;
        var frameDuration = 1.0 / animation.FramesPerSecond;
        var frames = animation.Directions[Direction];
        while (_walkFrameAccumulator >= frameDuration)
        {
            _walkFrameAccumulator -= frameDuration;
            _walkFrameIndex = (_walkFrameIndex + 1) % frames.Count;
            WalkFrameAdvances++;
            if (_walkFrameIndex == 0)
                CompletedWalkCycles++;
            var frame = frames[_walkFrameIndex];
            ApplyFrame(frame, textures[(Direction, frame.Frame)]);
        }
    }

    private void ApplyFrame(Fo2ArroyoPlayerFrame frame, Texture2D texture)
    {
        _currentFrame = frame;
        _currentTexture = texture;
        PresentRelief(frame);
        SetMeta("source_direction", Direction);
        SetMeta("source_frame", frame.Frame);
        SetMeta("animation_code", AnimationCode);
        SetMeta("animation_frame", frame.Frame);
        SetMeta("animation_playback", IsWalking);
        SetMeta("frame_logical_path", frame.LogicalPath);
        SetMeta("frame_source_sha256", frame.SourceSha256);
        SetMeta("png_sha256", frame.PngSha256);
        SetMeta("relief_normal_png_sha256", frame.Relief.NormalPngSha256);
        SetMeta("relief_solid_mask_png_sha256", frame.Relief.SolidMaskPngSha256);
        SetMeta("relief_depth_png_sha256", frame.Relief.DepthPngSha256);
        SetMeta("relief_islands", frame.Relief.IslandCount);
        SetMeta("molded_face_triangles", MoldedFaceTriangles);
        SetMeta("molded_side_triangles", MoldedSideTriangles);
        SetMeta("source_composite_includes_spear", _spearEquipped);
    }

    private void PresentRelief(Fo2ArroyoPlayerFrame frame)
    {
        if (!_reliefMeshes.TryGetValue(frame.Id, out var meshSet))
        {
            meshSet = Fo2FrmReliefMesh.Build(
                frame.Path,
                frame.Width,
                frame.Height,
                frame.DirectionOffset + frame.FrameOffset,
                _sourcePixelsPerMeter,
                _reliefDepthMeters,
                _reliefSideRoughness,
                frame.Relief,
                sourcePixelsOnly: false);
            meshSet.FaceMaterial.BillboardMode =
                BaseMaterial3D.BillboardModeEnum.FixedY;
            if (meshSet.SideMaterial is not null)
                meshSet.SideMaterial.BillboardMode =
                    BaseMaterial3D.BillboardModeEnum.FixedY;
            _reliefMeshes.Add(frame.Id, meshSet);
        }
        if (_visibleRelief is not null)
        {
            RemoveChild(_visibleRelief);
            _visibleRelief.Free();
        }
        _currentRelief = meshSet;
        _visibleRelief = Fo2FrmReliefMesh.Instantiate(
            $"SOURCE_COMPOSITE_{AnimationCode}_{Direction}_{frame.Frame}",
            meshSet);
        _visibleRelief.SetMeta("artifact_id", frame.Id);
        _visibleRelief.SetMeta("logical_path", frame.LogicalPath);
        _visibleRelief.SetMeta("source_sha256", frame.SourceSha256);
        _visibleRelief.SetMeta("png_sha256", frame.PngSha256);
        _visibleRelief.SetMeta("includes_authored_spear", _spearEquipped);
        AddChild(_visibleRelief);
    }

    private static Texture2D LoadTexture(Fo2ArroyoPlayerFrame frame)
    {
        var image = Image.LoadFromFile(frame.Path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != frame.Width || image.GetHeight() != frame.Height)
            throw new InvalidOperationException(
                $"Fallout 2 source-reference PNG dimensions drifted: {frame.Path}");
        return ImageTexture.CreateFromImage(image);
    }
}
