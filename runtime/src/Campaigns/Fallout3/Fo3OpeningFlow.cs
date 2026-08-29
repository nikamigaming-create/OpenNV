using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal static class Fo3OpeningFlowNumericContracts
{
    internal const int FormIdHexCharacters = 8;
    internal const int Sha256HexCharacters = 64;
    internal const int UiLayer = 120;
    internal const int MarginPixels = 48;
    internal const int SeparationPixels = 18;
    internal const int TitleFontPixels = 36;
    internal const int BodyFontPixels = 22;
    internal const int ButtonMinimumHeightPixels = 54;
    internal const float PanelWidthFraction = 0.62f;
    internal const float PanelHeightFraction = 0.72f;
    internal const float Center = 0.5f;
    internal const float DimmedColorScale = 0.68f;
    internal const float PanelAlpha = 0.94f;
    internal const float SkipButtonOffsetXPixels = -220.0f;
    internal const float SkipButtonOffsetYPixels = 24.0f;
    internal const float SkipButtonWidthPixels = 190.0f;
    internal const float AppearancePreviewTexturePixels = 150.0f;
    internal const float VaultPreviewMarginPixels = 24.0f;
    internal const float VaultPreviewPanelWidthPixels = 560.0f;
    internal const float BoundaryHorizontalInsetPixels = 120.0f;
    internal const float BoundaryTopOffsetPixels = -240.0f;
    internal const float BoundaryBottomOffsetPixels = -20.0f;
    internal const float BoundaryPanelAlpha = 0.9f;
    internal const int FaceGenSymmetricGeometryFloats = 50;
    internal const int FaceGenAsymmetricGeometryFloats = 30;
    internal const int FaceGenSymmetricTextureFloats = 50;
}

internal sealed record Fo3SexChoice(string Label, string EngineSex);

internal sealed record Fo3AppearanceAsset(
    string SourcePath,
    string SourceSha256,
    string PreviewPath,
    string PreviewSha256);

internal sealed record Fo3AppearanceOption(
    string FormId,
    string EditorId,
    string Label,
    Fo3AppearanceAsset Texture);

internal sealed record Fo3FaceGenDefaults(
    string SymmetricGeometrySha256,
    string AsymmetricGeometrySha256,
    string SymmetricTextureSha256);

internal sealed record Fo3AppearanceSex(
    Fo3AppearanceAsset HeadTexture,
    IReadOnlyList<Fo3AppearanceOption> HairOptions,
    IReadOnlyList<Fo3AppearanceOption> EyeOptions,
    string DefaultHairFormId,
    string DefaultEyesFormId,
    Fo3FaceGenDefaults FaceGen);

internal sealed record Fo3AppearanceRace(
    string FormId,
    string ChildRaceFormId,
    string EditorId,
    string Label,
    IReadOnlyDictionary<string, Fo3AppearanceSex> Sex);

internal sealed record Fo3AppearanceUi(
    int PanelWidth,
    int PanelHeight,
    int ListItemWidth,
    int ListItemHeight,
    Fo3AppearanceAsset BackgroundTexture);

internal sealed record Fo3AppearanceSelection(
    Fo3AppearanceRace Race,
    Fo3AppearanceSex Sex,
    Fo3AppearanceOption Hair,
    Fo3AppearanceOption Eyes);

internal sealed record Fo3AppearanceContract(
    int Stage,
    int MenuEnteredStage,
    int AcceptedStage,
    string Command,
    string AcceptedStageCommand,
    string PlayerEditorId,
    string DefaultRaceFormId,
    Fo3AppearanceUi Ui,
    IReadOnlyList<Fo3AppearanceRace> Races)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-appearance/v1";
    private const string ExpectedStatus = "source-backed-default-selection";
    private const string ExpectedPreview = "owned-head-hair-eye-source-textures-not-a-3d-face-render";

    internal static Fo3AppearanceContract Load(JsonElement source)
    {
        if (RequiredString(source, "schema") != ExpectedSchema ||
            RequiredString(source, "status") != ExpectedStatus ||
            RequiredString(source, "preview") != ExpectedPreview)
            throw new InvalidOperationException("Fallout 3 CG00 appearance contract is unsupported.");
        var stage = RequiredInteger(source, "stage");
        var menuEnteredStage = RequiredInteger(source, "menuEnteredStage");
        var acceptedStage = RequiredInteger(source, "acceptedStage");
        if (!(stage < menuEnteredStage && menuEnteredStage < acceptedStage))
            throw new InvalidOperationException("Fallout 3 CG00 appearance stages are not monotonic.");
        var player = RequiredObject(source, "player");
        var playerEditorId = RequiredString(player, "editorId");
        var defaultRaceFormId = RequiredFormId(player, "defaultRaceFormId");
        var uiSource = RequiredObject(source, "ui");
        var ui = new Fo3AppearanceUi(
            PositiveInteger(uiSource, "panelWidth"),
            PositiveInteger(uiSource, "panelHeight"),
            PositiveInteger(uiSource, "listItemWidth"),
            PositiveInteger(uiSource, "listItemHeight"),
            LoadAsset(RequiredObject(uiSource, "backgroundTexture")));
        var races = RequiredArray(source, "races").EnumerateArray().Select(LoadRace).ToArray();
        if (races.Length == 0 || races.Select(value => value.FormId).Distinct().Count() != races.Length ||
            races.All(value => value.FormId != defaultRaceFormId))
            throw new InvalidOperationException("Fallout 3 playable race inventory is incomplete.");
        return new Fo3AppearanceContract(
            stage,
            menuEnteredStage,
            acceptedStage,
            RequiredString(source, "command"),
            RequiredString(source, "acceptedStageCommand"),
            playerEditorId,
            defaultRaceFormId,
            ui,
            races);
    }

    internal Fo3AppearanceSelection DefaultSelection(string engineSex)
    {
        var race = Races.Single(value => value.FormId == DefaultRaceFormId);
        var sex = race.Sex[engineSex];
        return new Fo3AppearanceSelection(
            race,
            sex,
            sex.HairOptions.Single(value => value.FormId == sex.DefaultHairFormId),
            sex.EyeOptions.Single(value => value.FormId == sex.DefaultEyesFormId));
    }

    internal Fo3AppearanceSelection ResolveSelection(
        string engineSex,
        string raceFormId,
        string childRaceFormId,
        string hairFormId,
        string eyesFormId)
    {
        var race = Races.Single(value =>
            value.FormId == raceFormId && value.ChildRaceFormId == childRaceFormId);
        var sex = race.Sex[engineSex];
        return new Fo3AppearanceSelection(
            race,
            sex,
            sex.HairOptions.Single(value => value.FormId == hairFormId),
            sex.EyeOptions.Single(value => value.FormId == eyesFormId));
    }

    private static Fo3AppearanceRace LoadRace(JsonElement source)
    {
        var sexSource = RequiredObject(source, "sex");
        var sexes = new Dictionary<string, Fo3AppearanceSex>(StringComparer.Ordinal)
        {
            ["male"] = LoadSex(RequiredObject(sexSource, "male")),
            ["female"] = LoadSex(RequiredObject(sexSource, "female")),
        };
        return new Fo3AppearanceRace(
            RequiredFormId(source, "formId"),
            RequiredFormId(source, "childRaceFormId"),
            RequiredString(source, "editorId"),
            RequiredString(source, "label"),
            sexes);
    }

    private static Fo3AppearanceSex LoadSex(JsonElement source)
    {
        var hair = RequiredArray(source, "hairOptions").EnumerateArray()
            .Select(value => LoadOption(value, "HAIR")).ToArray();
        var eyes = RequiredArray(source, "eyeOptions").EnumerateArray()
            .Select(value => LoadOption(value, "EYES")).ToArray();
        var defaultHair = RequiredFormId(source, "defaultHairFormId");
        var defaultEyes = RequiredFormId(source, "defaultEyesFormId");
        if (hair.Length == 0 || eyes.Length == 0 ||
            hair.Select(value => value.FormId).Distinct().Count() != hair.Length ||
            eyes.Select(value => value.FormId).Distinct().Count() != eyes.Length ||
            hair.All(value => value.FormId != defaultHair) ||
            eyes.All(value => value.FormId != defaultEyes))
            throw new InvalidOperationException("Fallout 3 sex-aware appearance options are incomplete.");
        var face = RequiredObject(source, "faceGenDefaults");
        return new Fo3AppearanceSex(
            LoadAsset(RequiredObject(source, "headTexture")),
            hair,
            eyes,
            defaultHair,
            defaultEyes,
            new Fo3FaceGenDefaults(
                ValidateFloatContract(
                    RequiredObject(face, "symmetricGeometry"),
                    Fo3OpeningFlowNumericContracts.FaceGenSymmetricGeometryFloats),
                ValidateFloatContract(
                    RequiredObject(face, "asymmetricGeometry"),
                    Fo3OpeningFlowNumericContracts.FaceGenAsymmetricGeometryFloats),
                ValidateFloatContract(
                    RequiredObject(face, "symmetricTexture"),
                    Fo3OpeningFlowNumericContracts.FaceGenSymmetricTextureFloats)));
    }

    private static Fo3AppearanceOption LoadOption(JsonElement source, string recordType)
    {
        if (RequiredString(source, "recordType") != recordType)
            throw new InvalidOperationException("Fallout 3 appearance option record type differs.");
        return new Fo3AppearanceOption(
            RequiredFormId(source, "formId"),
            RequiredString(source, "editorId"),
            RequiredString(source, "label"),
            LoadAsset(RequiredObject(source, "texture")));
    }

    private static Fo3AppearanceAsset LoadAsset(JsonElement source)
    {
        var sourcePath = RequiredString(source, "output");
        var sourceSha256 = VerifyAsset(
            sourcePath,
            RequiredLong(source, "outputBytes"),
            RequiredString(source, "outputSha256"));
        var previewPath = RequiredString(source, "previewOutput");
        var previewSha256 = VerifyAsset(
            previewPath,
            RequiredLong(source, "previewOutputBytes"),
            RequiredString(source, "previewOutputSha256"));
        if (PositiveInteger(source, "previewWidth") <= 0 ||
            PositiveInteger(source, "previewHeight") <= 0)
            throw new InvalidOperationException("Fallout 3 appearance preview dimensions are invalid.");
        return new Fo3AppearanceAsset(
            System.IO.Path.GetFullPath(sourcePath),
            sourceSha256,
            System.IO.Path.GetFullPath(previewPath),
            previewSha256);
    }

    private static string VerifyAsset(string path, long expectedBytes, string expectedSha256)
    {
        if (!ValidHex(expectedSha256, Fo3OpeningFlowNumericContracts.Sha256HexCharacters))
            throw new InvalidOperationException("Fallout 3 appearance texture hash is invalid.");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedBytes)
            throw new InvalidOperationException("Fallout 3 appearance texture is absent or changed.");
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 appearance texture hash differs.");
        return actualSha256;
    }

    private static string ValidateFloatContract(JsonElement source, int expectedCount)
    {
        if (RequiredInteger(source, "count") != expectedCount)
            throw new InvalidOperationException("Fallout 3 FaceGen coordinate count differs.");
        var values = RequiredArray(source, "values").EnumerateArray()
            .Select(value => (float)value.GetDouble()).ToArray();
        if (values.Length != expectedCount || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("Fallout 3 FaceGen coordinates are incomplete.");
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
            foreach (var value in values)
                writer.Write(value);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        var expectedSha256 = RequiredString(source, "sha256");
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 FaceGen coordinate hash differs.");
        return actualSha256;
    }

    private static int PositiveInteger(JsonElement source, string name)
    {
        var value = RequiredInteger(source, name);
        if (value <= 0)
            throw new InvalidOperationException($"Fallout 3 appearance UI field {name} is invalid.");
        return value;
    }

    private static string RequiredFormId(JsonElement source, string name)
    {
        var value = RequiredString(source, name);
        if (!ValidHex(value, Fo3OpeningFlowNumericContracts.FormIdHexCharacters))
            throw new InvalidOperationException($"Fallout 3 appearance FormID {name} is invalid.");
        return value;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 appearance field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 appearance field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 appearance field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 appearance field {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidOperationException($"Fallout 3 appearance field {name} is invalid.");
        return result;
    }

    private static bool ValidHex(string value, int characters) =>
        value.Length == characters && value.All(Uri.IsHexDigit);
}

internal sealed record Fo3OwnedProfile(
    string Path,
    string Sha256,
    string ProfileId,
    Fo3BirthSliceContract BirthSlice,
    string QuestEditorId,
    string QuestFormId,
    string SexTitle,
    IReadOnlyList<Fo3SexChoice> SexChoices,
    int NameStage,
    string NameCommand,
    int AppearanceStage,
    string AppearanceCommand,
    Fo3AppearanceContract Appearance,
    Fo3PlayerPackageTransition Section4Transition,
    Fo3Stage65AppearanceTransition Stage65Appearance,
    Fo3Stage80Transition Stage80Transition,
    Fo3Stage85Transition Stage85Transition,
    Fo3Stage90Transition Stage90Transition,
    Fo3Stage100Transition Stage100Transition,
    Fo3Cg01Stage0Transition Cg01Stage0Transition,
    Fo3Cg01Stage10Transition Cg01Stage10Transition,
    Fo3Cg01Stage12Transition Cg01Stage12Transition,
    string MainMenuMusicPath,
    string IntroVideoPath,
    Color InterfaceColor)
{
    private const string ExpectedSchema = "opennv-owned-game-profile/v1";
    private const string ExpectedCampaign = "Fallout3";
    private const string ExpectedStatus = "registered-owned-profile";

    internal static Fo3OwnedProfile Load(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(fullPath);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (RequiredString(root, "schema") != ExpectedSchema ||
            RequiredString(root, "campaign") != ExpectedCampaign ||
            RequiredString(root, "status") != ExpectedStatus)
            throw new InvalidOperationException("Fallout 3 profile identity is unsupported.");

        var capabilities = RequiredObject(root, "capabilities");
        if (!RequiredBoolean(capabilities, "characterSelectionContractResolved") ||
            !RequiredBoolean(capabilities, "cg00SexAndNameRuntimeReady") ||
            !RequiredBoolean(capabilities, "cg00AppearanceRuntimeReady") ||
            !RequiredBoolean(capabilities, "cg00Section4PackageContractReady") ||
            !RequiredBoolean(capabilities, "cg00Stage65AppearanceContractReady") ||
            !RequiredBoolean(capabilities, "cg00Stage80ContractReady") ||
            !RequiredBoolean(capabilities, "cg00Stage85ContractReady") ||
            !RequiredBoolean(capabilities, "cg00Stage90ContractReady") ||
            !RequiredBoolean(capabilities, "cg00Stage100ContractReady") ||
            !RequiredBoolean(capabilities, "cg01Stage0ContractReady") ||
            !RequiredBoolean(capabilities, "cg01Stage10ContractReady") ||
            !RequiredBoolean(capabilities, "cg01Stage12ContractReady") ||
            !RequiredBoolean(capabilities, "vault101BirthGraphCompiled") ||
            !RequiredBoolean(capabilities, "mainMenuRuntimeReady") ||
            !RequiredBoolean(capabilities, "introVideoRuntimeReady") ||
            !RequiredBoolean(capabilities, "runtimeBootReady"))
            throw new InvalidOperationException(
                "Fallout 3 profile has not resolved the runnable CG00 character-selection subset.");

        var install = RequiredObject(root, "install");
        VerifySourceFile(RequiredObject(install, "master"));

        var opening = RequiredObject(root, "opening");
        var birthSlice = Fo3BirthSliceContract.Load(
            RequiredObject(opening, "birthSlice"),
            install);
        var selection = RequiredObject(opening, "characterSelection");
        var questEditorId = RequiredString(selection, "questEditorId");
        var name = RequiredObject(selection, "name");
        var appearance = RequiredObject(selection, "appearance");
        var appearanceContract = Fo3AppearanceContract.Load(appearance);
        var section4Transition = Fo3PlayerPackageTransition.Load(
            RequiredObject(selection, "section4Transition"),
            appearanceContract.AcceptedStage,
            appearanceContract.AcceptedStageCommand);
        var stage65Appearance = Fo3Stage65AppearanceTransition.Load(
            RequiredObject(selection, "stage65Appearance"),
            appearanceContract.AcceptedStage,
            section4Transition.NextStage,
            section4Transition.NextStageSourceSha256,
            section4Transition.NextStageContractSchema,
            appearanceContract.Races.Select(value => value.FormId).ToArray(),
            section4Transition.NextCommandKinds);
        var nameStage = RequiredInteger(name, "stage");
        var appearanceStage = RequiredInteger(appearance, "stage");
        var quest = RequiredArray(opening, "quests")
            .EnumerateArray()
            .Single(value => RequiredString(value, "editorId") == questEditorId);
        var stages = RequiredArray(quest, "stages")
            .EnumerateArray()
            .Select(value => value.GetInt32())
            .ToHashSet();
        if (!stages.Contains(nameStage) || !stages.Contains(appearanceStage))
            throw new InvalidOperationException(
                "Fallout 3 character-selection stages do not join the owned CG00 quest.");
        var stage80Transition = Fo3Stage80Transition.Load(
            RequiredObject(selection, "postStage65Dialogue"),
            RequiredObject(selection, "stage80Transition"),
            stage65Appearance.Stage,
            RequiredString(quest, "formId"));
        var stage85Transition = Fo3Stage85Transition.Load(
            RequiredObject(selection, "postStage80Dialogue"),
            RequiredObject(selection, "stage85Transition"),
            stage80Transition.Stage,
            RequiredString(quest, "formId"));
        var stage90Transition = Fo3Stage90Transition.Load(
            RequiredObject(selection, "postStage85Dialogue"),
            RequiredObject(selection, "stage90Transition"),
            stage85Transition.Stage,
            stage80Transition.Stage,
            RequiredString(quest, "formId"));
        var stage100Transition = Fo3Stage100Transition.Load(
            RequiredObject(selection, "stage100Transition"),
            stage90Transition,
            RequiredString(quest, "formId"));
        var cg01Source = RequiredObject(selection, "cg01Stage0Transition");
        var cg01Stage0Transition = Fo3Cg01Stage0Transition.Load(
            cg01Source,
            stage100Transition);
        var cg01Stage10Transition = Fo3Cg01Stage10Transition.Load(
            RequiredObject(cg01Source, "postStage5Transition"),
            cg01Stage0Transition);
        var cg01Stage12Transition = Fo3Cg01Stage12Transition.Load(
            RequiredObject(
                RequiredObject(cg01Source, "postStage5Transition"),
                "postStage10TriggerTransition"),
            cg01Stage0Transition,
            cg01Stage10Transition);
        if (!stages.Contains(stage65Appearance.Stage) ||
            !stages.Contains(stage80Transition.Stage) ||
            !stages.Contains(stage85Transition.Stage) ||
            !stages.Contains(stage90Transition.Stage) ||
            !stages.Contains(stage100Transition.Stage))
            throw new InvalidOperationException(
                "Fallout 3 compiled CG00 transitions do not join the owned quest stages.");

        var sex = RequiredObject(selection, "sex");
        var sexChoices = RequiredArray(sex, "choices")
            .EnumerateArray()
            .Select(value => new Fo3SexChoice(
                RequiredString(value, "label"),
                RequiredString(value, "engineSex")))
            .ToArray();
        if (sexChoices.Length != RequiredInteger(sex, "choiceCount") ||
            !sexChoices.Select(value => value.EngineSex).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "male", "female" }))
            throw new InvalidOperationException("Fallout 3 owned sex choices are incomplete.");

        var menu = RequiredObject(root, "mainMenu");
        var mainMenuMusic = RequiredObject(menu, "music");
        VerifySourceFile(mainMenuMusic);
        var settings = RequiredArray(menu, "iniSettings").EnumerateArray().ToArray();
        var interfaceColor = new Color(
            SettingByte(settings, "iSystemColorMainMenuRed") / (float)byte.MaxValue,
            SettingByte(settings, "iSystemColorMainMenuGreen") / (float)byte.MaxValue,
            SettingByte(settings, "iSystemColorMainMenuBlue") / (float)byte.MaxValue);
        var profileSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var questFormId = RequiredString(quest, "formId");
        if (!ValidHex(questFormId, Fo3OpeningFlowNumericContracts.FormIdHexCharacters))
            throw new InvalidOperationException("Fallout 3 CG00 FormID is invalid.");
        var introVideo = RequiredObject(opening, "introVideo");
        VerifySourceFile(introVideo);
        var runtimeIntroVideo = RequiredObject(introVideo, "runtime");
        VerifyOwnedVideo(runtimeIntroVideo, introVideo);

        return new Fo3OwnedProfile(
            fullPath,
            profileSha256,
            RequiredString(root, "profileId"),
            birthSlice,
            questEditorId,
            questFormId,
            RequiredString(sex, "title"),
            sexChoices,
            nameStage,
            RequiredString(name, "command"),
            appearanceStage,
            RequiredString(appearance, "command"),
            appearanceContract,
            section4Transition,
            stage65Appearance,
            stage80Transition,
            stage85Transition,
            stage90Transition,
            stage100Transition,
            cg01Stage0Transition,
            cg01Stage10Transition,
            cg01Stage12Transition,
            RequiredString(mainMenuMusic, "source"),
            RequiredString(runtimeIntroVideo, "output"),
            interfaceColor);
    }

    private static void VerifyOwnedVideo(JsonElement runtimeVideo, JsonElement sourceVideo)
    {
        if (RequiredString(runtimeVideo, "schema") != "opennv-owned-opening-video/v1" ||
            RequiredString(runtimeVideo, "status") != "deterministic-owned-video-transcode")
            throw new InvalidOperationException("Fallout 3 runtime intro-video identity is unsupported.");
        var inputs = RequiredObject(runtimeVideo, "inputs");
        if (!string.Equals(
                RequiredString(inputs, "source"),
                RequiredString(sourceVideo, "source"),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                RequiredString(inputs, "sourceSha256"),
                RequiredString(sourceVideo, "sha256"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 runtime intro video is not bound to the owned source.");
        VerifyOutputFile(runtimeVideo);
    }

    private static void VerifySourceFile(JsonElement row)
    {
        var path = RequiredString(row, "source");
        var expectedBytes = RequiredLong(row, "bytes");
        var expectedSha256 = RequiredString(row, "sha256");
        if (!ValidHex(expectedSha256, Fo3OpeningFlowNumericContracts.Sha256HexCharacters))
            throw new InvalidOperationException("Fallout 3 master hash is invalid.");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedBytes)
            throw new InvalidOperationException("Registered Fallout 3 master is absent or changed.");
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Registered Fallout 3 master hash differs.");
    }

    private static void VerifyOutputFile(JsonElement row)
    {
        var path = RequiredString(row, "output");
        var expectedBytes = RequiredLong(row, "outputBytes");
        var expectedSha256 = RequiredString(row, "outputSha256");
        if (!ValidHex(expectedSha256, Fo3OpeningFlowNumericContracts.Sha256HexCharacters))
            throw new InvalidOperationException("Fallout 3 runtime intro-video hash is invalid.");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedBytes)
            throw new InvalidOperationException("Fallout 3 runtime intro video is absent or changed.");
        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 runtime intro-video hash differs.");
    }

    private static byte SettingByte(IEnumerable<JsonElement> settings, string key)
    {
        var row = settings.Single(value => RequiredString(value, "key") == key);
        return byte.Parse(RequiredString(row, "value"), CultureInfo.InvariantCulture);
    }

    private static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 profile field {name} is absent.");
        return value;
    }

    private static JsonElement RequiredArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 profile field {name} is absent.");
        return value;
    }

    private static string RequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 profile field {name} is absent.");
        return value.GetString()!;
    }

    private static int RequiredInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 profile field {name} is invalid.");
        return result;
    }

    private static long RequiredLong(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt64(out var result))
            throw new InvalidOperationException($"Fallout 3 profile field {name} is invalid.");
        return result;
    }

    private static bool RequiredBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 profile field {name} is invalid.");
        return value.GetBoolean();
    }

    private static bool ValidHex(string value, int characters) =>
        value.Length == characters && value.All(Uri.IsHexDigit);
}

internal sealed record Fo3Stage100RuntimeContext(
    string PlayerName,
    Fo3SexChoice Sex,
    Fo3AppearanceSelection Selection,
    Fo3ActivePlayerPackage Section4Package,
    Fo3Stage65AppearanceState Stage65,
    Fo3Stage80State Stage80,
    Fo3Stage85State Stage85,
    Fo3Stage90State Stage90);

internal sealed record Fo3Cg01RuntimeContext(
    string PlayerName,
    Fo3SexChoice Sex,
    Fo3AppearanceSelection Selection,
    Fo3ActivePlayerPackage Section4Package,
    Fo3Stage65AppearanceState Stage65,
    Fo3Stage80State Stage80,
    Fo3Stage85State Stage85,
    Fo3Stage90State Stage90,
    Fo3Stage100State Stage100);

internal enum Fo3OwnedVideoMode
{
    None,
    Intro,
    Cg01Transition,
}

internal partial class Fo3OpeningFlow : CanvasLayer
{
    private Fo3OwnedProfile _profile = null!;
    private string _savePath = "";
    private Node3D _worldHost = null!;
    private Fo3Vault101BirthPresentationContract? _birthPresentation;
    private ColorRect _background = null!;
    private PanelContainer _panel = null!;
    private VBoxContainer _content = null!;
    private AudioStreamPlayer _music = null!;
    private Control? _introLayer;
    private VideoStreamPlayer? _video;
    private Fo3SexChoice? _selectedSex;
    private Node3D? _vaultPreviewHost;
    private Control? _vaultPreviewOverlay;
    private AudioStreamPlayer? _vaultDialogueVoice;
    private AudioStreamPlayer? _vaultEffectSound;
    private ColorRect? _vaultStage90Fade;
    private Fo3Stage90ImageSpaceModifier? _activeStage90ImageSpaceModifier;
    private double _stage90ImageSpaceElapsedSeconds;
    private Fo3Vault101BirthSceneCoverage? _vaultBirthCoverage;
    private Fo3Stage100RuntimeContext? _stage100Runtime;
    private double _stage100TimerRemainingSeconds;
    private bool _runAppearanceProof;
    private bool _introCompleted;
    private Fo3OwnedVideoMode _ownedVideoMode;
    private Fo3Cg01Stage0State? _activeCg01MovieState;
    private Fo3Cg01RuntimeContext? _activeCg01MovieContext;
    private string? _cg01ProofMode;
    private string? _cg01ProofReportPath;
    private bool _cg01ProofMovieEscapeSkipped;

    internal void Configure(
        Fo3OwnedProfile profile,
        string savePath,
        Node3D worldHost,
        Fo3Vault101BirthPresentationContract? birthPresentation,
        bool runAppearanceProof = false,
        string? cg01ProofMode = null,
        string? cg01ProofReportPath = null)
    {
        _profile = profile;
        _savePath = System.IO.Path.GetFullPath(savePath);
        _worldHost = worldHost;
        _birthPresentation = birthPresentation;
        if (_birthPresentation is not null &&
            (!_birthPresentation.EntryReferenceFormId.Equals(
                 _profile.Section4Transition.LocationReferenceFormId,
                 StringComparison.OrdinalIgnoreCase) ||
             !_birthPresentation.CellFormId.Equals(
                 _profile.BirthSlice.CellFormId,
                 StringComparison.OrdinalIgnoreCase) ||
             !_birthPresentation.DadActor.ReferenceFormId.Equals(
                 _profile.Stage100Transition.DisabledDad.FormId,
                 StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Fallout 3 stage-62 package or stage-100 Dad does not join the owned Vault 101 scene.");
        _runAppearanceProof = runAppearanceProof;
        _cg01ProofMode = cg01ProofMode;
        _cg01ProofReportPath = cg01ProofReportPath;
        Name = "Fallout3FrontEnd";
        Layer = Fo3OpeningFlowNumericContracts.UiLayer;
    }

    public override void _Ready()
    {
        BuildShell();
        if (_cg01ProofMode is not null)
        {
            RunCg01Proof();
            return;
        }
        if (_runAppearanceProof)
        {
            RunAppearanceProof();
            return;
        }
        StartMenuMusic();
        ShowMainMenu();
        GD.Print(
            $"OPENNV_FO3_BIRTH_CONTRACT_READY profile={_profile.ProfileId} " +
            $"schema={Fo3BirthSliceContract.ExpectedSchema} cell={_profile.BirthSlice.CellFormId} " +
            $"playerSpawn={_profile.BirthSlice.PlayerSpawnReferenceFormId} " +
            $"doctor={_profile.BirthSlice.DoctorActorReferenceFormId} " +
            $"references={_profile.BirthSlice.ReferenceCount} " +
            $"models={_profile.BirthSlice.CellModelResourceCount} rendered=0 interactive=0");
        GD.Print(
            $"OPENNV_FO3_FRONTEND_READY profile={_profile.ProfileId} " +
            $"quest={_profile.QuestEditorId} form={_profile.QuestFormId} " +
            $"intro=owned-transcode escapeSkip=1 sexChoices={_profile.SexChoices.Count} " +
            $"nameStage={_profile.NameStage} appearanceStage={_profile.AppearanceStage}");
    }

    public override void _Process(double delta)
    {
        if (_vaultStage90Fade is not null && _activeStage90ImageSpaceModifier is not null)
        {
            _stage90ImageSpaceElapsedSeconds += delta;
            var modifier = _activeStage90ImageSpaceModifier;
            var normalizedTime = modifier.DurationSeconds <= 0.0f
                ? 1.0f
                : Mathf.Clamp(
                    (float)(_stage90ImageSpaceElapsedSeconds / modifier.DurationSeconds),
                    0.0f,
                    1.0f);
            _vaultStage90Fade.Color = EvaluateStage90Fade(modifier.Fade, normalizedTime);
            if (normalizedTime >= 1.0f)
            {
                _vaultStage90Fade.QueueFree();
                _vaultStage90Fade = null;
                _activeStage90ImageSpaceModifier = null;
                _stage90ImageSpaceElapsedSeconds = 0.0;
            }
        }

        if (_stage100Runtime is null)
            return;
        _stage100TimerRemainingSeconds = Math.Max(
            0.0,
            _stage100TimerRemainingSeconds - delta);
        if (_stage100TimerRemainingSeconds > 0.0)
            return;
        var context = _stage100Runtime;
        _stage100Runtime = null;
        CompleteStage90Timer(context);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsEscapePressed(@event))
            return;
        if (_video is not null)
        {
            GetViewport().SetInputAsHandled();
            CompleteOwnedVideo(true);
            return;
        }
        if (_vaultPreviewHost is not null)
        {
            GetViewport().SetInputAsHandled();
            ExitVault101Preview();
            return;
        }
    }

    private void BuildShell()
    {
        _background = new ColorRect { Color = Colors.Black };
        _background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_background);

        _panel = new PanelContainer();
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.AnchorLeft -= Fo3OpeningFlowNumericContracts.PanelWidthFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.AnchorRight += Fo3OpeningFlowNumericContracts.PanelWidthFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.AnchorTop -= Fo3OpeningFlowNumericContracts.PanelHeightFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.AnchorBottom += Fo3OpeningFlowNumericContracts.PanelHeightFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(
                _profile.InterfaceColor.R * Fo3OpeningFlowNumericContracts.DimmedColorScale,
                _profile.InterfaceColor.G * Fo3OpeningFlowNumericContracts.DimmedColorScale,
                _profile.InterfaceColor.B * Fo3OpeningFlowNumericContracts.DimmedColorScale,
                Fo3OpeningFlowNumericContracts.PanelAlpha),
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        AddChild(_panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.MarginPixels);
        _panel.AddChild(margin);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddChild(scroll);
        _content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _content.AddThemeConstantOverride("separation", Fo3OpeningFlowNumericContracts.SeparationPixels);
        scroll.AddChild(_content);
    }

    private void StartMenuMusic()
    {
        var stream = AudioStreamMP3.LoadFromFile(_profile.MainMenuMusicPath);
        if (stream is null)
            throw new InvalidOperationException("Fallout 3 owned main-menu music could not be loaded.");
        stream.Loop = true;
        _music = new AudioStreamPlayer
        {
            Name = "Fallout3OwnedMainMenuMusic",
            Stream = stream,
        };
        AddChild(_music);
        _music.Play();
    }

    private void ShowMainMenu()
    {
        ClearContent();
        _content.AddChild(Label("FALLOUT 3", Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            "OWNED GOTY PROFILE  •  OPENNV",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var newGame = Button("NEW GAME");
        newGame.Pressed += PlayIntro;
        _content.AddChild(newGame);
        var continueGame = Button("CONTINUE CG00");
        continueGame.Disabled = !File.Exists(_savePath);
        continueGame.Pressed += ContinueCharacter;
        _content.AddChild(continueGame);
        var quit = Button("QUIT");
        quit.Pressed += () => GetTree().Quit();
        _content.AddChild(quit);
        Callable.From(newGame.GrabFocus).CallDeferred();
    }

    private void PlayIntro()
    {
        if (_video is not null)
            return;
        _introCompleted = false;
        _ownedVideoMode = Fo3OwnedVideoMode.Intro;
        _music.Stop();
        _introLayer = new Control { Name = "Fallout3OwnedIntro" };
        _introLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_introLayer);
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _introLayer.AddChild(black);
        _video = new VideoStreamPlayer
        {
            Name = "Fallout3OwnedIntroVideo",
            Stream = new VideoStreamTheora { File = _profile.IntroVideoPath },
            Expand = true,
            Loop = false,
        };
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += () => CompleteOwnedVideo(false);
        _introLayer.AddChild(_video);
        var skip = Button("SKIP  •  ESC");
        skip.Name = "SkipFallout3OwnedIntro";
        skip.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        skip.Position = new Vector2(
            Fo3OpeningFlowNumericContracts.SkipButtonOffsetXPixels,
            Fo3OpeningFlowNumericContracts.SkipButtonOffsetYPixels);
        skip.Size = new Vector2(
            Fo3OpeningFlowNumericContracts.SkipButtonWidthPixels,
            Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels);
        skip.Pressed += () => CompleteOwnedVideo(true);
        _introLayer.AddChild(skip);
        _video.Play();
        GD.Print(
            $"OPENNV_FO3_INTRO_STARTED profile={_profile.ProfileId} " +
            "source=owned-transcode escapeSkip=1");
    }

    private void CompleteIntro(bool skipped)
    {
        if (_introCompleted || _ownedVideoMode != Fo3OwnedVideoMode.Intro)
            return;
        _introCompleted = true;
        ClearOwnedVideo();
        ShowSexSelection();
        GD.Print(
            $"OPENNV_FO3_INTRO_COMPLETE profile={_profile.ProfileId} " +
            $"mode={(skipped ? "skipped" : "watched")} next={_profile.QuestEditorId}");
    }

    private void CompleteOwnedVideo(bool skipped)
    {
        switch (_ownedVideoMode)
        {
            case Fo3OwnedVideoMode.Intro:
                CompleteIntro(skipped);
                break;
            case Fo3OwnedVideoMode.Cg01Transition:
                CompleteCg01TransitionMovie(skipped);
                break;
        }
    }

    private void ClearOwnedVideo()
    {
        _video?.Stop();
        _video = null;
        _introLayer?.QueueFree();
        _introLayer = null;
        _ownedVideoMode = Fo3OwnedVideoMode.None;
    }

    private void ShowSexSelection()
    {
        ClearContent();
        _content.AddChild(Label("FALLOUT 3  •  CG00", Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(_profile.SexTitle, Fo3OpeningFlowNumericContracts.BodyFontPixels));
        foreach (var choice in _profile.SexChoices)
        {
            var captured = choice;
            var button = Button(choice.Label);
            button.Pressed += () => ShowNameSelection(captured);
            _content.AddChild(button);
        }
        GD.Print(
            $"OPENNV_FO3_CG00_READY profile={_profile.ProfileId} " +
            $"quest={_profile.QuestEditorId} form={_profile.QuestFormId} " +
            $"sexChoices={_profile.SexChoices.Count} nameStage={_profile.NameStage}");
    }

    private void ShowNameSelection(Fo3SexChoice sex)
    {
        _selectedSex = sex;
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {_profile.NameStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(_profile.NameCommand, Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var name = new LineEdit
        {
            PlaceholderText = "Name",
            CustomMinimumSize = new Vector2(
                0.0f,
                Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels),
        };
        name.AddThemeFontSizeOverride("font_size", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        name.TextSubmitted += _ => AcceptName(name);
        _content.AddChild(name);
        var accept = Button("ACCEPT");
        accept.Pressed += () => AcceptName(name);
        _content.AddChild(accept);
        Callable.From(name.GrabFocus).CallDeferred();
    }

    private void AcceptName(LineEdit input)
    {
        var playerName = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            input.GrabFocus();
            return;
        }
        PersistNamedCharacter(playerName, _selectedSex!);
        ShowAppearanceSelection(playerName, _selectedSex!);
        GD.Print(
            $"OPENNV_FO3_CG00_CHARACTER_SAVED profile={_profile.ProfileId} " +
            $"stage={_profile.AppearanceStage} save={_savePath}");
    }

    private void ContinueCharacter()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
            var root = document.RootElement;
            if (RequiredSaveString(root, "schema") != "opennv-fo3-opening-character/v2" ||
                RequiredSaveString(root, "profileId") != _profile.ProfileId ||
                RequiredSaveString(root, "profileSha256") != _profile.Sha256 ||
                RequiredSaveString(root, "questEditorId") != _profile.QuestEditorId ||
                RequiredSaveString(root, "questFormId") != _profile.QuestFormId)
                throw new InvalidOperationException("Saved Fallout 3 CG00 character does not match this profile.");
            var savedSex = RequiredSaveObject(root, "sex");
            _selectedSex = _profile.SexChoices.Single(value =>
                value.Label == RequiredSaveString(savedSex, "label") &&
                value.EngineSex == RequiredSaveString(savedSex, "engineSex"));
            var playerName = RequiredSaveString(root, "playerName");
            var stage = RequiredSaveInteger(root, "stage");
            if (stage == _profile.Appearance.Stage)
            {
                ShowAppearanceSelection(playerName, _selectedSex);
                return;
            }
            if (stage != _profile.Appearance.AcceptedStage &&
                stage != _profile.Stage65Appearance.Stage &&
                stage != _profile.Stage80Transition.Stage &&
                stage != _profile.Stage85Transition.Stage &&
                stage != _profile.Stage90Transition.Stage &&
                stage != _profile.Stage100Transition.Stage)
                throw new InvalidOperationException("Saved Fallout 3 CG00 stage is unsupported.");
            var savedAppearance = RequiredSaveObject(root, "appearance");
            if (RequiredSaveString(savedAppearance, "sourceContract") !=
                Fo3AppearanceContract.ExpectedSchema)
                throw new InvalidOperationException("Saved Fallout 3 appearance contract is unsupported.");
            var selection = _profile.Appearance.ResolveSelection(
                _selectedSex.EngineSex,
                RequiredSaveString(savedAppearance, "adultRaceFormId"),
                RequiredSaveString(savedAppearance, "childRaceFormId"),
                RequiredSaveString(savedAppearance, "hairFormId"),
                RequiredSaveString(savedAppearance, "eyesFormId"));
            var faceGen = RequiredSaveObject(savedAppearance, "faceGen");
            if (RequiredSaveString(faceGen, "symmetricGeometrySha256") !=
                    selection.Sex.FaceGen.SymmetricGeometrySha256 ||
                RequiredSaveString(faceGen, "asymmetricGeometrySha256") !=
                    selection.Sex.FaceGen.AsymmetricGeometrySha256 ||
                RequiredSaveString(faceGen, "symmetricTextureSha256") !=
                    selection.Sex.FaceGen.SymmetricTextureSha256)
                throw new InvalidOperationException("Saved Fallout 3 FaceGen defaults differ from the profile.");
            if (stage == _profile.Appearance.AcceptedStage)
            {
                if (root.TryGetProperty("playerPackage", out var savedStage62Package))
                    _profile.Section4Transition.ValidateSavedState(savedStage62Package);
                ShowVault101BirthRoom(playerName, _selectedSex, selection);
                return;
            }
            var savedPackage = RequiredSaveObject(root, "playerPackage");
            if (stage == _profile.Stage100Transition.Stage)
                ValidateRemovedPlayerPackageState(savedPackage);
            else
                _profile.Section4Transition.ValidateSavedState(savedPackage);
            var stage65 = _profile.Stage65Appearance.Apply(
                _selectedSex.EngineSex,
                selection.Race.FormId,
                selection.Sex.FaceGen);
            _profile.Stage65Appearance.ValidateSavedState(
                RequiredSaveObject(root, "stage65Appearance"),
                stage65);
            if (stage == stage65.Stage)
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage65-source-bound-ready");
                ShowVault101BirthRoom(playerName, _selectedSex, selection, stage65);
                return;
            }
            var stage80 = _profile.Stage80Transition.Apply(_selectedSex.EngineSex, stage65);
            _profile.Stage80Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage80Transition"),
                stage80);
            if (stage == stage80.Stage)
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage65-cue-finished-stage80-applied");
                ShowVault101BirthRoom(
                    playerName,
                    _selectedSex,
                    selection,
                    stage65,
                    stage80);
                return;
            }
            var stage85 = _profile.Stage85Transition.Apply(stage80);
            _profile.Stage85Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage85Transition"),
                stage85);
            if (stage == stage85.Stage)
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage80-info-trigger-stage85-applied");
                ShowVault101BirthRoom(
                    playerName,
                    _selectedSex,
                    selection,
                    stage65,
                    stage80,
                    stage85);
                return;
            }
            var stage90 = _profile.Stage90Transition.Apply(stage85);
            _profile.Stage90Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage90Transition"),
                stage90);
            if (stage == stage90.Stage)
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage85-info-finished-stage90-applied");
                ShowVault101BirthRoom(
                    playerName,
                    _selectedSex,
                    selection,
                    stage65,
                    stage80,
                    stage85,
                    stage90);
                return;
            }
            var stage100 = _profile.Stage100Transition.Apply(stage90, 0.0);
            _profile.Stage100Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage100Transition"),
                stage100);
            Fo3Cg01Stage0State? cg01 = null;
            Fo3Cg01Stage10State? cg01Stage10 = null;
            Fo3Cg01Stage12State? cg01Stage12 = null;
            if (root.TryGetProperty("cg01Stage0Transition", out var savedCg01) &&
                savedCg01.ValueKind == JsonValueKind.Object)
            {
                cg01 = _profile.Cg01Stage0Transition.Apply(stage100);
                _profile.Cg01Stage0Transition.ValidateSavedState(savedCg01, cg01);
                if (root.TryGetProperty("cg01Stage10Transition", out var savedCg01Stage10) &&
                    savedCg01Stage10.ValueKind == JsonValueKind.Object)
                {
                    cg01Stage10 = _profile.Cg01Stage10Transition.Apply(
                        cg01,
                        _selectedSex.EngineSex);
                    _profile.Cg01Stage10Transition.ValidateSavedState(
                        savedCg01Stage10,
                        cg01Stage10);
                    if (root.TryGetProperty("cg01Stage12Transition", out var savedCg01Stage12) &&
                        savedCg01Stage12.ValueKind == JsonValueKind.Object)
                    {
                        cg01Stage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
                            cg01Stage10,
                            _profile.Cg01Stage12Transition.Trigger.ReferenceFormId,
                            actionReferenceWasPlayer: true);
                        _profile.Cg01Stage12Transition.ValidateSavedState(
                            savedCg01Stage12,
                            cg01Stage12);
                    }
                    ValidateBirthRuntimeState(
                        RequiredSaveObject(root, "birthRuntime"),
                        cg01Stage12 is null
                            ? "cg01-stage10-applied-post-stage10-blocked"
                            : "cg01-stage12-authored-trigger-applied-post-stage12-blocked");
                }
                else
                {
                    ValidateBirthRuntimeState(
                        RequiredSaveObject(root, "birthRuntime"),
                        "cg01-stage0-stage5-applied-dad-dialogue-pending");
                }
            }
            else
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage90-timer-finished-stage100-applied");
            }
            ShowVault101BirthRoom(
                playerName,
                _selectedSex,
                selection,
                stage65,
                stage80,
                stage85,
                stage90,
                stage100,
                cg01,
                cg01Stage10,
                cg01Stage12);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            GD.PushError($"OPENNV_FO3_CONTINUE_FAIL {exception.Message}");
            ShowMainMenu();
        }
    }

    private void ShowAppearanceSelection(string playerName, Fo3SexChoice sex)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {_profile.AppearanceStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT OWNED COMMAND: {_profile.AppearanceCommand}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));

        var selectors = new GridContainer { Columns = 2 };
        selectors.AddThemeConstantOverride("h_separation", Fo3OpeningFlowNumericContracts.SeparationPixels);
        selectors.AddThemeConstantOverride("v_separation", Fo3OpeningFlowNumericContracts.SeparationPixels);
        var raceSelect = new OptionButton();
        var hairSelect = new OptionButton();
        var eyesSelect = new OptionButton();
        AddSelector(selectors, "RACE", raceSelect);
        AddSelector(selectors, "HAIR", hairSelect);
        AddSelector(selectors, "EYES", eyesSelect);
        _content.AddChild(selectors);

        var defaultSelection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        FillOptions(raceSelect, _profile.Appearance.Races, defaultSelection.Race.FormId);
        var preview = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        preview.AddThemeConstantOverride("separation", Fo3OpeningFlowNumericContracts.SeparationPixels);
        _content.AddChild(preview);

        void RenderRaceDefaults(Fo3AppearanceRace race)
        {
            var raceSex = race.Sex[sex.EngineSex];
            FillOptions(hairSelect, raceSex.HairOptions, raceSex.DefaultHairFormId);
            FillOptions(eyesSelect, raceSex.EyeOptions, raceSex.DefaultEyesFormId);
            RenderAppearancePreview(
                preview,
                raceSex.HeadTexture,
                raceSex.HairOptions[hairSelect.Selected].Texture,
                raceSex.EyeOptions[eyesSelect.Selected].Texture,
                raceSex.FaceGen);
        }

        void RenderCurrentPreview()
        {
            var race = _profile.Appearance.Races[raceSelect.Selected];
            var raceSex = race.Sex[sex.EngineSex];
            RenderAppearancePreview(
                preview,
                raceSex.HeadTexture,
                raceSex.HairOptions[hairSelect.Selected].Texture,
                raceSex.EyeOptions[eyesSelect.Selected].Texture,
                raceSex.FaceGen);
        }

        raceSelect.ItemSelected += index => RenderRaceDefaults(_profile.Appearance.Races[(int)index]);
        hairSelect.ItemSelected += _ => RenderCurrentPreview();
        eyesSelect.ItemSelected += _ => RenderCurrentPreview();
        RenderRaceDefaults(defaultSelection.Race);

        _content.AddChild(Label(
            "OWNED HEAD / HAIR / EYE SOURCE TEXTURES  •  NOT A 3D FACE RENDER",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var accept = Button("ACCEPT APPEARANCE");
        accept.Pressed += () =>
        {
            var race = _profile.Appearance.Races[raceSelect.Selected];
            var raceSex = race.Sex[sex.EngineSex];
            var selection = new Fo3AppearanceSelection(
                race,
                raceSex,
                raceSex.HairOptions[hairSelect.Selected],
                raceSex.EyeOptions[eyesSelect.Selected]);
            PersistAppearance(playerName, sex, selection);
            if (_birthPresentation is null)
                ShowAppearanceAccepted(playerName, sex, selection);
            else
                ShowVault101BirthRoom(playerName, sex, selection);
        };
        _content.AddChild(accept);
        Callable.From(raceSelect.GrabFocus).CallDeferred();
        GD.Print(
            $"OPENNV_FO3_CG00_APPEARANCE_READY profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.Stage} entered={_profile.Appearance.MenuEnteredStage} " +
            $"races={_profile.Appearance.Races.Count} sex={sex.EngineSex} preview=owned-source-textures");
    }

    private void ShowAppearanceAccepted(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {_profile.Appearance.AcceptedStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"HAIR: {selection.Hair.Label}  •  EYES: {selection.Eyes.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT OWNED COMMAND: {_profile.Appearance.AcceptedStageCommand}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            _birthPresentation is null
                ? "The owned CG00 appearance choice is saved at stage 62. The Section 4 " +
                    "package and later stage contracts are compiled, but normal progression stops " +
                    "until the authored package/dialogue triggers execute in the Vault 101 world."
                : "The owned CG00 appearance choice is saved at stage 62. Its next authored " +
                    "package targets the exact Vault 101 player-start marker. The bounded preview " +
                    "shows that owned room only; it does not execute the package or dialogue.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        if (_birthPresentation is not null)
        {
            var enter = Button("ENTER OWNED VAULT 101 BIRTH ROOM");
            enter.Pressed += () => ShowVault101BirthRoom(playerName, sex, selection);
            _content.AddChild(enter);
        }
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_APPEARANCE_ACCEPTED profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.AcceptedStage} race={selection.Race.FormId} " +
            $"hair={selection.Hair.FormId} eyes={selection.Eyes.FormId} " +
            $"next={_profile.Appearance.AcceptedStageCommand} packageRuntimeReady=0");
    }

    private void ShowVault101BirthRoom(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState? resumedStage65 = null,
        Fo3Stage80State? resumedStage80 = null,
        Fo3Stage85State? resumedStage85 = null,
        Fo3Stage90State? resumedStage90 = null,
        Fo3Stage100State? resumedStage100 = null,
        Fo3Cg01Stage0State? resumedCg01 = null,
        Fo3Cg01Stage10State? resumedCg01Stage10 = null,
        Fo3Cg01Stage12State? resumedCg01Stage12 = null)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 Vault 101 birth room has no owned presentation contract.");
        var transition = _profile.Section4Transition;
        if (transition.SourceStage != _profile.Appearance.AcceptedStage ||
            !transition.LocationReferenceFormId.Equals(
                contract.EntryReferenceFormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 Vault 101 birth room does not join the stage-62 package location.");
        if (_vaultPreviewHost is not null)
            throw new InvalidOperationException("Fallout 3 Vault 101 birth room is already active.");

        Fo3PlayerPackageRuntimeActivation? activation = null;
        var stage65 = resumedStage65;
        if (stage65 is null)
        {
            activation = transition.ActivateAtOwnedMarker(
                contract.EntryReferenceFormId,
                _profile.Appearance.AcceptedStage,
                targetStageDone: false);
            stage65 = _profile.Stage65Appearance.Apply(
                sex.EngineSex,
                selection.Race.FormId,
                selection.Sex.FaceGen);
            if (stage65.Stage != activation.TriggeredStage)
                throw new InvalidOperationException(
                    "Fallout 3 player-package trigger differs from the stage-65 result.");
        }
        if ((resumedStage80 is not null &&
                resumedStage80.Stage != _profile.Stage80Transition.Stage) ||
            (resumedStage85 is not null &&
                (resumedStage80 is null ||
                 resumedStage85.Stage != _profile.Stage85Transition.Stage)) ||
            (resumedStage90 is not null &&
                (resumedStage85 is null ||
                 resumedStage90.Stage != _profile.Stage90Transition.Stage)) ||
            (resumedStage100 is not null &&
                (resumedStage90 is null ||
                 resumedStage100.Stage != _profile.Stage100Transition.Stage)) ||
            (resumedCg01 is not null && resumedStage100 is null) ||
            (resumedCg01Stage10 is not null && resumedCg01 is null) ||
            (resumedCg01Stage12 is not null && resumedCg01Stage10 is null))
            throw new InvalidOperationException(
                "Fallout 3 resumed birth-room stage chain is incomplete.");

        var previewHost = new Node3D { Name = "FO3_VAULT101_BIRTH_ROOM" };
        _worldHost.AddChild(previewHost);
        Fo3Vault101BirthSceneCoverage coverage;
        try
        {
            coverage = Fo3Vault101BirthScene.Build(previewHost, contract);
        }
        catch
        {
            previewHost.QueueFree();
            throw;
        }
        _vaultPreviewHost = previewHost;
        _vaultBirthCoverage = coverage;
        _background.Visible = false;
        _panel.Visible = false;

        if (activation is not null)
            PersistStage65Appearance(
                playerName,
                sex,
                selection,
                activation.Package,
                stage65,
                activation);
        if (resumedStage80 is null)
        {
            var subtitle = AddVaultDialogueOverlay();
            var branch = _profile.Stage80Transition.DialogueFor(sex.EngineSex);
            Callable.From(() => PlayVaultDialogue(
                branch,
                subtitle,
                playerName,
                sex,
                selection,
                stage65)).CallDeferred();
        }
        else if (resumedStage85 is null)
        {
            var stage85 = _profile.Stage85Transition.Apply(resumedStage80);
            PersistStage85Transition(
                playerName,
                sex,
                selection,
                transition.Activate(),
                stage65,
                resumedStage80,
                stage85);
            PrintStage85Applied(stage85, resumed: true);
            resumedStage85 = stage85;
        }
        if (resumedStage80 is not null && resumedStage90 is null)
            BeginStage85ProgressionDialogue(
                playerName,
                sex,
                selection,
                stage65,
                resumedStage80,
                resumedStage85!);
        if (resumedStage100 is not null)
        {
            ApplyStage100Presentation(resumedStage100);
            if (resumedCg01 is not null)
            {
                var cg01Context = new Fo3Cg01RuntimeContext(
                    playerName,
                    sex,
                    selection,
                    transition.Activate(),
                    stage65,
                    resumedStage80!,
                    resumedStage85!,
                    resumedStage90!,
                    resumedStage100);
                if (resumedCg01Stage12 is not null)
                    ShowCg01PostStage12Boundary(resumedCg01Stage12, resumed: true);
                else if (resumedCg01Stage10 is not null)
                    ShowCg01PostStage10Boundary(resumedCg01Stage10, resumed: true);
                else
                    BeginCg01DadDialogue(resumedCg01, cg01Context, resumed: true);
            }
            else
                ApplyCg01AfterStage100(new Fo3Cg01RuntimeContext(
                    playerName,
                    sex,
                    selection,
                    transition.Activate(),
                    stage65,
                    resumedStage80!,
                    resumedStage85!,
                    resumedStage90!,
                    resumedStage100));
        }
        else if (resumedStage90 is not null)
            StartStage100Timer(new Fo3Stage100RuntimeContext(
                playerName,
                sex,
                selection,
                transition.Activate(),
                stage65,
                resumedStage80!,
                resumedStage85!,
                resumedStage90));
        var activeStage = resumedCg01Stage12?.ActiveStage ??
            resumedCg01Stage10?.ActiveStage ?? resumedCg01?.ActiveStage ??
            resumedStage100?.Stage ??
            resumedStage90?.Stage ?? resumedStage85?.Stage ??
            resumedStage80?.Stage ?? stage65.Stage;
        GD.Print(
            $"OPENNV_FO3_CG00_VAULT101_BIRTH_ROOM_READY profile={_profile.ProfileId} " +
            $"stage={activeStage} package={transition.PackageFormId} " +
            $"entry={contract.EntryReferenceFormId} cell={contract.CellFormId} " +
            $"references={coverage.PlacedReferences} actors=2 " +
            $"doctor={coverage.DoctorActor.ReferenceFormId} " +
            $"dad={coverage.DadActor.ReferenceFormId} " +
            $"resumed={(resumedStage65 is null ? 0 : 1)} " +
            $"packageActive={(resumedStage100 is null ? 1 : 0)} " +
            $"trigger={transition.NextCommand} playerIdleExecuted=0 " +
            $"dialoguePlaybackReady={(resumedStage80 is null ? 1 : 0)} retailTiming=0 " +
            $"stage80Applied={(resumedStage80 is null ? 0 : 1)} " +
            $"stage85Applied={(resumedStage85 is null ? 0 : 1)} " +
            $"stage90Applied={(resumedStage90 is null ? 0 : 1)} " +
            $"stage100Applied={(resumedStage100 is null ? 0 : 1)} " +
            $"cg01Stage0Applied={(resumedCg01 is null ? 0 : 1)} " +
            $"cg01Stage10Applied={(resumedCg01Stage10 is null ? 0 : 1)} " +
            $"cg01MovieReplayed={(resumedCg01 is null ? "n/a" : "0")} " +
            $"dadEnabled={(resumedStage100 is null ? 1 : 0)}");
    }

    private Label AddVaultDialogueOverlay(
        string nodeName = "FO3_STAGE65_VAULT101_DIALOGUE")
    {
        var overlay = new PanelContainer
        {
            Name = nodeName,
            AnchorLeft = 0.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = 150.0f,
            OffsetTop = -180.0f,
            OffsetRight = -150.0f,
            OffsetBottom = -20.0f,
        };
        overlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.0f, 0.0f, 0.0f, 0.84f),
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.SeparationPixels);
        overlay.AddChild(margin);
        var subtitle = Label(" ", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        subtitle.Visible = false;
        margin.AddChild(subtitle);
        AddChild(overlay);
        _vaultPreviewOverlay = overlay;
        return subtitle;
    }

    private void PlayVaultDialogue(
        Fo3Stage80DialogueBranch branch,
        Label subtitle,
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65)
    {
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        var stream = AudioStreamOggVorbis.LoadFromFile(branch.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 owned Dad voice could not be decoded: " +
                branch.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException("Fallout 3 owned Dad voice has no duration.");
        _music.Stop();
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = "FO3_CG00_OWNED_DAD_DIALOGUE",
            Stream = stream,
        };
        _vaultDialogueVoice.Finished += () => CompleteStage65Dialogue(
            subtitle,
            playerName,
            sex,
            selection,
            stage65);
        AddChild(_vaultDialogueVoice);
        subtitle.Text = $"DAD: {branch.Response.Text}";
        subtitle.Visible = true;
        _vaultDialogueVoice.Play();
        GD.Print(
            $"OPENNV_FO3_CG00_DAD_CUE_STARTED stage=65 info={branch.InfoFormId} " +
            $"response={branch.Response.Index} duration={durationSeconds:F3} " +
            $"voice={branch.Response.Voice.LogicalPath} " +
            $"lip={branch.Response.Lip.LogicalPath} sourceTriggerAdvance=1 explicitUiAdvance=0 " +
            "dadRendered=1 lipPlayback=0 retailTiming=0 stage80Applied=0");
    }

    private void CompleteStage65Dialogue(
        Label subtitle,
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65)
    {
        subtitle.Visible = false;
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = null;
        var package = _profile.Section4Transition.Activate();
        var stage80 = _profile.Stage80Transition.Apply(sex.EngineSex, stage65);
        PersistStage80Transition(playerName, sex, selection, package, stage65, stage80);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE80_APPLIED_NORMAL stage={stage80.Stage} " +
            $"info={stage80.AppliedInfoFormId} commands={stage80.AppliedCommandCount} " +
            $"package={stage80.AddedPlayerPackage.FormId} " +
            $"variables={stage80.ScriptVariables.Count} " +
            $"evaluated={stage80.EvaluatedPackageReferences.Count} " +
            $"enabled={stage80.EnabledReferences.Count} cueFinished=1 playerIdleExecuted=0");
        var stage85 = _profile.Stage85Transition.Apply(stage80);
        PersistStage85Transition(
            playerName,
            sex,
            selection,
            package,
            stage65,
            stage80,
            stage85);
        PrintStage85Applied(stage85, resumed: false);
        BeginStage85ProgressionDialogue(
            playerName,
            sex,
            selection,
            stage65,
            stage80,
            stage85);
    }

    private static void PrintStage85Applied(Fo3Stage85State stage85, bool resumed) =>
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE85_APPLIED_NORMAL stage={stage85.Stage} " +
            $"info={stage85.AppliedInfoFormId} commands={stage85.AppliedCommandCount} " +
            $"resumed={(resumed ? 1 : 0)} infoConditionsEvaluated=1 " +
            "dialoguePlayback=0 playerIdleExecuted=0 retailTiming=0");

    private void BeginStage85ProgressionDialogue(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85)
    {
        var dialogue = _profile.Stage90Transition.Dialogue;
        var subtitle = AddVaultDialogueOverlay("FO3_STAGE85_VAULT101_DIALOGUE");
        var stream = AudioStreamOggVorbis.LoadFromFile(dialogue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                "Fallout 3 owned post-stage-85 Dad voice could not be decoded: " +
                dialogue.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException(
                "Fallout 3 owned post-stage-85 Dad voice has no duration.");
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = "FO3_CG00_OWNED_DAD_STAGE90_DIALOGUE",
            Stream = stream,
        };
        _vaultDialogueVoice.Finished += () => CompleteStage85ProgressionDialogue(
            subtitle,
            playerName,
            sex,
            selection,
            stage65,
            stage80,
            stage85);
        AddChild(_vaultDialogueVoice);
        subtitle.Text = $"DAD: {dialogue.Response.Text}";
        subtitle.Visible = true;
        _vaultDialogueVoice.Play();
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE90_CUE_STARTED stage=85 info={dialogue.InfoFormId} " +
            $"response={dialogue.Response.Index} duration={durationSeconds:F3} " +
            $"voice={dialogue.Response.Voice.LogicalPath} " +
            $"lip={dialogue.Response.Lip.LogicalPath} continuationMarker=1 " +
            "sourceTriggerAdvance=1 explicitUiAdvance=0 packageAi=0 " +
            "lipPlayback=0 retailTiming=0 stage90Applied=0");
    }

    private void CompleteStage85ProgressionDialogue(
        Label subtitle,
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85)
    {
        subtitle.Visible = false;
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = null;
        var stage90 = _profile.Stage90Transition.Apply(stage85);
        StartStage90ImageSpace(stage90.ImageSpaceModifier);
        StartStage90Sound(stage90.Sound);
        PersistStage90Transition(
            playerName,
            sex,
            selection,
            _profile.Section4Transition.Activate(),
            stage65,
            stage80,
            stage85,
            stage90);
        StartStage100Timer(new Fo3Stage100RuntimeContext(
            playerName,
            sex,
            selection,
            _profile.Section4Transition.Activate(),
            stage65,
            stage80,
            stage85,
            stage90));
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE90_APPLIED_NORMAL stage={stage90.Stage} " +
            $"info={stage90.AppliedInfoFormId} commands={stage90.AppliedCommandCount} " +
            $"timer={stage90.QuestVariables.Single(value => value.Name == "timer").Value:F1} " +
            $"runTimer={stage90.QuestVariables.Single(value => value.Name == "runTimer").Value:F0} " +
            $"imad={stage90.ImageSpaceModifier.FormId} imadFade=1 imadOtherChannels=0 " +
            $"sound={stage90.Sound.FormId} soundStarted=1 timerAdvancing=1 " +
            "playerIdleExecuted=0 packageAi=0 retailTiming=0 stage100Applied=0");
    }

    private void StartStage100Timer(Fo3Stage100RuntimeContext context)
    {
        if (_stage100Runtime is not null || !context.Stage90.TimerAdvancing)
            throw new InvalidOperationException("Fallout 3 stage-100 timer is already active.");
        var timer = context.Stage90.QuestVariables.Single(value => value.Name == "timer");
        if (timer.Value != _profile.Stage100Transition.TimerInitialSeconds)
            throw new InvalidOperationException("Fallout 3 stage-100 timer start differs.");
        _stage100Runtime = context;
        _stage100TimerRemainingSeconds = timer.Value;
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE100_TIMER_STARTED sourceStage={context.Stage90.Stage} " +
            $"seconds={_stage100TimerRemainingSeconds:F1} decrement=GetSecondsPassed " +
            "debugJump=0 retailTiming=0");
    }

    private void CompleteStage90Timer(Fo3Stage100RuntimeContext context)
    {
        var stage100 = _profile.Stage100Transition.Apply(
            context.Stage90,
            _stage100TimerRemainingSeconds);
        ApplyStage100Presentation(stage100);
        PersistStage100Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            stage100);
        ApplyCg01AfterStage100(
            new Fo3Cg01RuntimeContext(
                context.PlayerName,
                context.Sex,
                context.Selection,
                context.Section4Package,
                context.Stage65,
                context.Stage80,
                context.Stage85,
                context.Stage90,
                stage100));
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE100_APPLIED_NORMAL stage={stage100.Stage} " +
            $"commandsApplied={stage100.AppliedCommandCount} " +
            $"commandsAccounted={stage100.AccountedCommandCount} packageActive=0 " +
            $"dad={stage100.DisabledDad.FormId} dadEnabled=0 cg00Running=0 " +
            $"playerYoung=1 nextQuest={stage100.NextBoundary.QuestFormId} " +
            $"nextStage={stage100.NextBoundary.Stage} nextApplied=1 " +
            $"nextContract={stage100.NextBoundary.TransitionContract.Sha256}");
    }

    private void ApplyCg01AfterStage100(Fo3Cg01RuntimeContext context)
    {
        var state = _profile.Cg01Stage0Transition.Apply(context.Stage100);
        PersistCg01Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            state);
        StartCg01TransitionMovie(state, context);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE0_STAGE5_APPLIED quest={state.ActiveQuestFormId} " +
            $"stage={state.ActiveStage} commands={state.AppliedCommandCount} " +
            $"trace={string.Join(',', state.AppliedExecutionTrace)} " +
            $"dad={state.Dad.Reference.FormId} dadEnabled=1 " +
            $"nextDad={state.NextDad.Reference.FormId} nextDadEnabled=1 " +
            $"playerScale={state.Player.Scale:F1} movieRequested=1 " +
            $"nextApplied=0 blocker={state.NextBoundary.Blocker}");
    }

    private void ApplyStage100Presentation(Fo3Stage100State stage100)
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 stage-100 Dad has no owned Vault 101 scene.");
        if (!coverage.DadActor.ReferenceFormId.Equals(
                stage100.DisabledDad.FormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 stage-100 Dad identity differs.");
        coverage.DadActor.Placement.Visible = false;
        coverage.DadActor.Placement.ProcessMode = ProcessModeEnum.Disabled;
    }

    private void StartCg01TransitionMovie(
        Fo3Cg01Stage0State state,
        Fo3Cg01RuntimeContext? context = null)
    {
        if (_video is not null || _ownedVideoMode != Fo3OwnedVideoMode.None)
            throw new InvalidOperationException("Fallout 3 CG01 transition movie is already active.");
        _ownedVideoMode = Fo3OwnedVideoMode.Cg01Transition;
        _activeCg01MovieState = state;
        _activeCg01MovieContext = context;
        _introLayer = new Control { Name = "Fallout3OwnedCg01Transition" };
        _introLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_introLayer);
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _introLayer.AddChild(black);
        _video = new VideoStreamPlayer
        {
            Name = "Fallout3OwnedCg01TransitionVideo",
            Stream = new VideoStreamTheora { File = state.TransitionMovie.RuntimeOutput },
            Expand = true,
            Loop = false,
        };
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += () => CompleteOwnedVideo(false);
        _introLayer.AddChild(_video);
        var skip = Button("SKIP  •  ESC");
        skip.Name = "SkipFallout3OwnedCg01Transition";
        skip.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        skip.Position = new Vector2(
            Fo3OpeningFlowNumericContracts.SkipButtonOffsetXPixels,
            Fo3OpeningFlowNumericContracts.SkipButtonOffsetYPixels);
        skip.Size = new Vector2(
            Fo3OpeningFlowNumericContracts.SkipButtonWidthPixels,
            Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels);
        skip.Pressed += () => CompleteOwnedVideo(true);
        _introLayer.AddChild(skip);
        _video.Play();
        GD.Print(
            $"OPENNV_FO3_CG01_TRANSITION_MOVIE_STARTED path={state.TransitionMovie.LogicalPath} " +
            $"runtime={state.TransitionMovie.RuntimeOutput} requestCount=1 escapeSkip=1");
    }

    private void CompleteCg01TransitionMovie(bool skipped)
    {
        if (_ownedVideoMode != Fo3OwnedVideoMode.Cg01Transition ||
            _activeCg01MovieState is null)
            return;
        var state = _activeCg01MovieState;
        var context = _activeCg01MovieContext;
        _activeCg01MovieState = null;
        _activeCg01MovieContext = null;
        ClearOwnedVideo();
        if (_cg01ProofMode == "apply")
            _cg01ProofMovieEscapeSkipped = skipped;
        BeginCg01DadDialogue(
            state,
            context ?? throw new InvalidOperationException(
                "Fallout 3 CG01 Dad dialogue has no runtime context."),
            resumed: false);
        GD.Print(
            $"OPENNV_FO3_CG01_TRANSITION_MOVIE_COMPLETE " +
            $"mode={(skipped ? "skipped" : "watched")} stage={state.ActiveStage} " +
            $"nextApplied=0 blocker={state.NextBoundary.Blocker}");
    }

    private void BeginCg01DadDialogue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        bool resumed)
    {
        _vaultPreviewOverlay?.QueueFree();
        var subtitle = AddVaultDialogueOverlay("FO3_CG01_STAGE5_DAD_DIALOGUE");
        var cues = _profile.Cg01Stage10Transition.DialogueFor(context.Sex.EngineSex);
        PlayCg01DadCue(stage5, context, cues, 0, subtitle);
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_DIALOGUE_STARTED stage={stage5.ActiveStage} " +
            $"sex={context.Sex.EngineSex} cues={cues.Count} resumed={(resumed ? 1 : 0)} " +
            "movieReplayed=0");
    }

    private void PlayCg01DadCue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        IReadOnlyList<Fo3Cg01DadSpeechCue> cues,
        int index,
        Label subtitle)
    {
        if (index < 0 || index >= cues.Count)
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue cursor differs.");
        var cue = cues[index];
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad voice could not be decoded: " +
                cue.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException("Fallout 3 CG01 Dad voice has no duration.");
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = $"Fallout3Cg01DadVoice{cue.Sequence}",
            Stream = stream,
        };
        _vaultDialogueVoice.Finished += () =>
        {
            _vaultDialogueVoice?.QueueFree();
            _vaultDialogueVoice = null;
            if (index + 1 < cues.Count)
            {
                var timer = GetTree().CreateTimer(cue.DadTimerAfterSeconds);
                timer.Timeout += () => PlayCg01DadCue(
                    stage5,
                    context,
                    cues,
                    index + 1,
                    subtitle);
                GD.Print(
                    $"OPENNV_FO3_CG01_DAD_TIMER_SET info={cue.InfoFormId} " +
                    $"seconds={cue.DadTimerAfterSeconds:F1}");
                return;
            }
            CompleteCg01DadDialogue(stage5, context, cues, subtitle);
        };
        AddChild(_vaultDialogueVoice);
        subtitle.Text = $"DAD: {cue.Response.Text}";
        subtitle.Visible = true;
        _vaultDialogueVoice.Play();
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_CUE_STARTED sequence={cue.Sequence} " +
            $"info={cue.InfoFormId} duration={durationSeconds:F3} " +
            $"voice={cue.Response.Voice.LogicalPath} lip={cue.Response.Lip.LogicalPath}");
    }

    private void CompleteCg01DadDialogue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        IReadOnlyList<Fo3Cg01DadSpeechCue> cues,
        Label subtitle)
    {
        subtitle.Visible = false;
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        var state = _profile.Cg01Stage10Transition.Apply(stage5, context.Sex.EngineSex);
        if (!state.AppliedInfoFormIds.SequenceEqual(cues.Select(value => value.InfoFormId)))
            throw new InvalidOperationException("Fallout 3 CG01 applied INFO sequence differs.");
        PersistCg01Stage10Transition(context, stage5, state);
        if (_cg01ProofMode == "apply")
        {
            var stage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
                state,
                _profile.Cg01Stage12Transition.Trigger.ReferenceFormId,
                actionReferenceWasPlayer: true);
            PersistCg01Stage12Transition(context, stage5, state, stage12);
            WriteCg01ProofReport(
                stage5,
                state,
                stage12,
                context.Sex.EngineSex,
                "apply",
                movieSurfaceRequested: true,
                escapeSkipped: _cg01ProofMovieEscapeSkipped,
                movieReplayed: false,
                dialoguePlayed: true);
            GD.Print(
                $"OPENNV_FO3_CG01_STAGE12_PROOF_APPLY stage={stage12.ActiveStage} " +
                $"infos={string.Join(',', state.AppliedInfoFormIds)} movieSkipped=" +
                $"{(_cg01ProofMovieEscapeSkipped ? 1 : 0)} dialoguePlayed=1 " +
                $"autosave={state.AutosaveRequestCount} " +
                $"trigger={stage12.TriggerReferenceFormId}");
            GetTree().Quit(0);
            return;
        }
        ShowCg01PostStage10Boundary(state, resumed: false);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE10_APPLIED quest={state.ActiveQuestFormId} " +
            $"stage={state.ActiveStage} infos={string.Join(',', state.AppliedInfoFormIds)} " +
            $"commands={state.AppliedCommandCount} dadTimer={state.DadTimerSeconds:F1} " +
            $"objective={state.DisplayedObjectiveIndex} tutorial={state.TutorialQuestStage} " +
            $"autosave={state.AutosaveRequestCount} blocker={state.NextBoundary.Blocker}");
    }

    private void ShowCg01PostStage10Boundary(Fo3Cg01Stage10State state, bool resumed)
    {
        _vaultPreviewOverlay?.QueueFree();
        var overlay = new PanelContainer
        {
            Name = "FO3_CG01_POST_STAGE10_BOUNDARY",
            AnchorLeft = 0.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetTop = Fo3OpeningFlowNumericContracts.BoundaryTopOffsetPixels,
            OffsetRight = -Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetBottom = Fo3OpeningFlowNumericContracts.BoundaryBottomOffsetPixels,
        };
        overlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(
                0.0f,
                0.0f,
                0.0f,
                Fo3OpeningFlowNumericContracts.BoundaryPanelAlpha),
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.SeparationPixels);
        overlay.AddChild(margin);
        var content = new VBoxContainer();
        margin.AddChild(content);
        content.AddChild(Label(
            $"{state.ActiveQuestEditorId}  •  STAGE {state.ActiveStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        content.AddChild(Label(
            $"OBJECTIVE: {_profile.Cg01Stage12Transition.ObjectiveText}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        content.AddChild(Label(
            "Dad's two source-authored cues completed and stage 10 is saved. The exact owned " +
            "walk trigger is compiled; toddler world locomotion and CG01 Dad presentation " +
            "remain deliberately stopped.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var exit = Button("RETURN TO MAIN MENU");
        exit.Pressed += ExitVault101Preview;
        content.AddChild(exit);
        AddChild(overlay);
        _vaultPreviewOverlay = overlay;
        Callable.From(exit.GrabFocus).CallDeferred();
        if (resumed)
        {
            GD.Print(
                $"OPENNV_FO3_CG01_COLD_RESTORE quest={state.ActiveQuestFormId} " +
                $"stage={state.ActiveStage} commands={state.AppliedCommandCount} " +
                $"movieReplayed=0 dialogueReplayed=0 transitionEffectsReplayed=0 " +
                $"nextApplied=0 blocker={state.NextBoundary.Blocker}");
        }
    }

    private void ShowCg01PostStage12Boundary(Fo3Cg01Stage12State state, bool resumed)
    {
        _vaultPreviewOverlay?.QueueFree();
        var overlay = new PanelContainer
        {
            Name = "FO3_CG01_POST_STAGE12_BOUNDARY",
            AnchorLeft = 0.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetTop = Fo3OpeningFlowNumericContracts.BoundaryTopOffsetPixels,
            OffsetRight = -Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetBottom = Fo3OpeningFlowNumericContracts.BoundaryBottomOffsetPixels,
        };
        overlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(
                0.0f,
                0.0f,
                0.0f,
                Fo3OpeningFlowNumericContracts.BoundaryPanelAlpha),
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.SeparationPixels);
        overlay.AddChild(margin);
        var content = new VBoxContainer();
        margin.AddChild(content);
        content.AddChild(Label(
            $"{state.ActiveQuestEditorId}  •  STAGE {state.ActiveStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        content.AddChild(Label(
            $"OBJECTIVE COMPLETE: {_profile.Cg01Stage12Transition.ObjectiveText}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        content.AddChild(Label(
            "The owned Dad trigger and exact stage-12 commands are saved. CG01 Dad's response, " +
            "toddler locomotion, and the wider Vault route remain deliberately stopped.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var exit = Button("RETURN TO MAIN MENU");
        exit.Pressed += ExitVault101Preview;
        content.AddChild(exit);
        AddChild(overlay);
        _vaultPreviewOverlay = overlay;
        Callable.From(exit.GrabFocus).CallDeferred();
        if (resumed)
        {
            GD.Print(
                $"OPENNV_FO3_CG01_STAGE12_COLD_RESTORE quest={state.ActiveQuestFormId} " +
                $"stage={state.ActiveStage} trigger={state.TriggerReferenceFormId} " +
                "transitionEffectsReplayed=0 nextApplied=0 " +
                $"blocker={state.NextBoundary.Blocker}");
        }
    }

    private void StartStage90ImageSpace(Fo3Stage90ImageSpaceModifier modifier)
    {
        _vaultStage90Fade?.QueueFree();
        _activeStage90ImageSpaceModifier = modifier;
        _stage90ImageSpaceElapsedSeconds = 0.0;
        _vaultStage90Fade = new ColorRect
        {
            Name = "FO3_CG00_STAGE90_OWNED_FADE",
            Color = EvaluateStage90Fade(modifier.Fade, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _vaultStage90Fade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_vaultStage90Fade);
    }

    private void StartStage90Sound(Fo3Stage90Sound sound)
    {
        _vaultEffectSound?.Stop();
        _vaultEffectSound?.QueueFree();
        var stream = AudioStreamWav.LoadFromFile(sound.Asset.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 owned stage-90 sound could not be decoded: " +
                sound.Asset.LogicalPath);
        _vaultEffectSound = new AudioStreamPlayer
        {
            Name = "FO3_CG00_STAGE90_OWNED_SOUND",
            Stream = stream,
        };
        AddChild(_vaultEffectSound);
        _vaultEffectSound.Play();
    }

    private static Color EvaluateStage90Fade(
        IReadOnlyList<Fo3Stage90FadeKey> keys,
        float normalizedTime)
    {
        if (normalizedTime <= keys[0].Time)
            return keys[0].Color;
        if (normalizedTime >= keys[^1].Time)
            return keys[^1].Color;
        for (var index = 1; index < keys.Count; index++)
        {
            var right = keys[index];
            if (normalizedTime > right.Time)
                continue;
            var left = keys[index - 1];
            var width = right.Time - left.Time;
            var weight = width <= 0.0f
                ? 1.0f
                : (normalizedTime - left.Time) / width;
            return left.Color.Lerp(right.Color, weight);
        }
        throw new InvalidOperationException("Fallout 3 stage-90 fade curve is incomplete.");
    }

    private void ExitVault101Preview()
    {
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        _vaultDialogueVoice = null;
        _vaultEffectSound?.Stop();
        _vaultEffectSound?.QueueFree();
        _vaultEffectSound = null;
        _vaultStage90Fade?.QueueFree();
        _vaultStage90Fade = null;
        _activeStage90ImageSpaceModifier = null;
        _stage90ImageSpaceElapsedSeconds = 0.0;
        _stage100Runtime = null;
        _stage100TimerRemainingSeconds = 0.0;
        _vaultBirthCoverage = null;
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        _vaultPreviewHost?.QueueFree();
        _vaultPreviewHost = null;
        _background.Visible = true;
        _panel.Visible = true;
        StartMenuMusicAfterStop();
        ShowMainMenu();
    }

    private void ShowSection4PackageActive(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection)
    {
        var activation = _profile.Section4Transition.ActivateAtOwnedMarker(
            _profile.Section4Transition.LocationReferenceFormId,
            _profile.Appearance.AcceptedStage,
            targetStageDone: false);
        var package = activation.Package;
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {_profile.Appearance.AcceptedStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"ACTIVE PLAYER PACKAGE: {package.EditorId} ({package.FormId})",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"OWNED LOCATION REFERENCE: {package.LocationReferenceFormId}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT OWNED COMMAND: {package.NextCommand}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"Stage {package.NextStage} applies every owned MatchRace and MatchFaceGeometry " +
            "command to the four source-resolved Dad references.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var apply = Button($"APPLY STAGE {package.NextStage}");
        apply.Pressed += () =>
        {
            var state = _profile.Stage65Appearance.Apply(
                sex.EngineSex,
                selection.Race.FormId,
                selection.Sex.FaceGen);
            PersistStage65Appearance(
                playerName,
                sex,
                selection,
                package,
                state,
                activation);
            ShowStage65AppearanceApplied(playerName, sex, selection, state);
        };
        _content.AddChild(apply);
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_SECTION4_ACTIVE profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.AcceptedStage} package={package.FormId} " +
            $"location={package.LocationReferenceFormId} nextStage={package.NextStage} " +
            "advanced=0 stage65ContractReady=1");
    }

    private void ShowStage65AppearanceApplied(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState state)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {state.Stage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"{state.AppliedCommandCount} OWNED COMMANDS APPLIED  •  " +
            $"{state.Parents.Count} PARENT APPEARANCES RESOLVED",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The selected player race and FaceGen remain authoritative. Each Dad now uses that " +
            "race, its default face texture, and the owned percentage geometry match.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT BOUNDARY: {state.NextBoundary}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The owned post-stage-65 INFO conditions select one sex-specific result. " +
            "Its source-bound cue plays in the bounded Vault preview; this state screen " +
            "applies only the exact stage result.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var apply = Button($"APPLY OWNED INFO RESULT  •  STAGE {_profile.Stage80Transition.Stage}");
        apply.Pressed += () =>
        {
            var stage80 = _profile.Stage80Transition.Apply(sex.EngineSex, state);
            var package = _profile.Section4Transition.Activate();
            PersistStage80Transition(playerName, sex, selection, package, state, stage80);
            ShowStage80Applied(playerName, sex, selection, state, stage80);
        };
        _content.AddChild(apply);
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE65_APPLIED profile={_profile.ProfileId} " +
            $"stage={state.Stage} commands={state.AppliedCommandCount} " +
            $"parents={state.Parents.Count} playerRace={selection.Race.FormId} " +
            $"nextBoundary={state.NextBoundary}");
    }

    private void ShowStage80Applied(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State state)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {state.Stage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"OWNED INFO RESULT: {state.AppliedInfoFormId}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"{state.AppliedCommandCount} OWNED COMMANDS APPLIED  •  " +
            $"ADDED PACKAGE {state.AddedPlayerPackage.EditorId} " +
            $"({state.AddedPlayerPackage.FormId})",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"SCRIPT VARIABLES: {state.ScriptVariables.Count}  •  " +
            $"PACKAGE REEVALUATIONS: {state.EvaluatedPackageReferences.Count}  •  " +
            $"ENABLED REFERENCES: {state.EnabledReferences.Count}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "This is authoritative CG00 state only. Vault 101 world placement, actors, " +
            "animation, and dialogue are not rendered by this transition.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT BOUNDARY: {state.NextBoundary}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The next owned INFO result advances CG00 to an authored stage with no executable " +
            "stage commands. Dialogue playback remains outside this slice.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var apply = Button($"APPLY OWNED INFO RESULT  •  STAGE {_profile.Stage85Transition.Stage}");
        apply.Pressed += () =>
        {
            var stage85 = _profile.Stage85Transition.Apply(state);
            var package = _profile.Section4Transition.Activate();
            PersistStage85Transition(
                playerName,
                sex,
                selection,
                package,
                stage65,
                state,
                stage85);
            ShowStage85Applied(playerName, sex, selection, stage85);
        };
        _content.AddChild(apply);
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE80_APPLIED profile={_profile.ProfileId} " +
            $"sourceStage={stage65.Stage} stage={state.Stage} info={state.AppliedInfoFormId} " +
            $"commands={state.AppliedCommandCount} package={state.AddedPlayerPackage.FormId} " +
            $"variables={state.ScriptVariables.Count} evp={state.EvaluatedPackageReferences.Count} " +
            $"enabled={state.EnabledReferences.Count} dialoguePlayback=0 worldRendered=0 " +
            $"nextBoundary={state.NextBoundary}");
    }

    private void ShowStage85Applied(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3Stage85State state)
    {
        ClearContent();
        _content.AddChild(Label(
            $"{_profile.QuestEditorId}  •  STAGE {state.Stage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            $"{playerName}  •  {sex.Label}  •  {selection.Race.Label}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"OWNED INFO RESULT: {state.AppliedInfoFormId}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"{state.AppliedCommandCount} OWNED STAGE COMMANDS  •  AUTHORITATIVE STATE SAVED",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            "The owned stage result contains comments only. No dialogue, animation, actors, " +
            "or Vault 101 world scene are rendered here.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        _content.AddChild(Label(
            $"NEXT BOUNDARY: {state.NextBoundary}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var menu = Button("MAIN MENU");
        menu.Pressed += () =>
        {
            StartMenuMusicAfterStop();
            ShowMainMenu();
        };
        _content.AddChild(menu);
        GD.Print(
            $"OPENNV_FO3_CG00_STAGE85_APPLIED profile={_profile.ProfileId} " +
            $"stage={state.Stage} info={state.AppliedInfoFormId} " +
            $"commands={state.AppliedCommandCount} dialoguePlayback=0 worldRendered=0 " +
            $"nextBoundary={state.NextBoundary}");
    }

    private void StartMenuMusicAfterStop()
    {
        if (!_music.Playing)
            _music.Play();
    }

    private static bool IsEscapePressed(InputEvent @event) =>
        @event is InputEventKey key &&
        key.Pressed &&
        !key.Echo &&
        (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape);

    private static string RequiredSaveString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value.GetString()!;
    }

    private static int RequiredSaveInteger(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return result;
    }

    private static JsonElement RequiredSaveObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value;
    }

    private static JsonElement RequiredSaveArray(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value;
    }

    private static bool RequiredSaveBoolean(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return value.GetBoolean();
    }

    private void ValidateRemovedPlayerPackageState(JsonElement source)
    {
        var transition = _profile.Section4Transition;
        if (RequiredSaveString(source, "schema") != "opennv-fo3-player-package-state/v1" ||
            RequiredSaveBoolean(source, "active") ||
            RequiredSaveString(source, "formId") != transition.PackageFormId ||
            RequiredSaveString(source, "editorId") != transition.PackageEditorId ||
            RequiredSaveString(source, "locationReferenceFormId") !=
                transition.LocationReferenceFormId ||
            RequiredSaveString(source, "nextCommand") != transition.NextCommand ||
            RequiredSaveInteger(source, "nextStage") != transition.NextStage)
            throw new InvalidOperationException(
                "Saved Fallout 3 removed player package differs from the profile.");
        var idles = RequiredSaveArray(source, "idleFormIds").EnumerateArray()
            .Select(value => value.GetString() ?? "")
            .ToArray();
        if (idles.Any(string.IsNullOrWhiteSpace) ||
            !transition.IdleFormIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(idles))
            throw new InvalidOperationException(
                "Saved Fallout 3 removed player-package idles differ.");
    }

    private void ValidateBirthRuntimeState(JsonElement source, string expectedCueState)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Saved Fallout 3 birth runtime has no owned presentation contract.");
        var transition = _profile.Section4Transition;
        if (RequiredSaveString(source, "schema") != "opennv-fo3-cg00-birth-runtime/v1" ||
            RequiredSaveString(source, "cellFormId") != contract.CellFormId ||
            RequiredSaveString(source, "entryReferenceFormId") != contract.EntryReferenceFormId ||
            RequiredSaveString(source, "doctorLiReferenceFormId") !=
                contract.DoctorActor.ReferenceFormId ||
            RequiredSaveString(source, "dadReferenceFormId") !=
                contract.DadActor.ReferenceFormId ||
            RequiredSaveString(source, "beginEventIdleFormId") !=
                transition.BeginEventIdleFormId ||
            RequiredSaveString(source, "changeEventIdleFormId") !=
                transition.ChangeEventIdleFormId ||
            RequiredSaveString(source, "triggerScriptEditorId") !=
                transition.TriggerScriptEditorId ||
            RequiredSaveString(source, "triggerScriptFormId") !=
                transition.TriggerScriptFormId ||
            RequiredSaveString(source, "triggerScriptSourceSha256") !=
                transition.TriggerScriptSourceSha256 ||
            RequiredSaveString(source, "triggerCondition") != transition.TriggerCondition ||
            RequiredSaveString(source, "triggerCommand") != transition.NextCommand ||
            RequiredSaveInteger(source, "triggeredStage") != transition.NextStage ||
            RequiredSaveString(source, "cueState") != expectedCueState)
            throw new InvalidOperationException(
                "Saved Fallout 3 birth runtime differs from its owned source contracts.");
    }

    private Dictionary<string, object?> BirthRuntimeState(string cueState)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 birth runtime has no owned presentation contract.");
        var transition = _profile.Section4Transition;
        return new Dictionary<string, object?>
        {
            ["schema"] = "opennv-fo3-cg00-birth-runtime/v1",
            ["cellFormId"] = contract.CellFormId,
            ["entryReferenceFormId"] = contract.EntryReferenceFormId,
            ["doctorLiReferenceFormId"] = contract.DoctorActor.ReferenceFormId,
            ["dadReferenceFormId"] = contract.DadActor.ReferenceFormId,
            ["beginEventIdleFormId"] = transition.BeginEventIdleFormId,
            ["endEventIdleFormId"] = transition.EndEventIdleFormId,
            ["changeEventIdleFormId"] = transition.ChangeEventIdleFormId,
            ["triggerScriptEditorId"] = transition.TriggerScriptEditorId,
            ["triggerScriptFormId"] = transition.TriggerScriptFormId,
            ["triggerScriptSourceSha256"] = transition.TriggerScriptSourceSha256,
            ["triggerCondition"] = transition.TriggerCondition,
            ["triggerCommand"] = transition.NextCommand,
            ["triggeredStage"] = transition.NextStage,
            ["cueState"] = cueState,
        };
    }

    private void PersistNamedCharacter(string playerName, Fo3SexChoice sex)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = _profile.Appearance.Stage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            nextCommand = _profile.AppearanceCommand,
            completed = false,
        };
        WriteState(state);
    }

    private void PersistAppearance(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = _profile.Appearance.AcceptedStage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                },
            },
            nextCommand = _profile.Appearance.AcceptedStageCommand,
            completed = false,
        };
        WriteState(state);
    }

    private void PersistSection4Package(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage package)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = _profile.Appearance.AcceptedStage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                },
            },
            playerPackage = new
            {
                schema = "opennv-fo3-player-package-state/v1",
                active = true,
                formId = package.FormId,
                editorId = package.EditorId,
                locationReferenceFormId = package.LocationReferenceFormId,
                idleFormIds = package.IdleFormIds,
                nextCommand = package.NextCommand,
                nextStage = package.NextStage,
            },
            nextCommand = package.NextCommand,
            completed = false,
        };
        WriteState(state);
    }

    private void PersistStage65Appearance(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage package,
        Fo3Stage65AppearanceState stage65,
        Fo3PlayerPackageRuntimeActivation? birthActivation = null)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = stage65.Stage,
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                },
            },
            playerPackage = new
            {
                schema = "opennv-fo3-player-package-state/v1",
                active = true,
                formId = package.FormId,
                editorId = package.EditorId,
                locationReferenceFormId = package.LocationReferenceFormId,
                idleFormIds = package.IdleFormIds,
                nextCommand = package.NextCommand,
                nextStage = package.NextStage,
            },
            stage65Appearance = new
            {
                schema = Fo3Stage65AppearanceTransition.ExpectedSchema,
                stage = stage65.Stage,
                appliedCommandCount = stage65.AppliedCommandCount,
                playerFaceGen = new
                {
                    symmetricGeometrySha256 = stage65.PlayerSymmetricGeometrySha256,
                    asymmetricGeometrySha256 = stage65.PlayerAsymmetricGeometrySha256,
                    symmetricTextureSha256 = stage65.PlayerSymmetricTextureSha256,
                },
                parents = stage65.Parents.Select(parent => new
                {
                    referenceFormId = parent.ReferenceFormId,
                    referenceEditorId = parent.ReferenceEditorId,
                    baseFormId = parent.BaseFormId,
                    raceFormId = parent.RaceFormId,
                    symmetricGeometrySha256 = parent.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = parent.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = parent.SymmetricTextureSha256,
                }),
                nextBoundary = stage65.NextBoundary,
            },
            birthRuntime = birthActivation is null
                ? null
                : BirthRuntimeState("stage65-source-bound-ready"),
            completed = false,
        };
        WriteState(state);
    }

    private void PersistStage80Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State? stage85 = null,
        Fo3Stage90State? stage90 = null,
        Fo3Stage100State? stage100 = null,
        Fo3Cg01Stage0State? cg01 = null,
        Fo3Cg01Stage10State? cg01Stage10 = null,
        Fo3Cg01Stage12State? cg01Stage12 = null)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = cg01Stage12?.ActiveStage ?? cg01Stage10?.ActiveStage ?? cg01?.ActiveStage ??
                stage100?.Stage ?? stage90?.Stage ?? stage85?.Stage ?? stage80.Stage,
            activeQuest = cg01 is null
                ? null
                : new
                {
                    formId = cg01.ActiveQuestFormId,
                    editorId = cg01.ActiveQuestEditorId,
                    stage = cg01.ActiveStage,
                },
            playerName,
            sex = new { label = sex.Label, engineSex = sex.EngineSex },
            appearance = new
            {
                sourceContract = Fo3AppearanceContract.ExpectedSchema,
                adultRaceFormId = selection.Race.FormId,
                childRaceFormId = selection.Race.ChildRaceFormId,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                faceGen = new
                {
                    symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = selection.Sex.FaceGen.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = selection.Sex.FaceGen.SymmetricTextureSha256,
                },
            },
            playerPackage = new
            {
                schema = "opennv-fo3-player-package-state/v1",
                active = stage100 is null,
                formId = section4Package.FormId,
                editorId = section4Package.EditorId,
                locationReferenceFormId = section4Package.LocationReferenceFormId,
                idleFormIds = section4Package.IdleFormIds,
                nextCommand = section4Package.NextCommand,
                nextStage = section4Package.NextStage,
            },
            stage65Appearance = new
            {
                schema = Fo3Stage65AppearanceTransition.ExpectedSchema,
                stage = stage65.Stage,
                appliedCommandCount = stage65.AppliedCommandCount,
                playerFaceGen = new
                {
                    symmetricGeometrySha256 = stage65.PlayerSymmetricGeometrySha256,
                    asymmetricGeometrySha256 = stage65.PlayerAsymmetricGeometrySha256,
                    symmetricTextureSha256 = stage65.PlayerSymmetricTextureSha256,
                },
                parents = stage65.Parents.Select(parent => new
                {
                    referenceFormId = parent.ReferenceFormId,
                    referenceEditorId = parent.ReferenceEditorId,
                    baseFormId = parent.BaseFormId,
                    raceFormId = parent.RaceFormId,
                    symmetricGeometrySha256 = parent.SymmetricGeometrySha256,
                    asymmetricGeometrySha256 = parent.AsymmetricGeometrySha256,
                    symmetricTextureSha256 = parent.SymmetricTextureSha256,
                }),
                nextBoundary = stage65.NextBoundary,
            },
            stage80Transition = new
            {
                schema = Fo3Stage80Transition.ExpectedSchema,
                stage = stage80.Stage,
                appliedInfoFormId = stage80.AppliedInfoFormId,
                appliedCommandCount = stage80.AppliedCommandCount,
                addedPlayerPackage = new
                {
                    active = true,
                    formId = stage80.AddedPlayerPackage.FormId,
                    editorId = stage80.AddedPlayerPackage.EditorId,
                    locationReferenceFormId = stage80.AddedPlayerPackage.LocationReferenceFormId,
                    idleFormIds = stage80.AddedPlayerPackage.IdleFormIds,
                },
                scriptVariables = stage80.ScriptVariables.Select(variable => new
                {
                    referenceFormId = variable.ReferenceFormId,
                    referenceEditorId = variable.ReferenceEditorId,
                    variable = variable.Variable,
                    value = variable.Value,
                }),
                evaluatedPackageReferences = stage80.EvaluatedPackageReferences.Select(
                    reference => new
                    {
                        formId = reference.FormId,
                        editorId = reference.EditorId,
                    }),
                enabledReferences = stage80.EnabledReferences.Select(reference => new
                {
                    formId = reference.FormId,
                    editorId = reference.EditorId,
                }),
                nextBoundary = stage80.NextBoundary,
            },
            stage85Transition = stage85 is null
                ? null
                : new
                {
                    schema = Fo3Stage85Transition.ExpectedSchema,
                    stage = stage85.Stage,
                    appliedInfoFormId = stage85.AppliedInfoFormId,
                    appliedCommandCount = stage85.AppliedCommandCount,
                    nextBoundary = stage85.NextBoundary,
                },
            stage90Transition = stage90 is null
                ? null
                : new
                {
                    schema = Fo3Stage90Transition.ExpectedSchema,
                    stage = stage90.Stage,
                    appliedInfoFormId = stage90.AppliedInfoFormId,
                    appliedCommandCount = stage90.AppliedCommandCount,
                    questVariables = stage90.QuestVariables.Select(variable => new
                    {
                        name = variable.Name,
                        type = variable.Type,
                        value = variable.Value,
                    }),
                    imageSpaceModifier = new
                    {
                        formId = stage90.ImageSpaceModifier.FormId,
                        editorId = stage90.ImageSpaceModifier.EditorId,
                        recordSha256 = stage90.ImageSpaceModifier.RecordSha256,
                    },
                    sound = new
                    {
                        formId = stage90.Sound.FormId,
                        editorId = stage90.Sound.EditorId,
                        assetSha256 = stage90.Sound.Asset.Sha256,
                    },
                    imageSpaceFadeApplied = stage90.ImageSpaceFadeApplied,
                    imageSpaceOtherChannelsApplied = stage90.ImageSpaceOtherChannelsApplied,
                    soundStarted = stage90.SoundStarted,
                    timerAdvancing = stage90.TimerAdvancing,
                    nextBoundary = stage90.NextBoundary,
                },
            stage100Transition = stage100 is null
                ? null
                : new
                {
                    schema = Fo3Stage100Transition.ExpectedSchema,
                    stage = stage100.Stage,
                    accountedCommandCount = stage100.AccountedCommandCount,
                    appliedCommandCount = stage100.AppliedCommandCount,
                    timerRemainingSeconds = stage100.TimerRemainingSeconds,
                    timerAdvancing = stage100.TimerAdvancing,
                    playerScriptPackageActive = stage100.PlayerScriptPackageActive,
                    scriptVariables = stage100.ScriptVariables.Select(variable => new
                    {
                        referenceFormId = variable.ReferenceFormId,
                        referenceEditorId = variable.ReferenceEditorId,
                        variable = variable.Variable,
                        value = variable.Value,
                    }),
                    removedImageSpaceModifier = new
                    {
                        formId = stage100.RemovedImageSpaceModifier.FormId,
                        editorId = stage100.RemovedImageSpaceModifier.EditorId,
                        recordSha256 = stage100.RemovedImageSpaceModifier.RecordSha256,
                    },
                    disabledDad = new
                    {
                        formId = stage100.DisabledDad.FormId,
                        editorId = stage100.DisabledDad.EditorId,
                    },
                    cg00Running = stage100.Cg00Running,
                    playerYoung = stage100.PlayerYoung,
                    nextBoundary = new
                    {
                        commandIndex = 7,
                        kind = "setStage",
                        questFormId = stage100.NextBoundary.QuestFormId,
                        questEditorId = stage100.NextBoundary.QuestEditorId,
                        stage = stage100.NextBoundary.Stage,
                        stageResultSourceSha256 =
                            stage100.NextBoundary.StageResultSourceSha256,
                        stageResultCommandCount =
                            stage100.NextBoundary.StageResultCommandCount,
                        transitionContract = new
                        {
                            schema = stage100.NextBoundary.TransitionContract.Schema,
                            sha256 = stage100.NextBoundary.TransitionContract.Sha256,
                        },
                        applied = stage100.NextBoundary.Applied,
                        blocker = stage100.NextBoundary.Blocker,
                    },
                },
            cg01Stage0Transition = cg01 is null
                ? null
                : _profile.Cg01Stage0Transition.SavedState(cg01),
            cg01Stage10Transition = cg01Stage10 is null
                ? null
                : _profile.Cg01Stage10Transition.SavedState(cg01Stage10),
            cg01Stage12Transition = cg01Stage12 is null
                ? null
                : _profile.Cg01Stage12Transition.SavedState(cg01Stage12),
            birthRuntime = BirthRuntimeState(cg01Stage12 is not null
                ? "cg01-stage12-authored-trigger-applied-post-stage12-blocked"
                : cg01Stage10 is not null
                ? "cg01-stage10-applied-post-stage10-blocked"
                : cg01 is not null
                ? "cg01-stage0-stage5-applied-dad-dialogue-pending"
                : stage100 is not null
                ? "stage90-timer-finished-stage100-applied"
                : stage90 is not null
                    ? "stage85-info-finished-stage90-applied"
                : stage85 is not null
                    ? "stage80-info-trigger-stage85-applied"
                    : "stage65-cue-finished-stage80-applied"),
            completed = false,
        };
        WriteState(state);
    }

    private void PersistStage85Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85);

    private void PersistStage90Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85,
        Fo3Stage90State stage90) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85,
            stage90);

    private void PersistStage100Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85,
        Fo3Stage90State stage90,
        Fo3Stage100State stage100) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85,
            stage90,
            stage100);

    private void PersistCg01Transition(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        Fo3ActivePlayerPackage section4Package,
        Fo3Stage65AppearanceState stage65,
        Fo3Stage80State stage80,
        Fo3Stage85State stage85,
        Fo3Stage90State stage90,
        Fo3Stage100State stage100,
        Fo3Cg01Stage0State cg01) =>
        PersistStage80Transition(
            playerName,
            sex,
            selection,
            section4Package,
            stage65,
            stage80,
            stage85,
            stage90,
            stage100,
            cg01);

    private void PersistCg01Stage10Transition(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State cg01,
        Fo3Cg01Stage10State cg01Stage10) =>
        PersistStage80Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            cg01,
            cg01Stage10);

    private void PersistCg01Stage12Transition(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State cg01,
        Fo3Cg01Stage10State cg01Stage10,
        Fo3Cg01Stage12State cg01Stage12) =>
        PersistStage80Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            cg01,
            cg01Stage10,
            cg01Stage12);

    private void WriteState(object state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_savePath)!);
        var temporary = _savePath + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        File.Move(temporary, _savePath, true);
    }

    private void RunAppearanceProof()
    {
        var sex = _profile.SexChoices.Single(value => value.EngineSex == "male");
        var selection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        _ = LoadAppearanceImage(_profile.Appearance.Ui.BackgroundTexture);
        _ = LoadAppearanceImage(selection.Sex.HeadTexture);
        _ = LoadAppearanceImage(selection.Hair.Texture);
        _ = LoadAppearanceImage(selection.Eyes.Texture);
        PersistAppearance(_profile.Appearance.PlayerEditorId, sex, selection);
        GD.Print(
            $"OPENNV_FO3_CG00_APPEARANCE_PROOF profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.AcceptedStage} sex={sex.EngineSex} " +
            $"race={selection.Race.FormId} hair={selection.Hair.FormId} " +
            $"eyes={selection.Eyes.FormId} previewTextures=4 save={_savePath} " +
            $"next={_profile.Appearance.AcceptedStageCommand}");
        GetTree().Quit(0);
    }

    private void RunCg01Proof()
    {
        if (_cg01ProofMode is not "apply" and not "restore" ||
            string.IsNullOrWhiteSpace(_cg01ProofReportPath) ||
            _birthPresentation is null)
            throw new InvalidOperationException("Fallout 3 CG01 proof configuration differs.");
        var sex = _profile.SexChoices.Single(value => value.EngineSex == "male");
        var selection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        var package = _profile.Section4Transition.Activate();
        var stage65 = _profile.Stage65Appearance.Apply(
            sex.EngineSex,
            selection.Race.FormId,
            selection.Sex.FaceGen);
        var stage80 = _profile.Stage80Transition.Apply(sex.EngineSex, stage65);
        var stage85 = _profile.Stage85Transition.Apply(stage80);
        var stage90 = _profile.Stage90Transition.Apply(stage85);
        var stage100 = _profile.Stage100Transition.Apply(stage90, 0.0);
        var cg01 = _profile.Cg01Stage0Transition.Apply(stage100);
        if (_cg01ProofMode == "apply")
        {
            if (File.Exists(_savePath))
                throw new InvalidOperationException(
                    "Fallout 3 CG01 apply proof requires a fresh save path.");
            PersistCg01Transition(
                _profile.Appearance.PlayerEditorId,
                sex,
                selection,
                package,
                stage65,
                stage80,
                stage85,
                stage90,
                stage100,
                cg01);
            StartCg01TransitionMovie(
                cg01,
                new Fo3Cg01RuntimeContext(
                    _profile.Appearance.PlayerEditorId,
                    sex,
                    selection,
                    package,
                    stage65,
                    stage80,
                    stage85,
                    stage90,
                    stage100));
            Callable.From(() => Input.ParseInputEvent(new InputEventKey
            {
                Keycode = Key.Escape,
                PhysicalKeycode = Key.Escape,
                Pressed = true,
            })).CallDeferred();
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var root = document.RootElement;
        if (RequiredSaveString(root, "schema") != "opennv-fo3-opening-character/v2" ||
            RequiredSaveString(root, "profileId") != _profile.ProfileId ||
            RequiredSaveString(root, "profileSha256") != _profile.Sha256)
            throw new InvalidOperationException("Fallout 3 CG01 proof save identity differs.");
        _profile.Stage100Transition.ValidateSavedState(
            RequiredSaveObject(root, "stage100Transition"),
            stage100);
        _profile.Cg01Stage0Transition.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage0Transition"),
            cg01);
        var cg01Stage10 = _profile.Cg01Stage10Transition.Apply(cg01, sex.EngineSex);
        _profile.Cg01Stage10Transition.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage10Transition"),
            cg01Stage10);
        var cg01Stage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
            cg01Stage10,
            _profile.Cg01Stage12Transition.Trigger.ReferenceFormId,
            actionReferenceWasPlayer: true);
        _profile.Cg01Stage12Transition.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage12Transition"),
            cg01Stage12);
        ValidateBirthRuntimeState(
            RequiredSaveObject(root, "birthRuntime"),
            "cg01-stage12-authored-trigger-applied-post-stage12-blocked");
        WriteCg01ProofReport(
            cg01,
            cg01Stage10,
            cg01Stage12,
            sex.EngineSex,
            "restore",
            movieSurfaceRequested: false,
            escapeSkipped: false,
            movieReplayed: false,
            dialoguePlayed: false);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE12_PROOF_RESTORE stage={cg01Stage12.ActiveStage} " +
            "movieReplayed=0 dialogueReplayed=0 transitionEffectsReplayed=0");
        GetTree().Quit(0);
    }

    private void WriteCg01ProofReport(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        string engineSex,
        string phase,
        bool movieSurfaceRequested,
        bool escapeSkipped,
        bool movieReplayed,
        bool dialoguePlayed)
    {
        var path = _cg01ProofReportPath ?? throw new InvalidOperationException(
            "Fallout 3 CG01 proof report path is absent.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var cues = _profile.Cg01Stage10Transition.DialogueFor(engineSex);
        var report = new
        {
            schema = "opennv-fo3-cg01-runtime-proof/v3",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            phase,
            savePath = _savePath,
            activeQuest = new
            {
                formId = stage10.ActiveQuestFormId,
                editorId = stage10.ActiveQuestEditorId,
                stage = stage12.ActiveStage,
            },
            stage5Commands = new
            {
                accounted = stage5.AccountedCommandCount,
                applied = stage5.AppliedCommandCount,
                trace = stage5.AppliedExecutionTrace,
            },
            dialogue = new
            {
                engineSex,
                infoFormIds = stage10.AppliedInfoFormIds,
                sourceTimerSeconds = cues[0].DadTimerAfterSeconds,
                playedThisProcess = dialoguePlayed,
                replayed = false,
                assets = cues.Select(cue => new
                {
                    cue.Sequence,
                    cue.InfoFormId,
                    voiceSha256 = cue.Response.Voice.Sha256,
                    lipSha256 = cue.Response.Lip.Sha256,
                }),
            },
            stage10Commands = new
            {
                accounted = stage10.AccountedCommandCount,
                applied = stage10.AppliedCommandCount,
                trace = stage10.AppliedExecutionTrace,
                dadTimerSeconds = stage10.DadTimerSeconds,
                displayedObjectiveIndex = stage10.DisplayedObjectiveIndex,
                enabledPlayerControls = stage10.EnabledPlayerControls,
                tutorialQuest = new
                {
                    formId = stage10.TutorialQuestFormId,
                    editorId = stage10.TutorialQuestEditorId,
                    stage = stage10.TutorialQuestStage,
                },
                autosaveRequestCount = stage10.AutosaveRequestCount,
            },
            stage12Trigger = new
            {
                referenceFormId = stage12.TriggerReferenceFormId,
                actionReferenceWasPlayer = stage12.ActionReferenceWasPlayer,
                objectiveText = _profile.Cg01Stage12Transition.ObjectiveText,
                completedObjectiveIndex = stage12.CompletedObjectiveIndex,
                disabledPlayerControls = stage12.DisabledPlayerControls,
                dadDoTalk = stage12.DadDoTalk,
                dadTimerSeconds = stage12.DadTimerSeconds,
                accounted = stage12.AccountedCommandCount,
                applied = stage12.AppliedCommandCount,
                trace = stage12.AppliedExecutionTrace,
            },
            movie = new
            {
                logicalPath = stage5.TransitionMovie.LogicalPath,
                runtimeOutputSha256 = stage5.TransitionMovie.RuntimeOutputSha256,
                requestCount = stage5.TransitionMovieRequestCount,
                surfaceRequested = movieSurfaceRequested,
                escapeSkipped,
                replayed = movieReplayed,
            },
            nextBoundary = new
            {
                applied = stage12.NextBoundary.Applied,
                blocker = stage12.NextBoundary.Blocker,
            },
        };
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        File.Move(temporary, path, true);
    }

    private void AddSelector(GridContainer grid, string title, OptionButton selector)
    {
        grid.AddChild(Label(title, Fo3OpeningFlowNumericContracts.BodyFontPixels));
        selector.CustomMinimumSize = new Vector2(
            _profile.Appearance.Ui.ListItemWidth,
            _profile.Appearance.Ui.ListItemHeight);
        selector.AddThemeFontSizeOverride("font_size", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        grid.AddChild(selector);
    }

    private static void FillOptions(
        OptionButton selector,
        IReadOnlyList<Fo3AppearanceRace> options,
        string selectedFormId)
    {
        selector.Clear();
        for (var index = 0; index < options.Count; index++)
        {
            selector.AddItem(options[index].Label);
            selector.SetItemMetadata(index, options[index].FormId);
            if (options[index].FormId == selectedFormId)
                selector.Select(index);
        }
    }

    private static void FillOptions(
        OptionButton selector,
        IReadOnlyList<Fo3AppearanceOption> options,
        string selectedFormId)
    {
        selector.Clear();
        for (var index = 0; index < options.Count; index++)
        {
            selector.AddItem(options[index].Label);
            selector.SetItemMetadata(index, options[index].FormId);
            if (options[index].FormId == selectedFormId)
                selector.Select(index);
        }
    }

    private void RenderAppearancePreview(
        HBoxContainer preview,
        Fo3AppearanceAsset head,
        Fo3AppearanceAsset hair,
        Fo3AppearanceAsset eyes,
        Fo3FaceGenDefaults faceGen)
    {
        foreach (var child in preview.GetChildren())
        {
            preview.RemoveChild(child);
            child.QueueFree();
        }
        preview.AddChild(AppearancePreviewTile("MENU", _profile.Appearance.Ui.BackgroundTexture));
        preview.AddChild(AppearancePreviewTile("HEAD", head));
        preview.AddChild(AppearancePreviewTile("HAIR", hair));
        preview.AddChild(AppearancePreviewTile("EYES", eyes));
        preview.TooltipText =
            $"FaceGen defaults: {faceGen.SymmetricGeometrySha256} / " +
            $"{faceGen.AsymmetricGeometrySha256} / {faceGen.SymmetricTextureSha256}";
    }

    private VBoxContainer AppearancePreviewTile(string title, Fo3AppearanceAsset asset)
    {
        var image = LoadAppearanceImage(asset);
        var tile = new VBoxContainer();
        tile.AddChild(Label(title, Fo3OpeningFlowNumericContracts.BodyFontPixels));
        tile.AddChild(new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(image),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(
                Fo3OpeningFlowNumericContracts.AppearancePreviewTexturePixels,
                Fo3OpeningFlowNumericContracts.AppearancePreviewTexturePixels),
            TooltipText = $"source={asset.SourceSha256} preview={asset.PreviewSha256}",
        });
        return tile;
    }

    private static Image LoadAppearanceImage(Fo3AppearanceAsset asset)
    {
        var image = Image.LoadFromFile(asset.PreviewPath);
        if (image is null || image.IsEmpty())
            throw new InvalidOperationException(
                $"Fallout 3 owned appearance preview could not be loaded: {asset.PreviewPath}");
        return image;
    }

    private void ClearContent()
    {
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
    }

    private Label Label(string text, int fontSize)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeColorOverride("font_color", _profile.InterfaceColor);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private Button Button(string text)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(
                0.0f,
                Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels),
        };
        button.AddThemeColorOverride("font_color", _profile.InterfaceColor);
        button.AddThemeColorOverride("font_hover_color", Colors.Black);
        button.AddThemeColorOverride("font_pressed_color", Colors.Black);
        button.AddThemeFontSizeOverride("font_size", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        var highlight = new StyleBoxFlat
        {
            BgColor = _profile.InterfaceColor,
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        };
        button.AddThemeStyleboxOverride("hover", highlight);
        button.AddThemeStyleboxOverride("focus", highlight);
        button.AddThemeStyleboxOverride("pressed", highlight);
        return button;
    }
}
