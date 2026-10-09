using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal const string MainUtilitySchema = "opennv-source-Main-platform-utilities/v1";
    private bool _utilityConstructed, _utilityAchievementsConstructed, _utilityLoginConstructed, _utilityThirdConstructed;
    private bool _utilityLoggedOn;
    private long _utilityLoginChanged, _utilityRequests, _utilityClearedThrough, _utilityCalls, _utilityFaults, _utilityRegistrations;
    private readonly List<FalloutPlatformAchievementRequest> _utilityPending = [];
    private IReadOnlyList<FalloutPlatformAchievementRequest> _utilityLastCleared = [];
    private FalloutMainUtilityCall? _utilityFrame, _utilityCommand, _utilityActive, _utilityCallbackAttempt;
    private FalloutMainUtilityCallbackReceipt? _utilityCallbackReceipt;
    private Action<int, bool>? _utilityCallback;
    private IFalloutMainUtilityPlatform? _utilityPlatform;
    private IFalloutMainUtilityCommandHost? _utilityCommandHost;
    private Guid _utilityPlatformLease, _utilityCommandLease;
    internal FalloutMainUtilitySource MainUtilitySource => FalloutMainUtilitySource.Read(MainScriptCallerSource());
    internal string? MainUtilitySaveBlocker => _utilityActive is not null ? "source-Main-platform-utility-child-entered" :
        _utilityCallback is not null ? "source-Main-platform-login-opaque-callback" :
        _utilityFrame?.Error is { } frame ? "source-Main-platform-utility:" + frame :
        _utilityCommand?.Error is { } command ? "source-Main-platform-command:" + command :
        _utilityCallbackAttempt?.Error is { } callback ? "source-Main-platform-callback:" + callback : null;
    internal object MainUtilityState => new { source = MainUtilitySource, constructed = _utilityConstructed,
        achievementsConstructed = _utilityAchievementsConstructed, loginConstructed = _utilityLoginConstructed,
        thirdConstructed = _utilityThirdConstructed, loggedOn = _utilityLoggedOn, loginChanged = _utilityLoginChanged,
        requests = _utilityRequests, clearedThrough = _utilityClearedThrough, pending = _utilityPending,
        lastCleared = _utilityLastCleared, calls = _utilityCalls, lastFrame = _utilityFrame, lastCommand = _utilityCommand,
        callback = _utilityCallbackReceipt, registrations = _utilityRegistrations, lastCallbackAttempt = _utilityCallbackAttempt,
        platformBound = _utilityPlatformLease != Guid.Empty,
        commandHostBound = _utilityCommandLease != Guid.Empty, blocker = MainUtilitySaveBlocker };

    internal IDisposable BindMainUtilityPlatform(IFalloutMainUtilityPlatform platform)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(platform);
        if (_utilityPlatformLease != Guid.Empty || platform.Source != MainUtilitySource || string.IsNullOrWhiteSpace(platform.Owner) || _utilityActive is not null)
            throw new InvalidDataException("Main platform utility requires one genuine selected interface lifetime.");
        _utilityPlatform = platform; var lease = _utilityPlatformLease = Guid.NewGuid(); return new MainUtilityLease(this, lease, false);
    }
    internal IDisposable BindMainUtilityCommandHost(IFalloutMainUtilityCommandHost commands)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(commands);
        if (_utilityCommandLease != Guid.Empty || commands.Source != MainUtilitySource || string.IsNullOrWhiteSpace(commands.Owner) || _utilityActive is not null)
            throw new InvalidDataException("Original achievement suppression/console owner changed its source lifetime.");
        _utilityCommandHost = commands; var lease = _utilityCommandLease = Guid.NewGuid(); return new MainUtilityLease(this, lease, true);
    }
    private sealed class MainUtilityLease(FalloutActorProcessRuntimeState owner, Guid lease, bool commands) : IDisposable
    {
        public void Dispose()
        {
            if (commands ? owner._utilityCommandLease != lease : owner._utilityPlatformLease != lease) return;
            if (owner._utilityActive is not null)
            { owner.FaultMainUtility(); throw new InvalidOperationException("An entered platform utility cannot retire its actual provider."); }
            if (commands) { owner._utilityCommandLease = Guid.Empty; owner._utilityCommandHost = null; }
            else { owner._utilityPlatformLease = Guid.Empty; owner._utilityPlatform = null; }
        }
    }
    private IFalloutMainUtilityPlatform UtilityPlatform() => _utilityPlatformLease != Guid.Empty && _utilityPlatform is { } platform ? platform :
        throw new NotSupportedException("source-Main-SteamAPI_IsSteamRunning-SteamUser-SteamUserStats-provider-unbound");
    private IFalloutMainUtilityCommandHost UtilityCommands() => _utilityCommandLease != Guid.Empty && _utilityCommandHost is { } commands ? commands :
        throw new NotSupportedException("source-Main-original-achievement-suppression-byte-and-console-producer-unbound");
    private void FaultMainUtility()
    { _utilityFaults = checked(_utilityFaults + 1); if (_scriptCallerInvocation is not null) FaultMainScriptReentry(); }

    internal void ExecuteMainUtilities(FalloutMainScriptInvocation invocation)
    {
        RequireNotBusy(); RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.Prologue);
        if (_utilityFrame?.Error is not null || _utilityFrame?.MainInvocation == invocation.Identity)
            throw new InvalidOperationException("Main utilities retain their attempted child prefix and cannot replay it.");
        if (_utilityActive is not null) { FaultMainUtility(); throw new InvalidOperationException("A utility callback cannot recursively invoke the original Main utility frame."); }
        _utilityCalls = checked(_utilityCalls + 1);
        BeginUtility(invocation.Identity, invocation.Ordinal);
        try
        {
            Construct(FalloutMainUtilityStep.ConstructUtility, ref _utilityConstructed);
            Construct(FalloutMainUtilityStep.ConstructAchievements, ref _utilityAchievementsConstructed);
            // Actual source queue emptiness bypasses all user-statistics calls.
            if (_utilityPending.Count != 0)
            {
                for (var index = 0; index < _utilityPending.Count; index++)
                {
                    var request = _utilityPending[index];
                    if (request.PayloadRetired is not null) throw new InvalidOperationException("A retired achievement payload cannot be dereferenced or replayed.");
                    Effect(FalloutMainUtilityStep.RetirePayload, () =>
                    { _utilityPending[index] = request with { PayloadRetired = Next() }; }, request.Request);
                    if (request.Id > 100)
                        Effect(FalloutMainUtilityStep.RangeDiagnostic, () => UtilityCommands().OutOfRangeAchievement(request.Id), request.Request);
                    else
                    {
                        IFalloutMainUtilityStatistics? statistics = null;
                        Effect(FalloutMainUtilityStep.StatisticsTest, () => statistics = UtilityPlatform().SteamUserStats(), request.Request,
                            boolean: () => statistics is not null);
                        if (statistics is not null)
                        {
                            Effect(FalloutMainUtilityStep.StatisticsQuery, () => statistics = UtilityPlatform().SteamUserStats(), request.Request,
                                boolean: () => statistics is not null);
                            var identifier = FalloutMainUtilitySource.AchievementIdentifier(request.Id); var result = false;
                            Effect(FalloutMainUtilityStep.SetAchievement, () =>
                            {
                                var actual = statistics ?? throw new InvalidDataException("Original second SteamUserStats pointer disappeared before its source dereference.");
                                if (string.IsNullOrWhiteSpace(actual.Owner)) throw new InvalidDataException("Steam statistics interface has no genuine provider identity.");
                                result = actual.SetAchievement(identifier);
                            }, request.Request, boolean: () => result, argument: identifier);
                        }
                    }
                }
                IFalloutMainUtilityStatistics? store = null;
                Effect(FalloutMainUtilityStep.StoreStatisticsTest, () => store = UtilityPlatform().SteamUserStats(), boolean: () => store is not null);
                if (store is not null)
                {
                    Effect(FalloutMainUtilityStep.StoreStatisticsQuery, () => store = UtilityPlatform().SteamUserStats(), boolean: () => store is not null);
                    var result = false; Effect(FalloutMainUtilityStep.StoreStatistics, () =>
                    {
                        var actual = store ?? throw new InvalidDataException("Original second SteamUserStats pointer disappeared before StoreStats.");
                        if (string.IsNullOrWhiteSpace(actual.Owner)) throw new InvalidDataException("Steam statistics interface has no genuine provider identity.");
                        result = actual.StoreStats();
                    }, boolean: () => result);
                }
                Effect(FalloutMainUtilityStep.ClearQueue, () =>
                { _utilityLastCleared = _utilityPending.ToArray(); _utilityClearedThrough = _utilityRequests; _utilityPending.Clear(); _ = Next(); });
            }
            Construct(FalloutMainUtilityStep.ConstructLogin, ref _utilityLoginConstructed);
            var before = _utilityLoggedOn;
            var running = false; Effect(FalloutMainUtilityStep.PlatformRunning, () => running = UtilityPlatform().IsSteamRunning(), boolean: () => running);
            var loggedOn = false;
            if (running)
            {
                IFalloutMainUtilityUser? user = null;
                Effect(FalloutMainUtilityStep.UserTest, () => user = UtilityPlatform().SteamUser(), boolean: () => user is not null);
                if (user is not null)
                {
                    Effect(FalloutMainUtilityStep.UserQuery, () => user = UtilityPlatform().SteamUser(), boolean: () => user is not null);
                    Effect(FalloutMainUtilityStep.LoggedOn, () =>
                    {
                        var actual = user ?? throw new InvalidDataException("Original second SteamUser pointer disappeared before BLoggedOn.");
                        if (string.IsNullOrWhiteSpace(actual.Owner)) throw new InvalidDataException("Steam user interface has no genuine provider identity.");
                        loggedOn = actual.BLoggedOn();
                    }, boolean: () => loggedOn);
                }
            }
            Effect(FalloutMainUtilityStep.StoreLogin, () => { _utilityLoggedOn = loggedOn; _utilityLoginChanged = Next(); }, boolean: () => loggedOn);
            if (before != loggedOn && _utilityCallback is { } callback)
                Effect(FalloutMainUtilityStep.ChangedCallback, () => callback(0, loggedOn), boolean: () => loggedOn);
            Construct(FalloutMainUtilityStep.ConstructThird, ref _utilityThirdConstructed);
            Effect(FalloutMainUtilityStep.ThirdNoOp, () => { }); // Selected original body has no effects.
            FinishUtility();
        }
        catch (Exception error) { FailUtility(error); throw; }
        finally { _utilityFrame = _utilityActive; _utilityActive = null; }
    }

    internal void AddSourceAchievement(int id)
    {
        RequireNotBusy(); _ = MainUtilitySource;
        if (_utilityCommand?.Error is not null) throw new InvalidOperationException("Original achievement command retains its refused prefix: " + _utilityCommand.Error);
        var parent = _utilityActive; _utilityActive = null;
        BeginUtility(null, null);
        try
        {
            var suppressed = false; Effect(FalloutMainUtilityStep.SuppressionByte,
                () => suppressed = UtilityCommands().AchievementSuppressionByte(), boolean: () => suppressed);
            if (!suppressed)
            {
                Construct(FalloutMainUtilityStep.ConstructAchievements, ref _utilityAchievementsConstructed);
                Effect(FalloutMainUtilityStep.QueueAchievement, () =>
                {
                    var ordinal = checked(_utilityRequests + 1);
                    var request = new FalloutPlatformAchievementRequest(ordinal, id, Next());
                    _utilityPending.Add(request); _utilityRequests = ordinal;
                }, argument: id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            // Original command returns true after parsing whether suppressed
            // or queued. That return is not a platform award/completion fact.
            FinishUtility();
        }
        catch (Exception error) { FailUtility(error); throw; }
        finally { _utilityCommand = _utilityActive; _utilityActive = parent; }
    }

    internal void RegisterSourceLoginCallback(string actualOwner, Action<int, bool>? callback)
    {
        RequireNotBusy(); ArgumentException.ThrowIfNullOrWhiteSpace(actualOwner); _ = MainUtilitySource;
        if (_utilityCallbackAttempt?.Error is not null) throw new InvalidOperationException("Login callback registration retains its attempted prefix.");
        var parent = _utilityActive; _utilityActive = null;
        BeginUtility(null, null); var registration = _utilityRegistrations = checked(_utilityRegistrations + 1);
        try
        {
            Construct(FalloutMainUtilityStep.ConstructLogin, ref _utilityLoginConstructed);
            Effect(FalloutMainUtilityStep.RegisterCallback, () => _utilityCallback = callback, boolean: () => callback is not null, argument: actualOwner);
            if (callback is not null) Effect(FalloutMainUtilityStep.InitialCallback, () => callback(0, _utilityLoggedOn), boolean: () => _utilityLoggedOn);
            FinishUtility();
        }
        catch (Exception error) { FailUtility(error); throw; }
        finally
        {
            var call = _utilityActive!;
            _utilityCallbackAttempt = call;
            // A real nested registration may have replaced the source pointer.
            // The outer callback's return cannot resurrect the old pointer.
            if (registration == _utilityRegistrations)
                _utilityCallbackReceipt = new(registration, actualOwner, call.Changed, callback is not null, call);
            _utilityActive = parent;
        }
    }
    private void BeginUtility(Guid? main, long? ordinal)
    {
        if (_utilityActive is not null) { FaultMainUtility(); throw new InvalidOperationException("Main utility child cannot reenter itself."); }
        var sequence = Next(); _utilityActive = new(Guid.NewGuid(), _process, main, ordinal, sequence, sequence, [], false);
    }
    private void Construct(FalloutMainUtilityStep step, ref bool constructed)
    {
        if (constructed) return;
        // No callback/user/platform outcome is certified by these constructors.
        // Original constructors initialize the queue/cached byte/null callback.
        Effect(step, () => { }); constructed = true;
    }
    private void Effect(FalloutMainUtilityStep step, Action action, long? request = null,
        Func<bool>? boolean = null, string? argument = null)
    {
        var current = _utilityActive ?? throw new InvalidOperationException("Utility effect has no entered source call.");
        var effect = new FalloutMainUtilityEffect(step, Next(), Request: request, Argument: argument);
        _utilityActive = current with { Changed = effect.Entered, Effects = [.. current.Effects, effect] };
        var faults = _utilityFaults; var mainFaults = _scriptCallerReentry; action();
        if (faults != _utilityFaults || mainFaults != _scriptCallerReentry)
            throw new InvalidOperationException("Utility child swallowed a forbidden source capture/retirement/reentry.");
        var effects = _utilityActive.Effects.ToArray(); effects[^1] = effects[^1] with { Returned = Next(), Boolean = boolean?.Invoke() };
        _utilityActive = _utilityActive with { Effects = effects, Changed = _sequence };
    }
    private void FinishUtility() => _utilityActive = _utilityActive! with { Returned = true, Changed = Next() };
    private void FailUtility(Exception error)
    {
        // A nested command/callback exception may be caught by its caller.
        // Keep that actual attempted operation poisonous to the enclosing
        // source consumer rather than returning a fabricated successful frame.
        FaultMainUtility();
        var type = error.GetType().FullName ?? error.GetType().Name; var effects = _utilityActive!.Effects.ToArray();
        if (effects.Length != 0 && effects[^1].Returned is null)
            effects[^1] = effects[^1] with { FailureType = type, Error = Message(error) };
        _utilityActive = _utilityActive with { Effects = effects, Changed = Next(), FailureType = type, Error = Message(error) };
    }
}
