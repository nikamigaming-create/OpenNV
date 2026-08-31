using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;
using OpenNV.Runtime.Campaigns.Fallout1;
using OpenNV.Runtime.Campaigns.Classic;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed record Fo2CharacterProfile(
    string Name,
    int Age,
    string Sex,
    IReadOnlyList<int> Special,
    IReadOnlyList<int> SkillBonuses,
    IReadOnlyList<string> TaggedSkills,
    IReadOnlyList<string> Traits)
{
    internal ClassicSkillInputs SkillInputs(
        string skillId,
        IReadOnlyList<int> effectiveSpecial,
        int? traitAdjustment,
        int? perkAdjustment,
        ClassicSkillDifficulty difficulty)
    {
        var skillIndex = Array.IndexOf(Fo2CharacterStartCatalog.SkillNames, skillId);
        if (skillIndex < 0)
            throw new InvalidOperationException($"Fallout 2 skill is unsupported: {skillId}");
        return new ClassicSkillInputs(
            effectiveSpecial,
            SkillBonuses[skillIndex],
            TaggedSkills.Contains(skillId, StringComparer.Ordinal),
            traitAdjustment,
            perkAdjustment,
            difficulty);
    }

    internal void Validate(bool allowUnselectedTags = false)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 11 ||
            Age is < 16 or > 35 ||
            Sex is not "Male" and not "Female" ||
            Special.Count != 7 || Special.Any(value => value is < 1 or > 10) ||
            Special.Sum() != 40 ||
            SkillBonuses.Count != Fo2CharacterStartCatalog.SkillNames.Length ||
            SkillBonuses.Any(value => value < 0) ||
            TaggedSkills.Count != 3 && !(allowUnselectedTags && TaggedSkills.Count == 0) ||
            TaggedSkills.Distinct(StringComparer.Ordinal).Count() != TaggedSkills.Count ||
            Traits.Count > 2 ||
            Traits.Distinct(StringComparer.Ordinal).Count() != Traits.Count)
            throw new InvalidOperationException("Fallout 2 character state is invalid.");
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

internal sealed record Fo2CharacterAppearanceContract(
    string Schema,
    string BasisPremadeId,
    string SourcePanelLogicalPath,
    string SourcePanelSha256,
    string LocalPanelPngSha256,
    string PreviewMode,
    string PortraitState,
    bool CustomFaceEdited,
    bool CustomPortraitGenerated,
    string FaceShapeId,
    string HairStyleId,
    string SkinToneId,
    string HairColorId,
    string EyeColorId,
    string BrowStyleId,
    string NoseStyleId,
    string MouthStyleId,
    string PortraitGeneratorId,
    string AppearanceRecipeId,
    string AppearanceRecipeSha256,
    string GeneratedPortraitPath,
    string GeneratedPortraitSha256,
    int GeneratedPortraitWidth,
    int GeneratedPortraitHeight,
    CharacterBodyProportions BodyProportions)
{
    internal const string ExpectedSchema = "opennv-fo2-character-appearance/v6";
    internal const string OwnedReliefPreview = "owned-panel-curved-relief-v1";
    internal const string GeneratedPortraitPreview = "opennv-local-classic-green-portrait-v1";
    internal const string OwnedPanelFaceShape = "owned-premade-panel";
    internal const string OwnedPanelHairStyle = "owned-premade-panel";
    internal const string OwnedPanelSkinTone = "owned-premade-panel";
    internal const string OwnedPanelHairColor = "owned-premade-panel";
    internal const string OwnedPanelEyeColor = "owned-premade-panel";
    internal const string OwnedPanelBrowStyle = "owned-premade-panel";
    internal const string OwnedPanelNoseStyle = "owned-premade-panel";
    internal const string OwnedPanelMouthStyle = "owned-premade-panel";
    internal const string NoPortraitGenerator = "none";
    internal const string NoAppearanceRecipe = "none";

    internal static Fo2CharacterAppearanceContract FromSelection(
        Fo2CharacterSelection selection) => new(
        ExpectedSchema,
        selection.Source.Id,
        selection.Source.Panel.LogicalPath,
        selection.Source.Panel.SourceSha256,
        selection.Source.Panel.PngSha256,
        OwnedReliefPreview,
        selection.Mode switch
        {
            Fo2CharacterSelection.PremadeMode => "owned-premade-panel",
            Fo2CharacterSelection.ModifyMode => "owned-premade-panel-modified-stats",
            Fo2CharacterSelection.CreateMode =>
                "owned-premade-panel-basis-pending-custom-face-editor",
            _ => throw new InvalidOperationException(
                "Fallout 2 appearance has an unsupported character mode."),
        },
        false,
        false,
        OwnedPanelFaceShape,
        OwnedPanelHairStyle,
        OwnedPanelSkinTone,
        OwnedPanelHairColor,
        OwnedPanelEyeColor,
        OwnedPanelBrowStyle,
        OwnedPanelNoseStyle,
        OwnedPanelMouthStyle,
        NoPortraitGenerator,
        NoAppearanceRecipe,
        "",
        "",
        "",
        0,
        0,
        Fo2CharacterBodyProfile.ForSex(selection.Profile.Sex));

    internal void Validate(Fo2CharacterSelection selection)
    {
        if (Schema != ExpectedSchema || BasisPremadeId != selection.Source.Id ||
            SourcePanelLogicalPath != selection.Source.Panel.LogicalPath ||
            SourcePanelSha256 != selection.Source.Panel.SourceSha256 ||
            LocalPanelPngSha256 != selection.Source.Panel.PngSha256)
            throw new InvalidOperationException(
                "Fallout 2 appearance/portrait contract differs from its owned source basis.");
        BodyProportions.Validate("fallout2-character-appearance");
        if (selection.Mode == Fo2CharacterSelection.PremadeMode)
        {
            if (PreviewMode != OwnedReliefPreview || this != FromSelection(selection))
                throw new InvalidOperationException(
                    "Fallout 2 premade appearance differs from its owned panel contract.");
            return;
        }
        Fo2ProceduralPortrait.Validate(this);
    }
}

internal static class Fo2CharacterBodyProfile
{
    internal static CharacterBodyProportions ForSex(string sex) =>
        sex.Equals("Male", StringComparison.OrdinalIgnoreCase)
            ? new CharacterBodyProportions(
                "fo2-chosen-one-broad-upper-lean-lower-v1",
                1.01f,
                1.12f,
                1.10f,
                0.96f,
                1.03f,
                0.94f,
                0.92f)
            : sex.Equals("Female", StringComparison.OrdinalIgnoreCase)
                ? CharacterBodyProportions.Neutral(
                    "fo2-chosen-one-female-neutral-v1")
                : throw new InvalidOperationException(
                    $"Fallout 2 body profile has an unsupported sex: {sex}");
}

internal sealed record Fo2CharacterSelection(
    string Mode,
    Fo2PremadeCharacter Source,
    Fo2CharacterProfile Profile,
    Fo2CharacterAppearanceContract? AppearanceState = null)
{
    internal const string PremadeMode = "owned-premade";
    internal const string ModifyMode = "modified-owned-premade";
    internal const string CreateMode = "custom-created-from-owned-rules";

    internal string Id => Mode == PremadeMode ? Source.Id : "custom";
    internal string Role => Mode == PremadeMode ? Source.Role : "Custom";
    internal string GcdSha256 => Source.GcdSha256;
    internal string BioSha256 => Source.BioSha256;
    internal Fo2CharacterAppearanceContract Appearance => AppearanceState ??
        Fo2CharacterAppearanceContract.FromSelection(this);

    internal static Fo2CharacterSelection FromPremade(Fo2PremadeCharacter source) =>
        new(PremadeMode, source, source.Profile);

    internal void Validate(Fo2CharacterStartCatalog catalog)
    {
        if (!catalog.Characters.Contains(Source) ||
            Mode != PremadeMode && Mode != ModifyMode && Mode != CreateMode)
            throw new InvalidOperationException(
                "Fallout 2 character selection source binding is invalid.");
        Source.Profile.Validate();
        Profile.Validate(Mode == CreateMode);
        Appearance.Validate(this);
        if (Mode == PremadeMode && !SameProfile(Profile, Source.Profile) ||
            Mode == ModifyMode &&
                (!Profile.TaggedSkills.SequenceEqual(Source.Profile.TaggedSkills) ||
                 !Profile.Traits.SequenceEqual(Source.Profile.Traits)))
            throw new InvalidOperationException(
                "Fallout 2 custom character changed an unsupported source rule.");
    }

    private static bool SameProfile(Fo2CharacterProfile left, Fo2CharacterProfile right) =>
        left.Name == right.Name && left.Age == right.Age && left.Sex == right.Sex &&
        left.Special.SequenceEqual(right.Special) &&
        left.SkillBonuses.SequenceEqual(right.SkillBonuses) &&
        left.TaggedSkills.SequenceEqual(right.TaggedSkills) &&
        left.Traits.SequenceEqual(right.Traits);
}

internal sealed class Fo2CharacterStartCatalog
{
    private const string CacheSchema = "opennv-fo2-character-start-cache/v1";
    private const string RecipeSchema = "opennv-fo2-character-start-recipe/v1";
    private const string ProfileSchema = "opennv-fo2-owned-profile/v1";
    internal const string LegacyRecipeId = "fo2-character-start-v1";
    internal const string FemaleFid = "0100003d";
    internal const string FemaleLogicalPath = "art\\critters\\hfprimaa.frm";
    internal const string FemaleWalkLogicalPath = "art\\critters\\hfprimab.frm";
    internal const string FemaleEquippedIdleLogicalPath = "art\\critters\\hfprimga.frm";
    internal const string FemaleEquippedWalkLogicalPath = "art\\critters\\hfprimgb.frm";
    internal const string FemalePrototypeLogicalPath = "proto\\critters\\00000002.pro";
    internal const string FemalePrototypePid = "01000002";
    internal static readonly string[] SkillNames =
    [
        "Small Guns", "Big Guns", "Energy Weapons", "Unarmed", "Melee Weapons",
        "Throwing", "First Aid", "Doctor", "Sneak", "Lockpick", "Steal", "Traps",
        "Science", "Repair", "Speech", "Barter", "Gambling", "Outdoorsman",
    ];
    internal static readonly string[] TraitNames =
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
        Fo2CharacterStartAsset inventory,
        IReadOnlyList<Fo2PremadeCharacter> characters,
        Fo2ArroyoPlayerPresentationSource femalePresentation,
        Fo2OpeningTailContract? openingTail,
        int verifiedResources)
    {
        ManifestPath = manifestPath;
        ManifestSha256 = manifestSha256;
        SourceProfileId = sourceProfileId;
        RecipeSha256 = recipeSha256;
        Picker = picker;
        Inventory = inventory;
        Characters = characters;
        FemalePresentation = femalePresentation;
        OpeningTail = openingTail;
        VerifiedResources = verifiedResources;
    }

    internal string ManifestPath { get; }
    internal string ManifestSha256 { get; }
    internal string SourceProfileId { get; }
    internal string RecipeSha256 { get; }
    internal Fo2CharacterStartAsset Picker { get; }
    internal Fo2CharacterStartAsset Inventory { get; }
    internal IReadOnlyList<Fo2PremadeCharacter> Characters { get; }
    internal Fo2ArroyoPlayerPresentationSource FemalePresentation { get; }
    internal Fo2OpeningTailContract? OpeningTail { get; }
    internal int VerifiedResources { get; }

    internal Fo2ArroyoPlayerPresentationSource PresentationFor(
        Fo2CharacterSelection character,
        Fo2ArroyoPlayerPresentationCatalog malePresentation)
    {
        character.Validate(this);
        if (malePresentation.SourceProfileId != SourceProfileId)
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
        var inventory = LoadAsset(
            cache.GetProperty("inventory"),
            recipe.GetProperty("inventory"),
            cacheRoot,
            "inventory");
        var recipeId = Fo2TemplePresentationCatalog.RequiredString(recipe, "id");
        var openingTail = recipe.TryGetProperty("openingTail", out var openingRecipe)
            ? Fo2OpeningTailContract.Load(
                cache.GetProperty("openingTail"),
                openingRecipe,
                cacheRoot)
            : null;
        if (recipeId == LegacyRecipeId && cache.TryGetProperty("openingTail", out _))
            throw new InvalidOperationException(
                "Legacy Fallout 2 character-start cache contains an opening tail.");
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

        var femaleRow = cache.GetProperty("femalePresentation");
        var female = LoadFemalePresentation(
            femaleRow,
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
            .Append($"{inventory.LogicalPath}|{inventory.SourceSha256}")
            .Append($"{female.LogicalPath}|{female.SourceSha256}")
            .Append($"{female.Walk.LogicalPath}|{female.Walk.SourceSha256}")
            .Append(
                $"{FemaleEquippedIdleLogicalPath}|" +
                $"{female.EquippedWeapon.IdleDirections.Values.First().SourceSha256}")
            .Append(
                $"{female.EquippedWeapon.Walk.LogicalPath}|" +
                $"{female.EquippedWeapon.Walk.SourceSha256}")
            .Append(
                $"proto\\critters\\critters.lst|" +
                Fo2TemplePresentationCatalog.RequiredHash(
                    femaleRow.GetProperty("prototype"),
                    "listSha256"))
            .Append(
                $"{FemalePrototypeLogicalPath}|" +
                Fo2TemplePresentationCatalog.RequiredHash(
                    femaleRow.GetProperty("prototype"),
                    "sha256"))
            .ToList();
        if (openingTail is not null)
        {
            required.Add($"{openingTail.MovieLogicalPath}|{openingTail.MovieSha256}");
            required.Add(
                $"{openingTail.FadeConfigLogicalPath}|{openingTail.FadeConfigSha256}");
        }
        if (identities.Count != resources.Length || required.Any(row => !identities.Contains(row)))
            throw new InvalidOperationException(
                "Fallout 2 character-start resource identity closure failed.");
        var counts = cache.GetProperty("counts");
        if (counts.GetProperty("premades").GetInt32() != characters.Count ||
            counts.GetProperty("uiPngs").GetInt32() != 2 + characters.Count ||
            counts.GetProperty("femaleDirectionPngs").GetInt32() !=
                female.Directions.Count ||
            counts.GetProperty("femaleWalkFramePngs").GetInt32() !=
                female.Walk.Directions.Values.Sum(row => row.Count) ||
            counts.GetProperty("femaleEquippedIdleDirectionPngs").GetInt32() !=
                female.EquippedWeapon.IdleDirections.Count ||
            counts.GetProperty("femaleEquippedWalkFramePngs").GetInt32() !=
                female.EquippedWeapon.Walk.Directions.Values.Sum(row => row.Count) ||
            counts.GetProperty("femaleClosedReliefArtifacts").GetInt32() !=
                female.Directions.Count +
                    female.Walk.Directions.Values.Sum(row => row.Count) +
                    female.EquippedWeapon.IdleDirections.Count +
                    female.EquippedWeapon.Walk.Directions.Values.Sum(row => row.Count) ||
            openingTail is not null &&
                counts.GetProperty("openingTailPngs").GetInt32() != openingTail.Frames.Count ||
            openingTail is null && counts.TryGetProperty("openingTailPngs", out _) ||
            counts.GetProperty("sourceResources").GetInt32() != resources.Length)
            throw new InvalidOperationException(
                "Fallout 2 character-start cache counts drifted.");

        return new Fo2CharacterStartCatalog(
            manifestPath,
            Fo2TemplePresentationCatalog.Sha256(cacheBytes),
            expectedSourceProfileId,
            Fo2TemplePresentationCatalog.RequiredHash(cache.GetProperty("recipe"), "sha256"),
            picker,
            inventory,
            characters,
            female,
            openingTail,
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
        var descriptorId = Fo2TemplePresentationCatalog.RequiredString(descriptor, "id");
        var recipeId = Fo2TemplePresentationCatalog.RequiredString(recipe, "id");
        if (Fo2TemplePresentationCatalog.RequiredString(descriptor, "schema") != RecipeSchema ||
            descriptorId != recipeId ||
            Path.GetFileNameWithoutExtension(path) != recipeId ||
            recipeId != LegacyRecipeId &&
                !recipe.TryGetProperty("openingTail", out _) ||
            Fo2TemplePresentationCatalog.RequiredString(recipe, "schema") != RecipeSchema ||
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
        ReadInts(row.GetProperty("skillBonuses")),
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
        var expectedEquipped = expected.GetProperty("equippedWeapon");
        if (Fo2TemplePresentationCatalog.RequiredString(row, "fid") != FemaleFid ||
            Fo2TemplePresentationCatalog.RequiredString(row, "fid") !=
                Fo2TemplePresentationCatalog.RequiredString(expected, "fid") ||
            Fo2TemplePresentationCatalog.RequiredString(row, "artListEntry") !=
                "hfprim,11,1" ||
            row.GetProperty("artIndex").GetInt32() != 61 ||
            Fo2TemplePresentationCatalog.RequiredString(row, "logicalPath") !=
                FemaleLogicalPath ||
            row.GetProperty("frame").GetInt32() != 0 ||
            row.GetProperty("animationPlayback").GetBoolean() ||
            Fo2TemplePresentationCatalog.RequiredString(
                expected,
                "prototypeListLogicalPath") != "proto\\critters\\critters.lst" ||
            expected.GetProperty("prototypeListIndex").GetInt32() != 2 ||
            Fo2TemplePresentationCatalog.RequiredString(
                expected,
                "prototypeListEntry") != "00000002.pro" ||
            Fo2TemplePresentationCatalog.RequiredString(
                expected,
                "prototypeLogicalPath") != FemalePrototypeLogicalPath ||
            Fo2TemplePresentationCatalog.RequiredString(
                expected,
                "prototypePid") != FemalePrototypePid ||
            Fo2TemplePresentationCatalog.RequiredString(
                expected,
                "walkAnimationCode") != "AB" ||
            Fo2TemplePresentationCatalog.RequiredString(
                expected,
                "walkLogicalPath") != FemaleWalkLogicalPath ||
            !ReadInts(expected.GetProperty("walkFrames")).SequenceEqual(
                Enumerable.Range(0, Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection)) ||
            expected.GetProperty("walkFps").GetInt32() !=
                Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerSecond ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "itemFid") != Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemFid ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "itemPid") != Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemPid ||
            expectedEquipped.GetProperty("weaponAnimationCode").GetInt32() !=
                Fo2ArroyoPlayerPresentationCatalog.ExpectedWeaponAnimationCode ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "weaponArtSuffix") != "g" ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "idleAnimationCode") != "GA" ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "idleLogicalPath") != FemaleEquippedIdleLogicalPath ||
            expectedEquipped.GetProperty("idleFrame").GetInt32() != 0 ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "walkAnimationCode") != "GB" ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "walkLogicalPath") != FemaleEquippedWalkLogicalPath ||
            !ReadInts(expectedEquipped.GetProperty("walkFrames")).SequenceEqual(
                Enumerable.Range(0, Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection)) ||
            Fo2TemplePresentationCatalog.RequiredString(
                expectedEquipped,
                "geometryDisposition") !=
                Fo2ArroyoPlayerPresentationCatalog.EquippedGeometryDisposition)
            throw new InvalidOperationException(
                "Fallout 2 female player source binding drifted.");
        var prototype = row.GetProperty("prototype");
        if (Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "listLogicalPath") != "proto\\critters\\critters.lst" ||
            prototype.GetProperty("listIndex").GetInt32() != 2 ||
            Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "listEntry") != "00000002.pro" ||
            Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "logicalPath") != FemalePrototypeLogicalPath ||
            Fo2TemplePresentationCatalog.RequiredString(
                prototype,
                "pid") != FemalePrototypePid ||
            Fo2TemplePresentationCatalog.RequiredString(prototype, "fid") != FemaleFid ||
            prototype.GetProperty("bytes").GetInt64() <= 0)
            throw new InvalidOperationException(
                "Fallout 2 female player PRO/FID binding drifted.");
        var frames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
        foreach (var artifact in row.GetProperty("directions").EnumerateArray())
        {
            var direction = artifact.GetProperty("rotation").GetInt32();
            var frame = Fo2ArroyoPlayerPresentationCatalog.LoadFrame(
                artifact,
                cacheRoot,
                $"Fallout 2 female idle PNG direction {direction}");
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
        var walkArt = row.GetProperty("walkArt");
        var walkSourceSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            walkArt,
            "sourceSha256");
        if (Fo2TemplePresentationCatalog.RequiredString(
                walkArt,
                "animationCode") != "AB" ||
            Fo2TemplePresentationCatalog.RequiredString(
                walkArt,
                "logicalPath") != FemaleWalkLogicalPath ||
            walkArt.GetProperty("sourceBytes").GetInt64() <= 0 ||
            walkArt.GetProperty("fps").GetInt32() !=
                Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerSecond ||
            walkArt.GetProperty("actionFrame").GetInt32() != 0 ||
            walkArt.GetProperty("framesPerDirection").GetInt32() !=
                Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection ||
            !walkArt.GetProperty("animationPlayback").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 female AB walk binding drifted.");
        var walkFrames = new Dictionary<int, Dictionary<int, Fo2ArroyoPlayerFrame>>();
        foreach (var artifact in walkArt.GetProperty("directions").EnumerateArray())
        {
            var frame = Fo2ArroyoPlayerPresentationCatalog.LoadFrame(
                artifact,
                cacheRoot,
                "Fallout 2 female walk PNG");
            if (Fo2TemplePresentationCatalog.RequiredString(artifact, "kind") !=
                    "female-player-walk" ||
                frame.LogicalPath != FemaleWalkLogicalPath ||
                frame.SourceSha256 != walkSourceSha256 ||
                frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                frame.Frame is < 0 or >=
                    Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection ||
                frame.Width <= 0 || frame.Height <= 0)
                throw new InvalidOperationException(
                    "Fallout 2 female walk artifact drifted.");
            if (!walkFrames.TryGetValue(frame.Direction, out var directionFrames))
            {
                directionFrames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
                walkFrames.Add(frame.Direction, directionFrames);
            }
            if (!directionFrames.TryAdd(frame.Frame, frame))
                throw new InvalidOperationException(
                    "Duplicate Fallout 2 female walk artifact.");
        }
        var walkDirections = walkFrames.ToDictionary(
            row => row.Key,
            row => (IReadOnlyList<Fo2ArroyoPlayerFrame>)row.Value
                .OrderBy(frame => frame.Key).Select(frame => frame.Value).ToArray());
        if (!walkDirections.Keys.Order().SequenceEqual(
                Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            walkDirections.Values.Any(direction =>
                direction.Count != Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection ||
                !direction.Select(frame => frame.Frame).SequenceEqual(
                    Enumerable.Range(
                        0,
                        Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection))))
            throw new InvalidOperationException(
                "Fallout 2 female walk direction/frame coverage drifted.");
        var equippedArt = row.GetProperty("equippedWeaponArt");
        var equippedIdle = equippedArt.GetProperty("idle");
        var equippedWalk = equippedArt.GetProperty("walk");
        var equippedIdleSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            equippedIdle,
            "sourceSha256");
        var equippedWalkSha256 = Fo2TemplePresentationCatalog.RequiredHash(
            equippedWalk,
            "sourceSha256");
        if (Fo2TemplePresentationCatalog.RequiredString(equippedArt, "itemFid") !=
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemFid ||
            Fo2TemplePresentationCatalog.RequiredString(equippedArt, "itemPid") !=
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemPid ||
            equippedArt.GetProperty("weaponAnimationCode").GetInt32() !=
                Fo2ArroyoPlayerPresentationCatalog.ExpectedWeaponAnimationCode ||
            Fo2TemplePresentationCatalog.RequiredString(equippedArt, "weaponArtSuffix") != "g" ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedArt,
                "geometryDisposition") !=
                Fo2ArroyoPlayerPresentationCatalog.EquippedGeometryDisposition ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedIdle,
                "animationCode") != "GA" ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedIdle,
                "logicalPath") != FemaleEquippedIdleLogicalPath ||
            equippedIdle.GetProperty("sourceBytes").GetInt64() <= 0 ||
            equippedIdle.GetProperty("framesPerDirection").GetInt32() <= 0 ||
            equippedIdle.GetProperty("animationPlayback").GetBoolean() ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedWalk,
                "animationCode") != "GB" ||
            Fo2TemplePresentationCatalog.RequiredString(
                equippedWalk,
                "logicalPath") != FemaleEquippedWalkLogicalPath ||
            equippedWalk.GetProperty("sourceBytes").GetInt64() <= 0 ||
            equippedWalk.GetProperty("fps").GetInt32() !=
                Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerSecond ||
            equippedWalk.GetProperty("actionFrame").GetInt32() != 0 ||
            equippedWalk.GetProperty("framesPerDirection").GetInt32() !=
                Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection ||
            !equippedWalk.GetProperty("animationPlayback").GetBoolean())
            throw new InvalidOperationException(
                "Fallout 2 female Spear-equipped GA/GB binding drifted.");
        var equippedIdleFrames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
        foreach (var artifact in equippedIdle.GetProperty("directions").EnumerateArray())
        {
            var frame = Fo2ArroyoPlayerPresentationCatalog.LoadFrame(
                artifact,
                cacheRoot,
                "Fallout 2 female Spear-equipped idle PNG");
            if (Fo2TemplePresentationCatalog.RequiredString(artifact, "kind") !=
                    "female-player-equipped" ||
                frame.LogicalPath != FemaleEquippedIdleLogicalPath ||
                frame.SourceSha256 != equippedIdleSha256 || frame.Frame != 0 ||
                frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                !equippedIdleFrames.TryAdd(frame.Direction, frame))
                throw new InvalidOperationException(
                    "Fallout 2 female Spear-equipped idle artifact drifted.");
        }
        var equippedWalkFrames = new Dictionary<int, Dictionary<int, Fo2ArroyoPlayerFrame>>();
        foreach (var artifact in equippedWalk.GetProperty("directions").EnumerateArray())
        {
            var frame = Fo2ArroyoPlayerPresentationCatalog.LoadFrame(
                artifact,
                cacheRoot,
                "Fallout 2 female Spear-equipped walk PNG");
            if (Fo2TemplePresentationCatalog.RequiredString(artifact, "kind") !=
                    "female-player-equipped-walk" ||
                frame.LogicalPath != FemaleEquippedWalkLogicalPath ||
                frame.SourceSha256 != equippedWalkSha256 ||
                frame.Direction is < 0 or >= Fo1HexMath.DirectionCount ||
                frame.Frame is < 0 or >=
                    Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection)
                throw new InvalidOperationException(
                    "Fallout 2 female Spear-equipped walk artifact drifted.");
            if (!equippedWalkFrames.TryGetValue(frame.Direction, out var directionFrames))
            {
                directionFrames = new Dictionary<int, Fo2ArroyoPlayerFrame>();
                equippedWalkFrames.Add(frame.Direction, directionFrames);
            }
            if (!directionFrames.TryAdd(frame.Frame, frame))
                throw new InvalidOperationException(
                    "Duplicate Fallout 2 female Spear-equipped walk artifact.");
        }
        var equippedWalkDirections = equippedWalkFrames.ToDictionary(
            row => row.Key,
            row => (IReadOnlyList<Fo2ArroyoPlayerFrame>)row.Value
                .OrderBy(frame => frame.Key).Select(frame => frame.Value).ToArray());
        if (!equippedIdleFrames.Keys.Order().SequenceEqual(
                Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            !equippedWalkDirections.Keys.Order().SequenceEqual(
                Enumerable.Range(0, Fo1HexMath.DirectionCount)) ||
            equippedWalkDirections.Values.Any(direction =>
                direction.Count != Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection ||
                !direction.Select(frame => frame.Frame).SequenceEqual(
                    Enumerable.Range(
                        0,
                        Fo2ArroyoPlayerPresentationCatalog.WalkFramesPerDirection))))
            throw new InvalidOperationException(
                "Fallout 2 female Spear-equipped direction/frame coverage drifted.");
        return new Fo2ArroyoPlayerPresentationSource(
            sourceProfileId,
            "CHOSEN_ONE_OWNED_HFPRIM_DIRECTIONAL_FRM",
            FemaleFid,
            FemalePrototypePid,
            FemalePrototypeLogicalPath,
            Fo2TemplePresentationCatalog.RequiredHash(prototype, "sha256"),
            FemaleLogicalPath,
            sourceSha256,
            frames,
            new Fo2ArroyoPlayerAnimation(
                "AB",
                FemaleWalkLogicalPath,
                walkSourceSha256,
                walkArt.GetProperty("fps").GetInt32(),
                walkArt.GetProperty("actionFrame").GetInt32(),
                walkDirections),
            new Fo2ArroyoEquippedWeaponPresentation(
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemFid,
                Fo2ArroyoPlayerPresentationCatalog.ExpectedEquippedItemPid,
                Fo2ArroyoPlayerPresentationCatalog.ExpectedWeaponAnimationCode,
                "g",
                Fo2ArroyoPlayerPresentationCatalog.EquippedGeometryDisposition,
                equippedIdleFrames,
                new Fo2ArroyoPlayerAnimation(
                    "GB",
                    FemaleEquippedWalkLogicalPath,
                    equippedWalkSha256,
                    equippedWalk.GetProperty("fps").GetInt32(),
                    equippedWalk.GetProperty("actionFrame").GetInt32(),
                    equippedWalkDirections)),
            Fo2ArroyoPlayerPresentationCatalog.ExpectedReliefDepthMeters,
            Fo2ArroyoPlayerPresentationCatalog.ExpectedReliefSideRoughness);
    }

    private static int[] ReadInts(JsonElement source) =>
        source.EnumerateArray().Select(row => row.GetInt32()).ToArray();
}
