using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutScriptEngineContexts? _scriptEngineContexts;
    internal object? ScriptEngineContextState => _scriptEngineContexts?.State;
    internal string? ScriptEngineContextSaveBlocker => CampaignChallengesConfigured && Challenges.Source is not null ?
        _scriptEngineContexts?.SaveBlocker ?? (_scriptEngineContexts is null ?
            "source-shared-immediate-script-interpreter-construction-unbound" :
            _processRuntime?.SourceScriptFrameSaveBlocker ?? (_processRuntime is null ? "source-Main-script-scalar-constructor-unbound" : null)) : null;
    internal void ConfigureCampaignScriptContexts(FalloutAdvancementRuntimeSource runtime,
        FalloutScriptEngineContextsSnapshot? contexts, FalloutMainScriptFrameSnapshot? main)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scriptEngineContexts is not null || !CampaignChallengesConfigured || Challenges.Source is null ||
            (contexts is null) != (main is null))
            throw new InvalidOperationException("Source challenge interpreter must join its actual campaign registry once.");
        var source = FalloutImmediateScriptSource.Read(runtime.Receipt);
        if (Challenges.Source.EngineSha256 != source.EngineSha256 || Challenges.Source.RuntimeSha256 != source.RuntimeSha256)
            throw new InvalidDataException("Script contexts selected a different actual campaign source.");
        var candidate = new FalloutScriptEngineContexts(source, contexts);
        try
        {
            candidate.RequireSources(records);
            ProcessRuntime.ConstructScriptFrame(source, main);
            BindChallengeGameModeSource(ProcessRuntime);
            _scriptEngineContexts = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }
    internal void ConfigureCampaignScriptEngineContexts(FalloutImmediateScriptSource source,
        FalloutScriptEngineContextsSnapshot? contexts = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); source.Validate();
        if (_scriptEngineContexts is not null || !CampaignChallengesConfigured || Challenges.Source is not { } challenges ||
            challenges.EngineSha256 != source.EngineSha256 || challenges.RuntimeSha256 != source.RuntimeSha256)
            throw new InvalidOperationException("Shared script interpreter constructor needs its exact current campaign source once.");
        var candidate = new FalloutScriptEngineContexts(source, contexts);
        try { candidate.RequireSources(records); _scriptEngineContexts = candidate; }
        catch { candidate.Dispose(); throw; }
    }
    internal IDisposable BindCampaignScriptFrameFade(FalloutInterfaceFade fade) => ProcessRuntime.BindScriptFrameFade(fade);
    internal void SampleCampaignScriptFrame(long actualMainFrame, FalloutMainScriptSampleSite site) =>
        ProcessRuntime.SampleScriptFrame(actualMainFrame, site);
    internal FalloutMainScriptFrameSnapshot CaptureCampaignScriptFrame() => ProcessRuntime.CaptureScriptFrame();
    internal FalloutScriptEngineContextsSnapshot CaptureCampaignScriptContexts() => ScriptEngineContexts().Capture();
    private FalloutScriptEngineContexts ScriptEngineContexts() => _scriptEngineContexts ??
        throw new NotSupportedException("source-shared-immediate-script-interpreter-construction-unbound");
    internal FalloutScriptEngineContexts.Lease EnterChallengeScriptContext(FalloutChallengeRewardInvocation reward)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Challenges.RequireReward(reward);
        return ScriptEngineContexts().Enter(reward.Target, reward.Program, 0, null, reward, immediate: true);
    }
    internal FalloutScriptEngineContexts.Lease? EnterCompiledScriptContext(FalloutFormKey caller,
        FalloutCompiledScriptProgram program, double seconds, FalloutFormKey? action, IFalloutCompiledEventLocalAuthority? locals)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // This packet admits the selected shared interpreter family. Other
        // executable contexts retain their independent current execution owner.
        if (!CampaignChallengesConfigured || Challenges.Source is null) return null;
        return ScriptEngineContexts().Enter(caller, program, seconds, action, locals, immediate: false);
    }
    internal float? CompiledScriptSeconds(double seconds) => _scriptEngineContexts is null ? null :
        double.IsFinite(seconds) && seconds >= 0 && float.IsFinite((float)seconds) ? (float)seconds :
        throw new NotSupportedException("Original script seconds has no finite nonnegative Float32 scalar.");
    internal FalloutChallengeGameModeFilter RequireChallengeImmediateGameModeScalar(FalloutChallengeRewardInvocation reward)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); Challenges.RequireReward(reward);
        ScriptEngineContexts().RequireImmediate(reward.Target, reward.Program, reward);
        if (!Menus.GameMode) return new(0, true, null);
        var producer = _challengeGameModeSource ??
            throw new NotSupportedException("source-challenge-immediate-GameMode-independent-predicate-unbound");
        var observation = producer.Observe();
        if (observation is null || observation.Ordinal <= 0 || observation.EngineSha256 != Challenges.Source?.EngineSha256 ||
            observation.ProducerSha256 != FalloutImmediateScriptSource.Read(Challenges.Source!).Identity)
            throw new InvalidDataException("Challenge GameMode scalar has no genuine current selected Main byte.");
        producer.RequireCurrent(observation);
        return new(observation.Allows ? 1 : 0, false, observation);
    }
    private void RetireCampaignScriptContexts() => _scriptEngineContexts?.Dispose();
}
