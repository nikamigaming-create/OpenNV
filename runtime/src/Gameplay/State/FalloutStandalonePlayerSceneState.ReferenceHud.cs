using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutStandalonePlayerSceneState
{
    private static void ValidateReferenceHud(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.ReferenceHudCall is not { } hud)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.ReferenceRelease or FalloutStandaloneScenePreludeStep.ActorScalarReset or FalloutStandaloneScenePreludeStep.ThreadContext or FalloutStandaloneScenePreludeStep.InputManagerQuery or FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player virtual lost its actually returned source HUD child.");
            return;
        }
        if (saved.ReferenceQuery is not { Returned: true } query || saved.Prelude is not { } prefix ||
            hud.Source != FalloutPlayerReferenceHudSource.Reviewed(saved.Source.Main).Identity || hud.Stack != saved.Stack ||
            hud.Process != query.Process || hud.Factory == Guid.Empty || hud.Main != prefix.Main || hud.MainOrdinal != prefix.MainOrdinal ||
            hud.Reference != query.Result || hud.Argument != saved.SceneReferenceArgument ||
            hud.Entered < 1 || hud.Changed < hud.Entered || hud.Hostile != 0 || hud.DeliveredArgument != 0 ||
            !Enum.IsDefined(hud.Step) || hud.Step is not (FalloutPlayerReferenceHudStep.Returned or FalloutPlayerReferenceHudStep.Failed) ||
            (hud.Step == FalloutPlayerReferenceHudStep.Failed) != (hud.Failure is not null) ||
            (hud.Step == FalloutPlayerReferenceHudStep.Failed) != (hud.FailedAt is not null) ||
            hud.Step == FalloutPlayerReferenceHudStep.Returned && (hud.ColorBits is null ||
                prefix.FailedAt is not (FalloutStandaloneScenePreludeStep.ReferenceRelease or FalloutStandaloneScenePreludeStep.ActorScalarReset or FalloutStandaloneScenePreludeStep.ThreadContext or FalloutStandaloneScenePreludeStep.InputManagerQuery or FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)) ||
            hud.Step == FalloutPlayerReferenceHudStep.Failed && (prefix.FailedAt != FalloutStandaloneScenePreludeStep.ReferenceFrame ||
                hud.Failure != saved.Failure || hud.FailedAt is not { } at || !Enum.IsDefined(at) ||
                at is FalloutPlayerReferenceHudStep.Returned or FalloutPlayerReferenceHudStep.Failed))
            throw new InvalidDataException("Player HUD receipt lost its actual source, process, flag stores or following failed service.");
    }
    private static void ValidateActorScalarReset(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.ActorScalarReset is not { } reset)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.ThreadContext or FalloutStandaloneScenePreludeStep.InputManagerQuery or FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player thread-context entry lost its actually returned Actor scalar-reset child.");
            return;
        }
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.ThreadContext or FalloutStandaloneScenePreludeStep.InputManagerQuery or FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            saved.ReferenceHudCall is not { Step: FalloutPlayerReferenceHudStep.Returned } ||
            reset.Main != prefix.Main || reset.MainOrdinal != prefix.MainOrdinal || reset.Entered <= prefix.Entered ||
            reset.Changed < reset.Entered || reset.Changed > saved.Sequence || reset.Fields is null || reset.Fields.Count != 4 ||
            reset.Stores != 4 || !reset.Returned || reset.Fields.Any(bits => bits != FalloutStandalonePlayerSceneSource.ActorSceneResetBits))
            throw new InvalidDataException("Player Actor reset omitted or changed an actual ordered scalar store.");
    }
    private static void ValidateThreadContextEntry(FalloutStandalonePlayerSceneSnapshot saved)
    {
        if (saved.ThreadContextEntry is not { } entry)
        {
            if (saved.Prelude?.FailedAt is FalloutStandaloneScenePreludeStep.InputManagerQuery or FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation)
                throw new InvalidDataException("Player input-manager query lost its actual entered thread-context scope.");
            return;
        }
        var source = FalloutPlayerThreadContextSource.Reviewed(saved.Source.Main);
        if (saved.Prelude is not { FailedAt: FalloutStandaloneScenePreludeStep.InputManagerQuery or FalloutStandaloneScenePreludeStep.ActorProcessQuery or FalloutStandaloneScenePreludeStep.ActorSceneContinuation or FalloutStandaloneScenePreludeStep.QueuedSceneNodes or FalloutStandaloneScenePreludeStep.ProcessSceneUpdate or FalloutStandaloneScenePreludeStep.PostProcessContext or FalloutStandaloneScenePreludeStep.TimedActions or FalloutStandaloneScenePreludeStep.PlayerSceneContinuation } prefix ||
            entry.Source != source.Identity || !ChildBelongsToParentProcessLineage(saved, entry.Process) ||
            entry.Factory == Guid.Empty || entry.Main != prefix.Main || entry.MainOrdinal != prefix.MainOrdinal || entry.Thread <= 0 ||
            entry.BeforeKind != source.InitialKind || entry.CurrentKind != source.PlayerKind || entry.Initialized != 1 ||
            !entry.InitializerReturned || !entry.ScopeEntered)
            throw new InvalidDataException("Player thread scope changed its source current-thread, initialization or independent kind store.");
    }
}
