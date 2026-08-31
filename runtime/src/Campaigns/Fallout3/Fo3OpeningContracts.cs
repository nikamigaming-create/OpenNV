using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;

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
    internal const int CreatorPanelSeparationPixels = 8;
    internal const int CreatorAppearancePanelSeparationPixels = 4;
    internal const int CreatorPanelMarginPixels = 12;
    internal const int CreatorStatusFontPixels = 16;
    internal const int SourceUiCanvasWidthPixels = 1600;
    internal const int SourceUiCanvasHeightPixels = 1200;
    internal const int ButtonMinimumHeightPixels = 54;
    internal const float PanelWidthFraction = 0.62f;
    internal const float PanelHeightFraction = 0.72f;
    internal const float Center = 0.5f;
    internal const float DimmedColorScale = 0.68f;
    internal const float PanelAlpha = 0.94f;
    internal const float SkipButtonOffsetXPixels = -220.0f;
    internal const float SkipButtonOffsetYPixels = 24.0f;
    internal const float SkipButtonWidthPixels = 190.0f;
    internal const float VaultPreviewMarginPixels = 24.0f;
    internal const float VaultPreviewPanelWidthPixels = 560.0f;
    internal const float BoundaryHorizontalInsetPixels = 120.0f;
    internal const float BoundaryTopOffsetPixels = -240.0f;
    internal const float BoundaryBottomOffsetPixels = -20.0f;
    internal const float BoundaryPanelAlpha = 0.9f;
    internal const float Cg01ProofTimeoutMultiplier = 4.0f;
    internal const int ProofFailureExitCode = 2;
    internal const int Cg01CaptureWarmupFrames = 4;
    internal const int CaptureBytesPerPixel = 4;
    internal const int CaptureRgbChannels = 3;
    internal const int FaceGenSymmetricGeometryFloats = 50;
    internal const int FaceGenAsymmetricGeometryFloats = 30;
    internal const int FaceGenSymmetricTextureFloats = 50;
    internal const int AabbCornerCount = 8;
    internal const float FaceGenPreviewNormalizedMorphWeightScale = 1.0f;
    internal const float FaceGenSliderSourceMinimum = -5.0f;
    internal const float FaceGenSliderSourceMaximum = 5.0f;
    internal const float FaceGenSliderUiScale = 10.0f;
    internal const float FaceGenSliderUiMinimum = -50.0f;
    internal const float FaceGenSliderUiMaximum = 50.0f;
    internal const float FaceGenSliderOrdinaryIncrement = 1.0f;
    internal const float FaceGenSliderJump = 25.0f;
    internal const float FaceGenSliderMorphWeightScale = 0.1f;
    internal const float FaceGenSliderIncrementDefaultThreshold = 1.0f;
    internal const string FaceGenSliderEvidenceClassification =
        "independent-sibling-gamebryo-racesexmenu-static-contract";
    internal const string FaceGenSliderEvidenceEngineBuild = "1.7.0.4";
    internal const string FaceGenSliderEvidenceExecutableSha256Prefix =
        "c3f97c2255fa041a851c17cf372d69aa";
    internal const string FaceGenSliderEvidenceExecutableSha256Suffix =
        "add8694e2dc4230ba556001bbfbd2f3e";
    internal const string FaceGenSliderLowGlobalAddress = "0x1115438";
    internal const string FaceGenSliderHighGlobalAddress = "0x1115444";
    internal const string FaceGenSliderIncrementTrait = "user6";
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
    string SymmetricTextureSha256,
    IReadOnlyList<float> SymmetricGeometry,
    IReadOnlyList<float> AsymmetricGeometry,
    IReadOnlyList<float> SymmetricTexture);

internal sealed record Fo3AppearanceNameUi(
    int PanelWidth,
    int PanelHeight,
    Fo3AppearanceAsset BackgroundTexture,
    OwnedGamebryoTextEditMenu TextEditMenu);

internal sealed record Fo3AppearancePreviewPresentation(
    float ViewportWidthFraction,
    float ViewportHeightFraction,
    float VerticalFovHalfAngleFactor,
    float DepthExtentFraction,
    float FullInVerticalOffsetGameUnits,
    float FullInDistanceGameUnits,
    float FullInYawRadians,
    float FullOutVerticalOffsetGameUnits,
    float FullOutDistanceGameUnits,
    float FullOutYawRadians,
    float StartingZoomFraction);

internal sealed record Fo3AppearanceFaceControl(
    int ControlIndex,
    string SettingEntity,
    string SourceLabel,
    string AxisSha256,
    IReadOnlyList<float> Axis,
    float Minimum,
    float Maximum,
    float Step,
    float Jump,
    float MorphWeightScale,
    float ResetValue,
    float AcceptanceValue,
    Fo3AppearancePreviewPresentation Presentation,
    string Semantics);

internal sealed record Fo3AppearanceProofCapture(
    string Path,
    string Sha256,
    int Width,
    int Height,
    int RgbSpan);

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
    int PanelX,
    int PanelY,
    int PanelWidth,
    int PanelHeight,
    int FaceGrabX,
    int FaceGrabY,
    int FaceGrabWidth,
    int FaceGrabHeight,
    int ListItemWidth,
    int ListItemHeight,
    int SliderWidth,
    int SliderHeight,
    Fo3AppearanceAsset BackgroundTexture,
    OwnedGamebryoRaceSexControls RaceSexControls,
    Fo3AppearanceNameUi Name);

internal sealed record Fo3AppearanceSelection(
    Fo3AppearanceRace Race,
    Fo3AppearanceSex Sex,
    Fo3AppearanceOption Hair,
    Fo3AppearanceOption Eyes,
    IReadOnlyDictionary<string, float> FaceControlValues)
{
    internal float FaceControlValue(string settingEntity) => FaceControlValues[settingEntity];
}

internal sealed record Fo3AppearanceContract(
    int Stage,
    int MenuEnteredStage,
    int AcceptedStage,
    string Command,
    string AcceptedStageCommand,
    string PlayerEditorId,
    string DefaultRaceFormId,
    Fo3AppearanceUi Ui,
    Fo3AppearanceFaceControl FaceControl,
    IReadOnlyList<Fo3AppearanceFaceControl> FaceControls,
    OpeningPlayerFaceGenPreviewSet PreviewSet,
    IReadOnlyList<Fo3AppearanceRace> Races)
{
    internal const string ExpectedSchema = "opennv-fo3-cg00-appearance/v1";
    private const string ExpectedStatus =
        "source-backed-native-creator-all-native-geometry-controls";
    private const string ExpectedPreview =
        "owned-playable-race-male-and-female-source-default-full-body-live-previews-" +
        "all-native-geometry-controls";
    private const string ExpectedPreviewSchema =
        "opennv-owned-player-facegen-preview-set/v4";
    private const string ExpectedPreviewStatus =
        "compiled-playable-race-male-and-female-source-default-full-body-live-previews-" +
        "with-ctl-egm-targets-all-native-geometry-controls-runtime-bound";
    private const string ExpectedPreviewRuntimeDisposition =
        "owned-playable-race-male-and-female-source-default-identity-preview-hosts-" +
        "and-all-native-geometry-controls-bound-nondefault-hair-eye-cache-artifacts-" +
        "absent-and-fail-closed-sibling-gamebryo-slider-semantics-corroborated";
    private const string ExpectedPreviewSelectionScope =
        "all-playable-race-sex-source-order-default-hair-eyes";
    private const string ExpectedUnsupportedPreviewSelectionScope =
        "nondefault-hair-or-eyes-cache-artifact-absent";
    private static readonly string[] ExpectedBodyRoles = ["body", "left-hand", "right-hand"];
    private static readonly string[] ExpectedPreviewSexes = ["male", "female"];

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
        var playerFormId = RequiredFormId(player, "formId");
        var playerEditorId = RequiredString(player, "editorId");
        var defaultRaceFormId = RequiredFormId(player, "defaultRaceFormId");
        var uiSource = RequiredObject(source, "ui");
        var nameSource = RequiredObject(uiSource, "name");
        var nameWidth = PositiveInteger(nameSource, "panelWidth");
        var nameHeight = PositiveInteger(nameSource, "panelHeight");
        var textEditMenu = OwnedGamebryoTileRuntime.ParseTextEditMenu(
            RequiredObject(nameSource, "textEditMenuTiles"));
        var raceSexControls = OwnedGamebryoTileRuntime.ParseRaceSexControls(
            RequiredObject(uiSource, "raceSexMenuTiles"));
        if (raceSexControls.BackgroundRect != new Rect2(
                PositiveInteger(uiSource, "panelX"),
                PositiveInteger(uiSource, "panelY"),
                PositiveInteger(uiSource, "panelWidth"),
                PositiveInteger(uiSource, "panelHeight")) ||
            raceSexControls.FaceGrabRect != new Rect2(
                PositiveInteger(uiSource, "faceGrabX"),
                PositiveInteger(uiSource, "faceGrabY"),
                PositiveInteger(uiSource, "faceGrabWidth"),
                PositiveInteger(uiSource, "faceGrabHeight")) ||
            raceSexControls.List.Rect.Size != new Vector2(
                PositiveInteger(uiSource, "listItemWidth"),
                PositiveInteger(uiSource, "listItemHeight")) ||
            raceSexControls.Slider.Rect.Size != new Vector2(
                PositiveInteger(uiSource, "sliderWidth"),
                PositiveInteger(uiSource, "sliderHeight")))
            throw new InvalidOperationException(
                "Fallout 3 RaceSex shared tile geometry differs.");
        if (textEditMenu.Panel.Rect.Size != new Vector2(nameWidth, nameHeight))
            throw new InvalidOperationException(
                "Fallout 3 TextEditMenu panel dimensions differ.");
        var ui = new Fo3AppearanceUi(
            PositiveInteger(uiSource, "panelX"),
            PositiveInteger(uiSource, "panelY"),
            PositiveInteger(uiSource, "panelWidth"),
            PositiveInteger(uiSource, "panelHeight"),
            PositiveInteger(uiSource, "faceGrabX"),
            PositiveInteger(uiSource, "faceGrabY"),
            PositiveInteger(uiSource, "faceGrabWidth"),
            PositiveInteger(uiSource, "faceGrabHeight"),
            PositiveInteger(uiSource, "listItemWidth"),
            PositiveInteger(uiSource, "listItemHeight"),
            PositiveInteger(uiSource, "sliderWidth"),
            PositiveInteger(uiSource, "sliderHeight"),
            LoadAsset(RequiredObject(uiSource, "backgroundTexture")),
            raceSexControls,
            new Fo3AppearanceNameUi(
                nameWidth,
                nameHeight,
                LoadAsset(RequiredObject(nameSource, "backgroundTexture")),
                textEditMenu));
        var playerFaceGen = RequiredObject(RequiredObject(source, "player"), "faceGen");
        var controlSpace = RequiredObject(playerFaceGen, "controlSpace");
        var previewControl = RequiredObject(controlSpace, "runtimePreviewControl");
        var nativeControls = RequiredArray(
            RequiredObject(controlSpace, "nativeGeometryExposure"),
            "controls").EnumerateArray().ToArray();
        var previewIndex = RequiredInteger(previewControl, "controlIndex");
        var formatControlSource = RequiredObject(
            RequiredObject(controlSpace, "format"),
            "controls");
        if (!formatControlSource.TryGetProperty("symmetricGeometry", out var formatControls) ||
            formatControls.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                "Fallout 3 FaceGen symmetric control inventory is absent.");
        var presentationSource = RequiredObject(previewControl, "presentation");
        var presentation = new Fo3AppearancePreviewPresentation(
            RequiredSingle(presentationSource, "viewportWidthFraction"),
            RequiredSingle(presentationSource, "viewportHeightFraction"),
            RequiredSingle(presentationSource, "verticalFovHalfAngleFactor"),
            RequiredSingle(presentationSource, "depthExtentFraction"),
            RequiredSingle(presentationSource, "fullInVerticalOffsetGameUnits"),
            RequiredSingle(presentationSource, "fullInDistanceGameUnits"),
            RequiredSingle(presentationSource, "fullInYawRadians"),
            RequiredSingle(presentationSource, "fullOutVerticalOffsetGameUnits"),
            RequiredSingle(presentationSource, "fullOutDistanceGameUnits"),
            RequiredSingle(presentationSource, "fullOutYawRadians"),
            RequiredSingle(presentationSource, "startingZoomFraction"));
        ValidateSliderSemantics(RequiredObject(previewControl, "sliderSemanticsEvidence"));
        var formatControlRows = formatControls.EnumerateArray().ToArray();
        var faceControls = nativeControls.Select(native =>
        {
            var index = RequiredInteger(native, "controlIndex");
            var axisSource = formatControlRows.Single(value =>
                RequiredInteger(value, "index") == index);
            var axis = RequiredArray(axisSource, "axis").EnumerateArray()
                .Select(value => value.GetSingle()).ToArray();
            var axisSha256 = RequiredString(axisSource, "axisSha256");
            if (axis.Length != Fo3OpeningFlowNumericContracts.FaceGenSymmetricGeometryFloats ||
                axis.Any(value => !float.IsFinite(value)) ||
                RequiredString(native, "axisSha256") != axisSha256)
                throw new InvalidOperationException("Fallout 3 FaceGen control axis differs.");
            var control = new Fo3AppearanceFaceControl(
                index,
                RequiredString(native, "settingEntity"),
                RequiredString(native, "sourceLabel"),
                axisSha256,
                axis,
                RequiredSingle(previewControl, "minimum"),
                RequiredSingle(previewControl, "maximum"),
                RequiredSingle(previewControl, "step"),
                RequiredSingle(previewControl, "jump"),
                RequiredSingle(previewControl, "morphWeightScale"),
                RequiredSingle(previewControl, "resetValue"),
                RequiredSingle(previewControl, "acceptanceValue"),
                presentation,
                RequiredString(previewControl, "semantics"));
            ValidateFaceControl(control);
            return control;
        }).ToArray();
        var faceControl = faceControls.Single(value => value.ControlIndex == previewIndex);
        if (RequiredString(previewControl, "axisSha256") != faceControl.AxisSha256)
            throw new InvalidOperationException("Fallout 3 FaceGen preview axis differs.");
        var races = RequiredArray(source, "races").EnumerateArray().Select(LoadRace).ToArray();
        if (races.Length == 0 || races.Select(value => value.FormId).Distinct().Count() != races.Length ||
            races.All(value => value.FormId != defaultRaceFormId))
            throw new InvalidOperationException("Fallout 3 playable race inventory is incomplete.");
        var previewSet = LoadPreviewSet(
            RequiredObject(playerFaceGen, "previewHead"),
            playerFormId,
            defaultRaceFormId,
            faceControls,
            races);
        return new Fo3AppearanceContract(
            stage,
            menuEnteredStage,
            acceptedStage,
            RequiredString(source, "command"),
            RequiredString(source, "acceptedStageCommand"),
            playerEditorId,
            defaultRaceFormId,
            ui,
            faceControl,
            faceControls,
            previewSet,
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
            sex.EyeOptions.Single(value => value.FormId == sex.DefaultEyesFormId),
            FaceControls.ToDictionary(
                value => value.SettingEntity,
                value => value.ResetValue,
                StringComparer.Ordinal));
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
            sex.EyeOptions.Single(value => value.FormId == eyesFormId),
            FaceControls.ToDictionary(
                value => value.SettingEntity,
                value => value.ResetValue,
                StringComparer.Ordinal));
    }

    internal Fo3AppearanceSelection ApplyFaceControl(
        Fo3AppearanceSelection selection,
        Fo3AppearanceFaceControl control,
        float value)
    {
        var engineSex = selection.Race.Sex.Single(value => value.Value == selection.Sex).Key;
        var preview = PreviewFor(selection, engineSex);
        if (selection.Race.FormId != preview.RaceFormId ||
            selection.Hair.FormId != preview.HairFormId ||
            selection.Eyes.FormId != preview.EyesFormId ||
            value < control.Minimum || value > control.Maximum ||
            !selection.FaceControlValues.TryGetValue(control.SettingEntity, out var priorValue))
            throw new InvalidOperationException(
                "Fallout 3 live FaceGen preview supports only owned default sex identities.");
        var symmetric = selection.Sex.FaceGen.SymmetricGeometry
            .Zip(
                control.Axis,
                (baseline, axis) => baseline +
                    (value - priorValue) * control.MorphWeightScale * axis)
            .ToArray();
        var values = selection.FaceControlValues.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        values[control.SettingEntity] = value;
        var face = new Fo3FaceGenDefaults(
            HashFloats(symmetric),
            selection.Sex.FaceGen.AsymmetricGeometrySha256,
            selection.Sex.FaceGen.SymmetricTextureSha256,
            symmetric,
            selection.Sex.FaceGen.AsymmetricGeometry,
            selection.Sex.FaceGen.SymmetricTexture);
        return selection with
        {
            Sex = selection.Sex with { FaceGen = face },
            FaceControlValues = values,
        };
    }

    internal OpeningPlayerFaceGenPreview PreviewFor(
        Fo3AppearanceSelection selection,
        string engineSex)
    {
        var matches = PreviewSet.Previews.Where(preview =>
                preview.Sex == engineSex &&
                preview.RaceFormId.Equals(
                    selection.Race.FormId,
                    StringComparison.OrdinalIgnoreCase) &&
                preview.HairFormId.Equals(
                    selection.Hair.FormId,
                    StringComparison.OrdinalIgnoreCase) &&
                preview.EyesFormId.Equals(
                    selection.Eyes.FormId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Fallout 3 owned FaceGen preview cache identity is unavailable: " +
                $"sex={engineSex} race={selection.Race.FormId} " +
                $"hair={selection.Hair.FormId} eyes={selection.Eyes.FormId}.");
        return matches[0];
    }

    private static OpeningPlayerFaceGenPreviewSet LoadPreviewSet(
        JsonElement source,
        string playerFormId,
        string defaultRaceFormId,
        IReadOnlyList<Fo3AppearanceFaceControl> faceControls,
        IReadOnlyList<Fo3AppearanceRace> races)
    {
        var schema = RequiredString(source, "schema");
        var status = RequiredString(source, "status");
        var previewPlayerFormId = RequiredFormId(source, "playerFormId");
        var geometryControlNames = RequiredArray(source, "geometryControlNames")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var geometryControlCount = RequiredInteger(source, "geometryControlCount");
        var runtimeDisposition = RequiredString(source, "runtimeDisposition");
        var selectionScope = RequiredString(source, "selectionScope");
        var unsupportedSelectionScope = RequiredString(
            source,
            "unsupportedSelectionScope");
        var fullBody = source.GetProperty("fullBody").GetBoolean();
        var bodyRoles = RequiredArray(source, "bodyComponentRoles")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var bodySources = RequiredObject(source, "bodyComponentSourcesBySex")
            .EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => (IReadOnlyList<OpeningPlayerBodyComponentSource>)value.Value
                    .EnumerateArray().Select(LoadBodySource).ToArray(),
                StringComparer.Ordinal);
        var previews = RequiredArray(source, "previews").EnumerateArray()
            .Select(value =>
            {
                var outputs = RequiredObject(value, "outputs");
                return new OpeningPlayerFaceGenPreview(
                    schema,
                    status,
                    previewPlayerFormId,
                    RequiredFormId(value, "raceFormId"),
                    RequiredString(value, "sex"),
                    RequiredFormId(value, "hairFormId"),
                    RequiredFormId(value, "eyesFormId"),
                    RequiredArray(value, "headPartFormIds").EnumerateArray()
                        .Select(part => part.GetString()!).ToArray(),
                    geometryControlNames,
                    geometryControlCount,
                    VerifiedPath(outputs, "gltf", "gltfSha256"),
                    RequiredString(outputs, "gltfSha256"),
                    VerifiedPath(outputs, "sidecar", "sidecarSha256"),
                    RequiredString(outputs, "sidecarSha256"),
                    RequiredString(outputs, "bufferSha256"),
                    runtimeDisposition,
                    fullBody,
                    bodyRoles,
                    bodySources);
            }).ToArray();
        var previewSet = new OpeningPlayerFaceGenPreviewSet(
            schema,
            status,
            previewPlayerFormId,
            geometryControlNames,
            geometryControlCount,
            runtimeDisposition,
            fullBody,
            bodyRoles,
            bodySources,
            previews);
        var previewIdentities = previews.Select(preview =>
            (preview.Sex, preview.RaceFormId, preview.HairFormId, preview.EyesFormId)).ToArray();
        if (schema != ExpectedPreviewSchema ||
            status != ExpectedPreviewStatus ||
            runtimeDisposition != ExpectedPreviewRuntimeDisposition ||
            selectionScope != ExpectedPreviewSelectionScope ||
            unsupportedSelectionScope != ExpectedUnsupportedPreviewSelectionScope ||
            !previewPlayerFormId.Equals(playerFormId, StringComparison.OrdinalIgnoreCase) ||
            !races.Any(value => value.FormId == defaultRaceFormId) ||
            geometryControlCount != faceControls.Count ||
            !geometryControlNames.SequenceEqual(
                faceControls.Select(value => value.SettingEntity),
                StringComparer.Ordinal) ||
            !fullBody ||
            !bodyRoles.SequenceEqual(ExpectedBodyRoles, StringComparer.Ordinal) ||
            !bodySources.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedPreviewSexes) ||
            previews.Length != races.Count * ExpectedPreviewSexes.Length ||
            previewIdentities.Distinct().Count() != previewIdentities.Length ||
            previews.Any(preview =>
            {
                var race = races.SingleOrDefault(value => value.FormId == preview.RaceFormId);
                return race is null ||
                !race.Sex.TryGetValue(preview.Sex, out var sex) ||
                preview.HairFormId != sex.DefaultHairFormId ||
                preview.EyesFormId != sex.DefaultEyesFormId ||
                preview.GeometryControlCount != geometryControlCount ||
                !preview.GeometryControlNames.SequenceEqual(
                    geometryControlNames,
                    StringComparer.Ordinal) ||
                !preview.FullBody ||
                preview.BodyComponentRoles is null ||
                !preview.BodyComponentRoles.SequenceEqual(
                    bodyRoles,
                    StringComparer.Ordinal) ||
                !ReferenceEquals(preview.BodyComponentSourcesBySex, bodySources);
            }) ||
            bodySources.Values.Any(rows =>
                !rows.Select(value => value.Role).SequenceEqual(
                    ExpectedBodyRoles,
                    StringComparer.Ordinal) ||
                rows.Any(value =>
                    value.SourceSurfaceCount < 1 ||
                    value.RetainedSurfaceCount < 1 ||
                    value.SourceSurfaceCount != value.RetainedSurfaceCount +
                        value.OmittedDismemberCapSurfaceCount ||
                    value.RetainedSurfaceNames.Count != value.RetainedSurfaceCount)))
            throw new InvalidOperationException(
                "Fallout 3 full-body live FaceGen preview differs.");
        return previewSet;
    }

    private static OpeningPlayerBodyComponentSource LoadBodySource(JsonElement source) => new(
        RequiredString(source, "role"),
        RequiredString(source, "modelLogicalPath"),
        RequiredString(source, "modelSha256"),
        RequiredInteger(source, "sourceSurfaceCount"),
        RequiredInteger(source, "retainedSurfaceCount"),
        RequiredArray(source, "retainedSurfaceNames").EnumerateArray()
            .Select(value => value.GetString()!).ToArray(),
        RequiredInteger(source, "omittedDismemberCapSurfaceCount"),
        RequiredString(source, "diffuseLogicalPath"),
        RequiredString(source, "diffuseSha256"),
        RequiredString(source, "normalLogicalPath"),
        RequiredString(source, "normalSha256"),
        RequiredString(source, "shapeTransformDisposition"));

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
        var symmetric = LoadFloatContract(
            RequiredObject(face, "symmetricGeometry"),
            Fo3OpeningFlowNumericContracts.FaceGenSymmetricGeometryFloats);
        var asymmetric = LoadFloatContract(
            RequiredObject(face, "asymmetricGeometry"),
            Fo3OpeningFlowNumericContracts.FaceGenAsymmetricGeometryFloats);
        var texture = LoadFloatContract(
            RequiredObject(face, "symmetricTexture"),
            Fo3OpeningFlowNumericContracts.FaceGenSymmetricTextureFloats);
        return new Fo3AppearanceSex(
            LoadAsset(RequiredObject(source, "headTexture")),
            hair,
            eyes,
            defaultHair,
            defaultEyes,
            new Fo3FaceGenDefaults(
                symmetric.Sha256,
                asymmetric.Sha256,
                texture.Sha256,
                symmetric.Values,
                asymmetric.Values,
                texture.Values));
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

    private static (string Sha256, IReadOnlyList<float> Values) LoadFloatContract(
        JsonElement source,
        int expectedCount)
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
        return (actualSha256, values);
    }

    private static string HashFloats(IReadOnlyList<float> values)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true))
            foreach (var value in values)
                writer.Write(value);
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static void ValidateFaceControl(Fo3AppearanceFaceControl source)
    {
        if (!float.IsFinite(source.Minimum) || !float.IsFinite(source.Maximum) ||
            !float.IsFinite(source.Step) || !float.IsFinite(source.Jump) ||
            !float.IsFinite(source.MorphWeightScale) ||
            !float.IsFinite(source.ResetValue) ||
            !float.IsFinite(source.AcceptanceValue) || source.Minimum >= source.Maximum ||
            source.Step <= 0.0f || source.Jump <= 0.0f || source.MorphWeightScale <= 0.0f ||
            source.ResetValue < source.Minimum ||
            source.ResetValue > source.Maximum || source.AcceptanceValue < source.Minimum ||
            source.AcceptanceValue > source.Maximum ||
            source.AcceptanceValue == source.ResetValue ||
            source.Semantics !=
                "sibling-gamebryo-racesexmenu-ui-units-with-ctl-egm-weight-scale")
            throw new InvalidOperationException("Fallout 3 FaceGen preview control is invalid.");
    }

    private static void ValidateSliderSemantics(JsonElement source)
    {
        if (RequiredString(source, "classification") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceClassification ||
            RequiredString(source, "engineBuild") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceEngineBuild ||
            RequiredString(source, "sourceExecutableSha256") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Prefix +
                Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Suffix ||
            RequiredSingle(source, "sourceMinimum") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderSourceMinimum ||
            RequiredSingle(source, "sourceMaximum") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderSourceMaximum ||
            RequiredSingle(source, "uiScale") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderUiScale ||
            RequiredSingle(source, "uiMinimum") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderUiMinimum ||
            RequiredSingle(source, "uiMaximum") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderUiMaximum ||
            RequiredSingle(source, "ordinaryIncrement") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderOrdinaryIncrement ||
            RequiredSingle(source, "jump") != Fo3OpeningFlowNumericContracts.FaceGenSliderJump ||
            RequiredSingle(source, "morphWeightScale") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderMorphWeightScale ||
            RequiredString(source, "lowGlobalAddress") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderLowGlobalAddress ||
            RequiredString(source, "highGlobalAddress") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderHighGlobalAddress ||
            RequiredString(source, "incrementTrait") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderIncrementTrait ||
            RequiredSingle(source, "incrementDefaultThreshold") !=
                Fo3OpeningFlowNumericContracts.FaceGenSliderIncrementDefaultThreshold)
            throw new InvalidOperationException(
                "Fallout 3 FaceGen slider semantics evidence differs.");
    }

    private static string VerifiedPath(JsonElement source, string pathName, string hashName)
    {
        var path = System.IO.Path.GetFullPath(RequiredString(source, pathName));
        var expected = RequiredString(source, hashName);
        if (!ValidHex(expected, Fo3OpeningFlowNumericContracts.Sha256HexCharacters) ||
            !File.Exists(path))
            throw new InvalidOperationException("Fallout 3 FaceGen preview artifact is absent.");
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 FaceGen preview artifact hash differs.");
        return path;
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

    private static float RequiredSingle(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || !value.TryGetSingle(out var result) ||
            !float.IsFinite(result))
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
    Fo3Cg00EarlyBirthSequence EarlyBirthSequence,
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
    Fo3Cg01Stage12DadResponse Cg01Stage12DadResponse,
    Fo3Cg01PostStage14Transition Cg01PostStage14Transition,
    Fo3Cg01ToddlerWorldContract Cg01ToddlerWorld,
    string MainMenuMusicPath,
    string IntroVideoPath,
    Color InterfaceColor,
    float MenuBackgroundAlpha,
    OwnedGamebryoDialogueMenu DialogueMenu)
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
            !RequiredBoolean(capabilities, "cg01ToddlerWorldRuntimeReady") ||
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
        var earlyBirthSequence = Fo3Cg00EarlyBirthSequence.Load(
            RequiredObject(selection, "earlyBirthSequence"));
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
        var cg01Stage12DadResponse = Fo3Cg01Stage12DadResponse.Load(
            RequiredObject(
                RequiredObject(cg01Source, "postStage5Transition"),
                "postStage12DadResponse"),
            cg01Stage0Transition,
            cg01Stage12Transition);
        var cg01PostStage14Transition = Fo3Cg01PostStage14Transition.Load(
            RequiredObject(
                RequiredObject(cg01Source, "postStage5Transition"),
                "postStage14Transition"),
            cg01Stage0Transition,
            cg01Stage12DadResponse);
        var cg01ToddlerWorld = Fo3Cg01ToddlerWorldContract.Load(
            RequiredObject(cg01Source, "toddlerWorld"),
            cg01Stage0Transition,
            cg01Stage12Transition,
            RuntimeConfiguration.Load());
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
        var menuBackgroundAlpha = SettingSingle(settings, "fMenuBackgroundOpacity");
        if (menuBackgroundAlpha < 0.0f || menuBackgroundAlpha > 1.0f)
            throw new InvalidOperationException(
                "Fallout 3 menu background opacity is invalid.");
        var dialogueMenu = OwnedGamebryoTileRuntime.ParseDialogueMenu(
            RequiredObject(menu, "dialogueMenuTiles"));
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
            earlyBirthSequence,
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
            cg01Stage12DadResponse,
            cg01PostStage14Transition,
            cg01ToddlerWorld,
            RequiredString(mainMenuMusic, "source"),
            RequiredString(runtimeIntroVideo, "output"),
            interfaceColor,
            menuBackgroundAlpha,
            dialogueMenu);
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

    private static float SettingSingle(IEnumerable<JsonElement> settings, string key)
    {
        var row = settings.Single(value => RequiredString(value, "key") == key);
        var result = float.Parse(
            RequiredString(row, "value"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
        if (!float.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 setting {key} is invalid.");
        return result;
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
