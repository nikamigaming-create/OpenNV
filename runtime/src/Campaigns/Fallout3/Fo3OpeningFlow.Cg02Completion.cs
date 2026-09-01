namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg02CompletionProgress(
    int Stage,
    double TimerRemainingSeconds,
    bool TimerAdvancing,
    double ImageSpaceElapsedSeconds,
    bool SoundStarted);

internal partial class Fo3OpeningFlow
{
    private void StartCg02CompletionTimer(
        Fo3Cg02CompletionRuntime completion,
        int activeStage,
        double initialSeconds,
        double imageSpaceElapsedSeconds,
        bool soundStarted,
        Action<Fo3Cg02CompletionProgress> progress,
        Action completed)
    {
        if (_cg02PictureCompletionTick is not null)
            return;
        if (activeStage != completion.TimerStage &&
            activeStage != completion.FlashStage ||
            !double.IsFinite(initialSeconds) || initialSeconds <= 0.0)
            throw new InvalidOperationException(
                "Fallout 3 CG02 completion timer state differs.");
        var stage = activeStage;
        var remaining = initialSeconds;
        if (stage == completion.FlashStage)
        {
            StartStage90ImageSpace(completion.ImageSpaceModifier);
            _stage90ImageSpaceElapsedSeconds = imageSpaceElapsedSeconds;
            if (!soundStarted)
            {
                StartStage90Sound(completion.Sound);
                soundStarted = true;
            }
        }
        _cg02PictureCompletionTick = delta =>
        {
            remaining = Math.Max(0.0, remaining - delta);
            progress(new Fo3Cg02CompletionProgress(
                stage, remaining, remaining > 0.0,
                stage == completion.FlashStage
                    ? Math.Min(completion.ImageSpaceModifier.DurationSeconds,
                        _stage90ImageSpaceElapsedSeconds)
                    : 0.0,
                soundStarted));
            if (remaining > 0.0)
                return;
            if (stage == completion.TimerStage)
            {
                stage = completion.FlashStage;
                remaining = completion.Stage98TimerSeconds;
                StartStage90ImageSpace(completion.ImageSpaceModifier);
                StartStage90Sound(completion.Sound);
                soundStarted = true;
                progress(new Fo3Cg02CompletionProgress(
                    stage, remaining, true, 0.0, true));
                return;
            }
            _cg02PictureCompletionTick = null;
            completed();
        };
    }
}
