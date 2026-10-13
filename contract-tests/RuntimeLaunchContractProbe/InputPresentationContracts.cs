using OpenNV.Runtime;
using OpenNV.Runtime.Content;

internal static class InputPresentationContracts
{
    internal static void Run()
    {
        foreach (var route in new[] { "headless-play", "command-input" })
        {
            foreach (var (game, campaign) in new[]
            {
                (NativeGame.Fallout3, "fallout-3"),
                (NativeGame.FalloutNewVegas, "fallout-new-vegas"),
            })
            {
                var options = new Dictionary<string, string>
                {
                    [route] = "true",
                    ["data-root"] = "selected-owned-installation",
                    ["campaign"] = campaign,
                    ["save-path"] = "isolated/player.json",
                    ["live-harness"] = Path.Combine(Path.GetTempPath(), "command-contract-commands"),
                };
                RuntimeLaunchValidator.ValidatePreflight(options);
                var request = RuntimeLaunchRequest.Create(options);
                RuntimeLaunchValidator.ValidateContent(options, request);
                RuntimeLaunchValidator.ValidateInstallation(options, new(game, options["data-root"], options["data-root"]));
                if (request.OpensLauncher || !request.Is(RuntimeLaunchRoute.LiveRetailFiles) ||
                    !RuntimeInputPresentationSelection.UsesCommandInput(options))
                    throw new InvalidOperationException("Explicit command input lost its selected campaign route.");

                foreach (var backend in new[] { "headless", "Windows" })
                {
                    if (route == "headless-play" && backend != "headless")
                        MustReject<ArgumentException>(() => RuntimeInputPresentationSelection.ValidateBackend(options, backend));
                    else
                        RuntimeInputPresentationSelection.ValidateBackend(options, backend);
                    if (RuntimeInputPresentationSelection.SelectInputProvider(true, backend) != RuntimeNativeInputProvider.CommandInput ||
                        RuntimeInputPresentationSelection.UsesHeadlessPresentation(backend) != (backend == "headless"))
                        throw new InvalidOperationException("Command provider selection changed the actual presentation backend.");
                }

                foreach (var required in new[] { "data-root", "campaign", "save-path", "live-harness" })
                {
                    var missing = new Dictionary<string, string>(options); missing.Remove(required);
                    MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(missing));
                    var empty = new Dictionary<string, string>(options) { [required] = " " };
                    MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(empty));
                }
                var launcher = new Dictionary<string, string>(options) { ["launcher"] = "true" };
                MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(launcher));
                var xr = new Dictionary<string, string>(options) { ["vr"] = "true" };
                if (route == "headless-play")
                    MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(xr));
                else
                {
                    // Launch policy permits the existing real XR initialization;
                    // this pure selection test supplies no runtime or pose proof.
                    RuntimeLaunchValidator.ValidatePreflight(xr);
                    RuntimeInputPresentationSelection.ValidateBackend(xr, "Windows");
                    if (!RuntimeInputPresentationSelection.UsesCommandInput(xr) ||
                        RuntimeInputPresentationSelection.UsesHeadlessPresentation("Windows"))
                        throw new InvalidOperationException("Rendered XR command input changed its selected input or presentation route.");
                }
                foreach (var unsupported in new[] { "fallout-1", "fallout-2", "unknown" })
                {
                    var invalid = new Dictionary<string, string>(options) { ["campaign"] = unsupported };
                    MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(invalid));
                }
                var relativeHarness = new Dictionary<string, string>(options) { ["live-harness"] = "relative-commands" };
                MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(relativeHarness));

                options["new-game"] = "true";
                RuntimeLaunchValidator.ValidatePreflight(options);
                options["continue-game"] = "true";
                MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(options));
                options.Remove("new-game");
                RuntimeLaunchValidator.ValidatePreflight(options);
                options.Remove(route);
                MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidatePreflight(options));
            }
        }

        var desktop = new Dictionary<string, string> { ["vr"] = "true" };
        RuntimeLaunchValidator.ValidatePreflight(desktop);
        if (RuntimeInputPresentationSelection.UsesCommandInput(desktop) ||
            RuntimeInputPresentationSelection.SelectInputProvider(false, "Windows") != RuntimeNativeInputProvider.WindowsDirectInput)
            throw new InvalidOperationException("Ordinary desktop/OpenXR input lost its actual Windows device route.");
        MustReject<NotSupportedException>(() => RuntimeInputPresentationSelection.SelectInputProvider(false, "headless"));

        var both = new Dictionary<string, string> { ["headless-play"] = "true", ["command-input"] = "true" };
        MustReject<ArgumentException>(() => RuntimeInputPresentationSelection.ValidateBackend(both, "Windows"));
    }

    private static void MustReject<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name} from invalid input/presentation selection.");
    }
}
