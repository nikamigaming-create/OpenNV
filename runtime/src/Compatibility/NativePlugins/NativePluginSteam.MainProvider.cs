using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// The real platform provider executes in its retained x86 child. Managed
// interface wrappers never contain native pointers or manufacture null/false.
// Every borrowed query token retires at the actual entered Main child boundary.
internal sealed partial class NativePluginSteamMainProvider : IFalloutMainUtilityPlatform, IDisposable
{
    private readonly NativePluginExecutionDomain _domain;
    private readonly NativePluginSteamSource _source;
    private readonly List<NativePluginSteamToken> _queries = [];
    private FalloutMainScriptInvocation? _active;
    private bool _disposed, _terminal;
    private Exception? _failure;
    internal NativePluginSteamMainProvider(NativePluginExecutionDomain actualRestrictedDomain, NativePluginSteamSource source)
    {
        _domain = actualRestrictedDomain; _source = source;
        _domain.LoadSteamProvider(source);
    }
    public FalloutMainUtilitySource Source => _source.Declaration.Main;
    public string Owner => "actual-selected-x86-platform/" + _source.Identity + "/" + _domain.Generation;
    internal object State => new
    {
        source = _source.Declaration,
        sourceIdentity = _source.Identity,
        application = _source.Application,
        owner = Owner,
        nativeGeneration = _domain.Generation,
        nativePhase = _domain.SteamPhase,
        calls = _domain.SteamReceipts,
        enteredMainChild = _active?.Identity,
        queryLeases = _queries.Count,
        failure = _failure?.Message,
        nativeFailurePrefix = _domain.SteamNativeFault,
        retired = _disposed,
        terminalRetirement = _terminal,
        originalCallbackProjection = SourceServiceState,
        nativeCallbackState = _domain.SteamSourceCallbackState
    };
    internal IDisposable EnterMainUtility(FalloutMainScriptInvocation invocation)
    {
        RequireIdle(); invocation.Require(FalloutMainScriptCallerStep.Prologue);
        // Original direct utility queries can precede the source singleton getter.
        if (_domain.SteamPhase is not (NativePluginSteamPhase.Loaded or NativePluginSteamPhase.InitializeReturned))
            throw new InvalidOperationException("Actual source utility query followed an entered/retired provider.");
        if (invocation.Owner.MainUtilitySource != Source) throw new InvalidDataException("Platform utility entered another actual Main source.");
        _active = invocation; return new QueryScope(this, invocation);
    }
    public bool IsSteamRunning() { RequireUtility(); return Call(_domain.SteamIsRunning); }
    public IFalloutMainUtilityUser? SteamUser()
    { RequireUtility(); var token = Call(() => _domain.QuerySteamInterface(NativePluginSteamInterface.User)); if (token is null) return null; _queries.Add(token); return new User(this, token); }
    public IFalloutMainUtilityStatistics? SteamUserStats()
    { RequireUtility(); var token = Call(() => _domain.QuerySteamInterface(NativePluginSteamInterface.Statistics)); if (token is null) return null; _queries.Add(token); return new Statistics(this, token); }
    internal void PumpActualCallbacks(FalloutMainScriptInvocation invocation) => MainCallbacks(invocation);
    private void RequireIdle()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is { } retained) throw new InvalidOperationException("Actual platform retains its attempted failure prefix.", retained);
        if (_active is not null || _queries.Count != 0) throw new InvalidOperationException("Actual Main child still owns platform query leases.");
    }
    private void RequireUtility()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is { } retained) throw new InvalidOperationException("Actual platform query retains its attempted failed prefix.", retained);
        (_active ?? throw new InvalidOperationException("Platform query has no actual entered Main Prologue.")).Require(FalloutMainScriptCallerStep.Prologue);
    }
    private T Call<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception failure) { _failure = failure; throw; }
    }
    private sealed class User(NativePluginSteamMainProvider provider, NativePluginSteamToken token) : IFalloutMainUtilityUser
    {
        public string Owner => provider.Owner + "/user/" + token.Token;
        public bool BLoggedOn() { provider.RequireUtility(); return provider.Call(() => provider._domain.SteamLoggedOn(token)); }
    }
    private sealed class Statistics(NativePluginSteamMainProvider provider, NativePluginSteamToken token) : IFalloutMainUtilityStatistics
    {
        public string Owner => provider.Owner + "/statistics/" + token.Token;
        public bool SetAchievement(string actualSourceIdentifier)
        { provider.RequireUtility(); return provider.Call(() => provider._domain.SteamSetAchievement(token, actualSourceIdentifier)); }
        public bool StoreStats() { provider.RequireUtility(); return provider.Call(() => provider._domain.SteamStoreStatistics(token)); }
    }
    private sealed class QueryScope(NativePluginSteamMainProvider provider, FalloutMainScriptInvocation invocation) : IDisposable
    {
        private bool _closed;
        public void Dispose()
        {
            if (_closed) return;
            if (!ReferenceEquals(provider._active, invocation)) throw new InvalidOperationException("Actual platform query scope lost its Main child.");
            if (provider._domain.ChildExited && provider._domain.Fault is not null)
            {
                // Exact abnormal child exit retires its address space and all
                // native borrowed pointers. It supplies no ReleaseInterface,
                // Shutdown, DLL-detach or ordinary Main return receipt.
                provider._domain.RetireSteamAfterFaultedChildExit();
                provider._queries.Clear(); provider._active = null; provider._terminal = true; _closed = true; return;
            }
            var failures = new List<Exception>();
            for (var index = provider._queries.Count - 1; index >= 0; index--)
            {
                try { provider._domain.ReleaseSteamInterface(provider._queries[index]); provider._queries.RemoveAt(index); }
                catch (Exception failure)
                {
                    failures.Add(failure); provider._failure ??= failure;
                    if (provider._domain.ChildExited && provider._domain.Fault is not null)
                    {
                        try { provider._domain.RetireSteamAfterFaultedChildExit(); provider._queries.Clear(); provider._active = null; provider._terminal = true; _closed = true; }
                        catch (Exception cleanup) { failures.Add(cleanup); }
                    }
                    // A failed native prefix cannot issue another Release.
                    break;
                }
            }
            if (failures.Count != 0)
            { var failure = new AggregateException("Actual source query leases failed retirement.", failures); provider._failure ??= failure; throw failure; }
            provider._active = null; _closed = true;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_domain.ChildExited && _domain.Fault is not null)
        {
            _domain.RetireSteamAfterFaultedChildExit(); _queries.Clear(); _active = null;
            _terminal = true; _disposed = true; return;
        }
        if (_active is not null || _queries.Count != 0) throw new InvalidOperationException("Actual platform entered child must close before source shutdown.");
        if (_failure is { } retained) throw new InvalidOperationException("Failed platform requires exact-child terminal cleanup, not an invented orderly shutdown.", retained);
        RetireSourceService(); _disposed = true;
    }
}
