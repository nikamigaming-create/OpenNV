using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Main's platform service has its own actual process/source lifetime. A loaded
// NVSE contributor cannot stand in for the original platform singleton.
internal sealed class NativePluginSteamCampaign(RuntimeLiveContentSource source, FalloutReferenceWorld world,
    string companion, string privateStateRoot, FalloutSourceLaunchArguments launchArguments) : IDisposable
{
    private NativePluginSteamSource? _source;
    private NativePluginPrivateIo? _io;
    private NativePluginExecutionDomain? _domain;
    private NativePluginSteamMainProvider? _provider;
    private IDisposable? _utilityBinding, _steamBinding;
    private bool _entered, _retired;
    private Exception? _failure;
    internal bool ChildExited => _domain?.ChildExited ?? _io?.FailedChildrenExited ?? true;
    internal bool Retired => _retired;
    internal object State => new
    {
        entered = _entered,
        retired = _retired,
        source = _source?.Declaration,
        provider = _provider?.State,
        generation = _domain?.Generation,
        process = _domain?.ProcessId,
        desktop = _domain?.Desktop,
        nativeFault = _domain?.Fault,
        sourceHash = _domain?.SteamSourceHashState,
        utilityBound = _utilityBinding is not null,
        steamBound = _steamBinding is not null,
        childExited = ChildExited,
        retainedConstruction = _io?.HasFailedChildConstruction,
        failure = _failure?.Message
    };
    internal void Bind()
    {
        if (_entered || _retired) throw new InvalidOperationException("Actual platform construction cannot replay its entered prefix.");
        _entered = true;
        try
        {
            _source = NativePluginSteamSource.Open(source, FalloutMainUtilitySource.Read(world.CampaignMainScriptSource));
            var image = NativePluginImageDeclarations.Read(_source.ProviderPath, true);
            var nonIo = image.Imports.Where(import => import.Name is { } name &&
                    NativePluginPlatformImports.Owner(import.Library, name) is not null)
                .Select(import => import.Library + "!" + import.Name).ToHashSet(StringComparer.Ordinal);
            // There are no invented configuration writes. The actual platform
            // reads its selected original provider under the same token/input
            // protections, and native OS calls retain their real outcomes.
            _io = FalloutNativePluginPrivateIo.Create(source, _source.ProviderPath, _source.Declaration.ProviderSha256,
                privateStateRoot, [], [], [_source.RuntimeDirectory], "actual-source-Main-platform:" + _source.Identity, nonIo);
            _domain = new(companion, privateIo: _io); _io = null;
            _provider = new(_domain, _source);
            _utilityBinding = world.BindMainUtilityPlatform(_provider);
            _steamBinding = world.BindCampaignSteamService(_provider);
            world.ExecuteSourcePlatformStartup(FalloutExecutableStringTable.ReadMainPlatformStartupArguments(
                source.FalloutExecutablePath, _source.Declaration.Main), launchArguments, _provider);
        }
        catch (Exception failure)
        {
            _domain ??= (failure as NativePluginDomainFaultException)?.Owner;
            _failure = failure; throw;
        }
    }
    internal void RequireIdleForSave()
    {
        if (!_entered || _retired || _provider is null || _utilityBinding is null || _steamBinding is null || _domain is null)
            throw new InvalidOperationException("Campaign save has no actual bound current platform service.");
        if (_failure is { } failure) throw new InvalidOperationException("Campaign platform retains its original failure prefix.", failure);
        if (!world.SourcePlatformStartupReturned || world.MainUtilityCommandSaveBlocker is { })
            throw new InvalidOperationException("Campaign platform has no returned actual source startup/command lifetime.");
        if (_domain.SteamSaveBlocker is not null || _provider.SourceServiceSaveBlocker is not null)
            throw new InvalidOperationException("Campaign platform has an entered or failed source child: " + (_domain.SteamSaveBlocker ?? _provider.SourceServiceSaveBlocker));
        // SDK pointers, account state and callback objects belong to this process.
        // Cold constructs a fresh source-bound platform; it never restores them
        // as saved native memory or replays the previous singleton constructor.
    }
    public void Dispose()
    {
        if (_retired) return;
        // The product drains Main before closing either binding. Each binding
        // independently refuses an entered shared caller and remains retained.
        _steamBinding?.Dispose(); _steamBinding = null;
        _utilityBinding?.Dispose(); _utilityBinding = null;
        var errors = new List<Exception>();
        try { _provider?.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        try { _domain?.Dispose(); }
        catch (Exception error) { errors.Add(error); }
        if (_domain is not null && !_domain.ChildExited)
        {
            errors.Add(new InvalidOperationException("Actual platform child has not exited; its original capabilities remain retained."));
            throw new AggregateException("Actual platform still owns its native process/source lifetime.", errors);
        }
        // A failed source destructor is never replayed as orderly retirement.
        // Only the exact failed child's observed exit retires that address space.
        if (_domain?.Fault is not null && _domain.ChildExited)
        {
            try { _provider?.Dispose(); }
            catch (Exception error) { errors.Add(error); }
        }
        try { _io?.Dispose(); _io = null; }
        catch (Exception error) { errors.Add(error); }
        try { _source?.Dispose(); _source = null; }
        catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Actual platform retirement retains independent source/native failures.", errors);
        _retired = true;
    }
}
