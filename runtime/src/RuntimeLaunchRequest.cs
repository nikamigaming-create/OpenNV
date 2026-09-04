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
    private static readonly string[] LegacyLaunchOptions =
    [
        "source-root", "source-stack", "source-stack-sha256", "stack-id",
        "model", "sidecar", "cell-scene",
        "static-cell-compile", "actor-model", "actor-sidecar",
        "actor-review-scene", "fo1-owned-profile", "fo1-hex-scene",
        "fo1-campaign-transport", "fo1-campaign-presentation",
        "fo3-profile", "ttw-fo3-opening-profile", "opening-manifest", "actor-scenes",
    ];

    internal static RuntimeLaunchRequest Create(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var removed = LegacyLaunchOptions.FirstOrDefault(options.ContainsKey);
        if (removed is not null)
            throw new ArgumentException(
                $"--{removed} is not part of the direct owned-installation runtime. " +
                "Select the live --data-root route.");
        var route = options.ContainsKey("data-root")
            ? RuntimeLaunchRoute.LiveRetailFiles
            : RuntimeLaunchRoute.None;
        return new RuntimeLaunchRequest(route, SelectLoadingTitle(options));
    }

    internal bool Is(RuntimeLaunchRoute route) => Route == route;

    private static string? SelectLoadingTitle(IReadOnlyDictionary<string, string> options)
    {
        if (options.ContainsKey("data-root"))
        {
            var campaign = options.TryGetValue("campaign", out var selected)
                ? selected.Replace('-', ' ').ToUpperInvariant()
                : "RETAIL SOURCE";
            return $"{campaign}  //  INDEXING LIVE FILES";
        }
        return null;
    }
}

internal enum RuntimeLaunchRoute
{
    None,
    LiveRetailFiles,
}
