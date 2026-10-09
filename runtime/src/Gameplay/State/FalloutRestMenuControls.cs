using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutRestMenuTarget { Action, Slider }
internal sealed record FalloutRestMenuControlFailure(long Attempt, FalloutRestMenuTarget Target, string Error);
internal sealed record FalloutRestMenuControlSnapshot(string Schema, string SourceSha256, long Request,
    bool Counting, int TargetWrites, int NativePublications, long Attempt, FalloutRestMenuControlFailure? Failure)
{
    internal void Validate(FalloutSleepWaitSource source)
    {
        source.Validate();
        if (Schema != FalloutRestMenuControls.Schema || SourceSha256 != source.Identity || Request < 1 ||
            TargetWrites is < 0 or > 2 || NativePublications < 0 || NativePublications > TargetWrites || Attempt is < 0 or > 1 ||
            !Counting && (TargetWrites != 0 || NativePublications != 0 || Attempt != 0 || Failure is not null) ||
            Counting && (Attempt != 1 || TargetWrites == 0) || Failure is null && Counting &&
                (TargetWrites != 2 || NativePublications != 2) ||
            Failure is { } failure && (failure.Attempt != Attempt || !Enum.IsDefined(failure.Target) ||
                (int)failure.Target != NativePublications || TargetWrites != NativePublications + 1 ||
                string.IsNullOrWhiteSpace(failure.Error)))
            throw new InvalidDataException("Rest menu lost its actual counting/target publication prefix.");
    }
}

// The source menu's counting byte is independent from player hours, sleep pose
// and gameplay Phase. Start writes it before publishing target=0 on the action
// and slider, in that order. Native publication is a separate receipt.
internal sealed class FalloutRestMenuControls
{
    internal const string Schema = "opennv-rest-menu-controls/v1";
    private readonly FalloutSleepWaitSource _source;
    internal long Request { get; }
    internal bool Counting { get; private set; }
    internal int TargetWrites { get; private set; }
    internal int NativePublications { get; private set; }
    internal long Attempt { get; private set; }
    internal FalloutRestMenuControlFailure? Failure { get; private set; }
    private bool _busy;
    internal string? SaveBlocker => _busy ? "rest-menu-target-publication-prefix" : null;
    internal object State => new { Request, Counting, TargetWrites, NativePublications, Attempt, Failure, saveBlocker = SaveBlocker };

    internal FalloutRestMenuControls(FalloutSleepWaitSource source, long request,
        FalloutRestMenuControlSnapshot? restore = null)
    {
        source.Validate(); if (request < 1) throw new ArgumentOutOfRangeException(nameof(request));
        _source = source; Request = request;
        if (restore is null) return; restore.Validate(source);
        if (restore.Request != request) throw new InvalidDataException("Cold menu controls have another actual source request.");
        Counting = restore.Counting; TargetWrites = restore.TargetWrites;
        NativePublications = restore.NativePublications; Attempt = restore.Attempt; Failure = restore.Failure;
        // A new process-local view may project these retained values. It never
        // calls Begin or publishes a missing original target operation again.
    }

    internal bool OverridesTarget(FalloutRestMenuTarget target)
    {
        if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target));
        return TargetWrites > (int)target;
    }

    internal void Begin(long request, Action<FalloutRestMenuTarget> publishActualNativeTarget)
    {
        ArgumentNullException.ThrowIfNull(publishActualNativeTarget);
        if (_busy || Failure is not null || Counting || request != Request)
            throw new InvalidOperationException("Rest controls have no new healthy counting operation.");
        Attempt = 1; Counting = true; _busy = true;
        try
        {
            foreach (var target in new[] { FalloutRestMenuTarget.Action, FalloutRestMenuTarget.Slider })
            {
                // C# commits the original target value; Godot must then publish
                // it to the same source view. A failed adapter cannot undo it.
                TargetWrites = (int)target + 1;
                try { publishActualNativeTarget(target); NativePublications++; }
                catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
                {
                    Failure = new(Attempt, target, error.GetType().Name + ": " + error.Message); throw;
                }
            }
        }
        finally { _busy = false; }
    }

    internal void RequireHealthy()
    {
        if (Failure is { } failure)
            throw new InvalidOperationException("Rest target publication retained " + failure.Target + ": " + failure.Error);
    }
    internal FalloutRestMenuControlSnapshot Capture()
    {
        if (SaveBlocker is { } blocker) throw new NotSupportedException("Capture requires " + blocker);
        var state = new FalloutRestMenuControlSnapshot(Schema, _source.Identity, Request, Counting,
            TargetWrites, NativePublications, Attempt, Failure);
        state.Validate(_source); return state;
    }
}
