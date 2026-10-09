using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// Finite interface voices live under the real campaign presentation host, not
// the menu which can close before a cue finishes. No timer reports completion.
internal sealed partial class NativeOwnedRestInterfaceSounds : Node
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutRestInterfaceSounds _owner;
    private readonly Action<Exception> _failed;
    private readonly Dictionary<AudioStreamPlayer, FalloutRestInterfaceVoice> _voices = [];
    private readonly HashSet<AudioStreamPlayer> _stopping = [];
    private Guid _lease;
    private bool _retiring;
    internal string? SaveBlocker => _owner.SaveBlocker ?? (_voices.Count != 0 ? "rest-interface-native-retirement" : null);

    private NativeOwnedRestInterfaceSounds(FalloutPluginStack records, FalloutRestInterfaceSounds owner,
        Action<Exception> failed)
    {
        _records = records; _owner = owner; _failed = failed;
        Name = "NativeSourceRestInterfaceSounds"; ProcessMode = ProcessModeEnum.Always;
    }

    internal static NativeOwnedRestInterfaceSounds Attach(Node actualCampaignHost, FalloutPluginStack records,
        FalloutSleepWait rest, FalloutRestInterfaceSounds owner, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(actualCampaignHost); ArgumentNullException.ThrowIfNull(failed);
        owner.Source.RequireSource(records, rest.Source);
        if (!GodotObject.IsInstanceValid(actualCampaignHost) || !actualCampaignHost.IsInsideTree() ||
            actualCampaignHost.IsQueuedForDeletion() || records.OwnedSource is null ||
            !ReferenceEquals(RuntimeLiveContentSource.Current, records.OwnedSource))
            throw new NotSupportedException("Rest interface sound has no current actual campaign/native/owned-media host.");
        var adapter = new NativeOwnedRestInterfaceSounds(records, owner, failed);
        try
        {
            actualCampaignHost.AddChild(adapter);
            if (adapter.GetParent() != actualCampaignHost || !adapter.IsInsideTree())
                throw new InvalidOperationException("Rest interface sound adapter did not attach to its actual campaign host.");
            adapter._lease = owner.BindNative(); return adapter;
        }
        catch (Exception original)
        {
            List<Exception> failures = [original];
            try { adapter.RetireForSession(); }
            catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup)) { failures.Add(cleanup); }
            try { adapter.Free(); }
            catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup)) { failures.Add(cleanup); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Rest sound attachment retained its original and cleanup failures.", failures);
        }
    }

    internal void PlayStart(FalloutRestRequest request) => Play(request, FalloutRestInterfaceCueKind.Start);
    internal void PlayCancel(FalloutRestRequest request) => Play(request, FalloutRestInterfaceCueKind.Cancel);

    private void Play(FalloutRestRequest request, FalloutRestInterfaceCueKind kind)
    {
        if (_retiring || !IsInsideTree() || _lease == Guid.Empty)
            throw new NotSupportedException("Rest interface sound has no current actual native publication.");
        var receipt = _owner.RequestCue(_lease, request, kind);
        AudioStreamPlayer? player = null;
        try
        {
            var descriptor = receipt.Source.RequireCurrent(_records);
            if (descriptor.IsLooping)
                throw new NotSupportedException("Looping rest interface cues require their genuine explicit stop/cold-continuation owner.");
            player = NativeOwnedSoundPlayback.CreateSourceMenu(descriptor, _records, _owner.PlaybackSource);
            _voices.Add(player, receipt);
            _owner.NativeAllocated(_lease, receipt.Sequence, player.GetInstanceId());
            player.ProcessMode = ProcessModeEnum.Always;
            var actual = player;
            actual.Finished += () => Finished(actual);
            actual.TreeExiting += () => Exited(actual);
            AddChild(actual);
            if (actual.GetParent() != this || !actual.IsInsideTree())
                throw new InvalidOperationException("Rest cue did not attach its original native player.");
            actual.Play();
            if (!actual.Playing) throw new InvalidOperationException("Rest cue did not start its actual native playback.");
            _owner.NativeStarted(_lease, receipt.Sequence, actual.GetInstanceId());
        }
        catch (Exception original) when (FalloutPlayerPhysicalActivity.Ordinary(original))
        {
            List<Exception> failures = [original];
            try { Fail(receipt.Sequence, original); }
            catch (Exception retained) when (FalloutPlayerPhysicalActivity.Ordinary(retained)) { failures.Add(retained); }
            if (player is not null && GodotObject.IsInstanceValid(player))
                try { StopOriginal(player, receipt); }
                catch (Exception cleanup) when (FalloutPlayerPhysicalActivity.Ordinary(cleanup)) { failures.Add(cleanup); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Rest cue retained its attempted source/native prefix and cleanup failures.", failures);
        }
    }

    private void Finished(AudioStreamPlayer player)
    {
        if (!_voices.TryGetValue(player, out var receipt))
        {
            Fail(null, new InvalidOperationException("Rest cue emitted Finished without its actual original voice.")); return;
        }
        try
        {
            _owner.NativeFinished(_lease, receipt.Sequence, player.GetInstanceId());
            player.QueueFree(); _voices.Remove(player);
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { Fail(receipt.Sequence, error); }
    }

    private void Exited(AudioStreamPlayer player)
    {
        if (_stopping.Contains(player) || !_voices.TryGetValue(player, out var receipt)) return;
        List<Exception> failures = [];
        try
        {
            if (!_retiring && !_owner.FinishedVoice(_lease, receipt.Sequence))
                failures.Add(new InvalidOperationException("Rest cue left its actual tree without Finished or admitted source-stop continuation."));
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { failures.Add(error); }
        try
        {
            player.Stop();
            if (player.Playing) throw new InvalidOperationException("Exited rest cue still owns active native playback.");
            _owner.NativeStopped(_lease, receipt.Sequence, player.GetInstanceId()); _voices.Remove(player);
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { failures.Add(error); }
        if (failures.Count != 0) Fail(receipt.Sequence,
            failures.Count == 1 ? failures[0] : new AggregateException("Rest cue exit retained completion/stop failures.", failures));
    }

    private void StopOriginal(AudioStreamPlayer player, FalloutRestInterfaceVoice receipt)
    {
        // This is the original wrapper, never rediscovery or freeing an ID.
        var id = player.GetInstanceId(); List<Exception> failures = [];
        _stopping.Add(player);
        try
        {
            try
            {
                player.Stop();
                if (player.Playing) throw new InvalidOperationException("Rest native player did not stop its actual voice.");
                _owner.NativeStopped(_lease, receipt.Sequence, id);
            }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { failures.Add(error); }
            try { player.Free(); _voices.Remove(player); _owner.NativeDestroyed(_lease, receipt.Sequence, id); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { failures.Add(error); }
        }
        finally { _stopping.Remove(player); }
        if (failures.Count != 0) throw new AggregateException("Rest original voice retained independent stop/reference retirement failures.", failures);
    }

    private void Fail(long? sequence, Exception error)
    {
        _owner.Failed(_lease, sequence, error);
        GD.PushError("OPENNV_REST_INTERFACE_SOUND_FAIL " + error.GetType().Name + ": " + error.Message);
        _failed(error);
    }

    internal void RetireForSession()
    {
        if (_retiring && _lease == Guid.Empty) return;
        _retiring = true;
        List<Exception> failures = [];
        foreach (var (player, receipt) in _voices.ToArray())
        {
            try { StopOriginal(player, receipt); }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
            {
                failures.Add(error);
                try { Fail(receipt.Sequence, error); }
                catch (Exception retained) when (FalloutPlayerPhysicalActivity.Ordinary(retained)) { failures.Add(retained); }
            }
        }
        if (_lease != Guid.Empty && _voices.Count == 0)
            try { _owner.RetireNative(_lease); _lease = Guid.Empty; }
            catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error)) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Rest native sound retirement retained original owner failures.", failures);
    }

    public override void _ExitTree()
    {
        try { RetireForSession(); }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            GD.PushError("OPENNV_REST_INTERFACE_SOUND_RETIRE_FAIL " + error.Message); _failed(error);
        }
    }
}
