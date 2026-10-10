using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedExperienceHud
{
    private readonly FalloutExperiencePresentationLifetime _presentationLifetime = new();
    private readonly Func<string?> _cancellation;
    internal object LifetimeState => new
    {
        cancellation = _presentationLifetime.Cancellation,
        retirementEntered = _presentationLifetime.RetirementEntered,
        retirementReturned = _presentationLifetime.RetirementReturned,
        retirementFailure = _presentationLifetime.Failure,
        nativeCleanupFailure = _presentationLifetime.RetirementFailure,
        nativeView = _view,
        postDrawBound = _postDrawBound,
        voices = _voices.Count,
        failure = Error,
    };

    private bool ObserveNativeUpdateLifetime() =>
        _presentationLifetime.BeginUpdate(_cancellation, RetireNativePresentation);

    internal void RetireForSession() =>
        _presentationLifetime.Retire("actual-native-session-retirement", RetireNativePresentation);

    private void RetireNativePresentation()
    {
        // Close every callable lane before retiring the view/voices. A queued
        // native Finished callback may no longer mutate the source sound list.
        _retiring = true;
        _drawFrame = null; _submittedFrame = null; _submittedIdleRevision = null;
        var failures = new List<Exception>();
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { failures.Add(error); _owner.RetainFailure(error); }
        }
        Attempt(() => SetProcess(false));
        Attempt(() => SetProcessInput(false));
        Attempt(() => Visible = false);
        Attempt(() =>
        {
            if (!_postDrawBound) return;
            RenderingServer.FramePostDraw -= FramePresented;
            _postDrawBound = false;
        });
        Attempt(() => { _viewport?.Dispose(); _viewport = null; });

        var voices = _voices.Keys.ToArray();
        _voices.Clear();
        foreach (var voice in voices)
        {
            Attempt(() => { if (GodotObject.IsInstanceValid(voice)) voice.Stop(); });
            // Cancellation may be observed inside this player's Finished
            // signal. Keep native deletion at the actual deferred tree lane.
            Attempt(() => { if (GodotObject.IsInstanceValid(voice) && !voice.IsQueuedForDeletion()) voice.QueueFree(); });
        }
        Attempt(() =>
        {
            if (_view == Guid.Empty) return;
            _owner.DetachPresentation(_view);
            _view = Guid.Empty;
        });
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("XP native presentation retirement retained independent failures.", failures);
    }
}
