namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal IDisposable BindCampaignSteamService(IFalloutMainSteamService service) => ProcessRuntime.BindMainSteamService(service);
}
