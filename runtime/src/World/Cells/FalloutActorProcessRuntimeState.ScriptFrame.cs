using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal enum FalloutMainScriptSampleSite { BeforeMainChildren, AfterMainChildren }
internal sealed record FalloutMainScriptFrameSnapshot(string Source, string Stack, long Changed,
    bool BlocksGameMode, long Frame, FalloutMainScriptSampleSite? LastSite,
    FalloutInterfaceFadeQuery? LastQuery, string? FailureType, string? Error);

internal sealed partial class FalloutActorProcessRuntimeState : IFalloutChallengeGameModeSource
{
    private FalloutImmediateScriptSource? _scriptFrameSource;
    private FalloutMainScriptFrameSnapshot? _scriptFrame;
    private FalloutInterfaceFade? _scriptFrameFade;
    private Guid _scriptFrameLease;
    internal object? SourceScriptFrameState => _scriptFrame;
    internal string? SourceScriptFrameSaveBlocker => _scriptFrame is null ? "source-Main-script-scalar-constructor-unbound" :
        _scriptFrame is { Error: null, LastSite: FalloutMainScriptSampleSite.BeforeMainChildren } ?
            "source-Main-script-scalar-second-sample-pending" : null;
    internal void ConstructScriptFrame(FalloutImmediateScriptSource source, FalloutMainScriptFrameSnapshot? restore, bool allowFailedCallerPrefix = false)
    {
        RequireNotBusy(); source.Validate();
        if (_scriptFrame is not null || _source.ExecutableSha256 != source.EngineSha256)
            throw new InvalidOperationException("Main script scalar constructor belongs to another or an already constructed source lifetime.");
        _scriptFrameSource = source;
        if (restore is null)
        {
            // This is the original loader-owned zero, before either real Main
            // write. It does not certify a frame, UI publication or readiness.
            _scriptFrame = new(source.Identity, _stack, Next(), false, 0, null, null, null, null);
            return;
        }
        ValidateScriptFrame(restore, allowFailedCallerPrefix);
        if (restore.Source != source.Identity || restore.Stack != _stack || restore.Changed > _sequence)
            throw new InvalidDataException("Cold Main script byte lost its exact selected source/captured Main sequence.");
        _scriptFrame = restore;
    }
    internal IDisposable BindScriptFrameFade(FalloutInterfaceFade fade)
    {
        RequireNotBusy(); ArgumentNullException.ThrowIfNull(fade); var source = ScriptFrameSource();
        if (_scriptFrameLease != Guid.Empty || fade.Source.Declaration.EngineSha256 != source.EngineSha256 ||
            fade.Source.RuntimeSha256 != source.RuntimeSha256 ||
            _scriptFrame!.LastQuery is { } previous && previous.Source != fade.Source.Identity)
            throw new InvalidDataException("Main script field must consume the same actual selected interface fade owner once.");
        _scriptFrameFade = fade; var lease = _scriptFrameLease = Guid.NewGuid();
        return new ScriptFrameFadeLease(this, lease);
    }
    private sealed class ScriptFrameFadeLease(FalloutActorProcessRuntimeState owner, Guid lease) : IDisposable
    {
        public void Dispose()
        {
            if (owner._scriptFrameLease != lease) return;
            if (owner._scriptCallerInvocation is not null)
            { owner.FaultMainScriptReentry(); throw new InvalidOperationException("Main source fade cannot retire during its actual entered child."); }
            owner._scriptFrameLease = Guid.Empty; owner._scriptFrameFade = null;
        }
    }
    // These are writes in the actual selected Main call sequence. The caller
    // must join both real sites; a Godot frame number alone cannot mint them.
    internal void SampleScriptFrame(long actualMainFrame, FalloutMainScriptSampleSite site)
    {
        RequireNotBusy(); RequireMainScriptSampleCaller(site); var source = ScriptFrameSource(); var before = _scriptFrame!;
        if (!Enum.IsDefined(site) || actualMainFrame <= 0 || actualMainFrame < before.Frame ||
            site == FalloutMainScriptSampleSite.BeforeMainChildren && actualMainFrame <= before.Frame ||
            site == FalloutMainScriptSampleSite.BeforeMainChildren && before.Frame != 0 &&
                before.LastSite != FalloutMainScriptSampleSite.AfterMainChildren ||
            site == FalloutMainScriptSampleSite.AfterMainChildren &&
                (before.Frame != actualMainFrame || before.LastSite != FalloutMainScriptSampleSite.BeforeMainChildren))
            throw new InvalidOperationException("Cached GameMode byte has no next actual ordered Main sample site.");
        if (before.Error is not null) throw new InvalidOperationException("Main script sample retains its failed source prefix: " + before.Error);
        _scriptFrame = before with { Changed = Next(), Frame = actualMainFrame, LastSite = site, LastQuery = null };
        try
        {
            var fade = _scriptFrameFade ?? throw new NotSupportedException("source-Main-cached-GameMode-interface-channel1-producer-unbound");
            if (_scriptFrameLease == Guid.Empty) throw new NotSupportedException("source-Main-cached-GameMode-interface-native-lifetime-retired");
            var query = fade.QueryIncreasingOpaque(1);
            if (query.Source != fade.Source.Identity || fade.Source.Declaration.EngineSha256 != source.EngineSha256)
                throw new InvalidDataException("Main cached byte read another selected interface channel source.");
            _scriptFrame = _scriptFrame with { BlocksGameMode = query.Value, LastQuery = query };
        }
        catch (Exception failure)
        {
            _scriptFrame = _scriptFrame with { FailureType = failure.GetType().FullName ?? failure.GetType().Name, Error = failure.Message };
            throw;
        }
    }
    private FalloutImmediateScriptSource ScriptFrameSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _scriptFrameSource ?? throw new NotSupportedException("Source Main cached GameMode scalar constructor is absent.");
    }
    public FalloutChallengeGameModeObservation Observe()
    {
        var source = ScriptFrameSource(); var frame = _scriptFrame!;
        if (!source.HasImmediateInterpreter) throw new NotSupportedException("FO3 Main sampling is distinct from an admitted challenge immediate-script scalar consumer.");
        if (MainScriptCallerFailure is { } caller) throw new InvalidOperationException("Source Main caller retains its exact entered child prefix: " + caller);
        if (frame.Error is not null) throw new InvalidOperationException("Source Main GameMode sample retains its exact failed write: " + frame.Error);
        return new(source.EngineSha256, source.Identity, frame.Changed, !frame.BlocksGameMode);
    }
    public void RequireCurrent(FalloutChallengeGameModeObservation observation)
    {
        if (observation != Observe()) throw new InvalidDataException("Immediate script scalar read a stale or foreign actual Main field.");
    }
    internal FalloutMainScriptFrameSnapshot CaptureScriptFrame()
    {
        RequireNotBusy(); _ = ScriptFrameSource();
        if (SourceScriptFrameSaveBlocker is { } blocker) throw new NotSupportedException(blocker);
        ValidateScriptFrame(_scriptFrame!); return _scriptFrame!;
    }
    internal static void ValidateScriptFrame(FalloutMainScriptFrameSnapshot frame, bool allowFailedCallerPrefix = false)
    {
        if (frame is null || !FalloutAdvancementRuntimeReceipt.Digest(frame.Source) || string.IsNullOrWhiteSpace(frame.Stack) ||
            frame.Changed <= 0 || frame.Frame < 0 || (frame.Error is null) != (frame.FailureType is null) ||
            frame.LastSite is { } site && !Enum.IsDefined(site) ||
            frame.Frame == 0 && (frame.LastSite is not null || frame.LastQuery is not null || frame.BlocksGameMode || frame.Error is not null) ||
            frame.Frame != 0 && (frame.LastSite is null || frame.Error is null && frame.LastQuery is null) ||
            frame.LastSite == FalloutMainScriptSampleSite.BeforeMainChildren && frame.Error is null && !allowFailedCallerPrefix ||
            frame.LastQuery is { } query && (query.Channel != 1 || query.Value != frame.BlocksGameMode))
            throw new InvalidDataException("Cached Main GameMode byte omits its source constructor/sample/failure prefix.");
        frame.LastQuery?.Validate();
    }
}
