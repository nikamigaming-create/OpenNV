using Godot;

namespace OpenNV.Runtime.Presentation.CharacterCreation;

internal sealed record OwnedGamebryoFaceGenDeviceContract(
    Color PlayerDiffuse,
    Color PlayerAmbient,
    float NearDistanceGameUnits,
    float FarDistanceGameUnits,
    float FovHalfTangent,
    string CameraContractStatus,
    bool CameraContractReady,
    bool ParityReady);

internal sealed record OpeningNativeFaceGenGeometryControl(
    int ControlIndex,
    string SettingEntity,
    string SourceLabel,
    string AxisSha256);

internal sealed record OpeningNativeFaceGenTextureControl(
    int ControlIndex,
    string SettingEntity,
    string SourceLabel,
    string AxisSha256,
    IReadOnlyList<float> Axis);

internal sealed record OpeningFaceGenPreviewControl(
    int ControlIndex,
    string SettingEntity,
    string SourceLabel,
    string AxisSha256,
    float Minimum,
    float Maximum,
    float Step,
    float Jump,
    float MorphWeightScale,
    float ResetValue,
    float AcceptanceValue,
    OpeningFaceGenSliderSemanticsEvidence SliderSemanticsEvidence,
    OpeningFaceGenPreviewPresentation Presentation,
    string Semantics);

internal sealed record OpeningFaceGenSliderSemanticsEvidence(
    string Classification,
    string EngineBuild,
    string SourceExecutableSha256,
    float SourceMinimum,
    float SourceMaximum,
    float UiScale,
    float UiMinimum,
    float UiMaximum,
    float OrdinaryIncrement,
    float Jump,
    float MorphWeightScale,
    string LowGlobalAddress,
    string HighGlobalAddress,
    string IncrementTrait,
    float IncrementDefaultThreshold);

internal sealed record OpeningFaceGenPreviewPresentation(
    float ViewportWidthFraction,
    float ViewportHeightFraction,
    float VerticalFovHalfAngleFactor,
    float DepthExtentFraction,
    float FullInVerticalOffsetGameUnits = float.NaN,
    float FullInDistanceGameUnits = float.NaN,
    float FullInYawRadians = float.NaN,
    float FullOutVerticalOffsetGameUnits = float.NaN,
    float FullOutDistanceGameUnits = float.NaN,
    float FullOutYawRadians = float.NaN,
    float StartingZoomFraction = float.NaN);

internal sealed record OpeningPlayerFaceGenPreviewSet(
    string Schema,
    string Status,
    string PlayerFormId,
    IReadOnlyList<string> GeometryControlNames,
    int GeometryControlCount,
    IReadOnlyList<string> TextureControlNames,
    int TextureControlCount,
    string RuntimeDisposition,
    string SelectionScope,
    string UnsupportedSelectionScope,
    bool FullBody,
    IReadOnlyList<string>? BodyComponentRoles,
    IReadOnlyDictionary<string, IReadOnlyList<OpeningPlayerBodyComponentSource>>?
        BodyComponentSourcesBySex,
    IReadOnlyList<OpeningPlayerFaceGenPreview> Previews);

internal sealed record OpeningPlayerFaceGenPreview(
    string Schema,
    string Status,
    string PlayerFormId,
    string RaceFormId,
    string Sex,
    string HairFormId,
    string EyesFormId,
    IReadOnlyList<string> HeadPartFormIds,
    IReadOnlyList<string> GeometryControlNames,
    int GeometryControlCount,
    IReadOnlyList<string> TextureControlNames,
    int TextureControlCount,
    string GltfPath,
    string GltfSha256,
    string SidecarPath,
    string SidecarSha256,
    string BufferSha256,
    string EgtPath,
    string EgtSha256,
    IReadOnlyList<float> SymmetricTexture,
    IReadOnlyList<OpeningNativeFaceGenTextureControl> TextureControls,
    string RuntimeDisposition,
    bool FullBody = false,
    IReadOnlyList<string>? BodyComponentRoles = null,
    IReadOnlyDictionary<string, IReadOnlyList<OpeningPlayerBodyComponentSource>>?
        BodyComponentSourcesBySex = null);

internal sealed record OwnedGamebryoFaceGenSelectionDomain(
    string Sex,
    string RaceFormId,
    IReadOnlyList<string> HairFormIds,
    IReadOnlyList<string> EyesFormIds);

internal static class OwnedGamebryoFaceGenSelectionInventory
{
    internal static bool IsComplete(
        OpeningPlayerFaceGenPreviewSet previewSet,
        IEnumerable<OwnedGamebryoFaceGenSelectionDomain> domains)
    {
        var expected = domains.SelectMany(domain =>
                domain.HairFormIds.SelectMany(hairFormId =>
                    domain.EyesFormIds.Select(eyesFormId => Identity(
                        domain.Sex,
                        domain.RaceFormId,
                        hairFormId,
                        eyesFormId))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actual = previewSet.Previews.Select(preview => Identity(
                preview.Sex,
                preview.RaceFormId,
                preview.HairFormId,
                preview.EyesFormId))
            .ToArray();
        return expected.Count > 0 &&
            actual.Length == expected.Count &&
            actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() == actual.Length &&
            actual.All(expected.Contains);
    }

    internal static OpeningPlayerFaceGenPreview Require(
        OpeningPlayerFaceGenPreviewSet previewSet,
        string sex,
        string raceFormId,
        string hairFormId,
        string eyesFormId)
    {
        var identity = Identity(sex, raceFormId, hairFormId, eyesFormId);
        var matches = previewSet.Previews.Where(preview => Identity(
                preview.Sex,
                preview.RaceFormId,
                preview.HairFormId,
                preview.EyesFormId).Equals(identity, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                "Owned Gamebryo FaceGen preview identity is unavailable: " +
                $"sex={sex} race={raceFormId} hair={hairFormId} eyes={eyesFormId}.");
        return matches[0];
    }

    private static string Identity(
        string sex,
        string raceFormId,
        string hairFormId,
        string eyesFormId) =>
        $"{sex}:{raceFormId}:{hairFormId}:{eyesFormId}";
}

internal sealed record OpeningPlayerBodyComponentSource(
    string Role,
    string ModelLogicalPath,
    string ModelSha256,
    int SourceSurfaceCount,
    int RetainedSurfaceCount,
    IReadOnlyList<string> RetainedSurfaceNames,
    int OmittedDismemberCapSurfaceCount,
    string DiffuseLogicalPath,
    string DiffuseSha256,
    string NormalLogicalPath,
    string NormalSha256,
    string ShapeTransformDisposition);
