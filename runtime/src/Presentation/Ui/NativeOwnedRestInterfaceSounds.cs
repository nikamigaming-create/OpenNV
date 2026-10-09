using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// This is the source rest-button caller, not a second native audio host.
// Its receipt follows the one campaign indexed voice through real callbacks.
internal sealed partial class NativeOwnedRestInterfaceSounds : Node
{
    private readonly Dictionary<FalloutInterfaceSoundCall, long> _calls = [];
    private FalloutRestInterfaceSounds? _owner;
    private NativeOwnedIndexedInterfaceSounds? _indexed;
    private Action<Exception>? _failed;
    private Guid _lease;
    private bool _retiring;
    private FalloutRestInterfaceSounds RestState => _owner ?? throw new InvalidOperationException("Rest cue adapter has no actual state owner.");
    internal string? SaveBlocker => RestState.SaveBlocker;

    internal static NativeOwnedRestInterfaceSounds Attach(Node actualCampaignHost, FalloutPluginStack records,
        FalloutSleepWait rest, FalloutRestInterfaceSounds owner, Action<Exception> failed,
        NativeOwnedIndexedInterfaceSounds? indexed = null)
    {
        ArgumentNullException.ThrowIfNull(actualCampaignHost); ArgumentNullException.ThrowIfNull(failed);
        owner.Source.RequireSource(records, rest.Source, indexed?.SoundState.Source.Catalogue);
        if (indexed is null || !GodotObject.IsInstanceValid(indexed) || !indexed.IsInsideTree() || indexed.IsQueuedForDeletion() ||
            !ReferenceEquals(indexed.SoundState.Source.Records, records) ||
            owner.Source.Start.CatalogueSha256 != indexed.SoundState.Source.Catalogue.Identity ||
            owner.Source.Cancel.CatalogueSha256 != indexed.SoundState.Source.Catalogue.Identity ||
            !GodotObject.IsInstanceValid(actualCampaignHost) || !actualCampaignHost.IsInsideTree() || actualCampaignHost.IsQueuedForDeletion())
            throw new NotSupportedException("Rest cues require the same living campaign indexed sound owner and actual source catalogue.");
        var adapter = new NativeOwnedRestInterfaceSounds();
        try
        {
            adapter._owner = owner; adapter._indexed = indexed; adapter._failed = failed;
            adapter.Name = "NativeSourceRestInterfaceCaller"; adapter.ProcessMode = ProcessModeEnum.Always;
            actualCampaignHost.AddChild(adapter);
            if (adapter.GetParent() != actualCampaignHost || !adapter.IsInsideTree())
                throw new InvalidOperationException("Rest cue caller did not attach to its actual campaign.");
            adapter._lease = owner.BindNative(); indexed.SoundState.Changed += adapter.Changed;
            adapter.RequireColdReceipts(); return adapter;
        }
        catch (Exception original)
        {
            List<Exception> errors = [original];
            try { adapter.RetireForSession(); } catch (Exception cleanup) when (Ordinary(cleanup)) { errors.Add(cleanup); }
            try { adapter.Free(); } catch (Exception cleanup) when (Ordinary(cleanup)) { errors.Add(cleanup); }
            if (errors.Count == 1) throw;
            throw new AggregateException("Rest caller retained original attachment/retirement failures.", errors);
        }
    }
    internal void PlayStart(FalloutRestRequest request) => Play(request, FalloutRestInterfaceCueKind.Start);
    internal void PlayCancel(FalloutRestRequest request) => Play(request, FalloutRestInterfaceCueKind.Cancel);
    private void Play(FalloutRestRequest request, FalloutRestInterfaceCueKind kind)
    {
        var indexed = _indexed ?? throw new NotSupportedException("Rest indexed playback owner is absent.");
        if (_retiring || !IsInsideTree() || _lease == Guid.Empty)
            throw new NotSupportedException("Rest interface caller has no current native publication.");
        var receipt = RestState.RequestCue(_lease, request, kind);
        var call = Call(receipt);
        _calls.Add(call, receipt.Sequence);
        try
        {
            var actual = indexed.Play(call);
            var current = RestState.Voice(receipt.Sequence);
            if (actual.Sound != receipt.Source.Sound || actual.RecordSha256 != receipt.Source.RecordSha256 ||
                !actual.PlayReturned || current.IndexedOrdinal != actual.Ordinal ||
                current.State != FalloutRestInterfaceVoiceState.NativeStarted || current.NativePlayer != actual.NativePlayer)
                throw new InvalidOperationException("Rest cue returned without its actual correlated original native start.");
        }
        catch (Exception error) when (Ordinary(error)) { Fail(receipt.Sequence, error); throw; }
    }
    private static FalloutInterfaceSoundCall Call(FalloutRestInterfaceVoice receipt) =>
        new("source-sleep-wait-menu-" + receipt.Kind, receipt.RequestOrdinal, receipt.Source.CallerIndex);

    private void Changed(FalloutInterfaceSoundVoice voice)
    {
        if (!_calls.TryGetValue(voice.Call, out var sequence)) return;
        var current = RestState.Voice(sequence);
        if (current.IndexedOrdinal is null) RestState.IndexedEntered(_lease, sequence, voice.Ordinal);
        else if (current.IndexedOrdinal != voice.Ordinal) throw new InvalidOperationException("Rest cue callback changed its actual indexed generation.");
        current = RestState.Voice(sequence);
        if (voice.NativePlayer is { } player)
        {
            if (current.NativePlayer is null) RestState.NativeAllocated(_lease, sequence, player);
            else if (current.NativePlayer != player) throw new InvalidOperationException("Rest cue callback changed its original player.");
            current = RestState.Voice(sequence);
            if (voice.PlayReturned && current.State == FalloutRestInterfaceVoiceState.NativeAllocated)
                RestState.NativeStarted(_lease, sequence, player);
            current = RestState.Voice(sequence);
            if (voice.FinishObserved && !current.NativeFinishObserved)
                RestState.NativeFinished(_lease, sequence, player);
            if (voice.VoiceRetired && !RestState.Voice(sequence).NativeVoiceRetired)
                RestState.NativeStopped(_lease, sequence, player);
            if (voice.PlayerDestroyed && voice.StreamDestroyed)
                RestState.NativeDestroyed(_lease, sequence, player, actualPlayerGone: true, actualStreamGone: true);
        }
        if (voice.Error is { } detail && RestState.Voice(sequence).State != FalloutRestInterfaceVoiceState.Failed)
            Fail(sequence, new InvalidOperationException("Actual indexed cue failed: " + voice.FailureType + ": " + detail));
    }
    private void RequireColdReceipts()
    {
        var indexed = _indexed ?? throw new InvalidOperationException("Rest source validation lost its shared owner.");
        indexed.SoundState.RequireRestContinuation(RestState.Capture());
    }
    private void Fail(long? sequence, Exception error)
    {
        RestState.Failed(_lease, sequence, error);
        GD.PushError("OPENNV_REST_INTERFACE_SOUND_FAIL " + error.GetType().Name + ": " + error.Message); _failed?.Invoke(error);
    }

    internal void RetireForSession()
    {
        if (_retiring && _lease == Guid.Empty) return;
        _retiring = true;
        if (_lease == Guid.Empty) return;
        // The shared native host must retire first, so all real callbacks are
        // observed here. Removing this subscription early would lose evidence.
        if (RestState.SaveBlocker is not null)
            throw new InvalidOperationException("Rest caller retirement requires actual indexed voice/resource closure first.");
        RestState.RetireNative(_lease); _lease = Guid.Empty;
        if (_indexed is { } indexed) indexed.SoundState.Changed -= Changed;
        _indexed = null; _calls.Clear();
    }
    public override void _ExitTree()
    {
        try { RetireForSession(); }
        catch (Exception error) when (Ordinary(error))
        { GD.PushError("OPENNV_REST_INTERFACE_RETIRE_FAIL " + error.GetType().Name + ": " + error.Message); _failed?.Invoke(error); }
    }
    private static bool Ordinary(Exception error) => FalloutPlayerPhysicalActivity.Ordinary(error);
}
