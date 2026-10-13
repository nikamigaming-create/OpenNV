using OpenNV.Runtime.Content;

namespace OpenNV.Runtime;

/// <summary>
/// Admit launch selections before reading the selected installation or saves.
/// </summary>
internal static class RuntimeLaunchValidator
{
    internal static void ValidatePreflight(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var commandInput = RuntimeInputPresentationSelection.UsesCommandInput(options);
        if (options.ContainsKey("continue-game") && !commandInput)
            throw new ArgumentException("--continue-game requires explicit --command-input or --headless-play.");
        if (commandInput)
        {
            var required = new[] { "data-root", "campaign", "save-path", "live-harness" };
            var missing = required.FirstOrDefault(option => !options.TryGetValue(option, out var value) || string.IsNullOrWhiteSpace(value));
            if (missing is not null) throw new ArgumentException("Command gameplay requires --" + missing + ".");
            if (options.GetValueOrDefault("campaign") is not ("fallout-3" or "fallout-new-vegas") ||
                options.ContainsKey("launcher"))
                throw new ArgumentException("Command gameplay requires a selected Fallout 3/New Vegas source and windowless input.");
            if (options.ContainsKey("headless-play") && options.ContainsKey("vr"))
                throw new ArgumentException("Headless gameplay cannot replace the actual rendered OpenXR owner.");
            if (options.ContainsKey("new-game") && options.ContainsKey("continue-game"))
                throw new ArgumentException("Select one command New Game or Continue action.");
            if (!Path.IsPathFullyQualified(options["live-harness"]))
                throw new ArgumentException("Command gameplay requires an absolute private command directory.");
        }
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
