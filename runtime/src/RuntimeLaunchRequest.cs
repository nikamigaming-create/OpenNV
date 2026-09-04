namespace OpenNV.Runtime;

/// <summary>
/// The single, validated content source selected for one runtime invocation.
/// Proof and presentation switches refine this request; they never create a
/// second source of authoritative world state.
/// </summary>
internal sealed record RuntimeLaunchRequest(
    RuntimeLaunchRoute Route,
    string? LoadingTitle)
{
    private static readonly (string Option, RuntimeLaunchRoute Route)[] PrimarySources =
    [
        ("source-stack", RuntimeLaunchRoute.NativeOwnedData),
        // Kept only so old evidence invocations fail through the same native
        // validator while the launcher uses the v2 source-stack route.
        ("source-root", RuntimeLaunchRoute.NativeOwnedData),
        ("data-root", RuntimeLaunchRoute.NativeOwnedData),
        ("model", RuntimeLaunchRoute.Model),
        ("cell-scene", RuntimeLaunchRoute.CellScene),
        ("static-cell-compile", RuntimeLaunchRoute.StaticCellCompile),
        ("actor-model", RuntimeLaunchRoute.ActorModel),
        ("actor-review-scene", RuntimeLaunchRoute.ActorReviewScene),
        ("fo1-owned-profile", RuntimeLaunchRoute.Fallout1NativeOwned),
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
                "Use only one --source-stack or legacy source root, or one of --model/--sidecar, --cell-scene, " +
                "--static-cell-compile, --actor-model/--actor-sidecar, " +
                "--actor-review-scene, --fo1-owned-profile, --fo1-hex-scene, --fo1-campaign-transport, or " +
                "--fo1-campaign-presentation, --fo2-temple-cache, --fo3-profile, or " +
                "--ttw-fo3-opening-profile.");

        var route = selectedSources.Length == 1
            ? selectedSources[0].Route
            : RuntimeLaunchRoute.None;
        return new RuntimeLaunchRequest(route, SelectLoadingTitle(options));
    }

    internal bool Is(RuntimeLaunchRoute route) => Route == route;

    private static string? SelectLoadingTitle(IReadOnlyDictionary<string, string> options)
    {
        if (options.ContainsKey("fo1-owned-profile"))
            return "FALLOUT 1  //  VERIFYING OWNED DAT1 SOURCES";
        if (options.ContainsKey("source-stack"))
        {
            var campaign = options.TryGetValue("campaign", out var selected)
                ? selected.Replace('-', ' ').ToUpperInvariant()
                : "OWNED SOURCE";
            return $"{campaign}  //  VERIFYING SOURCE STACK";
        }
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
    NativeOwnedData,
    Model,
    CellScene,
    StaticCellCompile,
    ActorModel,
    ActorReviewScene,
    Fallout1NativeOwned,
    Fallout1HexScene,
    Fallout1CampaignTransport,
    Fallout1CampaignPresentation,
    Fallout2TemplePresentation,
    Fallout3Opening,
    TtwFallout3Opening,
}
