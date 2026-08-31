namespace OpenNV.Runtime;

/// <summary>
/// The single, validated content source selected for one runtime invocation.
/// Proof and presentation switches refine this request; they never create a
/// second source of authoritative world state.
/// </summary>
internal sealed record RuntimeLaunchRequest(
    RuntimeLaunchRoute Route,
    string? DataRoot,
    string? LoadingTitle)
{
    private static readonly (string Option, RuntimeLaunchRoute Route)[] PrimarySources =
    [
        ("data-root", RuntimeLaunchRoute.OwnedData),
        ("model", RuntimeLaunchRoute.Model),
        ("cell-scene", RuntimeLaunchRoute.CellScene),
        ("static-cell-compile", RuntimeLaunchRoute.StaticCellCompile),
        ("actor-model", RuntimeLaunchRoute.ActorModel),
        ("actor-review-scene", RuntimeLaunchRoute.ActorReviewScene),
        ("fo1-hex-scene", RuntimeLaunchRoute.Fallout1HexScene),
        ("fo1-campaign-transport", RuntimeLaunchRoute.Fallout1CampaignTransport),
        ("fo1-campaign-presentation", RuntimeLaunchRoute.Fallout1CampaignPresentation),
        ("fo2-temple-cache", RuntimeLaunchRoute.Fallout2TemplePresentation),
        ("fo3-profile", RuntimeLaunchRoute.Fallout3Opening),
        ("ttw-fo3-opening-profile", RuntimeLaunchRoute.TtwFallout3Opening),
    ];

    internal static RuntimeLaunchRequest Create(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var selectedSources = PrimarySources
            .Where(source => options.ContainsKey(source.Option))
            .ToArray();
        if (selectedSources.Length > 1)
            throw new ArgumentException(
                "Use only one of --data-root, --model/--sidecar, --cell-scene, " +
                "--static-cell-compile, --actor-model/--actor-sidecar, " +
                "--actor-review-scene, --fo1-hex-scene, --fo1-campaign-transport, or " +
                "--fo1-campaign-presentation, --fo2-temple-cache, --fo3-profile, or " +
                "--ttw-fo3-opening-profile.");

        var route = selectedSources.Length == 1
            ? selectedSources[0].Route
            : options.ContainsKey("reuse-cache")
                ? RuntimeLaunchRoute.PreparedCache
                : RuntimeLaunchRoute.None;
        options.TryGetValue("data-root", out var dataRoot);

        return new RuntimeLaunchRequest(route, dataRoot, SelectLoadingTitle(options));
    }

    internal bool Is(RuntimeLaunchRoute route) => Route == route;

    private static string? SelectLoadingTitle(IReadOnlyDictionary<string, string> options)
    {
        if (options.ContainsKey("fo1-hex-scene"))
        {
            var presentation = options.TryGetValue("fo1-start-presentation", out var selected)
                ? selected.Replace('-', ' ').ToUpperInvariant()
                : "V13ENT";
            return $"FALLOUT 1  //  {presentation}";
        }

        if (options.ContainsKey("fo1-campaign-transport"))
            return "FALLOUT 1  //  VERIFYING ALL MAPS";
        if (options.ContainsKey("fo1-campaign-presentation"))
            return "FALLOUT 1  //  VERIFYING CAMPAIGN ART";
        if (options.ContainsKey("fo2-temple-cache"))
            return "FALLOUT 2  //  VERIFYING TEMPLE MAP 126";
        if (options.ContainsKey("fo3-profile"))
            return "FALLOUT 3  //  CG00 CHARACTER SELECTION";
        if (options.ContainsKey("ttw-fo3-opening-profile"))
            return "TTW  //  VERIFYING CG00 TO CG01 STATE";
        return null;
    }
}

internal enum RuntimeLaunchRoute
{
    None,
    OwnedData,
    Model,
    CellScene,
    StaticCellCompile,
    ActorModel,
    ActorReviewScene,
    Fallout1HexScene,
    Fallout1CampaignTransport,
    Fallout1CampaignPresentation,
    Fallout2TemplePresentation,
    Fallout3Opening,
    TtwFallout3Opening,
    PreparedCache,
}
