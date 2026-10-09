namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutExperienceNotifications
{
    internal FalloutExperienceNotificationObservation ObserveOriginalUiFrameGate()
    {
        var notice = Observe();
        var state = notice.State == FalloutExperienceNotificationFact.Unowned ? notice.State :
            _frameGate.Ready && _nativeOwned && _idlePresented ?
                FalloutExperienceNotificationFact.Satisfied : FalloutExperienceNotificationFact.Held;
        return new(state, $"original-experience-frame:{_frameGate.Contract}:{_frameGate.Flags}:" +
            $"ready={_frameGate.Ready}:native={_nativeOwned}:freshIdle={_idlePresented}" +
            (_failure is null ? "" : ":" + _failure));
    }

    internal void CompletedLevelMenuSubmitted(FalloutLevelUpMenuSession menu)
    {
        RequireHealthy();
        try
        {
            if (!_nativeOwned || !_idlePresented || _display is not null || _pending.Count != 0)
                throw new InvalidOperationException("Level-menu submission does not own a completed current source XP frame.");
            _frameGate.CompletedMenuSubmitted(menu); ++_revision;
        }
        catch (Exception error) { RetainFailure(error); throw; }
    }
}
