namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private IFalloutMainSteamService? _steamService;
    private Guid _steamServiceLease;
    internal IDisposable BindMainSteamService(IFalloutMainSteamService service)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(service); service.Source.Validate();
        if (_steamServiceLease != Guid.Empty || service.Source.Main != MainScriptCallerSource() || string.IsNullOrWhiteSpace(service.Owner))
            throw new InvalidDataException("Main Steam child requires one actual selected source service lifetime.");
        _steamService = service; var lease = _steamServiceLease = Guid.NewGuid(); return new SteamServiceLease(this, lease);
    }
    internal void ExecuteMainSteamCallbacks(FalloutMainScriptInvocation invocation)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.SteamCallbacks);
        if (_steamServiceLease == Guid.Empty || _steamService is not { } service)
            throw new NotSupportedException("source-Main-Steam-service-constructor-and-callback-producer-unbound");
        service.MainCallbacks(invocation);
    }
    internal IDisposable EnterBoundMainUtilityScope(FalloutMainScriptInvocation invocation)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.Prologue);
        if (_utilityPlatformLease == Guid.Empty || _utilityPlatform is not IFalloutMainUtilityInvocationScope scoped)
            throw new NotSupportedException("source-Main-platform-utility-current-native-invocation-scope-unbound");
        return scoped.EnterMainUtility(invocation);
    }
    private sealed class SteamServiceLease(FalloutActorProcessRuntimeState owner, Guid lease) : IDisposable
    {
        public void Dispose()
        {
            if (owner._steamServiceLease != lease) return;
            owner.RequireNotBusy(); owner._steamServiceLease = Guid.Empty; owner._steamService = null;
        }
    }
}
