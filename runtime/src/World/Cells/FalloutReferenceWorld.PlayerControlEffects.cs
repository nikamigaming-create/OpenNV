using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerControlDisplayState? _standaloneControlDisplay;
    private FalloutPlayerControlHudState? _standaloneControlHud;
    private FalloutPlayerControlHudContext? _standaloneControlHudContext;
    private FalloutDirectInputState? _standaloneControlInput;
    private bool _standaloneControlAttempted, _standaloneControlRetired;
    private string? _standaloneControlConstructionFailure;

    internal bool StandalonePlayerControlEffectsAttempted => _standaloneControlAttempted;

    internal void PrepareStandalonePlayerControlEffects(FalloutUiComponentStore ui, FalloutDirectInputState input)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ArgumentNullException.ThrowIfNull(ui); ArgumentNullException.ThrowIfNull(input);
        RequireStandaloneControlPublication();
        if (_standaloneControlAttempted || _standaloneControlRetired)
            throw new InvalidOperationException("Source control effects cannot retry or replace their actual HUD constructor.");
        _standaloneControlAttempted = true; _standaloneControlInput = input;
        try
        {
            var scene = CampaignStandalonePlayerScene; scene.PublishControlCommandInput(input);
            var owned = records.OwnedSource ?? throw new NotSupportedException("source-Player-control-HUD-actual-owned-source-unbound");
            var source = FalloutExecutableStringTable.ReadPlayerControlHud(owned.FalloutExecutablePath,
                FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
            var savedHud = scene.PreparedControlHudRestore; var savedDisplay = scene.PreparedControlDisplayRestore;
            if ((savedHud is null) != (savedDisplay is null) || _processRuntimeRestore is not null && savedHud is null)
                throw new InvalidDataException("Cold source controls omitted actual HUD/display state before input publication.");
            _standaloneControlDisplay = new(source, scene.Stack, scene.Process, savedDisplay);
            if (savedDisplay is null)
            {
                _standaloneControlDisplay.ApplyDeclaredConfiguration(records.IniSettings);
                _standaloneControlDisplay.PublishCurrentLayout(Guid.NewGuid(), "actual-source-current-display-layout-publication", RequireStandaloneControlPublication);
            }
            _standaloneControlHudContext = new(source, scene.Stack, scene.Process, StandaloneInterface,
                ProcessRuntime.RequirePlayerControlTimer(), ProcessRuntime.RequirePlayerControlModeOwner(), input, scene.ControlFields);
            _standaloneControlHud = new(source, scene.Stack, scene.Process, ui, records.IniSettings, scene.ThreadContext, _standaloneControlHudContext, savedHud);
            scene.BindControlHud(_standaloneControlHud, _standaloneControlDisplay);
            if (savedHud is null) _standaloneControlHud.Initialize(_standaloneControlDisplay, scene.ControlFields.ReadDisabledWord);
        }
        catch (Exception error) { _standaloneControlConstructionFailure = error.Message; throw; }
    }

    internal void ApplyStandalonePlayerControlCommand(FalloutPlayerControlCommandObservation observation)
    {
        RequireStandaloneControlPublication(); ArgumentNullException.ThrowIfNull(observation); observation.Validate();
        var scene = CampaignStandalonePlayerScene;
        var hud = _standaloneControlHud ?? throw new NotSupportedException("source-Player-control-actual-HUD-constructor-unbound");
        var input = _standaloneControlInput ?? throw new NotSupportedException("source-Player-control-actual-input-publication-unbound");
        if (!ReferenceEquals(input.ReadManagerOwner(), input) || input.Source != scene.Stack)
            throw new InvalidOperationException("Source command changed its actual published input factory.");
        IFalloutPlayerControlFightingOwner? fighting = null;
        if (ActorProcessesConfigured && ProcessCommon.HasActor(_enginePlayer))
        {
            var registration = ActorProcesses.Read(_enginePlayer);
            var epoch = registration.Epoch;
            void RequireActor()
            {
                RequireStandaloneControlPublication(); var actor = ActorProcesses.Read(_enginePlayer);
                if (_actorProcessStack != scene.Stack || actor.Epoch != epoch || actor.Retired ||
                    actor.Source != registration.Source || actor.Level != registration.Level || !actor.Source.EnginePlayer)
                    throw new InvalidOperationException("Control effects lost the actual living source Player process constructor.");
            }
            fighting = new FalloutPlayerControlFightingOwner(hud.Source.Controls, scene.Stack, scene.Process,
                ProcessCommon, _enginePlayer, epoch, () => scene.ReadControlCommandMoverFlags(observation.Observation),
                () => ProcessRuntime.RequirePlayerControlModeOwner().ReadMainControlMode(scene.Process), RequireActor);
        }
        var consumers = new FalloutPlayerControlFlagConsumers(hud.Source.Controls, scene.Stack, scene.Process,
            observation.Observation, scene.ControlFields, hud, fighting, scene);
        scene.ApplySourceControlCommand(observation, consumers);
    }

    internal object StandalonePlayerControlEffectsState => new
    {
        attempted = _standaloneControlAttempted,
        hud = _standaloneControlHud?.Capture(),
        display = _standaloneControlDisplay?.Capture(),
        playerScene = HasStandalonePlayerScene ? CampaignStandalonePlayerScene.Capture() : null,
        failure = _standaloneControlConstructionFailure,
        retired = _standaloneControlRetired
    };
    internal string? StandalonePlayerControlEffectsSaveBlocker => !HasStandalonePlayerScene ? null :
        _standaloneControlConstructionFailure ?? (_standaloneControlRetired ? "source-Player-control-effects-retired" :
        _standaloneControlHud is null || _standaloneControlDisplay is null ? "source-Player-control-actual-HUD-display-constructor-unbound" :
        _standaloneControlHud.SaveBlocker);

    private void RequireStandaloneControlPublication()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sourceMutableNativeOwner is null || _sourceMutableNativeThread != Environment.CurrentManagedThreadId ||
            _sourceMutableRetirementOnly || _sourceMutableNativeFailure is not null || _standaloneControlRetired || !HasStandalonePlayerScene)
            throw new InvalidOperationException("Source control effects have no actual published world/input thread lifetime.");
    }
    internal void RetireStandalonePlayerControlEffects()
    {
        if (_standaloneControlRetired) return;
        if (_standaloneControlAttempted && _standaloneControlHud is not null)
        {
            if (_sourceMutableNativeThread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Source HUD retirement changed its actual published thread.");
            _standaloneControlHud.Retire();
        }
        _standaloneControlRetired = true;
    }
}
