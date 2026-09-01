using System.Text.Json;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed partial record OpeningNewGameFlow
{
    private static OpeningFaceGenSliderSemanticsEvidence
        ParseFaceGenSliderSemanticsEvidence(JsonElement source) => new(
            source.GetProperty("classification").GetString()!,
            source.GetProperty("engineBuild").GetString()!,
            source.GetProperty("sourceExecutableSha256").GetString()!,
            source.GetProperty("sourceMinimum").GetSingle(),
            source.GetProperty("sourceMaximum").GetSingle(),
            source.GetProperty("uiScale").GetSingle(),
            source.GetProperty("uiMinimum").GetSingle(),
            source.GetProperty("uiMaximum").GetSingle(),
            source.GetProperty("ordinaryIncrement").GetSingle(),
            source.GetProperty("jump").GetSingle(),
            source.GetProperty("morphWeightScale").GetSingle(),
            source.GetProperty("lowGlobalAddress").GetString()!,
            source.GetProperty("highGlobalAddress").GetString()!,
            source.GetProperty("incrementTrait").GetString()!,
            source.GetProperty("incrementDefaultThreshold").GetSingle());

    private static OpeningFaceGenPreviewPresentation ParseFaceGenPreviewPresentation(
        JsonElement source) => new(
            source.TryGetProperty("viewportWidthFraction", out var viewportWidth)
                ? viewportWidth.GetSingle()
                : float.NaN,
            source.TryGetProperty("viewportHeightFraction", out var viewportHeight)
                ? viewportHeight.GetSingle()
                : float.NaN,
            source.TryGetProperty("verticalFovHalfAngleFactor", out var fovFactor)
                ? fovFactor.GetSingle()
                : float.NaN,
            source.TryGetProperty("depthExtentFraction", out var depthExtent)
                ? depthExtent.GetSingle()
                : float.NaN,
            source.GetProperty("fullInVerticalOffsetGameUnits").GetSingle(),
            source.GetProperty("fullInDistanceGameUnits").GetSingle(),
            source.GetProperty("fullInYawRadians").GetSingle(),
            source.GetProperty("fullOutVerticalOffsetGameUnits").GetSingle(),
            source.GetProperty("fullOutDistanceGameUnits").GetSingle(),
            source.GetProperty("fullOutYawRadians").GetSingle(),
            source.GetProperty("startingZoomFraction").GetSingle());

    private static OpeningPlayerFaceGenPreviewSet ParsePlayerFaceGenPreviewSet(
        JsonElement source)
    {
        var schema = source.GetProperty("schema").GetString()!;
        var status = source.GetProperty("status").GetString()!;
        var playerFormId = source.GetProperty("playerFormId").GetString()!;
        var geometryControlNames = source.GetProperty("geometryControlNames")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var geometryControlCount = source.GetProperty("geometryControlCount").GetInt32();
        var textureControlNames = source.GetProperty("textureControlNames")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var textureControlCount = source.GetProperty("textureControlCount").GetInt32();
        var runtimeDisposition = source.GetProperty("runtimeDisposition").GetString()!;
        var selectionScope = source.GetProperty("selectionScope").GetString()!;
        var unsupportedSelectionScope = source.GetProperty("unsupportedSelectionScope")
            .GetString()!;
        var fullBody = source.GetProperty("fullBody").GetBoolean();
        var bodyComponentRoles = source.GetProperty("bodyComponentRoles")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        var bodyComponentSourcesBySex = source.GetProperty("bodyComponentSourcesBySex")
            .EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => (IReadOnlyList<OpeningPlayerBodyComponentSource>)value.Value
                    .EnumerateArray()
                    .Select(ParsePlayerBodyComponentSource)
                    .ToArray(),
                StringComparer.Ordinal);
        var previews = source.GetProperty("previews").EnumerateArray()
            .Select(value =>
            {
                var outputs = value.GetProperty("outputs");
                return new OpeningPlayerFaceGenPreview(
                    schema,
                    status,
                    playerFormId,
                    value.GetProperty("raceFormId").GetString()!,
                    value.GetProperty("sex").GetString()!,
                    value.GetProperty("hairFormId").GetString()!,
                    value.GetProperty("eyesFormId").GetString()!,
                    value.GetProperty("headPartFormIds").EnumerateArray()
                        .Select(part => part.GetString()!).ToArray(),
                    geometryControlNames,
                    geometryControlCount,
                    textureControlNames,
                    textureControlCount,
                    outputs.GetProperty("gltf").GetString()!,
                    outputs.GetProperty("gltfSha256").GetString()!,
                    outputs.GetProperty("sidecar").GetString()!,
                    outputs.GetProperty("sidecarSha256").GetString()!,
                    outputs.GetProperty("bufferSha256").GetString()!,
                    outputs.GetProperty("egt").GetString()!,
                    outputs.GetProperty("egtSha256").GetString()!,
                    ParseFloatArray(value.GetProperty("symmetricTexture")),
                    value.GetProperty("textureControls").EnumerateArray()
                        .Select(control => new OpeningNativeFaceGenTextureControl(
                            control.GetProperty("controlIndex").GetInt32(),
                            control.GetProperty("settingEntity").GetString()!,
                            control.GetProperty("sourceLabel").GetString()!,
                            control.GetProperty("axisSha256").GetString()!,
                            ParseFloatArray(control.GetProperty("axis"))))
                        .ToArray(),
                    runtimeDisposition,
                    fullBody,
                    bodyComponentRoles,
                    bodyComponentSourcesBySex,
                    ParseNativeFaceGenAgeControl(value.GetProperty("ageControl")));
            })
            .ToArray();
        return new OpeningPlayerFaceGenPreviewSet(
            schema,
            status,
            playerFormId,
            geometryControlNames,
            geometryControlCount,
            textureControlNames,
            textureControlCount,
            runtimeDisposition,
            selectionScope,
            unsupportedSelectionScope,
            fullBody,
            bodyComponentRoles,
            bodyComponentSourcesBySex,
            previews);
    }

    private static OpeningPlayerBodyComponentSource ParsePlayerBodyComponentSource(
        JsonElement source) => new(
            source.GetProperty("role").GetString()!,
            source.GetProperty("modelLogicalPath").GetString()!,
            source.GetProperty("modelSha256").GetString()!,
            source.GetProperty("sourceSurfaceCount").GetInt32(),
            source.GetProperty("retainedSurfaceCount").GetInt32(),
            source.GetProperty("retainedSurfaceNames").EnumerateArray()
                .Select(value => value.GetString()!).ToArray(),
            source.GetProperty("omittedDismemberCapSurfaceCount").GetInt32(),
            source.GetProperty("diffuseLogicalPath").GetString()!,
            source.GetProperty("diffuseSha256").GetString()!,
            source.GetProperty("normalLogicalPath").GetString()!,
            source.GetProperty("normalSha256").GetString()!,
            source.GetProperty("shapeTransformDisposition").GetString()!);

    private static OpeningAppearanceRace ParseAppearanceRace(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures)
    {
        var sexes = source.GetProperty("sex").EnumerateObject()
            .ToDictionary(
                value => value.Name,
                value => ParseAppearanceSex(value.Value, textures),
                StringComparer.Ordinal);
        return new OpeningAppearanceRace(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("label").GetString()!,
            source.GetProperty("recordSha256").GetString()!,
            sexes);
    }

    private static OpeningAppearanceSex ParseAppearanceSex(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures) => new(
            source.GetProperty("defaultHairFormId").GetString()!,
            source.GetProperty("defaultEyesFormId").GetString()!,
            source.GetProperty("hairOptions").EnumerateArray()
                .Select(value => ParseAppearanceOption(value, textures))
                .ToArray(),
            source.GetProperty("eyeOptions").EnumerateArray()
                .Select(value => ParseAppearanceOption(value, textures))
                .ToArray());

    private static OpeningAppearanceOption ParseAppearanceOption(
        JsonElement source,
        IReadOnlyDictionary<string, OwnedUiTexture> textures)
    {
        var texturePath = source.GetProperty("textureLogicalPath").GetString()!;
        if (!textures.TryGetValue(texturePath, out var texture))
            throw new InvalidOperationException(
                $"Owned appearance preview texture is absent: {texturePath}");
        return new OpeningAppearanceOption(
            source.GetProperty("formId").GetString()!,
            source.GetProperty("recordType").GetString()!,
            source.GetProperty("editorId").GetString()!,
            source.GetProperty("label").GetString()!,
            source.GetProperty("recordSha256").GetString()!,
            source.GetProperty("modelLogicalPath").ValueKind == JsonValueKind.String
                ? source.GetProperty("modelLogicalPath").GetString()
                : null,
            texture);
    }
}
