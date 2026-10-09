using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutInterfaceActivationFrame? _interfaceActivationFrames;
    private Guid _interfaceActivationSession;
    private Func<FalloutExperienceLevelIntroInput>? _experienceLevelIntroInput;
    internal object? InterfaceActivationFrameState => _interfaceActivationFrames?.State;
    internal string? InterfaceActivationFrameSaveBlocker => _interfaceActivationFrames?.SaveBlocker ??
        (_interfaceActivationFrames is null ? "interface-activation-frame-owner" : null);

    internal void ConfigureInterfaceActivationFrames(FalloutInterfaceActivationFrameSnapshot? restore)
    {
        if (_interfaceActivationFrames is not null) throw new InvalidOperationException("Interface frame is already configured.");
        _interfaceActivationFrames = new(FalloutAdvancementFrameDeclaration.Read(ExperienceHudSource.Declaration),
            _pluginStack, _scripts.References ?? throw new InvalidOperationException("Interface frame has no actual shared reference world."), restore);
    }
    internal void AttachInterfaceActivationFrames()
    {
        if (!IsInsideTree() || !_player.IsInsideTree())
            throw new InvalidOperationException("Interface frame cannot attach outside the actual native player/session.");
        _interfaceActivationSession = (_interfaceActivationFrames ??
            throw new InvalidOperationException("Interface frame declaration is absent.")).AttachSession();
    }
    internal void EnterOriginalContainerFactory(FalloutFormKey reference) =>
        (_interfaceActivationFrames ?? throw new InvalidOperationException("Interface activation frame is absent."))
            .EnterContainerFactory(_interfaceActivationSession, reference);
    internal FalloutInterfaceActivationFrameSnapshot CaptureInterfaceActivationFrames() =>
        (_interfaceActivationFrames ?? throw new InvalidOperationException("Interface frame capture has no actual owner.")).Capture();
    internal FalloutAdvancementActivityObservation ObserveInterfaceActivationFrameActivity() =>
        _interfaceActivationFrames?.Observe() ?? new(FalloutAdvancementActivityState.Unowned, "original-interface-frame-owner-absent");

    // The original XP scheduler counts targets in the actual player's source
    // combat group. Public IsInCombat reads a different producer; no Boolean
    // substitution or callback registration implies this count is zero.
    internal void BindExperienceLevelIntroInput(Func<FalloutExperienceLevelIntroInput> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_experienceLevelIntroInput is not null) throw new InvalidOperationException("Experience level-intro input is already bound.");
        _experienceLevelIntroInput = read;
    }
    private FalloutExperienceLevelIntroInput ReadExperienceLevelIntroInput() =>
        _experienceLevelIntroInput?.Invoke() ?? new(null, "original-player-combat-group-target-count-unowned");
    internal FalloutAdvancementActivityObservation ObserveOriginalExperienceFrameActivity()
    {
        if (_experienceNotifications is null)
            return new(FalloutAdvancementActivityState.Unowned, "original-experience-frame-owner-absent");
        var actual = _experienceNotifications.ObserveOriginalUiFrameGate();
        return new(actual.State switch
        {
            FalloutExperienceNotificationFact.Satisfied => FalloutAdvancementActivityState.Satisfied,
            FalloutExperienceNotificationFact.Held => FalloutAdvancementActivityState.Held,
            FalloutExperienceNotificationFact.Unowned => FalloutAdvancementActivityState.Unowned,
            _ => throw new InvalidDataException("Original experience frame observation is invalid."),
        }, actual.Owner);
    }
    private void RetireInterfaceActivationFrames()
    {
        _interfaceActivationFrames?.Dispose(); _interfaceActivationFrames = null;
        _interfaceActivationSession = Guid.Empty; _experienceLevelIntroInput = null;
    }
}
