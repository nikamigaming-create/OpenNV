using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;

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
    internal const float AppearancePreviewTexturePixels = 150.0f;
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
    Fo3AppearanceAsset BackgroundTexture);

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
        "owned-default-male-and-female-full-body-live-previews-" +
        "all-native-geometry-controls";
    private const string ExpectedPreviewSchema =
        "opennv-owned-player-facegen-preview-set/v3";
    private const string ExpectedPreviewStatus =
        "compiled-default-male-and-female-full-body-live-previews-with-ctl-egm-targets-" +
        "all-native-geometry-controls-runtime-bound";
    private const string ExpectedPreviewRuntimeDisposition =
        "owned-default-male-and-female-selection-preview-hosts-and-all-native-geometry-" +
        "controls-bound-other-identities-fail-closed-sibling-gamebryo-slider-semantics-" +
        "corroborated";
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
            new Fo3AppearanceNameUi(
                PositiveInteger(nameSource, "panelWidth"),
                PositiveInteger(nameSource, "panelHeight"),
                LoadAsset(RequiredObject(nameSource, "backgroundTexture"))));
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
        var preview = PreviewSet.Previews.Single(value =>
            value.RaceFormId.Equals(selection.Race.FormId, StringComparison.OrdinalIgnoreCase) &&
            value.HairFormId.Equals(selection.Hair.FormId, StringComparison.OrdinalIgnoreCase) &&
            value.EyesFormId.Equals(selection.Eyes.FormId, StringComparison.OrdinalIgnoreCase));
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
        string engineSex) => PreviewSet.Previews.Single(preview =>
            preview.Sex == engineSex &&
            preview.RaceFormId.Equals(selection.Race.FormId, StringComparison.OrdinalIgnoreCase) &&
            preview.HairFormId.Equals(selection.Hair.FormId, StringComparison.OrdinalIgnoreCase) &&
            preview.EyesFormId.Equals(selection.Eyes.FormId, StringComparison.OrdinalIgnoreCase));

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
        var race = races.Single(value => value.FormId == defaultRaceFormId);
        if (schema != ExpectedPreviewSchema ||
            status != ExpectedPreviewStatus ||
            runtimeDisposition != ExpectedPreviewRuntimeDisposition ||
            !previewPlayerFormId.Equals(playerFormId, StringComparison.OrdinalIgnoreCase) ||
            geometryControlCount != faceControls.Count ||
            !geometryControlNames.SequenceEqual(
                faceControls.Select(value => value.SettingEntity),
                StringComparer.Ordinal) ||
            !fullBody ||
            !bodyRoles.SequenceEqual(ExpectedBodyRoles, StringComparer.Ordinal) ||
            !bodySources.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ExpectedPreviewSexes) ||
            previews.Length != ExpectedPreviewSexes.Length ||
            !previews.Select(value => value.Sex).ToHashSet(StringComparer.Ordinal)
                .SetEquals(ExpectedPreviewSexes) ||
            previews.Any(preview =>
                !race.Sex.TryGetValue(preview.Sex, out var sex) ||
                preview.RaceFormId != race.FormId ||
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
                !ReferenceEquals(preview.BodyComponentSourcesBySex, bodySources)) ||
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
    Fo3Cg01ToddlerWorldContract Cg01ToddlerWorld,
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
            cg01ToddlerWorld,
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
    private Fo3Cg00RetailStage10Contract? _retailCg00Stage10Contract;
    private Fo3TtwCg00Stage10PresentationContract? _ttwCg00Stage10PresentationContract;
    private Fo3TtwCg00Stage10SurfaceContract? _ttwCg00Stage10SurfaceContract;
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
    private FaceGenMorphController? _cg01DadFace;
    private FaceGenLipAnimation? _activeCg01DadLip;
    private string? _activeCg01DadInfoFormId;
    private bool _cg01DadLipSampleLogged;
    private int _cg01DadLipCueSamples;
    private readonly List<string> _cg01DadPublishedSpeakerIdleInfoFormIds = [];
    private AudioStreamPlayer? _vaultEffectSound;
    private ColorRect? _vaultStage90Fade;
    private Fo3Stage90ImageSpaceModifier? _activeStage90ImageSpaceModifier;
    private double _stage90ImageSpaceElapsedSeconds;
    private Fo3Vault101BirthSceneCoverage? _vaultBirthCoverage;
    private Fo3Stage100RuntimeContext? _stage100Runtime;
    private double _stage100TimerRemainingSeconds;
    private RuntimeConfiguration _runtimeConfiguration = null!;
    private OpeningManifest? _characterReflectron;
    private OpeningRaceSexRenderedDeviceHost? _reflectron;
    private string? _appearanceProofMode;
    private string? _appearanceProofReportPath;
    private string? _appearanceProofCaptureRoot;
    private bool _characterVideo;
    private Control? _creatorLayer;
    private LineEdit? _activeNameInput;
    private OptionButton? _activeAppearanceCategory;
    private HSlider? _activeFaceControlSlider;
    private Fo3AppearanceSelection? _activeAppearanceSelection;
    private OpeningPlayerFaceGenPreviewHost? _activeFacePreview;
    private bool _introCompleted;
    private Fo3OwnedVideoMode _ownedVideoMode;
    private Fo3Cg01Stage0State? _activeCg01MovieState;
    private Fo3Cg01RuntimeContext? _activeCg01MovieContext;
    private Fo3Cg01ToddlerWorldRuntime? _cg01ToddlerWorld;
    private string? _cg01ProofMode;
    private string? _cg01ProofReportPath;
    private string? _cg01ProofCapturePath;
    private bool _cg01ProofMovieEscapeSkipped;
    private bool _ownedVideoFrameNonblank;
    private bool _ownedVideoEverVisible;
    private bool _ownedVideoCleared;
    private CellReferenceLedger.Geometry? _cg01DadDialogueGeometry;
    private bool _cg01ProofCaptureCompleted;
    private string? _cg01ProofCaptureSha256;
    private string? _cg01ProofCaptureInfoFormId;
    private string? _cg01ProofCaptureSpeakerIdleFormId;
    private int _cg01ProofCaptureWidth;
    private int _cg01ProofCaptureHeight;
    private int _cg01ProofCaptureRgbSpan;

    internal void Configure(
        Fo3OwnedProfile profile,
        string savePath,
        Node3D worldHost,
        RuntimeConfiguration runtimeConfiguration,
        Fo3Vault101BirthPresentationContract? birthPresentation,
        string? appearanceProofMode = null,
        string? appearanceProofReportPath = null,
        string? appearanceProofCaptureRoot = null,
        string? cg01ProofMode = null,
        string? cg01ProofReportPath = null,
        string? cg01ProofCapturePath = null,
        Fo3Cg00RetailStage10Contract? retailCg00Stage10Contract = null,
        Fo3TtwCg00Stage10PresentationContract? ttwCg00Stage10PresentationContract = null,
        Fo3TtwCg00Stage10SurfaceContract? ttwCg00Stage10SurfaceContract = null,
        OpeningManifest? characterReflectron = null,
        bool characterVideo = false)
    {
        _profile = profile;
        _savePath = System.IO.Path.GetFullPath(savePath);
        _worldHost = worldHost;
        _runtimeConfiguration = runtimeConfiguration;
        _birthPresentation = birthPresentation;
        _retailCg00Stage10Contract = retailCg00Stage10Contract;
        _ttwCg00Stage10PresentationContract = ttwCg00Stage10PresentationContract;
        _ttwCg00Stage10SurfaceContract = ttwCg00Stage10SurfaceContract;
        _characterReflectron = characterReflectron;
        if ((_ttwCg00Stage10PresentationContract is null) !=
            (_ttwCg00Stage10SurfaceContract is null))
            throw new InvalidOperationException(
                "Fallout 3 TTW stage-10 presentation and surface contracts must be paired.");
        if (_retailCg00Stage10Contract is not null &&
            _ttwCg00Stage10PresentationContract is not null)
            throw new InvalidOperationException(
                "Fallout 3 stage-10 proof cannot mix standalone and TTW observations.");
        if (_birthPresentation is not null &&
            (!_birthPresentation.EntryReferenceFormId.Equals(
                 _profile.Section4Transition.LocationReferenceFormId,
                 StringComparison.OrdinalIgnoreCase) ||
             !_birthPresentation.CellFormId.Equals(
                 _profile.BirthSlice.CellFormId,
                 StringComparison.OrdinalIgnoreCase) ||
             !_birthPresentation.DadActor.ReferenceFormId.Equals(
                 _profile.Stage100Transition.DisabledDad.FormId,
                 StringComparison.OrdinalIgnoreCase) ||
             _birthPresentation.Cg01DadActors.Count !=
                 _profile.Stage65Appearance.SelectionResults.Count ||
             _birthPresentation.Cg01DadActors.Values.Any(value =>
                 !value.Actor.ReferenceFormId.Equals(
                     _profile.Cg01Stage0Transition.Dad.FormId,
                     StringComparison.OrdinalIgnoreCase) ||
                 !value.Actor.BaseFormId.Equals(
                     _profile.Cg01Stage0Transition.Dad.BaseFormId,
                     StringComparison.OrdinalIgnoreCase) ||
                 !value.Actor.StartMarkerReferenceFormId.Equals(
                     _profile.Cg01Stage0Transition.DadStartMarker.FormId,
                     StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException(
                "Fallout 3 stage-62 package or stage-100 Dad does not join the owned Vault 101 scene.");
        _appearanceProofMode = appearanceProofMode;
        _appearanceProofReportPath = appearanceProofReportPath;
        _appearanceProofCaptureRoot = appearanceProofCaptureRoot;
        _characterVideo = characterVideo;
        _cg01ProofMode = cg01ProofMode;
        _cg01ProofReportPath = cg01ProofReportPath;
        _cg01ProofCapturePath = cg01ProofCapturePath;
        Name = "Fallout3FrontEnd";
        Layer = Fo3OpeningFlowNumericContracts.UiLayer;
    }

    public override void _Ready()
    {
        BuildShell();
        if (_characterVideo)
        {
            RunCharacterGenerationVideo();
            return;
        }
        if (_cg01ProofMode is not null)
        {
            RunCg01Proof();
            return;
        }
        if (_appearanceProofMode is not null)
        {
            if (_appearanceProofMode is "early-apply" or "early-restore" or
                "early-presentation" or "stage10-presentation")
            {
                try
                {
                    RunCg00EarlyProof();
                }
                catch (Exception exception)
                {
                    GD.PushError($"OPENNV_FO3_CG00_EARLY_PRESENTATION_FAIL {exception}");
                    GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
                }
                return;
            }
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
        EnforceOwnedPresentationShell();
        UpdateOwnedVideoSurface();
        UpdateCg00EarlyBirth(delta);
        UpdateCg00EarlyProof();
        UpdateCg01DadLip();
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
            Visible = false,
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
        BeginOwnedVideoSurfaceGate();
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
        StartCg00EarlyBirthSequence();
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
        if (_video is not null)
        {
            if (_video.Visible && !_ownedVideoFrameNonblank)
                throw new InvalidOperationException(
                    "Fallout 3 owned movie exposed an unvalidated frame.");
            _video.Stop();
            _video.Visible = false;
        }
        if (_introLayer is not null)
        {
            _introLayer.Visible = false;
            _introLayer.QueueFree();
            if (_introLayer.Visible || !_introLayer.IsQueuedForDeletion())
                throw new InvalidOperationException(
                    "Fallout 3 owned movie surface was not hidden and queued for release.");
        }
        _video = null;
        _introLayer = null;
        _ownedVideoMode = Fo3OwnedVideoMode.None;
        _ownedVideoCleared = true;
    }

    private void BeginOwnedVideoSurfaceGate()
    {
        if (_video is null || _introLayer is null)
            throw new InvalidOperationException(
                "Fallout 3 owned movie surface is absent before playback.");
        _video.Visible = false;
        _ownedVideoFrameNonblank = false;
        _ownedVideoEverVisible = false;
        _ownedVideoCleared = false;
        EnforceOwnedPresentationShell();
    }

    private void UpdateOwnedVideoSurface()
    {
        if (_video is null)
            return;
        if (_video.Visible && !_ownedVideoFrameNonblank)
            throw new InvalidOperationException(
                "Fallout 3 owned movie surface became visible before frame validation.");
        if (_ownedVideoFrameNonblank)
            return;
        var image = _video.GetVideoTexture()?.GetImage();
        if (image is null || image.IsEmpty())
            return;
        image.Convert(Image.Format.Rgba8);
        var pixels = image.GetData();
        if (pixels.Length < 4)
            return;
        var red = pixels[0];
        var green = pixels[1];
        var blue = pixels[2];
        for (var index = 4; index + 2 < pixels.Length; index += 4)
        {
            if (Math.Abs(pixels[index] - red) <= 4 &&
                Math.Abs(pixels[index + 1] - green) <= 4 &&
                Math.Abs(pixels[index + 2] - blue) <= 4)
                continue;
            _ownedVideoFrameNonblank = true;
            _ownedVideoEverVisible = true;
            _video.Visible = true;
            GD.Print(
                $"OPENNV_FO3_OWNED_VIDEO_NONBLANK_FRAME_READY " +
                $"mode={_ownedVideoMode} width={image.GetWidth()} height={image.GetHeight()}");
            return;
        }
    }

    private void EnforceOwnedPresentationShell()
    {
        var ownedPresentationActive = _cg01ProofMode is not null ||
            _vaultPreviewHost is not null ||
            _ownedVideoMode != Fo3OwnedVideoMode.None;
        if (ownedPresentationActive)
        {
            _background.Visible = false;
            _panel.Visible = _cg00EarlySexMenuActive;
        }
        if (ownedPresentationActive &&
            (_background.Visible || (_panel.Visible && !_cg00EarlySexMenuActive)))
            throw new InvalidOperationException(
                "Fallout 3 owned presentation exposed the menu shell.");
        if (_panel.Visible && _content.GetChildCount() == 0)
            throw new InvalidOperationException(
                "Fallout 3 menu panel became visible with empty content.");
    }

    private void ShowSexSelection()
    {
        ClearContent();
        if (_cg00EarlySequence is null)
            _content.AddChild(Label(
                "FALLOUT 3  •  CG00",
                Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(_profile.SexTitle, Fo3OpeningFlowNumericContracts.BodyFontPixels));
        foreach (var choice in _profile.SexChoices)
        {
            var captured = choice;
            var button = Button(choice.Label);
            button.Pressed += () =>
            {
                if (_cg00EarlySequence is null)
                    ShowNameSelection(captured);
                else
                    SelectCg00EarlySex(captured);
            };
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
        EnsureCreatorVaultBackdrop(sex);
        ClearContent();
        var nameUi = _profile.Appearance.Ui.Name;
        var panel = CreatorSurface(
            (Fo3OpeningFlowNumericContracts.Center -
                nameUi.PanelWidth /
                    (2.0f * Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels)),
            (Fo3OpeningFlowNumericContracts.Center -
                nameUi.PanelHeight /
                    (2.0f * Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels)),
            nameUi.PanelWidth /
                (float)Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels,
            nameUi.PanelHeight /
                (float)Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels,
            nameUi.BackgroundTexture,
            "FO3_TextEditMenu_TEM_MainRect");
        var content = CreatorColumn(
            panel,
            (int)Fo3OpeningFlowNumericContracts.VaultPreviewMarginPixels);
        content.AddChild(Label("ENTER NAME", Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var name = new LineEdit
        {
            PlaceholderText = "Name",
            CustomMinimumSize = new Vector2(
                0.0f,
                Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels),
        };
        name.AddThemeFontSizeOverride("font_size", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        name.TextSubmitted += _ => AcceptName(name);
        content.AddChild(name);
        var accept = Button("ACCEPT");
        accept.Pressed += () => AcceptName(name);
        content.AddChild(accept);
        _activeNameInput = name;
        Callable.From(name.GrabFocus).CallDeferred();
        GD.Print(
            $"OPENNV_FO3_CG00_NAME_READY stage={_profile.NameStage} " +
            $"sourcePanel={nameUi.PanelWidth}x{nameUi.PanelHeight} " +
            $"background={nameUi.BackgroundTexture.SourceSha256}");
    }

    private void EnsureCreatorVaultBackdrop(Fo3SexChoice sex)
    {
        if (_birthPresentation is null || _vaultPreviewHost is not null)
            return;
        var selection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        var stage65 = _profile.Stage65Appearance.Apply(
            sex.EngineSex,
            selection.Race.FormId,
            selection.Sex.FaceGen);
        var futureDad = _birthPresentation.Cg01DadActorFor(
            selection.Race.FormId,
            sex.EngineSex,
            stage65);
        var host = new Node3D { Name = "FO3_VAULT101_CREATOR_BACKDROP" };
        _worldHost.AddChild(host);
        try
        {
            _vaultBirthCoverage = Fo3Vault101BirthScene.Build(
                host,
                _birthPresentation,
                futureDad);
        }
        catch
        {
            host.QueueFree();
            throw;
        }
        _vaultPreviewHost = host;
        _background.Visible = false;
        _panel.Visible = false;
        GD.Print(
            $"OPENNV_FO3_CREATOR_VAULT_BACKDROP_READY cell=" +
            $"{_birthPresentation.CellFormId} sex={sex.EngineSex} " +
            $"doctorVisible={_vaultBirthCoverage.DoctorActor.Placement.Visible} " +
            $"dadVisible={_vaultBirthCoverage.DadActor.Placement.Visible}");
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
        if (_cg00EarlySequence is null)
            ShowAppearanceSelection(playerName, _selectedSex!);
        else
        {
            ClearContent();
            ResumeCg00AfterName(playerName);
        }
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
                stage != _profile.Stage100Transition.Stage &&
                stage != _profile.Cg01Stage0Transition.ResultingStage &&
                stage != _profile.Cg01Stage10Transition.TargetStage &&
                stage != _profile.Cg01Stage12Transition.TargetStage &&
                stage != _profile.Cg01Stage12DadResponse.TargetStage)
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
            selection = LoadSavedFaceControls(faceGen, selection);
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
                {
                    _profile.Section4Transition.ValidateSavedState(savedStage62Package);
                    ShowVault101BirthRoomBeforeStage65(
                        playerName,
                        _selectedSex,
                        selection,
                        persistPackage: false);
                    return;
                }
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
                ShowVault101BirthRoom(playerName, _selectedSex, selection, stage65);
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage65-source-bound-ready");
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
            Fo3Cg01ToddlerWorldState? cg01ToddlerWorld = null;
            Fo3Cg01Stage14State? cg01Stage14 = null;
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
                        cg01ToddlerWorld = _profile.Cg01ToddlerWorld.LoadSavedState(
                            RequiredSaveObject(root, "cg01ToddlerWorld"));
                        if (root.TryGetProperty(
                                "cg01Stage12DadResponse",
                                out var savedCg01Stage14) &&
                            savedCg01Stage14.ValueKind == JsonValueKind.Object)
                        {
                            cg01Stage14 = _profile.Cg01Stage12DadResponse.Apply(cg01Stage12);
                            _profile.Cg01Stage12DadResponse.ValidateSavedState(
                                savedCg01Stage14,
                                cg01Stage14);
                        }
                    }
                    ValidateBirthRuntimeState(
                        RequiredSaveObject(root, "birthRuntime"),
                        cg01Stage14 is not null
                            ? "cg01-stage14-dad-response-applied-package-evaluated"
                        : cg01Stage12 is null
                            ? "cg01-stage10-toddler-world-active"
                            : "cg01-stage12-physical-trigger-applied-post-stage12-blocked");
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
                cg01Stage12,
                cg01ToddlerWorld,
                cg01Stage14);
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
        var ui = _profile.Appearance.Ui;
        var characterReflectron = _characterReflectron ??
            throw new InvalidOperationException(
                "Fallout 3 character creation requires the shared owned Reflectron manifest.");
        _creatorLayer = new Control { Name = "FO3_SHARED_REFLECTRON_HOST" };
        _creatorLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_creatorLayer);
        _panel.Visible = false;
        _background.Visible = false;
        var referenceCanvas = characterReflectron.NewGameFlow.ReferenceCanvasSize;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var deviceScale = MathF.Min(
            viewportSize.X / referenceCanvas.X,
            viewportSize.Y / referenceCanvas.Y);
        var deviceCanvas = new Control
        {
            Name = "FO3_SHARED_REFLECTRON_1600X1200",
            Size = referenceCanvas,
            Scale = Vector2.One * deviceScale,
            Position = (viewportSize - referenceCanvas * deviceScale) * 0.5f,
        };
        _creatorLayer.AddChild(deviceCanvas);
        var renderedDevice = characterReflectron.NewGameFlow.Menus.Values
            .Select(menu => menu.RenderedDevice)
            .SingleOrDefault(device => device is not null)
            ?? throw new InvalidOperationException(
                "The shared owned opening manifest has no Reflectron device.");
        var creatorLighting = new CellContentLoader.LightingContract(
            "fo3-character-reflectron-2.0",
            _birthPresentation!.ProofAmbientColor,
            _birthPresentation.ProofAmbientColor,
            _birthPresentation.ProofBackgroundColor,
            _birthPresentation.ProofFogNearGameUnits,
            _birthPresentation.ProofFogFarGameUnits,
            _birthPresentation.ProofFogPower,
            Vector2.Zero,
            0.0f,
            []);
        _reflectron = new OpeningRaceSexRenderedDeviceHost(
            renderedDevice,
            deviceCanvas,
            referenceCanvas,
            _runtimeConfiguration,
            creatorLighting,
            _birthPresentation.UnitsToMeters);
        var panel = _reflectron.CreateMenuPresentationHost(
            new Rect2(0.0f, 0.0f, 340.0f, 500.0f));
        var content = CreatorColumn(
            panel,
            Fo3OpeningFlowNumericContracts.CreatorPanelMarginPixels);
        content.AddThemeConstantOverride(
            "separation",
            Fo3OpeningFlowNumericContracts.CreatorAppearancePanelSeparationPixels);
        content.AddChild(Label(
            $"{playerName}{System.Environment.NewLine}{sex.Label.ToUpperInvariant()}",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels));

        var scaledListItemHeight = ui.ListItemHeight;
        var categorySelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(0.0f, scaledListItemHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        foreach (var category in new[] { "RACE", "HAIR", "EYES", "FACE" })
            categorySelect.AddItem(category);
        categorySelect.AddThemeFontSizeOverride(
            "font_size",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels);
        content.AddChild(categorySelect);
        _activeAppearanceCategory = categorySelect;
        var selectors = new GridContainer { Columns = 1 };
        selectors.AddThemeConstantOverride(
            "h_separation",
            Fo3OpeningFlowNumericContracts.CreatorPanelSeparationPixels);
        selectors.AddThemeConstantOverride(
            "v_separation",
            Fo3OpeningFlowNumericContracts.CreatorPanelSeparationPixels);
        var raceSelect = new OptionButton();
        var hairSelect = new OptionButton();
        var eyesSelect = new OptionButton();
        AddSelector(selectors, "RACE", raceSelect);
        AddSelector(selectors, "HAIR", hairSelect);
        AddSelector(selectors, "EYES", eyesSelect);
        content.AddChild(selectors);

        var defaultSelection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        FillOptions(raceSelect, _profile.Appearance.Races, defaultSelection.Race.FormId, "RACE");
        var faceFrame = _reflectron.CreateFacePresentationHost();
        var previewSource = _profile.Appearance.PreviewFor(
            defaultSelection,
            sex.EngineSex);
        var control = _profile.Appearance.FaceControl;
        var activeControl = control;
        _activeFacePreview = OpeningPlayerFaceGenPreviewHost.Load(
            previewSource,
            _profile.Appearance.FaceControls.Select(value =>
                new OpeningNativeFaceGenGeometryControl(
                    value.ControlIndex,
                    value.SettingEntity,
                    value.SourceLabel,
                    value.AxisSha256)).ToArray(),
            new OpeningFaceGenPreviewControl(
                control.ControlIndex,
                control.SettingEntity,
                control.SourceLabel,
                control.AxisSha256,
                control.Minimum,
                control.Maximum,
                control.Step,
                control.Jump,
                control.MorphWeightScale,
                control.ResetValue,
                control.AcceptanceValue,
                new OpeningFaceGenSliderSemanticsEvidence(
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceClassification,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceEngineBuild,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Prefix +
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Suffix,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderSourceMinimum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderSourceMaximum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderUiScale,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderUiMinimum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderUiMaximum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderOrdinaryIncrement,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderJump,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderMorphWeightScale,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderLowGlobalAddress,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderHighGlobalAddress,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderIncrementTrait,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderIncrementDefaultThreshold),
                new OpeningFaceGenPreviewPresentation(
                    control.Presentation.ViewportWidthFraction,
                    control.Presentation.ViewportHeightFraction,
                    control.Presentation.VerticalFovHalfAngleFactor,
                    control.Presentation.DepthExtentFraction,
                    control.Presentation.FullInVerticalOffsetGameUnits,
                    control.Presentation.FullInDistanceGameUnits,
                    control.Presentation.FullInYawRadians,
                    control.Presentation.FullOutVerticalOffsetGameUnits,
                    control.Presentation.FullOutDistanceGameUnits,
                    control.Presentation.FullOutYawRadians,
                    control.Presentation.StartingZoomFraction),
                control.Semantics),
            faceFrame,
            _runtimeConfiguration,
            creatorLighting,
            _birthPresentation.UnitsToMeters,
            faceFrame.Size,
            renderedDevice);
        var previewProportions =
            CharacterBodyProportions.Neutral("fo3-custom-live-v1");
        var faceFraming = true;
        var greenProjection = false;
        void RefreshProjection()
        {
            _activeFacePreview.SetPreviewState(
                previewProportions,
                faceFraming,
                greenProjection);
            _reflectron.SetCreatorModeState(
                faceFraming ? "FACE" : "BODY",
                bodyEnabled: !faceFraming,
                projectionEnabled: greenProjection,
                faceEnabled: faceFraming);
        }
        RefreshProjection();
        var liveStatus = Label(
            "SCULPT FACE",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels);
        content.AddChild(liveStatus);
        var faceControlSelect = new OptionButton();
        foreach (var faceControl in _profile.Appearance.FaceControls)
            faceControlSelect.AddItem(faceControl.SourceLabel);
        faceControlSelect.Select(Array.IndexOf(
            _profile.Appearance.FaceControls.ToArray(),
            control));
        content.AddChild(faceControlSelect);
        var slider = new HSlider
        {
            Name = "FO3_RaceSexMenu_RSM_slider_option",
            MinValue = control.Minimum,
            MaxValue = control.Maximum,
            Step = control.Step,
            Value = control.ResetValue,
            CustomMinimumSize = new Vector2(
                0.0f,
                ui.SliderHeight * GetViewport().GetVisibleRect().Size.Y /
                    Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(slider);
        _activeFaceControlSlider = slider;

        void SelectRaceDefaults(Fo3AppearanceRace race)
        {
            var raceSex = race.Sex[sex.EngineSex];
            FillOptions(hairSelect, raceSex.HairOptions, raceSex.DefaultHairFormId, "HAIR");
            FillOptions(eyesSelect, raceSex.EyeOptions, raceSex.DefaultEyesFormId, "EYES");
            SelectCurrent();
        }

        void SelectCurrent()
        {
            var race = _profile.Appearance.Races[raceSelect.Selected];
            var raceSex = race.Sex[sex.EngineSex];
            var selection = new Fo3AppearanceSelection(
                race,
                raceSex,
                raceSex.HairOptions[hairSelect.Selected],
                raceSex.EyeOptions[eyesSelect.Selected],
                _profile.Appearance.FaceControls.ToDictionary(
                    value => value.SettingEntity,
                    value => value.ResetValue,
                    StringComparer.Ordinal));
            var previewSupported = sex.EngineSex == previewSource.Sex &&
                selection.Race.FormId == previewSource.RaceFormId &&
                selection.Hair.FormId == previewSource.HairFormId &&
                selection.Eyes.FormId == previewSource.EyesFormId;
            slider.Editable = previewSupported;
            foreach (var faceControl in _profile.Appearance.FaceControls)
                _activeFacePreview.Apply(faceControl.SettingEntity, faceControl.ResetValue);
            activeControl = control;
            faceControlSelect.Select(Array.IndexOf(
                _profile.Appearance.FaceControls.ToArray(),
                control));
            slider.Value = activeControl.ResetValue;
            _activeFacePreview.Control.Visible = previewSupported;
            liveStatus.Text = previewSupported
                ? "SCULPT FACE"
                : "3D PREVIEW NOT AVAILABLE FOR THIS SELECTION";
            _activeAppearanceSelection = selection;
        }

        raceSelect.ItemSelected += index => SelectRaceDefaults(_profile.Appearance.Races[(int)index]);
        hairSelect.ItemSelected += _ => SelectCurrent();
        eyesSelect.ItemSelected += _ => SelectCurrent();
        slider.ValueChanged += value =>
        {
            if (!slider.Editable || _activeAppearanceSelection is null)
                return;
            _activeFacePreview.Apply(
                activeControl.SettingEntity,
                (float)value * activeControl.MorphWeightScale);
            _activeAppearanceSelection = _profile.Appearance.ApplyFaceControl(
                _activeAppearanceSelection,
                activeControl,
                (float)value);
            liveStatus.Text =
                $"{activeControl.SourceLabel}{System.Environment.NewLine}" +
                $"{(float)value:+0.00;-0.00;0.00}";
        };
        faceControlSelect.ItemSelected += index =>
        {
            activeControl = _profile.Appearance.FaceControls[(int)index];
            slider.MinValue = activeControl.Minimum;
            slider.MaxValue = activeControl.Maximum;
            slider.Step = activeControl.Step;
            slider.Value = _activeAppearanceSelection?.FaceControlValue(
                activeControl.SettingEntity) ?? activeControl.ResetValue;
            liveStatus.Text = activeControl.SourceLabel;
        };
        SelectRaceDefaults(defaultSelection.Race);
        void ShowCategory(long index)
        {
            raceSelect.Visible = index == 0;
            hairSelect.Visible = index == 1;
            eyesSelect.Visible = index == 2;
            slider.Visible = index == 3;
            faceControlSelect.Visible = index == 3;
            liveStatus.Visible = index == 3;
            _reflectron.SetActiveList(index switch
            {
                0 => "race",
                1 => "hair",
                2 => "eyes",
                _ => "face",
            });
        }
        categorySelect.ItemSelected += ShowCategory;
        ShowCategory(0);
        void SelectCategory(int index)
        {
            categorySelect.Select(index);
            ShowCategory(index);
        }
        _reflectron.ConfigureCharacterControls(
            characterReflectron.Font,
            () => { },
            () => SelectCategory(0),
            () => SelectCategory(3),
            () => SelectCategory(1),
            () =>
            {
                faceFraming = true;
                SelectCategory(3);
                RefreshProjection();
            },
            () =>
            {
                faceFraming = false;
                RefreshProjection();
            },
            () =>
            {
                greenProjection = !greenProjection;
                RefreshProjection();
            });
        RefreshProjection();

        var accept = Button("ACCEPT APPEARANCE");
        accept.CustomMinimumSize = new Vector2(0.0f, scaledListItemHeight);
        accept.Pressed += () => AcceptAppearance(playerName, sex);
        content.AddChild(accept);
        Callable.From(raceSelect.GrabFocus).CallDeferred();
        GD.Print(
            $"OPENNV_FO3_CG00_APPEARANCE_READY profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.Stage} entered={_profile.Appearance.MenuEnteredStage} " +
            $"races={_profile.Appearance.Races.Count} sex={sex.EngineSex} " +
            $"preview=owned-live-default-full-body controls={_profile.Appearance.FaceControls.Count} " +
            $"boundSurfaces={_activeFacePreview.BoundSurfaceCount} " +
            $"bodySurfaces={_activeFacePreview.BodySurfaceCount}");
    }

    private void AcceptAppearance(string playerName, Fo3SexChoice sex)
    {
        var selection = _activeAppearanceSelection ?? throw new InvalidOperationException(
            "Fallout 3 appearance selection is absent.");
        PersistAppearance(playerName, sex, selection);
        if (_cg00EarlySequence is not null)
        {
            _cg00EarlySequence = null;
            _cg00EarlyStage = _profile.Appearance.AcceptedStage;
            _cg00EarlyTimerTargetStage = null;
            ClearCg00ImageSpace();
            _cg00EarlySubtitle?.QueueFree();
            _cg00EarlySubtitle = null;
        }
        if (_birthPresentation is null)
            ShowAppearanceAccepted(playerName, sex, selection);
        else if (_profile.Appearance.FaceControls.Any(control =>
                     selection.FaceControlValue(control.SettingEntity) != control.ResetValue))
            ShowVault101BirthRoomBeforeStage65(
                playerName,
                sex,
                selection,
                persistPackage: true);
        else
            ShowVault101BirthRoom(playerName, sex, selection);
    }

    private void ShowVault101BirthRoomBeforeStage65(
        string playerName,
        Fo3SexChoice sex,
        Fo3AppearanceSelection selection,
        bool persistPackage)
    {
        var contract = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 pre-stage-65 Vault room has no owned presentation contract.");
        if (_vaultPreviewHost is not null)
        {
            if (_vaultBirthCoverage is null)
                throw new InvalidOperationException(
                    "Fallout 3 creator Vault backdrop coverage is absent.");
            ClearContent();
            _background.Visible = false;
            _panel.Visible = false;
            var resumedPackage = _profile.Section4Transition.Activate();
            if (persistPackage)
                PersistSection4Package(playerName, sex, selection, resumedPackage);
            GD.Print(
                $"OPENNV_FO3_CG00_CREATOR_CONFIRMED_VAULT_READY profile={_profile.ProfileId} " +
                $"stage={_profile.Appearance.AcceptedStage} package={resumedPackage.FormId} " +
                $"location={resumedPackage.LocationReferenceFormId} playerGeometry=" +
                $"{selection.Sex.FaceGen.SymmetricGeometrySha256} " +
                $"doctorVisible={_vaultBirthCoverage.DoctorActor.Placement.Visible} " +
                $"dadVisible={_vaultBirthCoverage.DadActor.Placement.Visible} " +
                "stage65Triggered=0 sourceMarkerPending=1");
            return;
        }
        ClearContent();
        var baselineSelection = _profile.Appearance.ResolveSelection(
            sex.EngineSex,
            selection.Race.FormId,
            selection.Race.ChildRaceFormId,
            selection.Hair.FormId,
            selection.Eyes.FormId);
        var baselineStage65 = _profile.Stage65Appearance.Apply(
            sex.EngineSex,
            baselineSelection.Race.FormId,
            baselineSelection.Sex.FaceGen);
        var previewHost = new Node3D { Name = "FO3_VAULT101_BIRTH_ROOM_PRE_STAGE65" };
        _worldHost.AddChild(previewHost);
        Fo3Vault101BirthSceneCoverage coverage;
        try
        {
            var hiddenFutureDad = contract.Cg01DadActorFor(
                baselineSelection.Race.FormId,
                sex.EngineSex,
                baselineStage65);
            coverage = Fo3Vault101BirthScene.Build(previewHost, contract, hiddenFutureDad);
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
        var package = _profile.Section4Transition.Activate();
        if (persistPackage)
            PersistSection4Package(playerName, sex, selection, package);
        GD.Print(
            $"OPENNV_FO3_CG00_CREATOR_CONFIRMED_VAULT_READY profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.AcceptedStage} package={package.FormId} " +
            $"location={package.LocationReferenceFormId} playerGeometry=" +
            $"{selection.Sex.FaceGen.SymmetricGeometrySha256} " +
            $"doctorVisible={coverage.DoctorActor.Placement.Visible} " +
            $"dadVisible={coverage.DadActor.Placement.Visible} " +
            "stage65Triggered=0 sourceMarkerPending=1");
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
        Fo3Cg01Stage12State? resumedCg01Stage12 = null,
        Fo3Cg01ToddlerWorldState? resumedCg01ToddlerWorld = null,
        Fo3Cg01Stage14State? resumedCg01Stage14 = null)
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
        {
            _vaultPreviewHost.QueueFree();
            _vaultPreviewHost = null;
            _vaultBirthCoverage = null;
        }
        ClearContent();

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
            (resumedCg01Stage12 is not null && resumedCg01Stage10 is null) ||
            (resumedCg01ToddlerWorld is not null && resumedCg01Stage12 is null) ||
            (resumedCg01Stage14 is not null && resumedCg01ToddlerWorld is null))
            throw new InvalidOperationException(
                "Fallout 3 resumed birth-room stage chain is incomplete.");

        var previewHost = new Node3D { Name = "FO3_VAULT101_BIRTH_ROOM" };
        _worldHost.AddChild(previewHost);
        Fo3Vault101BirthSceneCoverage coverage;
        try
        {
            var cg01DadAppearance = contract.Cg01DadActorFor(
                selection.Race.FormId,
                sex.EngineSex,
                stage65 ?? throw new InvalidOperationException(
                    "Fallout 3 stage-65 appearance state is absent."));
            coverage = Fo3Vault101BirthScene.Build(
                previewHost,
                contract,
                cg01DadAppearance);
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
            if (_appearanceProofMode is not "early-apply" and not "early-restore")
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
                ApplyCg01Stage5Presentation(resumedCg01, stage65);
                if (resumedCg01Stage12 is not null)
                    BeginCg01ToddlerWorld(
                        resumedCg01,
                        cg01Context,
                        resumedCg01Stage10!,
                        resumedCg01ToddlerWorld,
                        acceptanceProof: false,
                        restoredStage14: resumedCg01Stage14);
                else if (resumedCg01Stage10 is not null)
                    BeginCg01ToddlerWorld(
                        resumedCg01,
                        cg01Context,
                        resumedCg01Stage10,
                        restored: null,
                        acceptanceProof: false);
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
            $"references={coverage.PlacedReferences} actors=3 " +
            $"doctor={coverage.DoctorActor.ReferenceFormId} " +
            $"dad={coverage.DadActor.ReferenceFormId} " +
            $"cg01Dad={coverage.Cg01DadActor.ReferenceFormId} " +
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
        EnsureCg01VaultScene(context);
        ApplyCg01Stage5Presentation(state, context.Stage65);
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

    private void ApplyCg01Stage5Presentation(
        Fo3Cg01Stage0State stage5,
        Fo3Stage65AppearanceState stage65)
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-5 presentation has no owned Vault 101 scene.");
        var appearance = coverage.Cg01DadAppearance;
        var actorContract = appearance.Actor;
        var rawMarker = actorContract.StartMarkerPositionGodotGameUnits;
        var groundedMarker = coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits;
        if (!coverage.Cg01DadActor.ReferenceFormId.Equals(
                stage5.Dad.Reference.FormId,
                StringComparison.OrdinalIgnoreCase) ||
            !coverage.Cg01DadActor.BaseFormId.Equals(
                stage5.Dad.Reference.BaseFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !coverage.Cg01DadGrounding.AuthoredPlacementGodotGameUnits.IsEqualApprox(
                rawMarker) ||
            !coverage.Cg01DadActor.Placement.Position.IsEqualApprox(groundedMarker) ||
            !Mathf.IsEqualApprox(groundedMarker.X, rawMarker.X) ||
            !Mathf.IsEqualApprox(groundedMarker.Z, rawMarker.Z) ||
            !Mathf.IsEqualApprox(
                groundedMarker.Y,
                rawMarker.Y +
                    coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits) ||
            !coverage.Cg01DadActor.Placement.Quaternion.IsEqualApprox(
                actorContract.StartMarkerRotationGodotQuaternion))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-5 Dad actor or MoveTo marker differs.");
        var matchedAppearance = stage65.Parents.Single(value =>
            value.ReferenceFormId.Equals(
                stage5.Dad.Reference.FormId,
                StringComparison.OrdinalIgnoreCase));
        if (actorContract.RaceFormId != matchedAppearance.RaceFormId ||
            appearance.SymmetricGeometrySha256 !=
                matchedAppearance.SymmetricGeometrySha256 ||
            appearance.AsymmetricGeometrySha256 !=
                matchedAppearance.AsymmetricGeometrySha256 ||
            appearance.SymmetricTextureSha256 !=
                matchedAppearance.SymmetricTextureSha256)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad stage-65 geometry was not applied before visibility.");
        coverage.DoctorActor.Placement.Visible = false;
        coverage.DoctorActor.Placement.ProcessMode = ProcessModeEnum.Disabled;
        coverage.DadActor.Placement.Visible = false;
        coverage.DadActor.Placement.ProcessMode = ProcessModeEnum.Disabled;
        coverage.Cg01DadActor.Placement.Visible = true;
        coverage.Cg01DadActor.Placement.ProcessMode = ProcessModeEnum.Inherit;
        ActivateCg01DadDialogueCamera(stage5, coverage);
        _cg01DadFace ??= new FaceGenMorphController(
            coverage.Cg01DadActor.Actor,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_PRESENTED reference={stage5.Dad.Reference.FormId} " +
            $"base={stage5.Dad.Reference.BaseFormId} marker={stage5.Dad.MoveTargetFormId} " +
            $"rawMarker={rawMarker} groundedMarker={groundedMarker} " +
            $"groundingDelta={coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits:F6} " +
            $"enabled={(stage5.Dad.Enabled ? 1 : 0)} previousDoctorVisible=0 " +
            $"previousCg00DadVisible=0 " +
            $"appearance=source-stage65-match-race-50-percent-facegen-applied " +
            $"matchedRace={matchedAppearance.RaceFormId} " +
            $"matchedFace={matchedAppearance.SymmetricGeometrySha256}");
    }

    private void ActivateCg01DadDialogueCamera(
        Fo3Cg01Stage0State stage5,
        Fo3Vault101BirthSceneCoverage coverage)
    {
        var playerMarker = stage5.Player.Transform.PositionGameUnits;
        var playerMarkerLocal = GamebryoCoordinate.ConvertVector(
            new Vector3(
                (float)playerMarker.X,
                (float)playerMarker.Y,
                (float)playerMarker.Z) - coverage.Contract.EntryPositionGameUnits);
        var camera = coverage.Camera;
        camera.GlobalPosition = coverage.CellRoot.ToGlobal(playerMarkerLocal) +
            _profile.Cg01ToddlerWorld.DesktopCameraOffsetMeters;
        camera.Fov = _profile.Cg01ToddlerWorld.VerticalFovDegrees;
        camera.Near = _profile.Cg01ToddlerWorld.NearGameUnits *
            coverage.Contract.UnitsToMeters;
        camera.LookAt(coverage.Cg01DadGrounding.GroundedBounds.GetCenter(), Vector3.Up);
        camera.Current = true;
        _cg01DadDialogueGeometry = CellReferenceLedger.MeasureGeometry(
            coverage.Cg01DadActor.Actor.Root,
            camera,
            coverage.Cg01DadGrounding.GroundedBounds.GetCenter());
        if (!camera.IsCurrent() ||
            !coverage.Cg01DadActor.Placement.Visible ||
            coverage.DoctorActor.Placement.Visible ||
            coverage.DadActor.Placement.Visible ||
            !_cg01DadDialogueGeometry.RenderLayerVisible ||
            !_cg01DadDialogueGeometry.AabbValid ||
            !_cg01DadDialogueGeometry.FrustumIntersection ||
            _cg01DadDialogueGeometry.Surfaces !=
                coverage.Cg01DadAppearance.Actor.Surfaces ||
            _cg01DadDialogueGeometry.Vertices <= 0 ||
            _cg01DadDialogueGeometry.Triangles <= 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad is not the active-camera dialogue subject.");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_CAMERA_READY camera={camera.Name} " +
            $"position={camera.GlobalPosition} target=" +
            $"{coverage.Cg01DadGrounding.GroundedBounds.GetCenter()} " +
            $"frustum=1 surfaces={_cg01DadDialogueGeometry.Surfaces}");
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
            Visible = false,
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
        BeginOwnedVideoSurfaceGate();
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
        if (!_ownedVideoCleared || _video is not null || _introLayer is not null)
            throw new InvalidOperationException(
                "Fallout 3 CG01 movie surface survived transition completion.");
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
        subtitle.SetMeta("opennv_speaker_reference_form_id", stage5.Dad.Reference.FormId);
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
        var speaker = subtitle.GetMeta("opennv_speaker_reference_form_id").AsString();
        if (!speaker.Equals(stage5.Dad.Reference.FormId, StringComparison.OrdinalIgnoreCase) ||
            _cg01DadDialogueGeometry is null ||
            !_cg01DadDialogueGeometry.FrustumIntersection)
            throw new InvalidOperationException(
                "Fallout 3 CG01 subtitle or camera subject differs from Dad.");
        var publishedSpeakerIdle = PublishCg01DadSpeakerIdle(cue);
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        ClearCg01DadLip();
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad voice could not be decoded: " +
                cue.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException("Fallout 3 CG01 Dad voice has no duration.");
        _activeCg01DadLip = FaceGenLipAnimation.Load(
            cue.Response.Lip.SourcePath,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        _activeCg01DadInfoFormId = cue.InfoFormId;
        _cg01DadLipSampleLogged = false;
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = $"Fallout3Cg01DadVoice{cue.Sequence}",
            Stream = stream,
        };
        _vaultDialogueVoice.SetMeta("opennv_info_form_id", cue.InfoFormId);
        _vaultDialogueVoice.SetMeta("opennv_speaker_reference_form_id", speaker);
        _vaultDialogueVoice.SetMeta(
            "opennv_speaker_idle_form_id",
            cue.SpeakerIdle.FormId);
        _vaultDialogueVoice.Finished += () =>
        {
            ClearCg01DadLip();
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
        if (_vaultDialogueVoice.GetMeta("opennv_info_form_id").AsString() !=
                _activeCg01DadInfoFormId ||
            _vaultDialogueVoice.GetMeta("opennv_speaker_reference_form_id").AsString() !=
                speaker ||
            _vaultDialogueVoice.GetMeta("opennv_speaker_idle_form_id").AsString() !=
                cue.SpeakerIdle.FormId ||
            publishedSpeakerIdle.Player.CurrentAnimation.ToString() !=
                publishedSpeakerIdle.RuntimeName)
            throw new InvalidOperationException(
                "Fallout 3 CG01 audio, LIP, and speaker idle do not own the same INFO.");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_CUE_STARTED sequence={cue.Sequence} " +
            $"info={cue.InfoFormId} duration={durationSeconds:F3} " +
            $"voice={cue.Response.Voice.LogicalPath} lip={cue.Response.Lip.LogicalPath}");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_LIP_LOADED info={cue.InfoFormId} " +
            $"frames={_activeCg01DadLip.FrameCount} " +
            $"startFrame={_activeCg01DadLip.StartFrame} " +
            $"metadata=0x{_activeCg01DadLip.MetadataWord:x8} " +
            $"actor={_vaultBirthCoverage?.Cg01DadActor.ReferenceFormId}");
        if (_cg01ProofCapturePath is not null && cue.Sequence == 1)
            CaptureCg01DadCue(cue, publishedSpeakerIdle, subtitle);
    }

    private async void CaptureCg01DadCue(
        Fo3Cg01DadSpeechCue cue,
        ActorModelSlice.LoadedAnimation publishedSpeakerIdle,
        Label subtitle)
    {
        try
        {
            for (var frame = 0;
                 frame < Fo3OpeningFlowNumericContracts.Cg01CaptureWarmupFrames;
                 frame++)
                await ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);
            var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                "Fallout 3 CG01 capture has no owned world.");
            if (_cg01ProofCaptureCompleted ||
                _background.Visible ||
                _panel.Visible ||
                _introLayer is not null ||
                _video is not null ||
                !coverage.Cg01DadActor.Placement.Visible ||
                coverage.DoctorActor.Placement.Visible ||
                coverage.DadActor.Placement.Visible ||
                _cg01DadDialogueGeometry is null ||
                !_cg01DadDialogueGeometry.FrustumIntersection ||
                _vaultDialogueVoice is null ||
                !_vaultDialogueVoice.Playing ||
                _activeCg01DadLip is null ||
                _activeCg01DadInfoFormId != cue.InfoFormId ||
                !subtitle.Visible ||
                publishedSpeakerIdle.Player.CurrentAnimation.ToString() !=
                    publishedSpeakerIdle.RuntimeName)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 capture presentation is blank, stale, or unsynchronized.");
            var path = _cg01ProofCapturePath ?? throw new InvalidOperationException(
                "Fallout 3 CG01 capture path is absent.");
            var image = GetViewport().GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            var data = image.GetData();
            var pixels = image.GetWidth() * image.GetHeight();
            if (pixels <= 0 ||
                data.Length != pixels * Fo3OpeningFlowNumericContracts.CaptureBytesPerPixel)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 capture viewport is empty.");
            var minimum = byte.MaxValue;
            var maximum = byte.MinValue;
            for (var offset = 0;
                 offset < data.Length;
                 offset += Fo3OpeningFlowNumericContracts.CaptureBytesPerPixel)
            {
                for (var channel = 0;
                     channel < Fo3OpeningFlowNumericContracts.CaptureRgbChannels;
                     channel++)
                {
                    minimum = Math.Min(minimum, data[offset + channel]);
                    maximum = Math.Max(maximum, data[offset + channel]);
                }
            }
            var rgbSpan = maximum - minimum;
            if (rgbSpan <= 0)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 capture contains one blank color.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var saveError = image.SavePng(path);
            if (saveError != Error.Ok)
                throw new InvalidOperationException(
                    $"Fallout 3 CG01 capture could not be saved: {saveError}.");
            using var stream = File.OpenRead(path);
            _cg01ProofCaptureSha256 = Convert.ToHexString(
                SHA256.HashData(stream)).ToLowerInvariant();
            _cg01ProofCaptureInfoFormId = cue.InfoFormId;
            _cg01ProofCaptureSpeakerIdleFormId = cue.SpeakerIdle.FormId;
            _cg01ProofCaptureWidth = image.GetWidth();
            _cg01ProofCaptureHeight = image.GetHeight();
            _cg01ProofCaptureRgbSpan = rgbSpan;
            _cg01ProofCaptureCompleted = true;
            GD.Print(
                $"OPENNV_FO3_CG01_COHERENT_CAPTURE_READY path={path} " +
                $"sha256={_cg01ProofCaptureSha256} info={cue.InfoFormId} " +
                $"idle={cue.SpeakerIdle.FormId} size={image.GetWidth()}x{image.GetHeight()} " +
                $"rgbSpan={rgbSpan} shellVisible=0 movieVisible=0 frustum=1 " +
                "audioLipIdleSynchronized=1");
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_CG01_COHERENT_CAPTURE_FAIL {exception.Message}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        }
    }

    private ActorModelSlice.LoadedAnimation PublishCg01DadSpeakerIdle(
        Fo3Cg01DadSpeechCue cue) =>
        PublishCg01DadSpeakerIdle(
            cue.Sequence,
            cue.InfoFormId,
            cue.SpeakerIdle,
            stage12Response: false);

    private ActorModelSlice.LoadedAnimation PublishCg01DadSpeakerIdle(
        Fo3Cg01Stage12DadResponseCue cue) =>
        PublishCg01DadSpeakerIdle(
            cue.Sequence,
            cue.InfoFormId,
            cue.SpeakerIdle,
            stage12Response: true);

    private ActorModelSlice.LoadedAnimation PublishCg01DadSpeakerIdle(
        int sequence,
        string infoFormId,
        Fo3Cg01DadSpeakerIdle speakerIdle,
        bool stage12Response)
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad speaker idle has no owned actor scene.");
        var expectedAnimations = stage12Response
            ? coverage.Cg01DadAppearance.Stage12DialogueAnimations
            : coverage.Cg01DadAppearance.DialogueAnimations;
        var expected = expectedAnimations.Single(value =>
            value.Sequence == sequence &&
            value.InfoFormId.Equals(infoFormId, StringComparison.OrdinalIgnoreCase));
        if (!Fo3Cg01Stage10Transition.SpeakerIdleEquals(
                expected.SpeakerIdle,
                speakerIdle))
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad INFO speaker-idle source differs from the actor derivative.");
        var loaded = coverage.Cg01DadActor.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(speakerIdle.ModelPath),
                StringComparison.OrdinalIgnoreCase) &&
            value.SourceSha256.Equals(
                speakerIdle.SourceSha256,
                StringComparison.OrdinalIgnoreCase));
        foreach (var player in coverage.Cg01DadActor.Actor.LoadedAnimations
                     .Select(value => value.Player).Distinct())
            player.Stop();
        loaded.Player.Play(loaded.RuntimeName);
        loaded.Player.Advance(0.0);
        if (loaded.Player.CurrentAnimation.ToString() != loaded.RuntimeName ||
            _cg01DadPublishedSpeakerIdleInfoFormIds.Contains(
                infoFormId,
                StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad speaker idle was not published exactly once.");
        _cg01DadPublishedSpeakerIdleInfoFormIds.Add(infoFormId);
        _cg01DadDialogueGeometry = CellReferenceLedger.MeasureGeometry(
            coverage.Cg01DadActor.Actor.Root,
            coverage.Camera,
            coverage.Cg01DadGrounding.GroundedBounds.GetCenter());
        if (!_cg01DadDialogueGeometry.RenderLayerVisible ||
            !_cg01DadDialogueGeometry.AabbValid ||
            !_cg01DadDialogueGeometry.FrustumIntersection ||
            _cg01DadDialogueGeometry.Surfaces != coverage.Cg01DadAppearance.Actor.Surfaces)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad speaker-idle pose is outside the active camera.");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_SPEAKER_IDLE_PUBLISHED sequence={sequence} " +
            $"info={infoFormId} idle={speakerIdle.FormId} " +
            $"path={speakerIdle.ModelPath} sha256={speakerIdle.SourceSha256} " +
            $"stage12Response={(stage12Response ? 1 : 0)} " +
            $"runtime={loaded.RuntimeName} channels={loaded.Channels} " +
            $"frustum=1 surfaces={_cg01DadDialogueGeometry.Surfaces}");
        return loaded;
    }

    private void UpdateCg01DadLip()
    {
        if (_vaultDialogueVoice is null ||
            !_vaultDialogueVoice.Playing ||
            _activeCg01DadLip is null ||
            _cg01DadFace is null)
            return;
        var seconds = _vaultDialogueVoice.GetPlaybackPosition();
        if (_vaultDialogueVoice.GetMeta("opennv_info_form_id").AsString() !=
                _activeCg01DadInfoFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 audio and LIP clock INFO identities diverged.");
        var dominant = _cg01DadFace.Apply(_activeCg01DadLip, seconds);
        if (_cg01DadLipSampleLogged || dominant.Value == 0.0f)
            return;
        _cg01DadLipSampleLogged = true;
        _cg01DadLipCueSamples++;
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_LIP_SAMPLE info={_activeCg01DadInfoFormId} " +
            $"seconds={seconds:F3} target={dominant.Target} value={dominant.Value:F6}");
    }

    private void ClearCg01DadLip()
    {
        _cg01DadFace?.Clear();
        _activeCg01DadLip = null;
        _activeCg01DadInfoFormId = null;
        _cg01DadLipSampleLogged = false;
    }

    private void CompleteCg01DadDialogue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        IReadOnlyList<Fo3Cg01DadSpeechCue> cues,
        Label subtitle)
    {
        RestoreCg01DadPrimaryIdle();
        subtitle.Visible = false;
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        var state = _profile.Cg01Stage10Transition.Apply(stage5, context.Sex.EngineSex);
        if (!state.AppliedInfoFormIds.SequenceEqual(cues.Select(value => value.InfoFormId)))
            throw new InvalidOperationException("Fallout 3 CG01 applied INFO sequence differs.");
        PersistCg01Stage10Transition(context, stage5, state);
        BeginCg01ToddlerWorld(
            stage5,
            context,
            state,
            restored: null,
            acceptanceProof: _cg01ProofMode == "apply");
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE10_APPLIED quest={state.ActiveQuestFormId} " +
            $"stage={state.ActiveStage} infos={string.Join(',', state.AppliedInfoFormIds)} " +
            $"commands={state.AppliedCommandCount} dadTimer={state.DadTimerSeconds:F1} " +
            $"objective={state.DisplayedObjectiveIndex} tutorial={state.TutorialQuestStage} " +
            $"autosave={state.AutosaveRequestCount} toddlerWorld=1 " +
            $"blocker={state.NextBoundary.Blocker}");
    }

    private void RestoreCg01DadPrimaryIdle()
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad primary idle has no owned actor scene.");
        var primary = coverage.Cg01DadActor.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(
                    coverage.Cg01DadAppearance.Actor.IdleAnimationPath),
                StringComparison.OrdinalIgnoreCase));
        foreach (var player in coverage.Cg01DadActor.Actor.LoadedAnimations
                     .Select(value => value.Player).Distinct())
            player.Stop();
        primary.Player.Play(primary.RuntimeName);
        primary.Player.Advance(0.0);
        if (primary.Player.CurrentAnimation.ToString() != primary.RuntimeName)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad primary idle was not restored after dialogue.");
    }

    private void BeginCg01ToddlerWorld(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01ToddlerWorldState? restored,
        bool acceptanceProof,
        Fo3Cg01Stage14State? restoredStage14 = null)
    {
        if (_cg01ToddlerWorld is not null)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler world is already active.");
        EnsureCg01VaultScene(context);

        if (restored is null)
            ShowCg01PostStage10Boundary(stage10, resumed: false);
        var scene = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 toddler world scene is absent after preparation.");
        _cg01ToddlerWorld = Fo3Cg01ToddlerWorldRuntime.Build(
            _vaultPreviewHost ?? throw new InvalidOperationException(
                "Fallout 3 CG01 toddler world host is absent."),
            scene,
            _profile.Cg01ToddlerWorld,
            stage5,
            stage10,
            _profile.Cg01Stage12Transition,
            restored,
            player => CompleteCg01ToddlerTrigger(
                stage5,
                context,
                stage10,
                player,
                acceptanceProof));

        if (restored is not null)
        {
            var restoredRuntime = _cg01ToddlerWorld.State(triggerEntered: true);
            if (!restoredRuntime.PlayerPositionMeters.IsEqualApprox(
                    restored.PlayerPositionMeters) ||
                !restoredRuntime.PlayerRotation.IsEqualApprox(restored.PlayerRotation) ||
                restoredRuntime.AuthoredCollisionBodies != restored.AuthoredCollisionBodies)
                throw new InvalidOperationException(
                    "Restored Fallout 3 CG01 toddler body differs.");
            var restoredStage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
                stage10,
                _profile.Cg01Stage12Transition.Trigger.ReferenceFormId,
                actionReferenceWasPlayer: true);
            if (restoredStage14 is not null)
            {
                var expectedStage14 = _profile.Cg01Stage12DadResponse.Apply(restoredStage12);
                if (restoredStage14.SourceStage != expectedStage14.SourceStage ||
                    restoredStage14.ActiveQuestFormId != expectedStage14.ActiveQuestFormId ||
                    restoredStage14.ActiveQuestEditorId != expectedStage14.ActiveQuestEditorId ||
                    restoredStage14.ActiveStage != expectedStage14.ActiveStage ||
                    !restoredStage14.AppliedInfoFormIds.SequenceEqual(
                        expectedStage14.AppliedInfoFormIds) ||
                    restoredStage14.DadTalking != expectedStage14.DadTalking ||
                    restoredStage14.DadLooksAtPlayer != expectedStage14.DadLooksAtPlayer ||
                    restoredStage14.DadPackageEvaluated !=
                        expectedStage14.DadPackageEvaluated ||
                    restoredStage14.AccountedCommandCount !=
                        expectedStage14.AccountedCommandCount ||
                    restoredStage14.AppliedCommandCount !=
                        expectedStage14.AppliedCommandCount ||
                    restoredStage14.NextBoundary != expectedStage14.NextBoundary)
                    throw new InvalidOperationException(
                        "Restored Fallout 3 CG01 stage-14 Dad response differs.");
                var dad = scene.Cg01DadActor.Placement;
                dad.SetMeta("opennv_talking", restoredStage14.DadTalking);
                dad.SetMeta(
                    "opennv_look_target",
                    restoredStage14.DadLooksAtPlayer ? "player" : "");
                dad.SetMeta(
                    "opennv_package_evaluated",
                    restoredStage14.DadPackageEvaluated);
                if (dad.GetMeta("opennv_talking").AsInt32() !=
                        restoredStage14.DadTalking ||
                    dad.GetMeta("opennv_look_target").AsString() != "player" ||
                    !dad.GetMeta("opennv_package_evaluated").AsBool())
                    throw new InvalidOperationException(
                        "Restored Fallout 3 CG01 Dad runtime state differs.");
                if (_cg01DadPublishedSpeakerIdleInfoFormIds.Count != 0 ||
                    _cg01DadLipCueSamples != 0 ||
                    _vaultDialogueVoice is not null ||
                    _activeCg01DadLip is not null)
                    throw new InvalidOperationException(
                        "Restored Fallout 3 CG01 Dad response replayed presentation effects.");
            }
            if (acceptanceProof)
            {
                if (restoredStage14 is null)
                    throw new InvalidOperationException(
                        "Fallout 3 CG01 restore proof has no saved stage-14 Dad response.");
                WriteCg01ProofReport(
                    stage5,
                    stage10,
                    restoredStage12,
                    restoredStage14,
                    restoredRuntime,
                    context.Sex.EngineSex,
                    "restore",
                    movieSurfaceRequested: false,
                    escapeSkipped: false,
                    movieReplayed: false,
                    dialoguePlayed: false);
                GD.Print(
                    $"OPENNV_FO3_CG01_TODDLER_WORLD_PROOF_RESTORE " +
                    $"stage={restoredStage14.ActiveStage} physicalEntry=1 " +
                    $"collisionBodies={restoredRuntime.AuthoredCollisionBodies} " +
                    "movieReplayed=0 dialogueReplayed=0 stage12ResponseReplayed=0 " +
                    "transitionEffectsReplayed=0 packageEffectsReplayed=0");
                GetTree().Quit(0);
                return;
            }
            if (restoredStage14 is not null)
                ShowCg01PostStage14Boundary(restoredStage14, resumed: true);
            else
                ShowCg01PostStage12Boundary(restoredStage12, resumed: true);
            return;
        }

        GD.Print(
            $"OPENNV_FO3_CG01_TODDLER_WORLD_READY cell={_profile.Cg01ToddlerWorld.CellFormId} " +
            $"marker={_profile.Cg01ToddlerWorld.PlayerStartMarkerFormId} " +
            $"scale={_profile.Cg01ToddlerWorld.PlayerScale:F1} " +
            $"collisionBodies={_cg01ToddlerWorld.AuthoredCollisionBodies} " +
            $"trigger={_profile.Cg01ToddlerWorld.TriggerReferenceFormId} visualBody=0");
        if (!acceptanceProof)
            return;
        _cg01ToddlerWorld.Player.SetAcceptanceTarget(
            _cg01ToddlerWorld.DadTrigger.GlobalPosition);
        var start = _profile.Cg01ToddlerWorld.PlayerStartTransform.PositionGameUnits;
        var trigger = _profile.Cg01Stage12Transition.Trigger.SourceTransform.PositionGameUnits;
        var distanceGameUnits = new Vector3(
            (float)(trigger.X - start.X),
            (float)(trigger.Y - start.Y),
            (float)(trigger.Z - start.Z)).Length();
        var timeoutSeconds =
            distanceGameUnits * _birthPresentation!.UnitsToMeters /
            _profile.Cg01ToddlerWorld.MoveSpeedMetersPerSecond *
            Fo3OpeningFlowNumericContracts.Cg01ProofTimeoutMultiplier;
        GetTree().CreateTimer(timeoutSeconds).Timeout += () =>
        {
            if (_cg01ToddlerWorld?.Player.MovementEnabled != true)
                return;
            var player = _cg01ToddlerWorld.Player;
            GD.PushError(
                "OPENNV_FO3_CG01_TODDLER_WORLD_PROOF_FAIL physical trigger was not entered " +
                $"frames={player.AcceptancePhysicsFrames} " +
                $"travel={player.AcceptanceHorizontalTravelMeters:F3} " +
                $"distance={player.AcceptanceTargetDistanceMeters:F3} " +
                $"wallContacts={player.AcceptanceWallContacts} " +
                $"position={player.GlobalPosition}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        };
    }

    private void EnsureCg01VaultScene(Fo3Cg01RuntimeContext context)
    {
        if (_vaultBirthCoverage is not null)
            return;
        var presentation = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 CG01 world has no owned Vault 101 presentation.");
        var previewHost = new Node3D { Name = "FO3_VAULT101_CG01_WORLD" };
        _worldHost.AddChild(previewHost);
        try
        {
            var cg01DadAppearance = presentation.Cg01DadActorFor(
                context.Selection.Race.FormId,
                context.Sex.EngineSex,
                context.Stage65);
            _vaultBirthCoverage = Fo3Vault101BirthScene.Build(
                previewHost,
                presentation,
                cg01DadAppearance);
        }
        catch
        {
            previewHost.QueueFree();
            throw;
        }
        _vaultPreviewHost = previewHost;
        _background.Visible = false;
        _panel.Visible = false;
        ApplyStage100Presentation(context.Stage100);
        GD.Print(
            $"OPENNV_FO3_CG01_WORLD_PRESENTATION_READY cell={presentation.CellFormId} " +
            $"references={_vaultBirthCoverage.PlacedReferences} " +
            $"models={_vaultBirthCoverage.LoadedAssets} " +
            $"collisionBodies={_vaultBirthCoverage.AuthoredCollisionBodies} " +
            $"cg01DadPrepared=1 cg01Dad={_vaultBirthCoverage.Cg01DadActor.ReferenceFormId} " +
            "cg01DadVisible=0 stage5PresentationApplied=0 " +
            "appearance=source-stage65-match-race-50-percent-facegen-applied");
    }

    private void CompleteCg01ToddlerTrigger(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01ToddlerPlayer player,
        bool acceptanceProof)
    {
        var runtime = _cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 toddler trigger has no active world.");
        if (player != runtime.Player)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler trigger actor differs.");
        var stage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
            stage10,
            runtime.Contract.TriggerReferenceFormId,
            actionReferenceWasPlayer: true);
        var toddlerState = runtime.State(triggerEntered: true);
        PersistCg01Stage12Transition(context, stage5, stage10, stage12, toddlerState);
        BeginCg01Stage12DadResponse(
            stage5,
            context,
            stage10,
            stage12,
            toddlerState,
            acceptanceProof);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE12_APPLIED_PHYSICAL_TRIGGER stage={stage12.ActiveStage} " +
            $"trigger={stage12.TriggerReferenceFormId} physicalEntry=1 movementEnabled=0");
    }

    private void BeginCg01Stage12DadResponse(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        bool acceptanceProof)
    {
        _vaultPreviewOverlay?.QueueFree();
        var subtitle = AddVaultDialogueOverlay("FO3_CG01_STAGE12_DAD_RESPONSE");
        subtitle.SetMeta("opennv_speaker_reference_form_id", stage5.Dad.Reference.FormId);
        PlayCg01Stage12DadResponseCue(
            stage5,
            context,
            stage10,
            stage12,
            toddlerState,
            _profile.Cg01Stage12DadResponse.Cues,
            0,
            subtitle,
            acceptanceProof);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE12_DAD_RESPONSE_STARTED stage={stage12.ActiveStage} " +
            $"cues={_profile.Cg01Stage12DadResponse.Cues.Count} physicalEntry=1");
    }

    private void PlayCg01Stage12DadResponseCue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        IReadOnlyList<Fo3Cg01Stage12DadResponseCue> cues,
        int index,
        Label subtitle,
        bool acceptanceProof)
    {
        if (index < 0 || index >= cues.Count)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response cursor differs.");
        var cue = cues[index];
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-12 Dad response has no owned actor scene.");
        var speaker = subtitle.GetMeta("opennv_speaker_reference_form_id").AsString();
        if (!speaker.Equals(stage5.Dad.Reference.FormId, StringComparison.OrdinalIgnoreCase) ||
            !coverage.Cg01DadActor.Placement.Visible)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 response speaker differs from visible Dad.");
        var publishedSpeakerIdle = PublishCg01DadSpeakerIdle(cue);
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        ClearCg01DadLip();
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 Dad voice could not be decoded: " +
                cue.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad voice has no duration.");
        _activeCg01DadLip = FaceGenLipAnimation.Load(
            cue.Response.Lip.SourcePath,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        _activeCg01DadInfoFormId = cue.InfoFormId;
        _cg01DadLipSampleLogged = false;
        coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 1);
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = $"Fallout3Cg01Stage12DadVoice{cue.Sequence}",
            Stream = stream,
        };
        _vaultDialogueVoice.SetMeta("opennv_info_form_id", cue.InfoFormId);
        _vaultDialogueVoice.SetMeta("opennv_speaker_reference_form_id", speaker);
        _vaultDialogueVoice.SetMeta(
            "opennv_speaker_idle_form_id",
            cue.SpeakerIdle.FormId);
        _vaultDialogueVoice.Finished += () =>
        {
            ClearCg01DadLip();
            _vaultDialogueVoice?.QueueFree();
            _vaultDialogueVoice = null;
            coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 0);
            coverage.Cg01DadActor.Placement.SetMeta("opennv_look_target", "player");
            if (index + 1 < cues.Count)
            {
                Callable.From(() => PlayCg01Stage12DadResponseCue(
                    stage5,
                    context,
                    stage10,
                    stage12,
                    toddlerState,
                    cues,
                    index + 1,
                    subtitle,
                    acceptanceProof)).CallDeferred();
                return;
            }
            CompleteCg01Stage12DadResponse(
                stage5,
                context,
                stage10,
                stage12,
                toddlerState,
                cues,
                subtitle,
                acceptanceProof);
        };
        AddChild(_vaultDialogueVoice);
        subtitle.Text = $"DAD: {cue.Response.Text}";
        subtitle.Visible = true;
        _vaultDialogueVoice.Play();
        if (_vaultDialogueVoice.GetMeta("opennv_info_form_id").AsString() !=
                _activeCg01DadInfoFormId ||
            _vaultDialogueVoice.GetMeta("opennv_speaker_reference_form_id").AsString() !=
                speaker ||
            _vaultDialogueVoice.GetMeta("opennv_speaker_idle_form_id").AsString() !=
                cue.SpeakerIdle.FormId ||
            publishedSpeakerIdle.Player.CurrentAnimation.ToString() !=
                publishedSpeakerIdle.RuntimeName)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 audio, LIP, and idle INFO identities diverged.");
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE12_DAD_CUE_STARTED sequence={cue.Sequence} " +
            $"info={cue.InfoFormId} duration={durationSeconds:F3} " +
            $"voice={cue.Response.Voice.LogicalPath} lip={cue.Response.Lip.LogicalPath} " +
            $"targetStage={(cue.TargetStage?.ToString() ?? "none")}");
    }

    private void CompleteCg01Stage12DadResponse(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        IReadOnlyList<Fo3Cg01Stage12DadResponseCue> cues,
        Label subtitle,
        bool acceptanceProof)
    {
        RestoreCg01DadPrimaryIdle();
        subtitle.Visible = false;
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        var stage14 = _profile.Cg01Stage12DadResponse.Apply(stage12);
        if (!stage14.AppliedInfoFormIds.SequenceEqual(cues.Select(value => value.InfoFormId)) ||
            cues[^1].TargetStage != stage14.ActiveStage)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response application differs.");
        var dad = (_vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-14 package evaluation has no owned actor scene."))
            .Cg01DadActor.Placement;
        dad.SetMeta("opennv_talking", stage14.DadTalking);
        dad.SetMeta("opennv_look_target", stage14.DadLooksAtPlayer ? "player" : "");
        dad.SetMeta("opennv_package_evaluated", stage14.DadPackageEvaluated);
        PersistCg01Stage14Response(
            context,
            stage5,
            stage10,
            stage12,
            toddlerState,
            stage14);
        if (acceptanceProof)
        {
            WriteCg01ProofReport(
                stage5,
                stage10,
                stage12,
                stage14,
                toddlerState,
                context.Sex.EngineSex,
                "apply",
                movieSurfaceRequested: true,
                escapeSkipped: _cg01ProofMovieEscapeSkipped,
                movieReplayed: false,
                dialoguePlayed: true);
            GD.Print(
                $"OPENNV_FO3_CG01_STAGE14_PROOF_APPLY stage={stage14.ActiveStage} " +
                $"infos={string.Join(',', stage14.AppliedInfoFormIds)} physicalEntry=1 " +
                $"packageEvaluated={(stage14.DadPackageEvaluated ? 1 : 0)} " +
                "movementEnabled=0");
            GetTree().Quit(0);
            return;
        }
        ShowCg01PostStage14Boundary(stage14, resumed: false);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE14_APPLIED quest={stage14.ActiveQuestFormId} " +
            $"stage={stage14.ActiveStage} infos={string.Join(',', stage14.AppliedInfoFormIds)} " +
            $"packageEvaluated={(stage14.DadPackageEvaluated ? 1 : 0)} " +
            $"blocker={stage14.NextBoundary.Blocker}");
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
            "Dad's two source-authored cues completed. Move with W/A/S/D to enter the exact " +
            "owned walk trigger. The physical body has no prepared toddler visual yet.",
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
            "The physical toddler body entered the owned Dad trigger and the exact stage-12 " +
            "commands are saved. Dad's response and the wider Vault route remain stopped.",
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

    private void ShowCg01PostStage14Boundary(Fo3Cg01Stage14State state, bool resumed)
    {
        _vaultPreviewOverlay?.QueueFree();
        var overlay = new PanelContainer
        {
            Name = "FO3_CG01_POST_STAGE14_BOUNDARY",
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
            "Dad's two source-ordered say-once responses completed. His SayToDone state and " +
            "the exact stage-14 package reevaluation command are saved.",
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
                $"OPENNV_FO3_CG01_STAGE14_COLD_RESTORE quest={state.ActiveQuestFormId} " +
                $"stage={state.ActiveStage} infos={string.Join(',', state.AppliedInfoFormIds)} " +
                "dialogueReplayed=0 packageEffectsReplayed=0 nextApplied=0 " +
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

    private static float RequiredSaveSingle(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) ||
            !value.TryGetSingle(out var result) ||
            !float.IsFinite(result))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return result;
    }

    private static Vector3 RequiredSaveVector3(JsonElement parent, string name)
    {
        var values = RequiredSaveArray(parent, name).EnumerateArray()
            .Select(value => value.GetSingle())
            .ToArray();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException($"Fallout 3 save field {name} is invalid.");
        return new Vector3(values[0], values[1], values[2]);
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
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Saved Fallout 3 birth runtime has no constructed presentation.");
        var cg01DadAppearance = coverage.Cg01DadAppearance;
        var cg01DadActor = cg01DadAppearance.Actor;
        var transition = _profile.Section4Transition;
        if (RequiredSaveString(source, "schema") != "opennv-fo3-cg00-birth-runtime/v2" ||
            RequiredSaveString(source, "cellFormId") != contract.CellFormId ||
            RequiredSaveString(source, "entryReferenceFormId") != contract.EntryReferenceFormId ||
            RequiredSaveString(source, "doctorLiReferenceFormId") !=
                contract.DoctorActor.ReferenceFormId ||
            RequiredSaveString(source, "dadReferenceFormId") !=
                contract.DadActor.ReferenceFormId ||
            RequiredSaveString(source, "cg01DadReferenceFormId") !=
                cg01DadActor.ReferenceFormId ||
            !RequiredSaveVector3(source, "cg01DadRawMarkerPositionGodotGameUnits")
                .IsEqualApprox(cg01DadActor.StartMarkerPositionGodotGameUnits) ||
            !RequiredSaveVector3(source, "cg01DadPresentationPositionGodotGameUnits")
                .IsEqualApprox(
                    coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits) ||
            !Mathf.IsEqualApprox(
                RequiredSaveSingle(source, "cg01DadGroundingCorrectionGodotGameUnits"),
                coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits) ||
            RequiredSaveString(source, "cg01DadAppearance") !=
                "source-stage65-match-race-50-percent-facegen-applied" ||
            RequiredSaveString(source, "cg01DadPlayerRaceFormId") !=
                cg01DadAppearance.PlayerRaceFormId ||
            RequiredSaveString(source, "cg01DadPlayerSex") != cg01DadAppearance.PlayerSex ||
            RequiredSaveString(source, "cg01DadSceneSha256") !=
                cg01DadActor.SceneSha256 ||
            RequiredSaveString(source, "cg01DadSymmetricGeometrySha256") !=
                cg01DadAppearance.SymmetricGeometrySha256 ||
            RequiredSaveString(source, "cg01DadAsymmetricGeometrySha256") !=
                cg01DadAppearance.AsymmetricGeometrySha256 ||
            RequiredSaveString(source, "cg01DadSymmetricTextureSha256") !=
                cg01DadAppearance.SymmetricTextureSha256 ||
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
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 birth runtime has no constructed presentation.");
        var cg01DadAppearance = coverage.Cg01DadAppearance;
        var cg01DadActor = cg01DadAppearance.Actor;
        var transition = _profile.Section4Transition;
        return new Dictionary<string, object?>
        {
            ["schema"] = "opennv-fo3-cg00-birth-runtime/v2",
            ["cellFormId"] = contract.CellFormId,
            ["entryReferenceFormId"] = contract.EntryReferenceFormId,
            ["doctorLiReferenceFormId"] = contract.DoctorActor.ReferenceFormId,
            ["dadReferenceFormId"] = contract.DadActor.ReferenceFormId,
            ["cg01DadReferenceFormId"] = cg01DadActor.ReferenceFormId,
            ["cg01DadRawMarkerPositionGodotGameUnits"] = new[]
            {
                cg01DadActor.StartMarkerPositionGodotGameUnits.X,
                cg01DadActor.StartMarkerPositionGodotGameUnits.Y,
                cg01DadActor.StartMarkerPositionGodotGameUnits.Z,
            },
            ["cg01DadPresentationPositionGodotGameUnits"] = new[]
            {
                coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits.X,
                coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits.Y,
                coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits.Z,
            },
            ["cg01DadGroundingCorrectionGodotGameUnits"] =
                coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits,
            ["cg01DadAppearance"] =
                "source-stage65-match-race-50-percent-facegen-applied",
            ["cg01DadPlayerRaceFormId"] = cg01DadAppearance.PlayerRaceFormId,
            ["cg01DadPlayerSex"] = cg01DadAppearance.PlayerSex,
            ["cg01DadSceneSha256"] = cg01DadActor.SceneSha256,
            ["cg01DadSymmetricGeometrySha256"] =
                cg01DadAppearance.SymmetricGeometrySha256,
            ["cg01DadAsymmetricGeometrySha256"] =
                cg01DadAppearance.AsymmetricGeometrySha256,
            ["cg01DadSymmetricTextureSha256"] =
                cg01DadAppearance.SymmetricTextureSha256,
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

    private object SavedFaceControls(Fo3AppearanceSelection selection) =>
        _profile.Appearance.FaceControls.Select(control => new
        {
            settingEntity = control.SettingEntity,
            axisSha256 = control.AxisSha256,
            value = selection.FaceControlValue(control.SettingEntity),
        }).ToArray();

    private Fo3AppearanceSelection LoadSavedFaceControls(
        JsonElement faceGen,
        Fo3AppearanceSelection selection)
    {
        var saved = RequiredSaveArray(faceGen, "geometryControls").EnumerateArray().ToArray();
        if (saved.Length != _profile.Appearance.FaceControls.Count)
            throw new InvalidOperationException(
                "Saved Fallout 3 FaceGen control count differs from the profile.");
        foreach (var control in _profile.Appearance.FaceControls)
        {
            var row = saved.Single(value =>
                RequiredSaveString(value, "settingEntity") == control.SettingEntity);
            if (RequiredSaveString(row, "axisSha256") != control.AxisSha256)
                throw new InvalidOperationException(
                    "Saved Fallout 3 FaceGen control identity differs from the profile.");
            selection = _profile.Appearance.ApplyFaceControl(
                selection,
                control,
                RequiredSaveSingle(row, "value"));
        }
        return selection;
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
                    geometryControls = SavedFaceControls(selection),
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
                    geometryControls = SavedFaceControls(selection),
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
                    geometryControls = SavedFaceControls(selection),
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
        Fo3Cg01Stage12State? cg01Stage12 = null,
        Fo3Cg01ToddlerWorldState? cg01ToddlerWorld = null,
        Fo3Cg01Stage14State? cg01Stage14 = null)
    {
        var state = new
        {
            schema = "opennv-fo3-opening-character/v2",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            questEditorId = _profile.QuestEditorId,
            questFormId = _profile.QuestFormId,
            stage = cg01Stage14?.ActiveStage ?? cg01Stage12?.ActiveStage ??
                cg01Stage10?.ActiveStage ?? cg01?.ActiveStage ?? stage100?.Stage ??
                stage90?.Stage ?? stage85?.Stage ?? stage80.Stage,
            activeQuest = cg01 is null
                ? null
                : new
                {
                    formId = cg01.ActiveQuestFormId,
                    editorId = cg01.ActiveQuestEditorId,
                    stage = cg01Stage14?.ActiveStage ?? cg01Stage12?.ActiveStage ??
                        cg01Stage10?.ActiveStage ?? cg01.ActiveStage,
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
                    geometryControls = SavedFaceControls(selection),
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
            cg01ToddlerWorld = cg01ToddlerWorld is null
                ? null
                : _profile.Cg01ToddlerWorld.SavedState(cg01ToddlerWorld),
            cg01Stage12DadResponse = cg01Stage14 is null
                ? null
                : _profile.Cg01Stage12DadResponse.SavedState(cg01Stage14),
            birthRuntime = BirthRuntimeState(cg01Stage14 is not null
                ? "cg01-stage14-dad-response-applied-package-evaluated"
                : cg01Stage12 is not null
                ? "cg01-stage12-physical-trigger-applied-post-stage12-blocked"
                : cg01Stage10 is not null
                ? "cg01-stage10-toddler-world-active"
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
        Fo3Cg01Stage12State cg01Stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld) =>
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
            cg01Stage12,
            toddlerWorld);

    private void PersistCg01Stage14Response(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State cg01,
        Fo3Cg01Stage10State cg01Stage10,
        Fo3Cg01Stage12State cg01Stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld,
        Fo3Cg01Stage14State cg01Stage14) =>
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
            cg01Stage12,
            toddlerWorld,
            cg01Stage14);

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

    private async void RunAppearanceProof()
    {
        try
        {
            if (_appearanceProofMode is not "apply" and not "restore" ||
                string.IsNullOrWhiteSpace(_appearanceProofReportPath) ||
                string.IsNullOrWhiteSpace(_appearanceProofCaptureRoot) ||
                _birthPresentation is null ||
                DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 3 creator proof requires apply|restore, owned Vault presentation, " +
                    "report/capture paths, and a rendering display driver.");
            if (File.Exists(_appearanceProofReportPath))
                throw new InvalidOperationException(
                    "Fallout 3 creator proof requires a fresh report path.");
            Directory.CreateDirectory(_appearanceProofCaptureRoot);
            if (_appearanceProofMode == "apply")
            {
                if (File.Exists(_savePath))
                    throw new InvalidOperationException(
                        "Fallout 3 creator apply proof requires a fresh save path.");
                var sex = _profile.SexChoices.Single(value => value.EngineSex == "male");
                ShowNameSelection(sex);
                _activeNameInput!.Text = "Lone Wanderer";
                var nameCapture = await CaptureAppearanceFrame("fo3-name-entry.png");
                AcceptName(_activeNameInput);
                _activeAppearanceCategory!.Select(3);
                _activeAppearanceCategory.EmitSignal(
                    OptionButton.SignalName.ItemSelected,
                    3);
                var defaultCapture = await CaptureAppearanceFrame(
                    "fo3-creator-default.png");
                _activeFaceControlSlider!.Value =
                    _profile.Appearance.FaceControl.AcceptanceValue;
                var editedSelection = _activeAppearanceSelection ??
                    throw new InvalidOperationException(
                        "Fallout 3 creator proof did not apply the visible face edit.");
                if (editedSelection.FaceControlValue(
                        _profile.Appearance.FaceControl.SettingEntity) !=
                            _profile.Appearance.FaceControl.AcceptanceValue ||
                    editedSelection.Sex.FaceGen.SymmetricGeometrySha256 ==
                        _profile.Appearance.DefaultSelection("male").Sex.FaceGen
                            .SymmetricGeometrySha256)
                    throw new InvalidOperationException(
                        "Fallout 3 creator proof face edit did not change geometry.");
                var creatorCapture = await CaptureAppearanceFrame("fo3-creator-edited.png");
                var morphDifference = MeasureAppearanceDifference(
                    defaultCapture,
                    creatorCapture);
                AcceptAppearance("Lone Wanderer", sex);
                if (_creatorLayer is not null || _vaultBirthCoverage is null)
                    throw new InvalidOperationException(
                        "Fallout 3 creator acceptance did not reveal the owned Vault room.");
                var persistedSelection = LoadSavedAppearanceSelection();
                if (!_profile.Appearance.FaceControls.All(control =>
                        persistedSelection.FaceControlValue(control.SettingEntity) ==
                            editedSelection.FaceControlValue(control.SettingEntity)) ||
                    persistedSelection.Sex.FaceGen.SymmetricGeometrySha256 !=
                        editedSelection.Sex.FaceGen.SymmetricGeometrySha256)
                    throw new InvalidOperationException(
                        "Fallout 3 creator acceptance did not persist the edited identity.");
                var birthCapture = await CaptureAppearanceFrame("fo3-birth-next-beat.png");
                WriteAppearanceProofReport(
                    "apply",
                    editedSelection,
                    [nameCapture, defaultCapture, creatorCapture, birthCapture],
                    morphDifference,
                    creatorActionsReplayed: false);
                GD.Print(
                    $"OPENNV_FO3_CREATOR_PROOF_APPLY_PASS profile={_profile.ProfileId} " +
                    $"control={_profile.Appearance.FaceControl.SettingEntity} " +
                    $"value={editedSelection.FaceControlValue(
                        _profile.Appearance.FaceControl.SettingEntity):F2} " +
                    $"geometry={editedSelection.Sex.FaceGen.SymmetricGeometrySha256} " +
                    $"save={_savePath}");
                GetTree().Quit(0);
                return;
            }

            if (!File.Exists(_savePath))
                throw new InvalidOperationException(
                    "Fallout 3 creator restore proof save is absent.");
            var restored = LoadSavedAppearanceSelection();
            ContinueCharacter();
            if (_creatorLayer is not null || _vaultBirthCoverage is null)
                throw new InvalidOperationException(
                    "Fallout 3 creator restore did not resume the owned Vault room.");
            var restoreCapture = await CaptureAppearanceFrame("fo3-birth-restored.png");
            WriteAppearanceProofReport(
                "restore",
                restored,
                [restoreCapture],
                morphDifference: null,
                creatorActionsReplayed: false);
            GD.Print(
                $"OPENNV_FO3_CREATOR_PROOF_RESTORE_PASS profile={_profile.ProfileId} " +
                $"control={_profile.Appearance.FaceControl.SettingEntity} " +
                $"value={restored.FaceControlValue(
                    _profile.Appearance.FaceControl.SettingEntity):F2} " +
                $"geometry={restored.Sex.FaceGen.SymmetricGeometrySha256} " +
                $"save={_savePath}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_CREATOR_PROOF_FAIL {exception}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        }
    }

    private async void RunCharacterGenerationVideo()
    {
        try
        {
            if (_birthPresentation is null || DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 3 character video requires the owned Vault presentation and a rendering display driver.");
            if (File.Exists(_savePath))
                throw new InvalidOperationException(
                    "Fallout 3 character video requires a fresh save path.");
            StartMenuMusic();
            ShowMainMenu();
            await WaitForCharacterVideoDraws(55);
            ShowSexSelection();
            await WaitForCharacterVideoDraws(55);
            var sex = _profile.SexChoices.Single(value => value.EngineSex == "male");
            ShowNameSelection(sex);
            await WaitForCharacterVideoDraws(40);
            _activeNameInput!.Text = "LONE WANDERER";
            await WaitForCharacterVideoDraws(55);
            AcceptName(_activeNameInput);
            var appearanceCategory = _activeAppearanceCategory ??
                throw new InvalidOperationException(
                    "Fallout 3 generated character did not open the appearance categories.");
            var faceControlSlider = _activeFaceControlSlider ??
                throw new InvalidOperationException(
                    "Fallout 3 generated character did not open the live face controls.");
            appearanceCategory.Select(3);
            appearanceCategory.EmitSignal(
                OptionButton.SignalName.ItemSelected,
                3);
            faceControlSlider.Value =
                _profile.Appearance.FaceControl.AcceptanceValue;
            await WaitForCharacterVideoDraws(55);
            _reflectron!.ActivateCreatorModeControl("BODY");
            await WaitForCharacterVideoDraws(55);
            _reflectron.ActivateCreatorModeControl("PROJECTION");
            await WaitForCharacterVideoDraws(55);
            _reflectron.ActivateCreatorModeControl("FACE");
            await WaitForCharacterVideoDraws(55);
            _reflectron.ActivateCreatorModeControl("PROJECTION");
            await WaitForCharacterVideoDraws(55);
            AcceptAppearance("LONE WANDERER", sex);
            if (_creatorLayer is not null || _vaultBirthCoverage is null)
                throw new InvalidOperationException(
                    "Fallout 3 generated character did not enter the Vault 101 birth slice.");
            await WaitForCharacterVideoDraws(180);
            GD.Print(
                $"OPENNV_FO3_CHARACTER_VIDEO_COMPLETE profile={_profile.ProfileId} save={_savePath}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_CHARACTER_VIDEO_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task WaitForCharacterVideoDraws(int count)
    {
        for (var frame = 0; frame < count; frame++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private Fo3AppearanceSelection LoadSavedAppearanceSelection()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var root = document.RootElement;
        if (RequiredSaveString(root, "profileId") != _profile.ProfileId ||
            RequiredSaveString(root, "profileSha256") != _profile.Sha256 ||
            RequiredSaveInteger(root, "stage") != _profile.Appearance.AcceptedStage)
            throw new InvalidOperationException(
                "Fallout 3 creator restore proof save identity/stage differs.");
        _profile.Section4Transition.ValidateSavedState(
            RequiredSaveObject(root, "playerPackage"));
        var sex = RequiredSaveObject(root, "sex");
        var engineSex = RequiredSaveString(sex, "engineSex");
        var appearance = RequiredSaveObject(root, "appearance");
        var selection = _profile.Appearance.ResolveSelection(
            engineSex,
            RequiredSaveString(appearance, "adultRaceFormId"),
            RequiredSaveString(appearance, "childRaceFormId"),
            RequiredSaveString(appearance, "hairFormId"),
            RequiredSaveString(appearance, "eyesFormId"));
        var face = RequiredSaveObject(appearance, "faceGen");
        selection = LoadSavedFaceControls(face, selection);
        if (RequiredSaveString(face, "symmetricGeometrySha256") !=
                selection.Sex.FaceGen.SymmetricGeometrySha256)
            throw new InvalidOperationException(
                "Fallout 3 creator restore proof geometry differs.");
        return selection;
    }

    private async Task<Fo3AppearanceProofCapture> CaptureAppearanceFrame(string fileName)
    {
        for (var frame = 0;
             frame < Fo3OpeningFlowNumericContracts.Cg01CaptureWarmupFrames;
             frame++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (fileName.Contains("birth", StringComparison.Ordinal) &&
            (_creatorLayer is not null || _panel.Visible || _background.Visible ||
             _vaultBirthCoverage is null ||
             !_vaultBirthCoverage.DoctorActor.Placement.Visible ||
             !_vaultBirthCoverage.DadActor.Placement.Visible ||
             !_vaultBirthCoverage.MomActor.Placement.Visible))
            throw new InvalidOperationException(
                "Fallout 3 creator birth capture has stale UI or an absent CG00 participant.");
        if (fileName.Contains("birth", StringComparison.Ordinal) ||
            fileName.Contains("creator", StringComparison.Ordinal))
            ValidateCg00ParticipantScreenPresentation();
        var image = GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        if (data.Length == 0)
            throw new InvalidOperationException("Fallout 3 creator capture is empty.");
        var minimum = data.Min();
        var maximum = data.Max();
        if (maximum <= minimum)
            throw new InvalidOperationException("Fallout 3 creator capture is one blank color.");
        var captureRoot = Path.GetFullPath(_appearanceProofCaptureRoot!);
        Directory.CreateDirectory(captureRoot);
        var path = Path.Combine(captureRoot, fileName);
        if (File.Exists(path))
            throw new InvalidOperationException(
                $"Fallout 3 creator capture path is not fresh: {path}");
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Fallout 3 creator capture could not be saved: {error}.");
        using var stream = File.OpenRead(path);
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new Fo3AppearanceProofCapture(
            path,
            sha256,
            image.GetWidth(),
            image.GetHeight(),
            maximum - minimum);
    }

    private object MeasureAppearanceDifference(
        Fo3AppearanceProofCapture baseline,
        Fo3AppearanceProofCapture edited)
    {
        var baselineImage = Image.LoadFromFile(baseline.Path);
        var editedImage = Image.LoadFromFile(edited.Path);
        if (baselineImage is null || editedImage is null ||
            baselineImage.GetSize() != editedImage.GetSize())
            throw new InvalidOperationException(
                "Fallout 3 creator comparison frames do not share one viewport.");
        baselineImage.Convert(Image.Format.Rgba8);
        editedImage.Convert(Image.Format.Rgba8);
        var baselineData = baselineImage.GetData();
        var editedData = editedImage.GetData();
        var left = baseline.Width * _profile.Appearance.Ui.FaceGrabX /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels;
        var top = baseline.Height * _profile.Appearance.Ui.FaceGrabY /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels;
        var width = baseline.Width * _profile.Appearance.Ui.FaceGrabWidth /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels;
        var height = baseline.Height * _profile.Appearance.Ui.FaceGrabHeight /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels;
        if (width <= 0 || height <= 0 || left + width > baseline.Width ||
            top + height > baseline.Height)
            throw new InvalidOperationException(
                "Fallout 3 creator face comparison region is outside the viewport.");
        long absoluteDifference = 0;
        var changedPixels = 0;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var offset = (y * baseline.Width + x) *
                    Fo3OpeningFlowNumericContracts.CaptureBytesPerPixel;
                var pixelChanged = false;
                for (var channel = 0;
                     channel < Fo3OpeningFlowNumericContracts.CaptureRgbChannels;
                     channel++)
                {
                    var difference = Math.Abs(baselineData[offset + channel] -
                        editedData[offset + channel]);
                    absoluteDifference += difference;
                    pixelChanged |= difference > 0;
                }
                if (pixelChanged)
                    changedPixels++;
            }
        }
        if (changedPixels == 0 || absoluteDifference == 0)
            throw new InvalidOperationException(
                "Fallout 3 normalized FaceGen edit produced no visible pixel change.");
        return new
        {
            baselinePath = baseline.Path,
            editedPath = edited.Path,
            region = new[] { left, top, width, height },
            changedPixels,
            absoluteRgbDifference = absoluteDifference,
            meanAbsoluteRgbDifference = absoluteDifference /
                (double)(width * height *
                    Fo3OpeningFlowNumericContracts.CaptureRgbChannels),
        };
    }

    private void WriteAppearanceProofReport(
        string phase,
        Fo3AppearanceSelection selection,
        IReadOnlyList<Fo3AppearanceProofCapture> captures,
        object? morphDifference,
        bool creatorActionsReplayed)
    {
        var preview = _profile.Appearance.PreviewSet.Previews.Single(value =>
            value.RaceFormId.Equals(selection.Race.FormId, StringComparison.OrdinalIgnoreCase) &&
            value.HairFormId.Equals(selection.Hair.FormId, StringComparison.OrdinalIgnoreCase) &&
            value.EyesFormId.Equals(selection.Eyes.FormId, StringComparison.OrdinalIgnoreCase));
        using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var root = document.RootElement;
        var savedPackage = RequiredSaveObject(root, "playerPackage");
        var activePackage = RequiredSaveBoolean(savedPackage, "active");
        var advancedIntoNextBirthBeat =
            RequiredSaveInteger(root, "stage") == _profile.Appearance.AcceptedStage &&
            activePackage;
        if (!advancedIntoNextBirthBeat)
            throw new InvalidOperationException(
                "Fallout 3 creator proof did not persist the next authored package beat.");
        var report = new
        {
            schema = "opennv-fo3-native-creator-proof/v1",
            phase,
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            sourceUi = new
            {
                canvas = new[]
                {
                    Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels,
                    Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels,
                },
                namePanel = new[]
                {
                    _profile.Appearance.Ui.Name.PanelWidth,
                    _profile.Appearance.Ui.Name.PanelHeight,
                },
                appearancePanel = new[]
                {
                    _profile.Appearance.Ui.PanelX,
                    _profile.Appearance.Ui.PanelY,
                    _profile.Appearance.Ui.PanelWidth,
                    _profile.Appearance.Ui.PanelHeight,
                },
                faceGrab = new[]
                {
                    _profile.Appearance.Ui.FaceGrabX,
                    _profile.Appearance.Ui.FaceGrabY,
                    _profile.Appearance.Ui.FaceGrabWidth,
                    _profile.Appearance.Ui.FaceGrabHeight,
                },
            },
            livePreview = new
            {
                raceFormId = selection.Race.FormId,
                sex = preview.Sex,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                control = _profile.Appearance.FaceControl.SettingEntity,
                controlAxisSha256 = _profile.Appearance.FaceControl.AxisSha256,
                value = selection.FaceControlValue(
                    _profile.Appearance.FaceControl.SettingEntity),
                controlCount = _profile.Appearance.FaceControls.Count,
                symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                disposition = preview.RuntimeDisposition,
                fullBody = preview.FullBody,
                bodyComponentRoles = preview.BodyComponentRoles,
                fullRetailSlidersImplemented = true,
            },
            morphDifference,
            persisted = new
            {
                stage = RequiredSaveInteger(root, "stage"),
                name = RequiredSaveString(root, "playerName"),
                race = RequiredSaveString(RequiredSaveObject(root, "appearance"), "adultRaceFormId"),
                faceControlValues = RequiredSaveArray(
                    RequiredSaveObject(
                        RequiredSaveObject(root, "appearance"),
                        "faceGen"),
                    "geometryControls").EnumerateArray().Select(value => new
                    {
                        settingEntity = RequiredSaveString(value, "settingEntity"),
                        value = RequiredSaveSingle(value, "value"),
                    }).ToArray(),
                creatorActionsReplayed,
                activePackage,
                advancedIntoNextBirthBeat,
            },
            captures = captures.Select(value => new
            {
                path = value.Path,
                sha256 = value.Sha256,
                width = value.Width,
                height = value.Height,
                rgbSpan = value.RgbSpan,
            }),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_appearanceProofReportPath!)!);
        File.WriteAllText(
            _appearanceProofReportPath!,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
    }

    private void RunCg01Proof()
    {
        if (_cg01ProofMode is not "apply" and not "restore" ||
            string.IsNullOrWhiteSpace(_cg01ProofReportPath) ||
            _birthPresentation is null)
            throw new InvalidOperationException("Fallout 3 CG01 proof configuration differs.");
        if (_cg01ProofCapturePath is not null &&
            (_cg01ProofMode != "apply" ||
             DisplayServer.GetName() == "headless" ||
             File.Exists(_cg01ProofCapturePath) ||
             !Path.GetExtension(_cg01ProofCapturePath).Equals(
                 ".png",
                 StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Fallout 3 CG01 capture requires a fresh PNG and a rendering display driver.");
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
            var context = new Fo3Cg01RuntimeContext(
                _profile.Appearance.PlayerEditorId,
                sex,
                selection,
                package,
                stage65,
                stage80,
                stage85,
                stage90,
                stage100);
            EnsureCg01VaultScene(context);
            ApplyCg01Stage5Presentation(cg01, stage65);
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
                context);
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
        var toddlerWorld = _profile.Cg01ToddlerWorld.LoadSavedState(
            RequiredSaveObject(root, "cg01ToddlerWorld"));
        var cg01Stage14 = _profile.Cg01Stage12DadResponse.Apply(cg01Stage12);
        _profile.Cg01Stage12DadResponse.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage12DadResponse"),
            cg01Stage14);
        var restoreContext = new Fo3Cg01RuntimeContext(
            _profile.Appearance.PlayerEditorId,
            sex,
            selection,
            package,
            stage65,
            stage80,
            stage85,
            stage90,
            stage100);
        EnsureCg01VaultScene(restoreContext);
        ValidateBirthRuntimeState(
            RequiredSaveObject(root, "birthRuntime"),
            "cg01-stage14-dad-response-applied-package-evaluated");
        ApplyCg01Stage5Presentation(cg01, stage65);
        BeginCg01ToddlerWorld(
            cg01,
            restoreContext,
            cg01Stage10,
            toddlerWorld,
            acceptanceProof: true,
            restoredStage14: cg01Stage14);
    }

    private void WriteCg01ProofReport(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01Stage14State stage14,
        Fo3Cg01ToddlerWorldState toddlerWorld,
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
        if (_cg01ProofCapturePath is not null && !_cg01ProofCaptureCompleted)
            throw new InvalidOperationException(
                "Fallout 3 CG01 proof reached its report before coherent capture completed.");
        var cues = _profile.Cg01Stage10Transition.DialogueFor(engineSex);
        var stage5PublishedInfoFormIds = _cg01DadPublishedSpeakerIdleInfoFormIds
            .Where(value => stage10.AppliedInfoFormIds.Contains(
                value,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var stage12PublishedInfoFormIds = _cg01DadPublishedSpeakerIdleInfoFormIds
            .Where(value => stage14.AppliedInfoFormIds.Contains(
                value,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var report = new
        {
            schema = "opennv-fo3-cg01-runtime-proof/v8",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            phase,
            savePath = _savePath,
            activeQuest = new
            {
                formId = stage10.ActiveQuestFormId,
                editorId = stage10.ActiveQuestEditorId,
                stage = stage14.ActiveStage,
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
                speakerReferenceFormId = stage5.Dad.Reference.FormId,
                lipClockBoundToSpeaker = true,
                lipCueSamplesThisProcess = _cg01DadLipCueSamples,
                speakerIdleInfoFormIdsPublishedThisProcess =
                    stage5PublishedInfoFormIds,
                speakerIdlesPublishedThisProcess =
                    stage5PublishedInfoFormIds.Length,
                assets = cues.Select(cue => new
                {
                    cue.Sequence,
                    cue.InfoFormId,
                    voiceSha256 = cue.Response.Voice.Sha256,
                    lipSha256 = cue.Response.Lip.Sha256,
                    speakerIdleFormId = cue.SpeakerIdle.FormId,
                    speakerIdlePath = cue.SpeakerIdle.ModelPath,
                    speakerIdleSha256 = cue.SpeakerIdle.SourceSha256,
                }),
            },
            actorPresentation = new
            {
                referenceFormId = _vaultBirthCoverage?.Cg01DadActor.ReferenceFormId,
                baseFormId = _vaultBirthCoverage?.Cg01DadActor.BaseFormId,
                startMarkerReferenceFormId =
                    _vaultBirthCoverage?.Cg01DadAppearance.Actor.StartMarkerReferenceFormId,
                rawMarkerPositionGodotGameUnits = _vaultBirthCoverage is null
                    ? null
                    : new[]
                    {
                        _vaultBirthCoverage.Cg01DadAppearance.Actor
                            .StartMarkerPositionGodotGameUnits.X,
                        _vaultBirthCoverage.Cg01DadAppearance.Actor
                            .StartMarkerPositionGodotGameUnits.Y,
                        _vaultBirthCoverage.Cg01DadAppearance.Actor
                            .StartMarkerPositionGodotGameUnits.Z,
                    },
                presentationPositionGodotGameUnits = _vaultBirthCoverage is null
                    ? null
                    : new[]
                    {
                        _vaultBirthCoverage.Cg01DadGrounding
                            .PresentationPlacementGodotGameUnits.X,
                        _vaultBirthCoverage.Cg01DadGrounding
                            .PresentationPlacementGodotGameUnits.Y,
                        _vaultBirthCoverage.Cg01DadGrounding
                            .PresentationPlacementGodotGameUnits.Z,
                    },
                groundingCorrectionGodotGameUnits = _vaultBirthCoverage?
                    .Cg01DadGrounding.VerticalCorrectionGodotGameUnits,
                stage5Enabled = stage5.Dad.Enabled,
                visible = _vaultBirthCoverage?.Cg01DadActor.Placement.Visible,
                previousDoctorVisible = _vaultBirthCoverage?.DoctorActor.Placement.Visible,
                previousCg00DadVisible = _vaultBirthCoverage?.DadActor.Placement.Visible,
                surfaces = _vaultBirthCoverage?.Cg01DadActorGeometry.Surfaces,
                activeCameraName = _vaultBirthCoverage?.Camera.Name.ToString(),
                activeCameraFramesDad = _cg01DadDialogueGeometry?.FrustumIntersection,
                activeCameraDadSurfaces = _cg01DadDialogueGeometry?.Surfaces,
                appearance = "source-stage65-match-race-50-percent-facegen-applied",
                playerRaceFormId = _vaultBirthCoverage?.Cg01DadAppearance.PlayerRaceFormId,
                playerSex = _vaultBirthCoverage?.Cg01DadAppearance.PlayerSex,
                sceneSha256 = _vaultBirthCoverage?.Cg01DadAppearance.Actor.SceneSha256,
                symmetricGeometrySha256 =
                    _vaultBirthCoverage?.Cg01DadAppearance.SymmetricGeometrySha256,
                asymmetricGeometrySha256 =
                    _vaultBirthCoverage?.Cg01DadAppearance.AsymmetricGeometrySha256,
                symmetricTextureSha256 =
                    _vaultBirthCoverage?.Cg01DadAppearance.SymmetricTextureSha256,
                stage65MatchedRaceApplied = true,
                stage65MatchedFaceGeometryApplied = true,
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
            stage12DadResponse = new
            {
                sourceStage = stage14.SourceStage,
                targetStage = stage14.ActiveStage,
                topicFormId = _profile.Cg01Stage12DadResponse.TopicFormId,
                topicEditorId = _profile.Cg01Stage12DadResponse.TopicEditorId,
                infoFormIds = stage14.AppliedInfoFormIds,
                sayOnce = _profile.Cg01Stage12DadResponse.Cues.All(value => value.SayOnce),
                playedThisProcess = phase == "apply",
                replayed = false,
                dadTalking = stage14.DadTalking,
                dadLooksAtPlayer = stage14.DadLooksAtPlayer,
                dadPackageEvaluated = stage14.DadPackageEvaluated,
                accounted = stage14.AccountedCommandCount,
                applied = stage14.AppliedCommandCount,
                speakerReferenceFormId = _profile.Cg01Stage12DadResponse.DadReferenceFormId,
                audioLipIdleClockBoundToSpeaker = true,
                speakerIdleInfoFormIdsPublishedThisProcess =
                    stage12PublishedInfoFormIds,
                speakerIdlesPublishedThisProcess = stage12PublishedInfoFormIds.Length,
                assets = _profile.Cg01Stage12DadResponse.Cues.Select(cue => new
                {
                    cue.Sequence,
                    cue.InfoFormId,
                    cue.TargetStage,
                    voiceSha256 = cue.Response.Voice.Sha256,
                    lipSha256 = cue.Response.Lip.Sha256,
                    speakerIdleFormId = cue.SpeakerIdle.FormId,
                    speakerIdlePath = cue.SpeakerIdle.ModelPath,
                    speakerIdleSha256 = cue.SpeakerIdle.SourceSha256,
                }),
            },
            toddlerWorld = new
            {
                schema = Fo3Cg01ToddlerWorldContract.ExpectedSavedStateSchema,
                physicalBody = true,
                collisionShape = "scaled-open-nv-policy-capsule",
                sourcePlayerScale = _profile.Cg01ToddlerWorld.PlayerScale,
                sourceStartMarkerFormId = toddlerWorld.PlayerStartMarkerFormId,
                sourceTriggerReferenceFormId = toddlerWorld.TriggerReferenceFormId,
                triggerEntered = toddlerWorld.TriggerEntered,
                movementEnabled = toddlerWorld.MovementEnabled,
                authoredCollisionBodies = toddlerWorld.AuthoredCollisionBodies,
                visualBodyPrepared = false,
                playerPositionMeters = new[]
                {
                    toddlerWorld.PlayerPositionMeters.X,
                    toddlerWorld.PlayerPositionMeters.Y,
                    toddlerWorld.PlayerPositionMeters.Z,
                },
            },
            movie = new
            {
                logicalPath = stage5.TransitionMovie.LogicalPath,
                runtimeOutputSha256 = stage5.TransitionMovie.RuntimeOutputSha256,
                requestCount = stage5.TransitionMovieRequestCount,
                surfaceRequested = movieSurfaceRequested,
                nonblankFrameValidatedBeforeVisible = _ownedVideoFrameNonblank,
                everVisible = _ownedVideoEverVisible,
                hiddenAndQueuedAfterCompletion = _ownedVideoCleared,
                escapeSkipped,
                replayed = movieReplayed,
            },
            visualCapture = new
            {
                requested = _cg01ProofCapturePath is not null,
                completed = _cg01ProofCaptureCompleted,
                path = _cg01ProofCapturePath,
                sha256 = _cg01ProofCaptureSha256,
                infoFormId = _cg01ProofCaptureInfoFormId,
                speakerIdleFormId = _cg01ProofCaptureSpeakerIdleFormId,
                width = _cg01ProofCaptureWidth,
                height = _cg01ProofCaptureHeight,
                rgbSpan = _cg01ProofCaptureRgbSpan,
                shellVisible = false,
                movieVisible = false,
                dadCameraFrustum = _cg01DadDialogueGeometry?.FrustumIntersection,
                audioLipIdleSynchronized = _cg01ProofCaptureCompleted,
            },
            nextBoundary = new
            {
                applied = stage14.NextBoundary.Applied,
                blocker = stage14.NextBoundary.Blocker,
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
        selector.Name = $"FO3_RaceSexMenu_{title}";
        selector.CustomMinimumSize = new Vector2(
            0.0f,
            _profile.Appearance.Ui.ListItemHeight);
        selector.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        selector.AddThemeFontSizeOverride(
            "font_size",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels);
        grid.AddChild(selector);
    }

    private static void FillOptions(
        OptionButton selector,
        IReadOnlyList<Fo3AppearanceRace> options,
        string selectedFormId,
        string prefix)
    {
        selector.Clear();
        for (var index = 0; index < options.Count; index++)
        {
            selector.AddItem($"{prefix}  •  {options[index].Label}");
            selector.SetItemMetadata(index, options[index].FormId);
            if (options[index].FormId == selectedFormId)
                selector.Select(index);
        }
    }

    private static void FillOptions(
        OptionButton selector,
        IReadOnlyList<Fo3AppearanceOption> options,
        string selectedFormId,
        string prefix)
    {
        selector.Clear();
        for (var index = 0; index < options.Count; index++)
        {
            selector.AddItem($"{prefix}  •  {options[index].Label}");
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

    private Control CreatorSurface(
        float left,
        float top,
        float width,
        float height,
        Fo3AppearanceAsset background,
        string name)
    {
        if (_creatorLayer is null)
        {
            _creatorLayer = new Control { Name = "FO3_OwnedCreatorCanvas_1600x1200" };
            _creatorLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            AddChild(_creatorLayer);
            _panel.Visible = false;
            _background.Visible = _vaultPreviewHost is null;
        }
        var surface = new Control { Name = name };
        surface.AnchorLeft = left;
        surface.AnchorTop = top;
        surface.AnchorRight = left + width;
        surface.AnchorBottom = top + height;
        _creatorLayer.AddChild(surface);
        var texture = new TextureRect
        {
            Name = $"{name}_OwnedBackground",
            Texture = ImageTexture.CreateFromImage(LoadAppearanceImage(background)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TooltipText =
                $"source={background.SourceSha256} preview={background.PreviewSha256}",
        };
        texture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        surface.AddChild(texture);
        return surface;
    }

    private static VBoxContainer CreatorColumn(Control surface, int marginPixels)
    {
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, marginPixels);
        surface.AddChild(margin);
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride(
            "separation",
            Fo3OpeningFlowNumericContracts.CreatorPanelSeparationPixels);
        margin.AddChild(column);
        return column;
    }

    private void ClearContent()
    {
        if (_creatorLayer is not null)
        {
            _creatorLayer.Visible = false;
            _creatorLayer.QueueFree();
            _creatorLayer = null;
        }
        _activeNameInput = null;
        _activeAppearanceCategory = null;
        _activeFaceControlSlider = null;
        _activeAppearanceSelection = null;
        _activeFacePreview = null;
        _reflectron = null;
        _panel.Visible = true;
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
