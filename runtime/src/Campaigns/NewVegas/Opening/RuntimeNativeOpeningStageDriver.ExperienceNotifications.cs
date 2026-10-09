using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutExperienceHudSource? _experienceHudSource;
    private FalloutExperienceNotifications? _experienceNotifications;
    private FalloutExperienceUiClock? _experienceUiClock;
    private NativeExperiencePauseClock? _experiencePauseClock;
    internal FalloutExperienceHudSource ExperienceHudSource => _experienceHudSource ??
        throw new NotSupportedException("Original experience HUD declaration owner is absent.");
    internal FalloutExperienceNotifications ExperienceNotifications => _experienceNotifications ??
        throw new NotSupportedException("Persistent experience notification owner is absent.");
    internal object? ExperienceNotificationState => _experienceNotifications?.State;
    internal string? ExperienceNotificationSaveBlocker => _experienceNotifications?.SaveBlocker ??
        (_experienceNotifications is null ? "experience-notification-owner" : null);

    // Root calls this after progress/vitals/XP creation but before any source
    // invocation, with the genuine player's deterministic sound state. A cold
    // save must supply this current snapshot; never reconstruct old notices
    // from XP totals or attach an event observer after source execution starts.
    internal void ConfigureExperienceNotifications(FalloutExperienceNotificationSnapshot? restore, ulong initialSoundRandomState)
    {
        if (_experienceNotifications is not null) throw new InvalidOperationException("Experience notifications are already configured.");
        var live = _pluginStack.OwnedSource ?? throw new InvalidOperationException("Experience notification source is absent.");
        var source = new FalloutExperienceHudSource(_pluginStack, live);
        var owner = new FalloutExperienceNotifications(source.Declaration,
            FalloutAdvancementFrameDeclaration.Read(source.Declaration), ReadExperienceLevelIntroInput, source.Contract, _experience,
            () => _vitals.State.Level, () => _vitals.State.ExperiencePoints, _vitals.ExperienceThreshold,
            () =>
            {
                var maximum = unchecked((int)FalloutGameSettingIntegers.Read(_pluginStack, "iMaxCharacterLevel"));
                if (maximum < 1) throw new InvalidDataException("Experience notification level cap is invalid.");
                return maximum;
            }, initialSoundRandomState, restore);
        _experienceHudSource = source; _experienceNotifications = owner;
        _experienceUiClock = new(restore?.LevelClockMilliseconds ?? 0);
    }

    // Called only after this actual gameplay node enters the tree. Its child
    // joins real tree-pause notifications before HUD publication; a temporary
    // Always-mode source-save drain cannot resume the global engine timer.
    internal void AttachExperiencePauseClock()
    {
        if (_experiencePauseClock is not null) throw new InvalidOperationException("XP pause clock is already attached.");
        _experiencePauseClock = NativeExperiencePauseClock.Attach(this,
            _experienceUiClock ?? throw new InvalidOperationException("XP source timer is absent."),
            () => Environment.TickCount64, error => ExperienceNotifications.RetainFailure(error));
    }
    internal FalloutExperienceClockReading ReadExperienceUiClock() =>
        (_experienceUiClock ?? throw new InvalidOperationException("XP source timer is absent.")).Read(Environment.TickCount64);
    internal FalloutExperienceNotificationSnapshot CaptureExperienceNotifications() =>
        ExperienceNotifications.Capture(ReadExperienceUiClock());
    internal bool ExperiencePendingLevel => RequirePlayerProgress().Pending;
    internal bool ExperienceCharacterGenerationEnded => !_scripts.Session.InCharGen;
    internal FalloutExperienceNotificationAdmission ExperienceHudAdmission(bool sourceSurfaceAvailable, bool loading)
    {
        if (_experienceNotifications is null || BlockingExecutionFault is not null || !sourceSurfaceAvailable)
            return FalloutExperienceNotificationAdmission.Unowned;
        // The shared native update/modal owners supply current eligibility.
        // Their absence is distinct from a currently held notification.
        if (!IsInsideTree() || !CanProcess() || GetTree().Paused || loading || _moviePlaying ||
            _nameEntry is not null || _raceSexEntry is not null || _vigorEntry is not null || _specialBookEntry is not null ||
            _tagSkillEntry is not null || _traitEntry is not null || _recipeMenu is not null || _barterMenu is not null ||
            _terminalMenus.Values.Any(menu => menu.Active) || _levelUpEntry is not null)
            return FalloutExperienceNotificationAdmission.Held;
        return FalloutExperienceNotificationAdmission.Ready;
    }
    private void RetireExperienceNotifications()
    {
        var failures = new List<Exception>();
        var clock = _experiencePauseClock;
        var notifications = _experienceNotifications;
        try
        {
            try { if (Godot.GodotObject.IsInstanceValid(clock)) clock!.Free(); }
            catch (Exception error) { failures.Add(error); }
            try { notifications?.Dispose(); }
            catch (Exception error) { failures.Add(error); }
        }
        finally
        {
            _experienceNotifications = null; _experienceHudSource = null;
            _experienceUiClock = null; _experiencePauseClock = null;
        }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Experience clock and award-subscription retirement failed.", failures);
    }
}
