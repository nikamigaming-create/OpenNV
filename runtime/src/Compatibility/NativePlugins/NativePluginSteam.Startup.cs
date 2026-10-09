using System.Buffers.Binary;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// This is the original service's C# gameplay owner. Its native callback members
// are independent first-party SDK ABI objects, not a fabricated original game
// object or a source event inferred from an exported pump returning.
internal sealed partial class NativePluginSteamStartup(NativePluginExecutionDomain domain,
    NativePluginSteamStartupDeclaration source)
{
    private NativePluginSteamStartupDeclaration SourceDeclaration => source;
    private readonly List<NativePluginSteamServiceEffect> _effects = [];
    private readonly List<NativePluginSteamSourceCallbackEffect> _callbacks = [];
    private readonly List<NativePluginSteamCallbackToken> _registrations = [];
    private NativePluginSteamServicePhase _phase;
    private IDisposable? _handlerLease;
    private FalloutMainScriptInvocation? _entered;
    private Exception? _failure;
    private long _sequence;
    private bool _fieldsInitialized, _initialized, _statisticsReady, _independentByte;
    private uint _application;
    internal bool Constructed => _phase != NativePluginSteamServicePhase.Unconstructed;
    internal string? SaveBlocker => _failure?.Message ?? (_entered is not null ? "source-platform-service-child-entered" :
        _sourceEntered is not null ? "source-platform-early-startup-entered" : null);
    internal object State => new
    {
        source,
        sourceIdentity = source.Identity,
        phase = _phase,
        fieldsInitialized = _fieldsInitialized,
        initialized = _initialized,
        statisticsReady = _statisticsReady,
        independentConstructorByte = _independentByte,
        application = _application,
        registrations = _registrations,
        effects = _effects.AsReadOnly(),
        callbacks = _callbacks.AsReadOnly(),
        enteredMain = _entered?.Identity,
        enteredSourceCaller = _sourceEntered?.Identity,
        failure = _failure?.ToString(),
        coldNativeQueue = "not-a-saved-game-owner"
    };
    internal void MainCallbacks(FalloutMainScriptInvocation invocation)
    {
        RequireInvocation(invocation);
        if (_failure is { } failure) throw new InvalidOperationException("Source platform service retains its failed prefix.", failure);
        if (_entered is not null || _sourceEntered is not null || _phase is NativePluginSteamServicePhase.RetirementEntered or NativePluginSteamServicePhase.Retired)
            throw new InvalidOperationException("Source platform service has another entered child or retirement owner.");
        _entered = invocation;
        try
        {
            if (!Constructed) Construct();
            Effect(NativePluginSteamServiceStep.Pump, domain.PumpSteamCallbacks);
        }
        catch (Exception error) { _failure ??= error; _phase = NativePluginSteamServicePhase.Failed; throw; }
        finally { _entered = null; }
    }
    private void RequireInvocation(FalloutMainScriptInvocation invocation)
    {
        invocation.Require(FalloutMainScriptCallerStep.SteamCallbacks);
        if (invocation.Owner.MainUtilitySource != source.Provider.Main)
            throw new InvalidDataException("Original Steam singleton getter entered another genuine Main source.");
    }
    private void Construct()
    {
        source.Validate(); _phase = NativePluginSteamServicePhase.ConstructorEntered;
        _handlerLease = domain.BindSteamSourceCallbacks(HandleCallback);
        Register(NativePluginSteamServiceStep.RegisterReceived, 1101);
        Register(NativePluginSteamServiceStep.RegisterStored, 1102);
        Register(NativePluginSteamServiceStep.RegisterAchievement, 1103);
        // The original field initialization is after the three member
        // constructors. An earlier callback cannot borrow CLR zeroed fields.
        Effect(NativePluginSteamServiceStep.InitializeFields, () =>
        { _initialized = false; _statisticsReady = false; _independentByte = true; _application = 0; _fieldsInitialized = true; });
        var initialized = false;
        Effect(NativePluginSteamServiceStep.Initialize, () => initialized = domain.InitializeSteamProvider(), () => initialized ? 1U : 0U);
        Effect(NativePluginSteamServiceStep.StoreInitialized, () => _initialized = initialized, () => _initialized ? 1U : 0U);
        if (_initialized)
        {
            var loggedOn = Login();
            WithQueries(queries =>
            {
                NativePluginSteamToken? utils = null;
                Effect(NativePluginSteamServiceStep.UtilsTest, () => utils = Query(queries, NativePluginSteamInterface.Utilities), () => utils is null ? 0U : 1U);
                if (utils is null) return;
                Effect(NativePluginSteamServiceStep.UtilsQuery, () => utils = Query(queries, NativePluginSteamInterface.Utilities), () => utils is null ? 0U : 1U);
                Effect(NativePluginSteamServiceStep.ApplicationId, () => _application = domain.SteamApplicationId(
                    utils ?? throw new InvalidDataException("Original fresh Utils query disappeared before its dereference.")), () => _application);
                // First-party source admission follows the original scalar
                // store, retaining that genuine commit if admission refuses.
                Effect(NativePluginSteamServiceStep.SourceApplicationAdmission, () => domain.ConfirmSteamSourceApplication(_application));
            });
            if (loggedOn) WithQueries(queries =>
            {
                NativePluginSteamToken? statistics = null;
                Effect(NativePluginSteamServiceStep.StatisticsTest, () => statistics = Query(queries, NativePluginSteamInterface.Statistics), () => statistics is null ? 0U : 1U);
                if (statistics is null) return;
                Effect(NativePluginSteamServiceStep.StatisticsQuery, () => statistics = Query(queries, NativePluginSteamInterface.Statistics), () => statistics is null ? 0U : 1U);
                var requested = false;
                Effect(NativePluginSteamServiceStep.RequestCurrentStatistics, () => requested = domain.SteamRequestCurrentStatistics(
                    statistics ?? throw new InvalidDataException("Original fresh statistics query disappeared before its dereference.")), () => requested ? 1U : 0U);
                // A returned RequestCurrentStats bool is not a received event.
            });
        }
        Effect(NativePluginSteamServiceStep.ConstructorReturn, () => _phase = NativePluginSteamServicePhase.Live);
    }
    private bool Login()
    {
        var loggedOn = false;
        WithQueries(queries =>
        {
            NativePluginSteamToken? user = null;
            Effect(NativePluginSteamServiceStep.LoginUserTest, () => user = Query(queries, NativePluginSteamInterface.User), () => user is null ? 0U : 1U);
            if (user is null) return;
            Effect(NativePluginSteamServiceStep.LoginUserQuery, () => user = Query(queries, NativePluginSteamInterface.User), () => user is null ? 0U : 1U);
            Effect(NativePluginSteamServiceStep.LoggedOn, () => loggedOn = domain.SteamLoggedOn(
                user ?? throw new InvalidDataException("Original fresh User query disappeared before its dereference.")), () => loggedOn ? 1U : 0U);
        });
        return loggedOn;
    }
    private void Register(NativePluginSteamServiceStep step, uint id) =>
        Effect(step, () => _registrations.Add(domain.RegisterSteamSourceCallback(id)), () => _registrations[^1].Flags);
    private NativePluginSteamToken? Query(List<NativePluginSteamToken> retained, NativePluginSteamInterface kind)
    {
        var value = domain.QuerySteamInterface(kind);
        if (value is not null) retained.Add(value);
        return value;
    }
    private void WithQueries(Action<List<NativePluginSteamToken>> body)
    {
        var retained = new List<NativePluginSteamToken>(); Exception? original = null;
        try { body(retained); }
        catch (Exception failure) { original = failure; throw; }
        finally
        {
            if (domain.ChildExited && domain.Fault is not null) domain.RetireSteamAfterFaultedChildExit();
            else
            {
                var failures = new List<Exception>();
                for (var index = retained.Count - 1; index >= 0; index--)
                {
                    try { domain.ReleaseSteamInterface(retained[index]); }
                    catch (Exception cleanup) { failures.Add(cleanup); break; }
                }
                if (failures.Count != 0)
                {
                    if (original is not null) failures.Insert(0, original);
                    throw new AggregateException("Source constructor query scope retained its execution/retirement failure.", failures);
                }
            }
        }
    }
    private void HandleCallback(NativePluginSteamCallbackDelivery delivery)
    {
        var index = _callbacks.Count;
        _callbacks.Add(new(Next(), null, delivery, _statisticsReady, _statisticsReady, false, null, null));
        try
        {
            if (_failure is not null ||
                _phase is not (NativePluginSteamServicePhase.ConstructorEntered or NativePluginSteamServicePhase.Live or NativePluginSteamServicePhase.RetirementEntered))
                throw new NotSupportedException("Actual platform callback has no current source receiver.");
            var matched = false;
            if (delivery.CallbackId == 1101)
            {
                if (!_fieldsInitialized)
                    throw new NotSupportedException("Received-statistics callback preceded its actual source field initialization.");
                // Original comparison includes the high GameID word; a
                // low-word match does not own a source statistics-ready write.
                matched = BinaryPrimitives.ReadUInt64LittleEndian(delivery.Payload.Span) == _application;
                if (matched) _statisticsReady = BinaryPrimitives.ReadInt32LittleEndian(delivery.Payload.Span[8..]) == 1;
            }
            else if (delivery.CallbackId is not (1102 or 1103)) throw new InvalidDataException("Source service received another callback family.");
            // Both overloads invoke this same source handler. IOFailure and
            // APICall are genuine observed inputs, ignored by the original.
            _callbacks[index] = _callbacks[index] with { Returned = Next(), StatisticsReadyAfter = _statisticsReady, ApplicationMatched = matched };
        }
        catch (Exception failure)
        {
            _callbacks[index] = _callbacks[index] with { StatisticsReadyAfter = _statisticsReady, FailureType = failure.GetType().FullName, Error = failure.Message };
            _failure ??= failure; _phase = NativePluginSteamServicePhase.Failed; throw;
        }
    }
    private void Effect(NativePluginSteamServiceStep step, Action action, Func<uint>? result = null)
    {
        var index = _effects.Count;
        _effects.Add(new(Next(), null, step, _entered?.Identity, null, null, null, _sourceEntered?.Identity));
        try { action(); _effects[index] = _effects[index] with { Returned = Next(), Result = result?.Invoke() }; }
        catch (Exception failure)
        { _effects[index] = _effects[index] with { FailureType = failure.GetType().FullName, Error = failure.Message }; _failure ??= failure; throw; }
    }
    private long Next() => _sequence = checked(_sequence + 1);
    internal void Retire()
    {
        if (_phase == NativePluginSteamServicePhase.Retired) return;
        if (_entered is not null || _sourceEntered is not null || _failure is not null || !Constructed)
            throw new InvalidOperationException("Entered/failed/absent source service cannot invent an orderly destructor.", _failure);
        _phase = NativePluginSteamServicePhase.RetirementEntered;
        try
        {
            Effect(NativePluginSteamServiceStep.Shutdown, domain.ShutdownSteamProvider);
            foreach (var (id, step) in new[] { (1103U, NativePluginSteamServiceStep.UnregisterAchievement),
                (1102U, NativePluginSteamServiceStep.UnregisterStored), (1101U, NativePluginSteamServiceStep.UnregisterReceived) })
            {
                var value = _registrations.Single(registration => registration.CallbackId == id);
                Effect(step, () => { domain.RetireSteamSourceCallback(value); _registrations.Remove(value); });
            }
            Effect(NativePluginSteamServiceStep.HandlerLeaseRetire, () => { _handlerLease!.Dispose(); _handlerLease = null; });
            _phase = NativePluginSteamServicePhase.Retired;
        }
        catch (Exception failure) { _failure ??= failure; _phase = NativePluginSteamServicePhase.Failed; throw; }
    }
}
