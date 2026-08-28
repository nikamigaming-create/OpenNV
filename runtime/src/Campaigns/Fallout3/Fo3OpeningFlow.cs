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
            !RequiredBoolean(capabilities, "cg00Section4PackageRuntimeReady") ||
            !RequiredBoolean(capabilities, "mainMenuRuntimeReady") ||
            !RequiredBoolean(capabilities, "introVideoRuntimeReady") ||
            !RequiredBoolean(capabilities, "runtimeBootReady"))
            throw new InvalidOperationException(
                "Fallout 3 profile has not resolved the runnable CG00 character-selection subset.");

        var install = RequiredObject(root, "install");
        VerifySourceFile(RequiredObject(install, "master"));

        var opening = RequiredObject(root, "opening");
        var selection = RequiredObject(opening, "characterSelection");
        var questEditorId = RequiredString(selection, "questEditorId");
        var name = RequiredObject(selection, "name");
        var appearance = RequiredObject(selection, "appearance");
        var appearanceContract = Fo3AppearanceContract.Load(appearance);
        var section4Transition = Fo3PlayerPackageTransition.Load(
            RequiredObject(selection, "section4Transition"),
            appearanceContract.AcceptedStage,
            appearanceContract.AcceptedStageCommand);
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

internal partial class Fo3OpeningFlow : CanvasLayer
{
    private Fo3OwnedProfile _profile = null!;
    private string _savePath = "";
    private VBoxContainer _content = null!;
    private AudioStreamPlayer _music = null!;
    private Control? _introLayer;
    private VideoStreamPlayer? _video;
    private Fo3SexChoice? _selectedSex;
    private bool _runAppearanceProof;
    private bool _introCompleted;

    internal void Configure(Fo3OwnedProfile profile, string savePath, bool runAppearanceProof = false)
    {
        _profile = profile;
        _savePath = System.IO.Path.GetFullPath(savePath);
        _runAppearanceProof = runAppearanceProof;
        Name = "Fallout3FrontEnd";
        Layer = Fo3OpeningFlowNumericContracts.UiLayer;
    }

    public override void _Ready()
    {
        BuildShell();
        if (_runAppearanceProof)
        {
            RunAppearanceProof();
            return;
        }
        StartMenuMusic();
        ShowMainMenu();
        GD.Print(
            $"OPENNV_FO3_FRONTEND_READY profile={_profile.ProfileId} " +
            $"quest={_profile.QuestEditorId} form={_profile.QuestFormId} " +
            $"intro=owned-transcode escapeSkip=1 sexChoices={_profile.SexChoices.Count} " +
            $"nameStage={_profile.NameStage} appearanceStage={_profile.AppearanceStage}");
    }

    public override void _Input(InputEvent @event)
    {
        if (_video is null || !IsEscapePressed(@event))
            return;
        GetViewport().SetInputAsHandled();
        CompleteIntro(true);
    }

    private void BuildShell()
    {
        var background = new ColorRect { Color = Colors.Black };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.AnchorLeft -= Fo3OpeningFlowNumericContracts.PanelWidthFraction * Fo3OpeningFlowNumericContracts.Center;
        panel.AnchorRight += Fo3OpeningFlowNumericContracts.PanelWidthFraction * Fo3OpeningFlowNumericContracts.Center;
        panel.AnchorTop -= Fo3OpeningFlowNumericContracts.PanelHeightFraction * Fo3OpeningFlowNumericContracts.Center;
        panel.AnchorBottom += Fo3OpeningFlowNumericContracts.PanelHeightFraction * Fo3OpeningFlowNumericContracts.Center;
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
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
        AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.MarginPixels);
        panel.AddChild(margin);
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
        _video.Finished += () => CompleteIntro(false);
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
        skip.Pressed += () => CompleteIntro(true);
        _introLayer.AddChild(skip);
        _video.Play();
        GD.Print(
            $"OPENNV_FO3_INTRO_STARTED profile={_profile.ProfileId} " +
            "source=owned-transcode escapeSkip=1");
    }

    private void CompleteIntro(bool skipped)
    {
        if (_introCompleted)
            return;
        _introCompleted = true;
        _video?.Stop();
        _video = null;
        _introLayer?.QueueFree();
        _introLayer = null;
        ShowSexSelection();
        GD.Print(
            $"OPENNV_FO3_INTRO_COMPLETE profile={_profile.ProfileId} " +
            $"mode={(skipped ? "skipped" : "watched")} next={_profile.QuestEditorId}");
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
            if (stage != _profile.Appearance.AcceptedStage)
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
            if (root.TryGetProperty("playerPackage", out var savedPackage))
            {
                _profile.Section4Transition.ValidateSavedState(savedPackage);
                ShowSection4PackageActive(playerName, _selectedSex, selection);
                return;
            }
            ShowAppearanceAccepted(playerName, _selectedSex, selection);
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
            ShowAppearanceAccepted(playerName, sex, selection);
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
            "The owned CG00 appearance choice is saved. The next action activates the exact " +
            "Section 4 player package while keeping CG00 at stage 62.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var activate = Button("CONTINUE CG00");
        activate.Pressed += () =>
        {
            var package = _profile.Section4Transition.Activate();
            PersistSection4Package(playerName, sex, selection, package);
            ShowSection4PackageActive(playerName, sex, selection);
        };
        _content.AddChild(activate);
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
            $"next={_profile.Appearance.AcceptedStageCommand} packageRuntimeReady=1");
    }

    private void ShowSection4PackageActive(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection)
    {
        var package = _profile.Section4Transition.Activate();
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
            $"Execution remains at stage {_profile.Appearance.AcceptedStage}. Stage {package.NextStage} " +
            "requires the owned parent MatchRace/MatchFaceGeometry commands, which are not yet supported.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
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
            $"advanced=0 blocker={package.Blocker}");
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
                blocker = package.Blocker,
            },
            nextCommand = package.NextCommand,
            completed = false,
        };
        WriteState(state);
    }

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
