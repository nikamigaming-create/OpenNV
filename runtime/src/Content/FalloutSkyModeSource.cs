using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutSkyModeSource(string EngineSha256, string SkyContract, string Contract)
{
    private const string Rules = "source-standalone-Sky-mode/v1;constructor-mode-four-all-scene-children-null-deferred-list-empty;" +
        "actual-CELL-load-skips-mode-call-when-current-mode-zero;winning-CELL-DATA-interior-bit-zero-requests-three;" +
        "interior-requests-one-plus-high-bit;old-zero-one-four-to-two-three-loads-resources-before-enable-and-show;" +
        "old-two-three-four-to-zero-one-resets-Clouds-before-release-before-Masser-Secunda-Sun-texture-release;" +
        "Night-parent-detach-reference-release-null-before-root-and-Sun-shadow-hide-before-deferred-list-retirement;" +
        "mode-store-before-optional-Precipitation-zero-update-before-flash-zero;final-mode-store-before-image-manager;" +
        "nonnull-missing-children-retain-entered-prefix;no-residency-or-weather-sample-mode-writer";
    internal static FalloutSkyModeSource Read(FalloutSkyTransferDeclaration sky)
    {
        sky.Validate();
        if (!sky.IsStandalone) throw new NotSupportedException("source-FNV-Sky-mode-CELL-caller-and-child-order-unowned");
        return new(sky.EngineSha256, sky.Contract, FalloutSkyTransferDeclaration.Hash(sky.EngineSha256 + ";" + Rules));
    }
    internal int? Requested(int current, byte actualCellFlags)
    {
        if (current is < 0 or > 4) throw new InvalidDataException("Sky mode has no original finite selection.");
        return current == 0 ? null : (actualCellFlags & 1) == 0 ? 3 : (actualCellFlags >> 7) + 1;
    }
    internal IReadOnlyList<FalloutSkyModeStep> Steps(int before, int after)
    {
        if (before is < 0 or > 4 || after is < 0 or > 3) throw new InvalidDataException("Sky mode branch changed its source operand.");
        var steps = new List<FalloutSkyModeStep>();
        if (before is 0 or 1 or 4 && after is 2 or 3)
            steps.AddRange([FalloutSkyModeStep.ActiveResourceLoad, FalloutSkyModeStep.CloudActive,
                FalloutSkyModeStep.RootShow, FalloutSkyModeStep.SunShadowShow]);
        else if (before is 2 or 3 or 4 && after is 0 or 1)
            steps.AddRange([FalloutSkyModeStep.CloudReset, FalloutSkyModeStep.CloudRelease,
                FalloutSkyModeStep.MasserTextures, FalloutSkyModeStep.SecundaTextures, FalloutSkyModeStep.SunTextures,
                FalloutSkyModeStep.NightRelease, FalloutSkyModeStep.RootHide, FalloutSkyModeStep.SunShadowHide,
                FalloutSkyModeStep.DeferredScenes, FalloutSkyModeStep.ModeStore, FalloutSkyModeStep.PrecipitationZero,
                FalloutSkyModeStep.FlashZero]);
        steps.Add(FalloutSkyModeStep.FinalModeStore); steps.Add(FalloutSkyModeStep.ImageManager);
        return steps;
    }
}

internal enum FalloutSkyModeStep
{
    ActiveResourceLoad, CloudActive, RootShow, SunShadowShow, CloudReset, CloudRelease,
    MasserTextures, SecundaTextures, SunTextures, NightRelease, RootHide, SunShadowHide,
    DeferredScenes, ModeStore, PrecipitationZero, FlashZero, FinalModeStore, ImageManager
}
internal enum FalloutSkyModeRole { Root, Sun, Night, Masser, Secunda }
internal sealed record FalloutSkyModeChild(FalloutSkyModeRole Role, Guid? Owner, string? Unowned);
internal sealed record FalloutSkyModeCall(Guid Call, Guid Main, Guid Process, Guid Sky, string SourceContract,
    FalloutFormKey Cell, string CellSha256, byte CellFlags, int Before, int After, long Entered, long Changed,
    IReadOnlyList<FalloutSkyModeStep> Completed, FalloutSkyModeStep? EnteredChild,
    bool Returned, string? FailureType, string? Error);
internal interface IFalloutSkyCloudModeRelease
{
    Guid Identity { get; }
    Guid Sky { get; }
    FalloutSkyChildSnapshot ReleaseForMode(FalloutSkyModeCall call);
}
