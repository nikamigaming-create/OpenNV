using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

/// <summary>
/// Fail-closed validation for the only admitted runtime content path: the
/// selected installation's live retail files.
/// Launch validation for the direct owned-installation runtime.
/// </summary>
internal static class RuntimeLaunchValidator
{
    internal static void ValidatePreflight(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ContainsKey("xr-simulator-proof") &&
            (!options.ContainsKey("vr") || !options.ContainsKey("report")))
            throw new ArgumentException("--xr-simulator-proof requires --vr and --report.");
        if (options.ContainsKey("vr") && options.ContainsKey("xr-rig-proof"))
            throw new ArgumentException(
                "Use --vr for a live OpenXR session or --xr-rig-proof for the " +
                "headless layout gate, not both.");
    }

    internal static void ValidateContent(
        IReadOnlyDictionary<string, string> options,
        RuntimeLaunchRequest launch)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(launch);
        if (!launch.Is(RuntimeLaunchRoute.LiveRetailFiles))
            return;

        var required = new[]
        {
            "data-root", "campaign", "save-path",
        };
        var missing = required.FirstOrDefault(option => !options.ContainsKey(option));
        if (missing is not null)
            throw new ArgumentException(
                $"The live retail-file route requires --{missing}.");
    }

    internal static void ValidateInstallation(
        IReadOnlyDictionary<string, string> options,
        NativeGameInstallation installation)
    {
        var expectedCampaign = installation.Game switch
        {
            NativeGame.Fallout1 => "fallout-1",
            NativeGame.Fallout2 => "fallout-2",
            NativeGame.Fallout3 => "fallout-3",
            NativeGame.FalloutNewVegas => "fallout-new-vegas",
            _ => throw new NotSupportedException("The detected game has no campaign launch contract."),
        };
        if (!options.TryGetValue("campaign", out var campaign) ||
            !campaign.Equals(expectedCampaign, StringComparison.Ordinal))
            throw new ArgumentException(
                $"The selected installation is {expectedCampaign}; --campaign must match before loading its state.");

        if (installation.Game is not (NativeGame.Fallout1 or NativeGame.Fallout2))
            return;
        var presentation = options.GetValueOrDefault("presentation",
            options.GetValueOrDefault("fo1-start-presentation", "hex-tactical"));
        if (options.TryGetValue("fo1-start-presentation", out var previousPresentation) &&
            previousPresentation != presentation)
            throw new ArgumentException("Conflicting classic presentation selections.");
        if (presentation != "hex-tactical" || options.ContainsKey("vr"))
            throw new NotSupportedException(
                $"{expectedCampaign} {presentation} is unavailable in the current native runtime; " +
                "the hex scene preview does not implement FPS or VR gameplay.");
    }
}
