using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

internal sealed partial class NativePluginSteamMainProvider : IFalloutMainSteamService, IFalloutMainUtilityInvocationScope, IFalloutPlatformStartupConsumer
{
    private NativePluginSteamStartup? _startup;
    FalloutMainSteamServiceSource IFalloutMainSteamService.Source => FalloutMainSteamServiceSource.Read(Source.Main);
    internal object? SourceServiceState => _startup?.State;
    internal string? SourceServiceSaveBlocker => _startup?.SaveBlocker;
    IDisposable IFalloutMainUtilityInvocationScope.EnterMainUtility(FalloutMainScriptInvocation invocation) => EnterMainUtility(invocation);
    public void MainCallbacks(FalloutMainScriptInvocation invocation)
    {
        RequireIdle(); invocation.Require(FalloutMainScriptCallerStep.SteamCallbacks);
        if (invocation.Owner.MainUtilitySource != Source) throw new InvalidDataException("Steam child changed its actual Main source.");
        _startup ??= new NativePluginSteamStartup(_domain, NativePluginSteamStartupDeclaration.Read(_source.Declaration));
        try { _startup.MainCallbacks(invocation); }
        catch (Exception failure) { _failure ??= failure; throw; }
    }
    public bool QuerySourceInitialized(FalloutPlatformStartupInvocation invocation)
    {
        RequireIdle(); invocation.Require(FalloutPlatformStartupStep.InitializationQuery);
        if (invocation.Source.Main != Source) throw new InvalidDataException("Early platform query changed its actual selected Main source.");
        _startup ??= new NativePluginSteamStartup(_domain, NativePluginSteamStartupDeclaration.Read(_source.Declaration));
        try { return _startup.QuerySourceInitialized(invocation); }
        catch (Exception failure) { _failure ??= failure; throw; }
    }
    public void WriteSourceIndependentByte(FalloutPlatformStartupInvocation invocation, byte value)
    {
        RequireIdle(); invocation.Require(FalloutPlatformStartupStep.IndependentByteWrite);
        if (invocation.Source.Main != Source) throw new InvalidDataException("Early platform setter changed its actual selected Main source.");
        _startup ??= new NativePluginSteamStartup(_domain, NativePluginSteamStartupDeclaration.Read(_source.Declaration));
        try { _startup.WriteSourceIndependentByte(invocation, value); }
        catch (Exception failure) { _failure ??= failure; throw; }
    }
    private void RetireSourceService()
    {
        if (_startup is { Constructed: true } service) service.Retire();
        // An unconstructed source singleton has no source Shutdown call.
        // Unload only retires our genuine loader reference in this branch.
        _domain.UnloadSteamProvider();
    }
}
