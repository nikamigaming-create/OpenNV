using OpenNV.Runtime;
using OpenNV.Runtime.Content;

if (!RuntimeLaunchRequest.Create(new Dictionary<string, string>()).OpensLauncher ||
    !RuntimeLaunchRequest.Create(new Dictionary<string, string> { ["launcher"] = "true" }).OpensLauncher ||
    RuntimeLaunchRequest.Create(new Dictionary<string, string> { ["xr-rig-proof"] = "true" }).OpensLauncher ||
    RuntimeLaunchRequest.Create(new Dictionary<string, string> { ["campaign"] = "fallout-3" }).OpensLauncher)
    throw new InvalidOperationException("Ordinary executable startup must open the picker; incomplete explicit requests must retain their validation.");

var campaigns = new Dictionary<NativeGame, string>
{
    [NativeGame.Fallout1] = "fallout-1",
    [NativeGame.Fallout2] = "fallout-2",
    [NativeGame.Fallout3] = "fallout-3",
    [NativeGame.FalloutNewVegas] = "fallout-new-vegas",
};
foreach (var (game, campaign) in campaigns)
{
    var options = new Dictionary<string, string>
    {
        ["data-root"] = "selected-owned-installation",
        ["campaign"] = campaign,
        ["save-path"] = $"isolated/{campaign}/player.json",
    };
    var installation = new NativeGameInstallation(game, options["data-root"], options["data-root"]);
    var request = RuntimeLaunchRequest.Create(options);
    if (request.OpensLauncher) throw new InvalidOperationException("An explicit owned installation was replaced by the launcher.");
    RuntimeLaunchValidator.ValidateContent(options, request);
    RuntimeLaunchValidator.ValidateInstallation(options, installation);
    foreach (var other in campaigns.Values.Where(value => value != campaign))
    {
        options["campaign"] = other;
        MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidateInstallation(options, installation));
    }
    options["campaign"] = campaign;
    if (game is not (NativeGame.Fallout1 or NativeGame.Fallout2))
        continue;
    foreach (var mode in new[] { "first-person", "openxr", "unknown" })
    {
        options["presentation"] = mode;
        MustReject<NotSupportedException>(() => RuntimeLaunchValidator.ValidateInstallation(options, installation));
    }
    options["presentation"] = "hex-tactical";
    RuntimeLaunchValidator.ValidateInstallation(options, installation);
    options["vr"] = "true";
    MustReject<NotSupportedException>(() => RuntimeLaunchValidator.ValidateInstallation(options, installation));
    options.Remove("vr");
    options["fo1-start-presentation"] = "first-person";
    MustReject<ArgumentException>(() => RuntimeLaunchValidator.ValidateInstallation(options, installation));
}
InputPresentationContracts.Run();
Console.WriteLine("OPENNV_RUNTIME_LAUNCH_CONTRACT_PASS campaigns=4 wrongCampaign=rejected unsupportedViews=rejected headlessSelection=true commandSelection=true rendererIndependent=true");

static void MustReject<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(TException).Name} from invalid launch selection.");
}
