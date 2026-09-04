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
}
