using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2CharacterProfile(
    string Name,
    int Age,
    string Sex,
    IReadOnlyList<int> Special,
    IReadOnlyList<string> TaggedSkills,
    IReadOnlyList<string> Traits)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 11 ||
            Age is < 16 or > 35 ||
            Sex is not "Male" and not "Female" ||
            Special.Count != 7 || Special.Any(value => value is < 1 or > 10) ||
            Special.Sum() != 40 ||
            TaggedSkills.Count != 3 ||
            TaggedSkills.Distinct(StringComparer.Ordinal).Count() != 3 ||
            Traits.Count > 2 ||
            Traits.Distinct(StringComparer.Ordinal).Count() != Traits.Count)
            throw new InvalidOperationException("Fallout 2 premade character state is invalid.");
    }
}

internal sealed record Fo2CharacterStartAsset(
    string Id,
    string LogicalPath,
    string SourceSha256,
    string Path,
    string PngSha256,
    long PngBytes,
    int Width,
    int Height,
    bool Opaque)
{
    internal Texture2D Load()
    {
        var image = Image.LoadFromFile(Path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != Width || image.GetHeight() != Height)
            throw new InvalidOperationException(
                $"Fallout 2 character-start PNG dimensions drifted: {Path}");
        return ImageTexture.CreateFromImage(image);
    }
}

internal sealed record Fo2PremadeCharacter(
    string Id,
    string Role,
    string GcdSha256,
    string BioSha256,
    string Biography,
    Fo2CharacterStartAsset Panel,
    Fo2CharacterProfile Profile);

internal sealed class Fo2CharacterStartCatalog
{
    private const string CacheSchema = "opennv-fo2-character-start-cache/v1";
    private const string RecipeSchema = "opennv-fo2-character-start-recipe/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    internal const string RecipeId = "fo2-character-start-v1";
    internal const string FemaleFid = "0100003d";
    internal const string FemaleLogicalPath = "art\\critters\\hfprimaa.frm";
    private static readonly string[] SkillNames =
    [
        "Small Guns", "Big Guns", "Energy Weapons", "Unarmed", "Melee Weapons",
        "Throwing", "First Aid", "Doctor", "Sneak", "Lockpick", "Steal", "Traps",
        "Science", "Repair", "Speech", "Barter", "Gambling", "Outdoorsman",
    ];
    private static readonly string[] TraitNames =
    [
        "Fast Metabolism", "Bruiser", "Small Frame", "One Hander", "Finesse",
        "Kamikaze", "Heavy Handed", "Fast Shot", "Bloody Mess", "Jinxed",
        "Good Natured", "Chem Reliant", "Chem Resistant", "Night Person", "Skilled",
        "Gifted",
    ];

    private Fo2CharacterStartCatalog(
        string manifestPath,
        string manifestSha256,
        string sourceProfileId,
        string recipeSha256,
        Fo2CharacterStartAsset picker,
        IReadOnlyList<Fo2PremadeCharacter> characters,
        Fo2ArroyoPlayerPresentationSource femalePresentation,
        int verifiedResources)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceProfileId = sourceProfileId;
        RecipeSha256 = recipeSha256;
        Picker = picker;
        Characters = characters;
        FemalePresentation = femalePresentation;
        VerifiedResources = verifiedResources;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string RecipeSha256 { get; }
    internal Fo2CharacterStartAsset Picker { get; }
    internal IReadOnlyList<Fo2PremadeCharacter> Characters { get; }
    internal Fo2ArroyoPlayerPresentationSource FemalePresentation { get; }
    internal int VerifiedResources { get; }

    internal Fo2ArroyoPlayerPresentationSource PresentationFor(
        Fo2PremadeCharacter character,
        Fo2ArroyoPlayerPresentationCatalog malePresentation)
    {
        if (!Characters.Contains(character) ||
            malePresentation.SourceProfileId != SourceProfileId)
            throw new InvalidOperationException(
                "Fallout 2 selected character presentation binding drifted.");
        return character.Profile.Sex == "Female"
            ? FemalePresentation
            : malePresentation.Source;
    }

    internal static Fo2CharacterStartCatalog Load(
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
                "CharacterStartToArroyo" ||
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
            throw new InvalidOperationException("Unexpected Fallout 2 character-start cache.");
        var cacheRoot = Path.GetDirectoryName(manifestPath)!;

        VerifyProfile(cache.GetProperty("sourceProfile"), cacheRoot, expectedSourceProfileId);
        var recipe = VerifyRecipe(cache.GetProperty("recipe"), cacheRoot);
        var presentation = cache.GetProperty("presentation");
        if (!ReadInts(presentation.GetProperty("viewport")).SequenceEqual([640, 480]) ||
            !ReadInts(presentation.GetProperty("panel")).SequenceEqual([24, 20, 592, 260]))
            throw new InvalidOperationException(
                "Fallout 2 character-start presentation dimensions drifted.");

        var picker = LoadAsset(
            cache.GetProperty("picker"),
            recipe.GetProperty("picker"),
            cacheRoot,
            "picker");
        var recipeRows = recipe.GetProperty("premades").EnumerateArray().ToArray();
        var cacheRows = cache.GetProperty("characters").EnumerateArray().ToArray();
        if (recipeRows.Length != 3 || cacheRows.Length != 3)
            throw new InvalidOperationException(
                "Fallout 2 character-start premade count drifted.");
        var characters = new List<Fo2PremadeCharacter>();
        for (var index = 0; index < cacheRows.Length; index++)
        {
            var expected = recipeRows[index];
            var row = cacheRows[index];
            var id = Fo2TemplePresentationCatalog.RequiredString(row, "id");
            if (id != Fo2TemplePresentationCatalog.RequiredString(expected, "id") ||
                Fo2TemplePresentationCatalog.RequiredString(row, "role") !=
                    Fo2TemplePresentationCatalog.RequiredString(expected, "role"))
                throw new InvalidOperationException(
                    "Fallout 2 premade identity or ordering drifted.");
            var gcd = row.GetProperty("gcd");
            var bio = row.GetProperty("bio");
            VerifySourceDescriptor(gcd, expected.GetProperty("gcd"), 432, "GCD");
            VerifySourceDescriptor(bio, expected.GetProperty("bio"), null, "BIO");
            var profile = ReadProfile(row.GetProperty("profile"));
            if (profile.Name != Fo2TemplePresentationCatalog.RequiredString(expected, "name"))
                throw new InvalidOperationException("Fallout 2 premade name drifted.");
            profile.Validate();
            var panel = LoadAsset(
                row.GetProperty("panel"),
                expected.GetProperty("panel"),
                cacheRoot,
                $"panel-{id}");
            var biography = Fo2TemplePresentationCatalog.RequiredString(bio, "text");
            if (biography.Length < 80 ||
                profile.TaggedSkills.Except(SkillNames, StringComparer.Ordinal).Any() ||
                profile.Traits.Except(TraitNames, StringComparer.Ordinal).Any())
                throw new InvalidOperationException(
                    $"Fallout 2 premade state vocabulary drifted: {id}");
            characters.Add(new Fo2PremadeCharacter(
                id,
                Fo2TemplePresentationCatalog.RequiredString(row, "role"),
                Fo2TemplePresentationCatalog.RequiredHash(gcd, "sha256"),
                Fo2TemplePresentationCatalog.RequiredHash(bio, "sha256"),
                biography,
                panel,
                profile));
        }
        if (!characters.Select(row => (row.Id, row.Profile.Name, row.Profile.Sex))
                .SequenceEqual(
                [
                    ("combat", "Narg", "Male"),
                    ("stealth", "Mingan", "Male"),
                    ("diplomat", "Chitsa", "Female"),
                ]))
            throw new InvalidOperationException(
                "Fallout 2 source premade roster drifted.");

        var female = LoadFemalePresentation(
            cache.GetProperty("femalePresentation"),
            recipe.GetProperty("femalePresentation"),
            cacheRoot,
            expectedSourceProfileId);
        var resources = cache.GetProperty("resources").EnumerateArray().ToArray();
        var identities = resources.Select(row =>
            $"{Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath")}|" +
            Fo2TemplePresentationCatalog.RequiredHash(row, "sha256"))
            .ToHashSet(StringComparer.Ordinal);
        var required = characters.SelectMany(character => new[]
            {
                $"premade\\{character.Id}.gcd|{character.GcdSha256}",
                $"premade\\{character.Id}.bio|{character.BioSha256}",
                $"{character.Panel.LogicalPath}|{character.Panel.SourceSha256}",
            })
            .Append($"{picker.LogicalPath}|{picker.SourceSha256}")
            .Append($"{female.LogicalPath}|{female.SourceSha256}")
            .ToArray();
        if (identities.Count != resources.Length || required.Any(row => !identities.Contains(row)))
            throw new InvalidOperationException(
                "Fallout 2 character-start resource identity closure failed.");
        var counts = cache.GetProperty("counts");
        if (counts.GetProperty("premades").GetInt32() != characters.Count ||
            counts.GetProperty("uiPngs").GetInt32() != 1 + characters.Count ||
            counts.GetProperty("femaleDirectionPngs").GetInt32() !=
                female.Directions.Count ||
            counts.GetProperty("sourceResources").GetInt32() != resources.Length)
            throw new InvalidOperationException(
                "Fallout 2 character-start cache counts drifted.");

        return new Fo2CharacterStartCatalog(
            manifestPath,
            Fo2TemplePresentationCatalog.Sha256(cacheBytes),
            expectedSourceProfileId,
            Fo2TemplePresentationCatalog.RequiredHash(cache.GetProperty("recipe"), "sha256"),
            picker,
            characters,
            female,
            resources.Length);
    }

    private static void VerifyProfile(
        JsonElement descriptor,
        string cacheRoot,
        string expectedSourceProfileId)
    {
        var path = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "file"),
            cacheRoot);
        var bytes = Fo2TemplePresentationCatalog.VerifyFile(
            path,
            Fo2TemplePresentationCatalog.RequiredHash(descriptor, "sha256"),
            null,
            "Fallout 2 character-start owned profile");
        using var document = JsonDocument.Parse(bytes);
        var profile = document.RootElement;
        if (Fo2TemplePresentationCatalog.RequiredString(descriptor, "schema") != ProfileSchema ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "sourceProfileId") !=
                expectedSourceProfileId ||
            Fo2TemplePresentationCatalog.RequiredString(profile, "schema") != ProfileSchema ||
            Fo2TemplePresentationCatalog.RequiredString(profile, "status") !=
                "registered-owned-install" ||
            Fo2TemplePresentationCatalog.RequiredString(profile, "sourceProfileId") !=
                expectedSourceProfileId ||
            profile.GetProperty("retailOrDerivedAssetsPackaged").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 character-start owned profile drifted.");
    }

    private static JsonElement VerifyRecipe(JsonElement descriptor, string cacheRoot)
    {
        var path = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "file"),
            cacheRoot);
        var bytes = Fo2TemplePresentationCatalog.VerifyFile(
            path,
            Fo2TemplePresentationCatalog.RequiredHash(descriptor, "sha256"),
            null,
            "Fallout 2 character-start recipe");
        var document = JsonDocument.Parse(bytes);
        var recipe = document.RootElement.Clone();
        document.Dispose();
        if (Fo2TemplePresentationCatalog.RequiredString(descriptor, "schema") != RecipeSchema ||
            Fo2TemplePresentationCatalog.RequiredString(descriptor, "id") != RecipeId ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "schema") != RecipeSchema ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "id") != RecipeId ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "campaign") != "Fallout2" ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "sourceProfileSchema") !=
                ProfileSchema)
            throw new InvalidOperationException(
                "Fallout 2 character-start recipe binding drifted.");
        return recipe;
    }

    private static Fo2CharacterStartAsset LoadAsset(
        JsonElement row,
        JsonElement expected,
        string cacheRoot,
        string expectedId)
    {
        var relative = Fo2TemplePresentationCatalog.RequiredString(row, "png");
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException(
                "Fallout 2 character-start PNG path must be cache-relative.");
        var path = Path.GetFullPath(Path.Combine(
            cacheRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(
                cacheRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 2 character-start PNG path escapes its cache.");
        var bytes = row.GetProperty("pngBytes").GetInt64();
        var hash = Fo2TemplePresentationCatalog.RequiredHash(row, "pngSha256");
        Fo2TemplePresentationCatalog.VerifyFile(
            path,
            hash,
            bytes,
            $"Fallout 2 character-start PNG {expectedId}");
        var asset = new Fo2CharacterStartAsset(
            Fo2TemplePresentationCatalog.RequiredString(row, "id"),
            Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath"),
            Fo2TemplePresentationCatalog.RequiredHash(row, "sourceSha256"),
            path,
            hash,
            bytes,
            row.GetProperty("width").GetInt32(),
            row.GetProperty("height").GetInt32(),
            row.GetProperty("opaque").GetBoolean());
        if (asset.Id != expectedId ||
            asset.LogicalPath != Fo2TemplePresentationCatalog.RequiredString(
                expected,
                "logicalPath") ||
            asset.SourceSha256 != Fo2TemplePresentationCatalog.RequiredHash(
                expected,
                "sha256") ||
            asset.Width != expected.GetProperty("width").GetInt32() ||
            asset.Height != expected.GetProperty("height").GetInt32() ||
            asset.Width <= 0 || asset.Height <= 0)
            throw new InvalidOperationException(
                $"Fallout 2 character-start asset binding drifted: {expectedId}");
        return asset;
    }

    private static void VerifySourceDescriptor(
        JsonElement row,
        JsonElement expected,
        long? expectedBytes,
        string label)
    {
        if (Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath") !=
                Fo2TemplePresentationCatalog.RequiredString(expected, "logicalPath") ||
            Fo2TemplePresentationCatalog.RequiredHash(row, "sha256") !=
                Fo2TemplePresentationCatalog.RequiredHash(expected, "sha256") ||
            row.GetProperty("bytes").GetInt64() <= 0 ||
            expectedBytes.HasValue &&
                row.GetProperty("bytes").GetInt64() != expectedBytes.Value)
            throw new InvalidOperationException(
                $"Fallout 2 character-start {label} binding drifted.");
    }

    private static Fo2CharacterProfile ReadProfile(JsonElement row) => new(
        Fo2TemplePresentationCatalog.RequiredString(row, "name"),
        row.GetProperty("age").GetInt32(),
        Fo2TemplePresentationCatalog.RequiredString(row, "sex"),
        ReadInts(row.GetProperty("allocatedSpecial")),
        row.GetProperty("taggedSkills").EnumerateArray()
            .Select(value => value.GetString() ?? "").ToArray(),
        row.GetProperty("traits").EnumerateArray()
            .Select(value => value.GetString() ?? "").ToArray());

    private static Fo2ArroyoPlayerPresentationSource LoadFemalePresentation(
        JsonElement row,
        JsonElement expected,
        string cacheRoot,
        string sourceProfileId)
    {
        var sourceSha256 = Fo2TemplePresentationCatalog.RequiredHash(row, "sourceSha256");
        if (Fo2TemplePresentationCatalog.RequiredString(row, "fid") != FemaleFid ||
            Fo2TemplePresentationCatalog.RequiredString(row, "fid") !=
                Fo2TemplePresentationCatalog.RequiredString(expected, "fid") ||
            Fo2TemplePresentationCatalog.RequiredString(row, "artListEntry") !=
                "hfprim,11,1" ||
            row.GetProperty("artIndex").GetInt32() != 61 ||
            Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath") !=
                FemaleLogicalPath ||
            row.GetProperty("frame").GetInt32() != 0 ||
            row.GetProperty("animationPlayback").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 female player source binding drifted.");
        var frames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
        foreach (var artifact in row.GetProperty("directions").EnumerateArray())
        {
            var direction = artifact.GetProperty("rotation").GetInt32();
            var relative = Fo2TemplePresentationCatalog.RequiredString(artifact, "png");
            var path = Path.GetFullPath(Path.Combine(
                cacheRoot,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (Path.IsPathRooted(relative) ||
                !path.StartsWith(
                    cacheRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Fallout 2 female player PNG path escapes its cache.");
            var pngBytes = artifact.GetProperty("pngBytes").GetInt64();
            var pngSha256 = Fo2TemplePresentationCatalog.RequiredHash(artifact, "pngSha256");
            Fo2TemplePresentationCatalog.VerifyFile(
                path,
                pngSha256,
                pngBytes,
                $"Fallout 2 female player PNG direction {direction}");
            var frame = new Fo2ArroyoPlayerFrame(
                Fo2TemplePresentationCatalog.RequiredString(artifact, "id"),
                Fo2TemplePresentationCatalog.RequiredString(artifact, "logicalPath"),
                path,
                Fo2TemplePresentationCatalog.RequiredHash(artifact, "sourceSha256"),
                pngSha256,
                pngBytes,
                artifact.GetProperty("width").GetInt32(),
                artifact.GetProperty("height").GetInt32(),
                direction,
                artifact.GetProperty("frame").GetInt32(),
                Fo2TemplePresentationCatalog.ReadVector2I(
                    artifact.GetProperty("directionOffset")),
                Fo2TemplePresentationCatalog.ReadVector2I(
                    artifact.GetProperty("frameOffset")));
            if (Fo2TemplePresentationCatalog.RequiredString(artifact, "kind") !=
                    "female-player" ||
                frame.LogicalPath != FemaleLogicalPath ||
                frame.SourceSha256 != sourceSha256 ||
                frame.Frame != 0 ||
                frame.Width <= 0 || frame.Height <= 0 ||
                !frames.TryAdd(direction, frame))
                throw new InvalidOperationException(
                    "Fallout 2 female player direction artifact drifted.");
        }
        if (!frames.Keys.Order().SequenceEqual(Enumerable.Range(0, 6)))
            throw new InvalidOperationException(
                "Fallout 2 female player direction coverage drifted.");
        return new Fo2ArroyoPlayerPresentationSource(
            sourceProfileId,
            "CHOSEN_ONE_OWNED_HFPRIM_IDLE_FRAME_ZERO",
            FemaleFid,
            FemaleLogicalPath,
            sourceSha256,
            frames);
    }

    private static int[] ReadInts(JsonElement source) =>
        source.EnumerateArray().Select(row => row.GetInt32()).ToArray();
}
