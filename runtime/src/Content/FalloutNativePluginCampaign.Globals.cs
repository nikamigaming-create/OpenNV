namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginCampaign
{
    private void PrepareEngineLoaderSource(Module module, string companion)
    {
        // The actual selected native module is loaded, and no original message
        // is active. Publish only the executable's real loader-declared TLS
        // index/template. This does not construct a TES, SceneGraph, player,
        // DataHandler, task queue or a current script/event context.
        _ = module.Domain.PrepareNvseEngineStaticTls(module.Plugin, companion);
        _ = module.Domain.PublishNvseEngineTlsIndex(module.Plugin);
    }
}
