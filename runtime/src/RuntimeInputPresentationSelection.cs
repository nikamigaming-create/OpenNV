namespace OpenNV.Runtime;

internal enum RuntimeNativeInputProvider
{
    WindowsDirectInput,
    CommandInput,
}

/// <summary>
/// Select input adaptation independently of the actual presentation backend.
/// </summary>
internal static class RuntimeInputPresentationSelection
{
    internal static bool UsesCommandInput(IReadOnlyDictionary<string, string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.ContainsKey("command-input") || options.ContainsKey("headless-play");
    }

    internal static bool UsesHeadlessPresentation(string displayBackend)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayBackend);
        return displayBackend == "headless";
    }

    internal static void ValidateBackend(IReadOnlyDictionary<string, string> options, string displayBackend)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ContainsKey("headless-play") && !UsesHeadlessPresentation(displayBackend))
            throw new ArgumentException("--headless-play requires Godot's --headless backend.");
    }

    internal static RuntimeNativeInputProvider SelectInputProvider(bool commandInput, string displayBackend)
    {
        var headless = UsesHeadlessPresentation(displayBackend);
        if (commandInput) return RuntimeNativeInputProvider.CommandInput;
        if (headless)
            throw new NotSupportedException("Headless has no complete native DirectInput product-window owner; select explicit command input.");
        return RuntimeNativeInputProvider.WindowsDirectInput;
    }
}
