using OpenNV.Runtime;
using OpenNV.Runtime.Content;

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
    RuntimeLaunchValidator.ValidateContent(options, RuntimeLaunchRequest.Create(options));
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
Console.WriteLine("OPENNV_RUNTIME_LAUNCH_CONTRACT_PASS campaigns=4 wrongCampaign=rejected unsupportedViews=rejected");

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
