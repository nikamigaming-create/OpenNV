using OpenNV.Runtime.Compatibility.NativePlugins;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginCampaign
{
    private NativePluginExecutionDomain? _sharedDomain;
    private bool _sharedDomainAttempted;
    private string? _sharedDomainFailure;

    private void RequireSupplementalConstruction(FalloutNativePluginModuleAdmission admission)
    {
        var source = NativePluginImageDeclarations.Read(admission.PhysicalPath, dll: true);
        if (!source.Sha256.Equals(admission.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Additional selected module changed its original source declaration.");
        NativePluginCoResidentImports.Require(source);
        if (admission.Heap is not null || admission.SourceFiles is not null || admission.ExpressionAbi is not null)
            throw new NotSupportedException("Additional original module requires its own heap/source-file/expression constructor owner; the primary module cannot substitute.");
    }
    private void RetainSharedDomainFailure(NativePluginExecutionDomain? domain, string failure)
    {
        if (domain is not null) _sharedDomain ??= domain;
        _sharedDomainFailure ??= failure;
        foreach (var module in _modules)
            if (ReferenceEquals(module.Domain, domain))
                module.ObjectFailure ??= "Shared original-module process lost its current execution lifetime: " + failure;
    }
    private NativePluginExecutionDomain[] ActualDomains()
    {
        var domains = _modules.Select(module => module.Domain).Concat(_retiringDomains);
        if (_sharedDomain is { } shared) domains = domains.Append(shared);
        return domains.Distinct().ToArray();
    }

    internal object ModuleSetDiagnostics => new
    {
        Source = _source.StackId,
        SharedDomainAttempted = _sharedDomainAttempted,
        SharedDomainFailure = _sharedDomainFailure,
        Domains = ActualDomains().Select(domain => new
        {
            domain.Generation,
            domain.ProcessId,
            domain.ChildExited,
            domain.NaturallyRetired,
            domain.ResourcesRetired,
            Modules = domain.NvseModules.Select(module => new
            {
                module.Module,
                module.Handle,
                module.Path,
                module.Sha256,
                Phase = module.Phase.ToString(),
                module.QueryReceipt,
                module.LoadReceipt,
                module.Retirement
            }).ToArray()
        }).ToArray()
    };
}
