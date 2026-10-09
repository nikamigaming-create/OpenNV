using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginCampaign
{
    private sealed class CampaignCommandTable(FalloutNativePluginCampaign campaign) : NativeNvseCommandTableAuthority
    {
        internal override string SourceIdentity => campaign._source.StackId;
        internal override void RequireCurrent() => campaign.RequireCurrent();
        internal override IReadOnlyList<NativeNvseCommand> Commands()
        {
            RequireCurrent();
            return campaign._modules.Where(module => !module.Retired && module.Domain.Fault is null &&
                module.Plugin.Phase is NativeNvsePhase.Loading or NativeNvsePhase.LoadedTrue)
                .SelectMany(module => module.Plugin.Registry.Commands).ToArray();
        }
        internal override IReadOnlyList<NativeNvseCommandTableModule> LoadedModules()
        {
            RequireCurrent();
            return campaign._modules.Where(module => !module.Retired && module.Domain.Fault is null &&
                module.Plugin.Phase == NativeNvsePhase.LoadedTrue && module.Plugin.LoadReceipt?.Returned == true)
                .Select(module => new NativeNvseCommandTableModule(module.Plugin.Generation, module.Plugin.Module,
                    module.Plugin.Handle, module.Plugin.Path, module.Plugin.Sha256,
                    module.Plugin.LoadReceipt?.Info ?? throw new InvalidDataException("Loaded original module has no actual source PluginInfo identity."))).ToArray();
        }
    }
}
