using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginCampaign
{
    private void BindNativeWindowProcess(NativePluginExecutionDomain domain)
    {
        RequireCurrent();
        var input = _dataBindings?.Input ?? throw new NotSupportedException("Original window/process imports lack the actual product-window input lifetime.");
        if (!input.Source.Equals(_source.StackId, StringComparison.OrdinalIgnoreCase) || !ReferenceEquals(input.Controls, _scripts.Controls))
            throw new InvalidDataException("Native window producer belongs to another source/control authority.");
        domain.BindProductWindow(input);
    }
    private void RequireNativeWindowProcessColdCapture()
    {
        RequireCurrent(); foreach (var module in _modules) module.Domain.RequireWindowProcessColdCapture();
    }
    internal object WindowProcessDiagnostics => new
    {
        Source = _source.StackId,
        Modules = _modules.Select(module => new
        {
            module.Domain.Generation,
            module.Plugin.Module,
            WindowSdkCalls = module.Domain.NvseWindowReceipts,
            ProcessSdkCalls = module.Domain.NvseProcessReceipts
        }).ToArray()
    };
}
