using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutCampaignRestAdmission? _campaignRestAdmission;
    private FalloutCampaignRestConsumers? _campaignRestConsumers;
    private object? _campaignRestConsumerLease;
    private FalloutRestAutoSave? _restAutoSave;
    private FalloutRestWorldTime? _restWorldTime;
    private FalloutRestInterfaceSounds? _restInterfaceSounds;
    private NativeOwnedRestInterfaceSounds? _restInterfaceSoundsNative;
    private FalloutRestInterfaceSoundSnapshot? _restInterfaceSoundRestore;
    private FalloutPlayerHardcoreNeeds? _hardcoreNeeds;
    private FalloutHardcoreNeedSnapshot? _hardcoreNeedRestore;
    private bool _campaignRestRetired;

    internal string? CampaignRestFailure => _playerRest?.Failure?.Error ??
        _restAutoSave?.Failure ?? _restInterfaceSounds?.Failure ?? _hardcoreNeeds?.Failure;
    internal string? CampaignRestSaveBlocker => _playerRest is null ? "source-rest-owner-absent" :
        _playerRest.SaveBlocker ?? SourceRestInterfaceSaveBlocker ?? _restInterfaceSoundsNative?.SaveBlocker ??
        (_restInterfaceSounds is null ? "source-rest-interface-cue-playback-unpublished" : _restInterfaceSounds.SaveBlocker) ??
        (_restAutoSave is null ? "source-rest-autosave-owner-absent" : null) ??
        (_restWorldTime is null ? "source-rest-cumulative-world-time-owner-absent" : null) ??
        ((_hardcoreNeedRestore is not null || _scripts.Session.Hardcore) && _hardcoreNeeds is null
            ? "source-hardcore-base-pools-stage-effect-and-reset-producers-unbound" : _hardcoreNeeds?.SaveBlocker);
    internal object CampaignRestState => new
    {
        rest = PlayerRestState, nativeMenu = PlayerRestNativeState,
        autosave = _restAutoSave?.Capture(), worldTime = _restWorldTime?.Capture(),
        sounds = _restInterfaceSounds?.State, interfaceState = SourceRestInterfaceState,
        hardcore = _hardcoreNeeds?.State, consumersBound = _campaignRestConsumerLease is not null,
        cuePlaybackPublished = _restInterfaceSounds is not null, retired = _campaignRestRetired,
        failure = CampaignRestFailure, saveBlocker = CampaignRestSaveBlocker,
    };

    internal void ConfigureCurrentCampaignRest(RuntimeNativeGameTime actualClock,
        Func<RuntimeSaveNativeSite> actualAutoSaveSite, FalloutNativeCampaignState? restore)
    {
        ArgumentNullException.ThrowIfNull(actualClock); ArgumentNullException.ThrowIfNull(actualAutoSaveSite);
        if (_campaignRestAdmission is not null || _campaignRestRetired)
            throw new InvalidOperationException("The campaign rest lifetime cannot be replaced.");
        _campaignRestAdmission = new(_pluginStack, _scripts.References!, _vitals, () => _activeCell,
            bed => ReferencePresentation().ObserveRestBedModel(bed), ObserveOtherCampaignRestFact);
        var host = new FalloutSleepWaitHost(_campaignRestAdmission.Observe, BeginCampaignRest,
            _player.WriteSourceSleepFlag, ApplyCampaignRestPrelude, AdvanceCampaignRestWorldSeconds,
            ApplyCampaignRestEffects, CompleteCampaignSleep, CloseCampaignRest,
            () => _player.CommittedPlayerSleeping)
        {
            AfterMenuPlayerHours = request => RequireCampaignRestConsumers("post-hours-statistic-and-world-start").AfterPlayerHours(request),
            BeforeCancelPlayerHours = request =>
            {
                RequireRestCuePublication().PlayCancel(request);
                RequireCampaignRestConsumers("native-cancel-controls").BeforeCancelHours(request);
            },
        };
        ConfigureCurrentPlayerRest(host, ObserveSourceRestCloseGate, restore is null ? null :
            restore.SleepWait ?? throw new InvalidDataException("Current campaign has no sleep/wait continuation."));
        _scriptHost = _scriptHost with { SleepWait = PlayerRest, OpenSleepWaitMenu = OpenCurrentPlayerRest };
        actualClock.BindSourceRestClock(() => PlayerRestOwnsMenuClock);
        _restWorldTime = new(PlayerRest.Source, actualClock.ObserveCumulativeWorldTimeFrame,
            restore is null ? null : restore.RestWorldTime ??
                throw new InvalidDataException("Current campaign has no cumulative world-time continuation."));
        actualClock.BindSourceCumulativeWorldTime(_restWorldTime);
        _restAutoSave = new(_pluginStack, PlayerRest,
            FalloutRestAutoSavePolicy.Read(PlayerRest.Source, _pluginStack.IniSettings), ManualSourceSaveRequests,
            actualAutoSaveSite, restore is null ? null : restore.RestAutoSave ??
                throw new InvalidDataException("Current campaign has no rest autosave continuation."));
        ConfigureSourceRestInterface(restore is null ? null : restore.InterfaceFades ??
            throw new InvalidDataException("Current campaign has no source interface fades."),
            () => RequireCampaignRestConsumers("global-interface-fade-force-retirement").FadeForceRetirement());
        _restInterfaceSoundRestore = restore is null ? null : restore.RestInterfaceSounds ??
            throw new InvalidDataException("Current campaign has no rest interface sound continuation.");
        _hardcoreNeedRestore = restore?.HardcoreNeeds;
        // The original exact-file branch performs no variant/random draw.
        // Random/folder requests retain their distinct unowned consumers.
        BindSourceRestCuePlayback();
    }

    internal IDisposable BindActualCampaignRestConsumers(FalloutCampaignRestConsumers consumers)
    {
        ArgumentNullException.ThrowIfNull(consumers); consumers.Validate();
        if (_campaignRestRetired || _campaignRestConsumerLease is not null)
            throw new InvalidOperationException("Rest source consumers need one living publication lease.");
        var lease = new object(); _campaignRestConsumers = consumers; _campaignRestConsumerLease = lease;
        return new CampaignRestConsumerLease(this, lease);
    }
    private sealed class CampaignRestConsumerLease(RuntimeNativeOpeningStageDriver driver, object lease) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            if (!ReferenceEquals(driver._campaignRestConsumerLease, lease)) { _disposed = true; return; }
            if (driver._playerRest?.Active == true)
                throw new InvalidOperationException("A running rest operation still owns its live world consumers.");
            driver._campaignRestConsumers = null; driver._campaignRestConsumerLease = null; _disposed = true;
        }
    }
    private FalloutCampaignRestConsumers RequireCampaignRestConsumers(string reached) => _campaignRestConsumers ??
        throw new NotSupportedException("original-rest-" + reached + "-producer-unbound");

    internal void BindSourceRestCuePlayback()
    {
        if (_campaignRestRetired || _restInterfaceSounds is not null || !IsInsideTree())
            throw new InvalidOperationException("Rest cue playback has no new living campaign publication.");
        var sounds = new FalloutRestInterfaceSounds(_pluginStack, PlayerRest, _restInterfaceSoundRestore);
        _restInterfaceSounds = sounds;
        _restInterfaceSoundsNative = NativeOwnedRestInterfaceSounds.Attach(this, _pluginStack,
            PlayerRest, sounds, RetainCurrentPlayerRestFailure);
    }
    internal void BindActualHardcoreNeeds(FalloutPlayerHardcoreNeeds actualNeeds)
    {
        ArgumentNullException.ThrowIfNull(actualNeeds);
        if (_campaignRestRetired || _hardcoreNeeds is not null)
            throw new InvalidOperationException("Hardcore needs already have an actual campaign owner.");
        var snapshot = actualNeeds.Capture(); snapshot.Validate();
        if (snapshot.SourceSha256 != PlayerRest.Source.Identity || _hardcoreNeedRestore is not null &&
            System.Text.Json.JsonSerializer.Serialize(snapshot) != System.Text.Json.JsonSerializer.Serialize(_hardcoreNeedRestore))
            throw new InvalidDataException("Hardcore publication changed its actual source or cold committed prefix.");
        _hardcoreNeeds = actualNeeds;
    }
    private NativeOwnedRestInterfaceSounds RequireRestCuePublication() => _restInterfaceSoundsNative ??
        throw new NotSupportedException("original-rest-interface-cue-native-publication-unbound");
    private FalloutRestObservation ObserveOtherCampaignRestFact(FalloutRestRequest request, FalloutRestFact fact)
    {
        if (fact == FalloutRestFact.SourceMenuBeginConsumerOwned && _restInterfaceSoundsNative is null)
            return new(FalloutRestFactState.Unowned, "original-rest-interface-cue-native-publication",
                "The actual selected cue host has not published.");
        if (fact == FalloutRestFact.SourceMenuBeginConsumerOwned)
        {
            var cue = _restInterfaceSounds!.ObserveCueCapability(FalloutRestInterfaceCueKind.Start);
            if (cue.State != FalloutRestFactState.Satisfied) return cue;
        }
        if (fact == FalloutRestFact.SourceMenuBeginConsumerOwned && request.Kind == FalloutRestKind.Sleep)
        {
            var fade = ObserveSourceRestFadePublication();
            if (fade.State != FalloutRestFactState.Satisfied) return fade;
        }
        if (_campaignRestConsumers is null)
            return new(FalloutRestFactState.Unowned, "original-rest-" + fact,
                "This reached source predicate/consumer has no living producer.");
        var actual = _campaignRestConsumers.Observe(request, fact) ??
            throw new InvalidDataException("Actual rest producer returned no source observation.");
        actual.Validate(); return actual;
    }
    private void BeginCampaignRest(FalloutRestRequest request)
    {
        RequireRestCuePublication().PlayStart(request);
        (_restAutoSave ?? throw new NotSupportedException("Actual rest autosave owner is absent.")).RequestBeforeCountdown(request);
        SourceRestInterface.BeforeMenuPlayerHours(request);
        RequireCampaignRestConsumers("native-counting-controls").BeginNativeCounting(request);
    }
    private void ApplyCampaignRestPrelude(FalloutRestHour hour) =>
        RequireCampaignRestConsumers("hour-prelude-hardcore-and-source-world").HourPrelude(hour);
    private void AdvanceCampaignRestWorldSeconds(float seconds)
    {
        (_restWorldTime ?? throw new NotSupportedException("Actual cumulative rest time owner is absent.")).AdvanceRestHour(PlayerRest, seconds);
        RequireCampaignRestConsumers("complete-world-hour-seconds").WorldHourSeconds(seconds);
    }
    private void ApplyCampaignRestEffects(FalloutRestHour hour) =>
        RequireCampaignRestConsumers("complete-hour-effects").HourEffects(hour);
    private void CompleteCampaignSleep(FalloutRestRequest request) =>
        RequireCampaignRestConsumers("complete-source-sleep-effects").CompleteSleep(request);
    private void CloseCampaignRest(FalloutRestRequest request, bool completed)
    {
        RequireCampaignRestConsumers("world-end-processing").EndWorldProcessing(request, completed);
        SourceRestInterface.BeforeNativeMenuRetirement(request, completed);
    }
    internal FalloutRestAutoSaveSnapshot CaptureCurrentRestAutoSave() => (_restAutoSave ??
        throw new NotSupportedException("Current campaign has no rest autosave owner.")).Capture();
    internal FalloutRestWorldTimeSnapshot CaptureCurrentRestWorldTime() => (_restWorldTime ??
        throw new NotSupportedException("Current campaign has no cumulative rest world-time owner.")).Capture();
    internal FalloutRestInterfaceSoundSnapshot CaptureCurrentRestInterfaceSounds() => (_restInterfaceSounds ??
        throw new NotSupportedException("Current campaign has no actual rest cue playback owner.")).Capture();
    internal FalloutHardcoreNeedSnapshot? CaptureCurrentHardcoreNeeds()
    {
        if ((_scripts.Session.Hardcore || _hardcoreNeedRestore is not null) && _hardcoreNeeds is null)
            throw new NotSupportedException("Current campaign has no complete Hardcore source pools and producers.");
        return _hardcoreNeeds?.Capture();
    }
    internal void RetireCurrentCampaignRest()
    {
        if (_campaignRestRetired) return;
        var failures = new List<Exception>();
        void Retire(Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error); RetainCurrentPlayerRestFailure(error); }
        }
        Retire(RetireSourceRestInterface);
        Retire(RetireCurrentPlayerRest);
        if (_restInterfaceSoundsNative is { } sounds) Retire(sounds.RetireForSession);
        if (failures.Count != 0)
            throw new AggregateException("Campaign rest retirement retains independent source/native failures.", failures);
        _campaignRestConsumers = null; _campaignRestConsumerLease = null; _campaignRestRetired = true;
    }
}
