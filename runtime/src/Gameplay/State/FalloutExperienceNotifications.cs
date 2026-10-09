using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutExperienceNotificationPhase
{
    MeterFadeIn, MeterSweep, MeterFadeOut, LevelFadeIn, LevelHold, LevelFadeOut, AwaitingHiddenFrame,
}
internal enum FalloutExperienceNotificationAdmission { Ready, Held, Unowned }
internal enum FalloutExperienceNotificationFact { Satisfied, Held, Unowned }
internal enum FalloutExperienceSoundState { Pending, Playing, Finished }
internal sealed record FalloutExperienceChange(ulong Ordinal, int Level, int Previous, int Published, bool Queued);
internal sealed record FalloutExperienceSoundReceipt(ulong Sequence, string EditorId, FalloutExperienceSoundState State);
internal sealed record FalloutExperienceDisplay(ulong Sequence, ulong FirstOrdinal, ulong LastOrdinal, int Amount,
    int Level, int TargetExperience, int SweepLevel, int SweepStartExperience, int SweepEndExperience,
    FalloutExperienceNotificationPhase Phase, double ElapsedMilliseconds, double LevelElapsedMilliseconds,
    bool PhasePresented, bool ThresholdCrossing, bool LevelTextPublished, bool MeterPublished);
internal sealed record FalloutExperienceNotificationSnapshot(string Schema, string Contract, int ObservedExperience,
    ulong LastOrdinal, ulong RetiredThrough, IReadOnlyList<FalloutExperienceChange> Pending,
    FalloutExperienceDisplay? Display, IReadOnlyList<FalloutExperienceSoundReceipt> Sounds,
    ulong SoundRandomState, long LevelClockMilliseconds, double? LevelTimestampMilliseconds, bool IdlePresented,
    ulong LastSequence, ulong RetiredSequence, FalloutExperienceFrameGateSnapshot FrameGate, string? Failure);
internal sealed record FalloutExperienceNotificationFrame(Guid View, ulong Revision, ulong Sequence,
    FalloutExperienceNotificationPhase Phase, float MeterAlpha, float LevelAlpha, float PointerFraction,
    int Amount, int LastLevel, int NextLevel, bool LevelBufferIsShort, bool MeterVisible, bool LevelVisible,
    float? LevelTimestamp, bool LevelTextStarted);
internal sealed record FalloutExperienceNotificationObservation(FalloutExperienceNotificationFact State, string Owner);

// Source events, the selected tile program, native publication and retirement
// have distinct ownership. Merely constructing a view cannot finish a request.
internal sealed partial class FalloutExperienceNotifications : IDisposable
{
    internal const string Schema = "opennv-experience-notifications/v1";
    private readonly FalloutExperienceHudDeclaration _source;
    private readonly string _contract;
    private readonly Func<int> _level;
    private readonly Func<int> _experience;
    private readonly Func<int, int> _threshold;
    private readonly Func<int> _maximum;
    private readonly FalloutPlayerExperience _events;
    private readonly FalloutExperienceFrameGate _frameGate;
    private readonly Func<FalloutExperienceLevelIntroInput> _levelIntroInput;
    private readonly Queue<FalloutExperienceChange> _pending = [];
    private readonly List<FalloutExperienceSoundReceipt> _sounds = [];
    private readonly FalloutSoundRandomState _random;
    private FalloutExperienceDisplay? _display;
    private int _observedExperience;
    private ulong _lastOrdinal, _retiredThrough, _revision;
    private ulong _lastSequence, _retiredSequence;
    private Guid _view;
    private long? _lastClock, _lastLevelClock;
    private double? _levelTimestamp;
    private bool _nativeOwned, _idlePresented, _disposed;
    private string? _failure;
    internal string? Failure => _failure;
    internal FalloutSoundRandomState SoundRandom => _random;
    internal string Contract => _contract;
    internal bool IsSettled => !_disposed && _failure is null && _nativeOwned && _idlePresented && _pending.Count == 0 &&
        _display is null && _retiredThrough == _lastOrdinal;
    internal string? SaveBlocker => _failure is not null ? "experience-notification-failure" : !_nativeOwned ? "experience-notification-owner" :
        _sounds.Any(sound => sound.State == FalloutExperienceSoundState.Playing) ? "experience-notification-active-sound" : null;
    internal object State => new
    {
        ordinal = _lastOrdinal,
        retiredThrough = _retiredThrough,
        pending = _pending.Count,
        sequence = _display?.Sequence,
        phase = _display?.Phase.ToString(),
        elapsedMilliseconds = _display?.ElapsedMilliseconds,
        nativeOwned = _nativeOwned,
        isSettled = IsSettled,
        frameGate = _frameGate.Capture(),
        lastSequence = _lastSequence,
        retiredSequence = _retiredSequence,
        failure = _failure,
        saveBlocker = SaveBlocker
    };

    internal FalloutExperienceNotifications(FalloutExperienceHudDeclaration source, FalloutAdvancementFrameDeclaration frameSource,
        Func<FalloutExperienceLevelIntroInput> levelIntroInput, string contract, FalloutPlayerExperience events,
        Func<int> level, Func<int> experience, Func<int, int> threshold, Func<int> maximum, ulong soundRandomState,
        FalloutExperienceNotificationSnapshot? restore = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(events);
        if (contract.Length != 64) throw new InvalidDataException("XP notification selection contract is absent.");
        _source = source; _contract = contract; _events = events; _level = level; _experience = experience;
        frameSource.Validate(); ArgumentNullException.ThrowIfNull(levelIntroInput);
        if (frameSource.ExecutableSha256 != source.ExecutableSha256)
            throw new InvalidDataException("XP frame producer differs from the selected notification consumer.");
        _frameGate = new(frameSource, restore?.FrameGate); _levelIntroInput = levelIntroInput;
        _threshold = threshold; _maximum = maximum; _random = new(soundRandomState); _observedExperience = experience();
        if (_observedExperience < 0 || level() < 1 || maximum() < level())
            throw new InvalidDataException("XP notification player state is invalid.");
        if (restore is not null) Restore(restore);
        _events.ExperienceChanged += Changed;
    }

    private void Changed(int previous, int published)
    {
        RequireHealthy();
        try
        {
            if (previous != _observedExperience || published < 0 || published == previous || published != _experience())
                throw new InvalidDataException("XP notification event differs from the actual player publication.");
            var ordinal = checked(++_lastOrdinal); _observedExperience = published; ++_revision;
            // The source queue producer does not append a new notice while
            // its own level-intro completion flag awaits menu submission.
            var queued = published > previous && _level() < _maximum() && !_frameGate.Ready;
            if (queued) { _pending.Enqueue(new(ordinal, _level(), previous, published, true)); _idlePresented = false; }
            else if (_display is null && _pending.Count == 0) _retiredThrough = ordinal;
            else if (published < previous)
                throw new NotSupportedException("XP reduction during a queued positive meter requires its source rebase consumer.");
            if (queued && _display is not null)
                throw new NotSupportedException("XP gain during an active meter requires its source pointer retarget/notification flag arbitration owner.");
        }
        catch (Exception error) { RetainFailure(error); throw; }
    }

    internal Guid AttachPresentation(string sourceContract)
    {
        RequireHealthy();
        if (_view != Guid.Empty || sourceContract != _contract)
            throw new InvalidOperationException("XP presentation lease differs from the actual notification owner.");
        _view = Guid.NewGuid(); _nativeOwned = true; _idlePresented = false; _lastClock = null; _lastLevelClock = null;
        if (_display is { } display) _display = display with { PhasePresented = false };
        return _view;
    }
    internal void DetachPresentation(Guid view)
    {
        if (view != _view || view == Guid.Empty) throw new InvalidOperationException("Foreign XP presentation retirement.");
        _view = Guid.Empty; _nativeOwned = false; _idlePresented = false; _lastClock = null; _lastLevelClock = null;
    }
    internal FalloutExperienceNotificationObservation Observe()
    {
        if (!_disposed && _failure is null && _observedExperience != _experience())
            RetainFailure(new InvalidDataException("XP changed without its actual notification event publication."));
        return new(_failure is not null || _disposed || !_nativeOwned ? FalloutExperienceNotificationFact.Unowned :
            IsSettled ? FalloutExperienceNotificationFact.Satisfied : FalloutExperienceNotificationFact.Held,
            $"experience-notification:{_contract}:{_lastOrdinal}:{_retiredThrough}:{_display?.Phase.ToString() ?? "idle"}" +
            (_failure is null ? "" : ":" + _failure));
    }

    internal void Tick(FalloutExperienceClockReading clock, FalloutExperienceNotificationAdmission admission, bool levelRequestPending,
        bool characterGenerationEnded)
    {
        RequireHealthy();
        try
        {
            var milliseconds = clock.TilesMilliseconds;
            if (!Enum.IsDefined(admission) || milliseconds < 0 || clock.LevelMilliseconds < 0 ||
                _lastClock is { } previous && milliseconds < previous ||
                _lastLevelClock is { } previousLevel && clock.LevelMilliseconds < previousLevel)
                throw new InvalidDataException("XP notification update clock/admission is invalid.");
            var elapsed = _lastClock is { } last ? milliseconds - last : 0;
            var levelElapsed = _lastLevelClock is { } lastLevel ? clock.LevelMilliseconds - lastLevel : 0;
            _lastClock = milliseconds; _lastLevelClock = clock.LevelMilliseconds;
            if (admission == FalloutExperienceNotificationAdmission.Unowned || !_nativeOwned)
                throw new NotSupportedException("XP notification lacks its actual native UI update/surface owner.");
            if (_display is { PhasePresented: true } timed)
                _display = timed with
                {
                    ElapsedMilliseconds = timed.ElapsedMilliseconds + elapsed,
                    LevelElapsedMilliseconds = timed.LevelElapsedMilliseconds + levelElapsed
                };
            // Already created source tile clocks run on the monotonic UI clock;
            // a held update may not consume a phase or publish an unseen sound.
            if (admission == FalloutExperienceNotificationAdmission.Held) return;
            if (_display is null)
            {
                if (_pending.Count != 0) BeginMeter();
                else if (levelRequestPending && characterGenerationEnded && !_frameGate.Ready &&
                    _frameGate.AdmitLevelText(_levelIntroInput())) BeginLevelText(clock);
                return;
            }
            if (!_display.PhasePresented) return;
            var current = _display;
            switch (current.Phase)
            {
                case FalloutExperienceNotificationPhase.MeterFadeIn when Complete(_source.MeterFadeSeconds):
                    Phase(FalloutExperienceNotificationPhase.MeterSweep); break;
                case FalloutExperienceNotificationPhase.MeterSweep when current.SweepStartExperience == current.SweepEndExperience ||
                    Complete(current.ThresholdCrossing ? _source.ThresholdSweepSeconds : _source.MeterSweepSeconds):
                    if (current.ThresholdCrossing && current.SweepLevel + 1 < _maximum())
                    {
                        var level = checked(current.SweepLevel + 1); var lower = _threshold(level); var upper = _threshold(checked(level + 1));
                        if (upper <= lower) throw new InvalidDataException("XP threshold continuation is not increasing.");
                        _display = current with
                        {
                            SweepLevel = level,
                            SweepStartExperience = lower,
                            SweepEndExperience = Math.Min(current.TargetExperience, upper),
                            ThresholdCrossing = current.TargetExperience > upper,
                            ElapsedMilliseconds = 0,
                            PhasePresented = false
                        }; ++_revision;
                    }
                    else Phase(FalloutExperienceNotificationPhase.MeterFadeOut);
                    break;
                case FalloutExperienceNotificationPhase.MeterFadeOut when Complete(_source.MeterFadeSeconds):
                    if (levelRequestPending)
                    {
                        if (!characterGenerationEnded) return;
                        if (!_frameGate.AdmitLevelText(_levelIntroInput())) return;
                        _display = current with
                        {
                            Phase = FalloutExperienceNotificationPhase.LevelFadeIn,
                            ElapsedMilliseconds = 0,
                            LevelElapsedMilliseconds = 0,
                            PhasePresented = false,
                            LevelTextPublished = true
                        };
                        _levelTimestamp = clock.LevelMilliseconds;
                        _sounds.Add(new(current.Sequence, _source.LevelSound, FalloutExperienceSoundState.Pending)); ++_revision;
                    }
                    else Phase(FalloutExperienceNotificationPhase.AwaitingHiddenFrame);
                    break;
                case FalloutExperienceNotificationPhase.LevelFadeIn when Complete(_source.LevelTextFadeSeconds):
                    Phase(FalloutExperienceNotificationPhase.LevelHold); break;
                case FalloutExperienceNotificationPhase.LevelHold when current.LevelElapsedMilliseconds > _source.LevelTextHoldMilliseconds:
                    _levelTimestamp = checked(clock.LevelMilliseconds + _source.LevelTextNextTimestampOffset);
                    if (_levelTimestamp > uint.MaxValue) throw new NotSupportedException("XP next timestamp requires its explicit source counter-wrap owner.");
                    Phase(FalloutExperienceNotificationPhase.LevelFadeOut); break;
                case FalloutExperienceNotificationPhase.LevelFadeOut when Complete(_source.LevelTextFadeSeconds):
                    Phase(FalloutExperienceNotificationPhase.AwaitingHiddenFrame); break;
            }
        }
        catch (Exception error) { RetainFailure(error); throw; }
    }

    private bool Complete(float seconds) => _display!.ElapsedMilliseconds >= seconds * 1000d;
    private void Phase(FalloutExperienceNotificationPhase phase)
    {
        _display = _display! with { Phase = phase, ElapsedMilliseconds = 0, PhasePresented = false }; ++_revision;
    }
    private void BeginMeter()
    {
        _frameGate.BeginMeter();
        var changes = _pending.ToArray();
        var sum = checked((int)changes.Sum(change => (long)change.Published - change.Previous));
        var level = _level(); var total = _experience(); var lower = _threshold(level); var upper = _threshold(checked(level + 1));
        if (changes.Any(change => !change.Queued || change.Level != level) || sum <= 0 || total != _observedExperience ||
            total - (long)sum < 0 || upper <= lower)
            throw new NotSupportedException("XP meter queue no longer joins its original player/threshold publication.");
        var end = Math.Min(total, upper); var start = Math.Clamp(total - sum, lower, upper);
        _display = new(checked(++_lastSequence), changes[0].Ordinal, changes[^1].Ordinal, sum, level, total, level, start, end,
            FalloutExperienceNotificationPhase.MeterFadeIn, 0, 0, false, total > upper, false, true);
        _pending.Clear(); _sounds.Add(new(_display.Sequence, _source.GainSound, FalloutExperienceSoundState.Pending)); ++_revision;
    }

    private void BeginLevelText(FalloutExperienceClockReading clock)
    {
        // A later due level has a genuine retained player request but needs no
        // invented XP award or meter. The source flags schedule this arm even
        // when the XP queue is empty after an actual menu-submit reset.
        var level = _level(); var total = _experience(); var lower = _threshold(level); var upper = _threshold(checked(level + 1));
        if (level >= _maximum() || total < upper || upper <= lower || _retiredThrough != _lastOrdinal)
            throw new InvalidDataException("Standalone level text does not join the current retained advancement request.");
        _display = new(checked(++_lastSequence), _retiredThrough, _retiredThrough, 0, level, total, level, lower, upper,
            FalloutExperienceNotificationPhase.LevelFadeIn, 0, 0, false, total > upper, true, false);
        _idlePresented = false; _levelTimestamp = clock.LevelMilliseconds;
        _sounds.Add(new(_display.Sequence, _source.LevelSound, FalloutExperienceSoundState.Pending)); ++_revision;
    }

    internal FalloutExperienceNotificationFrame? Frame(Guid view)
    {
        RequireView(view); if (_display is not { } display) return null;
        var phase = display.Phase;
        var alpha = phase switch
        {
            FalloutExperienceNotificationPhase.MeterFadeIn => Fraction(_source.MeterFadeSeconds),
            FalloutExperienceNotificationPhase.MeterSweep => 1,
            FalloutExperienceNotificationPhase.MeterFadeOut => 1 - Fraction(_source.MeterFadeSeconds),
            _ => 0,
        };
        var levelAlpha = phase switch
        {
            FalloutExperienceNotificationPhase.LevelFadeIn => Fraction(_source.LevelTextFadeSeconds),
            FalloutExperienceNotificationPhase.LevelHold => 1,
            FalloutExperienceNotificationPhase.LevelFadeOut => 1 - Fraction(_source.LevelTextFadeSeconds),
            _ => 0,
        };
        var lower = _threshold(display.SweepLevel); var upper = _threshold(checked(display.SweepLevel + 1));
        if (upper <= lower) throw new InvalidDataException("XP frame has no increasing threshold interval.");
        var target = phase == FalloutExperienceNotificationPhase.MeterFadeIn ? display.SweepStartExperience :
            phase == FalloutExperienceNotificationPhase.MeterSweep ? display.SweepStartExperience +
                (display.SweepEndExperience - display.SweepStartExperience) * Fraction(display.ThresholdCrossing ? _source.ThresholdSweepSeconds : _source.MeterSweepSeconds) : display.SweepEndExperience;
        // The original initial pointer consumer applies both source bound
        // selectors around its affine map. This is that authored interval,
        // not an invented XP storage/threshold clamp.
        var pointer = Math.Clamp((target - lower) / (upper - (float)lower), 0, 1);
        if (!float.IsFinite(pointer)) throw new InvalidDataException("XP pointer mapping is nonfinite.");
        return new(view, _revision, display.Sequence, phase, alpha, levelAlpha, pointer, display.Amount,
            display.SweepLevel, checked(display.SweepLevel + 1), display.SweepLevel != display.Level,
            phase is FalloutExperienceNotificationPhase.MeterFadeIn or FalloutExperienceNotificationPhase.MeterSweep or FalloutExperienceNotificationPhase.MeterFadeOut,
            phase is FalloutExperienceNotificationPhase.LevelFadeIn or FalloutExperienceNotificationPhase.LevelHold or FalloutExperienceNotificationPhase.LevelFadeOut,
            _levelTimestamp is { } stamp ? (float)stamp : null,
            phase is FalloutExperienceNotificationPhase.LevelFadeIn or FalloutExperienceNotificationPhase.LevelHold or FalloutExperienceNotificationPhase.LevelFadeOut);
    }
    private float Fraction(float seconds) => Math.Clamp((float)(_display!.ElapsedMilliseconds / (seconds * 1000d)), 0, 1);

    internal void Presented(FalloutExperienceNotificationFrame frame, FalloutExperienceClockReading clock)
    {
        RequireView(frame.View);
        if (_display is not { } display || frame.Revision != _revision || frame.Sequence != display.Sequence || frame.Phase != display.Phase)
            throw new InvalidOperationException("Stale XP draw receipt cannot retire or advance the current source phase.");
        var milliseconds = clock.TilesMilliseconds;
        if (milliseconds < 0 || clock.LevelMilliseconds < 0 || _lastClock is { } last && milliseconds < last ||
            _lastLevelClock is { } lastLevel && clock.LevelMilliseconds < lastLevel)
            throw new InvalidDataException("XP presentation receipt moved the UI clock backwards.");
        if (_sounds.Any(sound => sound.Sequence == display.Sequence && sound.State == FalloutExperienceSoundState.Pending))
            throw new InvalidOperationException("XP presentation cannot bypass its source sound publication.");
        if (display.Phase == FalloutExperienceNotificationPhase.AwaitingHiddenFrame)
        {
            if (frame.MeterVisible || frame.LevelVisible || frame.MeterAlpha != 0 || frame.LevelAlpha != 0)
                throw new InvalidDataException("XP retirement receipt still publishes source notification pixels.");
            if (display.LevelTextPublished) _frameGate.LevelTextHidden(display.Sequence, checked(display.Level + 1));
            _retiredThrough = display.LastOrdinal; _retiredSequence = display.Sequence;
            _display = null; _idlePresented = true; ++_revision;
            _sounds.RemoveAll(sound => sound.Sequence <= _retiredSequence && sound.State == FalloutExperienceSoundState.Finished);
        }
        else
        {
            if (!display.PhasePresented) { _lastClock = milliseconds; _lastLevelClock = clock.LevelMilliseconds; }
            _display = display with { PhasePresented = true };
        }
    }

    internal ulong IdleRevision(Guid view) { RequireView(view); return _revision; }
    internal bool CurrentSubmission(FalloutExperienceNotificationFrame frame) => _failure is null && !_disposed &&
        frame.View == _view && _display is { } display && frame.Revision == _revision &&
        frame.Sequence == display.Sequence && frame.Phase == display.Phase;
    internal bool CurrentIdleSubmission(Guid view, ulong revision) => _failure is null && !_disposed && view == _view &&
        revision == _revision && _display is null && _pending.Count == 0 && _retiredThrough == _lastOrdinal;
    internal void PresentedIdle(Guid view, ulong revision)
    {
        RequireView(view);
        if (!CurrentIdleSubmission(view, revision))
            throw new InvalidOperationException("Stale idle HUD receipt cannot settle an unseen XP publication.");
        _idlePresented = true;
    }

    internal IReadOnlyList<FalloutExperienceSoundReceipt> PendingSounds(Guid view)
    {
        RequireView(view); return _sounds.Where(sound => sound.State == FalloutExperienceSoundState.Pending).ToArray();
    }
    internal void SoundStarted(Guid view, FalloutExperienceSoundReceipt sound)
    {
        RequireView(view); var at = _sounds.IndexOf(sound);
        if (at < 0 || sound.State != FalloutExperienceSoundState.Pending || sound.Sequence != _display?.Sequence)
            throw new InvalidOperationException("XP sound does not own the current source phase.");
        _sounds[at] = sound with { State = FalloutExperienceSoundState.Playing };
    }
    internal void SoundFinished(Guid view, ulong sequence, string editorId)
    {
        RequireView(view); var at = _sounds.FindIndex(sound => sound.Sequence == sequence && sound.EditorId == editorId);
        if (at < 0 || _sounds[at].State != FalloutExperienceSoundState.Playing)
            throw new InvalidOperationException("XP sound retirement is absent, repeated or foreign.");
        _sounds[at] = _sounds[at] with { State = FalloutExperienceSoundState.Finished };
        if (sequence <= _retiredSequence) _sounds.RemoveAt(at);
    }
    internal void RetainFailure(Exception error)
    {
        _failure ??= string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
    }
    private void RequireView(Guid view)
    {
        RequireHealthy(); if (view == Guid.Empty || view != _view || !_nativeOwned)
            throw new InvalidOperationException("XP notification has no matching current native presentation lease.");
    }
    private void RequireHealthy()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FalloutExperienceNotifications));
        if (_failure is not null) throw new InvalidOperationException(_failure);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _events.ExperienceChanged -= Changed; _disposed = true; _nativeOwned = false;
        // A still-living native view must retire its exact lease. Disposing
        // gameplay never fabricates that separate native teardown receipt.
    }
}
