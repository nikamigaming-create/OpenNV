using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// This transient lease is prepared before native teardown. It is never a save
// continuation and owns no model, script cursor, playback or guessed completion.
internal sealed class FalloutActorRetirementCandidate
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutReferenceInstance _state;
    private readonly FalloutReferenceSnapshot _snapshot;
    private readonly IReadOnlyList<FalloutAnimationSoundEvent> _history;
    private readonly IReadOnlyList<FalloutFiniteSoundVoice> _voices;
    private readonly string _stamp;
    private readonly string _blocker;
    private readonly string _initialFailure;
    private readonly string _committedFailure;
    private readonly Func<bool> _ready;
    private readonly Func<FalloutActorSelectionFailure>? _selectionCapture;
    private readonly Func<FalloutActorPackageBindingFailure>? _bindingCapture;
    private readonly Func<IReadOnlyList<FalloutFiniteSoundVoice>?> _wait;
    private bool _committed;
    internal ulong NativeOwner { get; }
    internal object State => new
    {
        nativeOwner = NativeOwner,
        reference = _state.Reference.ToString(),
        kind = _snapshot.SelectionFailure is not null ? "stopped-selection" : "stopped-binding",
        committed = _committed,
        authoritative = Authoritative,
        originalVoices = _voices,
        ready = Ready,
        boundary = "pre-teardown-nonaudio-copy;exact-native-finished-only;no-model-reread"
    };

    private FalloutActorRetirementCandidate(FalloutPluginStack records, FalloutReferenceInstance state,
        ulong nativeOwner, FalloutReferenceSnapshot snapshot, IReadOnlyList<FalloutFiniteSoundVoice> voices)
    {
        _records = records; _state = state; NativeOwner = nativeOwner;
        _snapshot = JsonSerializer.Deserialize<FalloutReferenceSnapshot>(JsonSerializer.Serialize(snapshot))!;
        _history = Array.AsReadOnly(state.AnimationSoundEvents.Events.Select(entry => entry.Copy()).ToArray());
        _voices = Array.AsReadOnly(voices.ToArray()); _blocker = state.ProcedureCaptureBlocker!;
        _initialFailure = FailureStamp();
        _committedFailure = JsonSerializer.Serialize(_snapshot.SelectionFailure is { } selection
            ? (object)selection : _snapshot.PackageBindingFailure);
        _stamp = Stamp(); _ready = () => Ready; _wait = Pending;
        if (snapshot.SelectionFailure is not null) _selectionCapture = CommitSelection;
        else _bindingCapture = CommitBinding;
    }

    internal static FalloutActorRetirementCandidate? Prepare(FalloutPluginStack records,
        FalloutReferenceInstance state, ulong nativeOwner, FalloutActorSelectionFailure? selection,
        FalloutActorPackageBindingFailure? binding)
    {
        if ((selection is null) == (binding is null))
            throw new ArgumentException("Retirement requires exactly one already-owned stopped procedure.");
        if (string.IsNullOrWhiteSpace(state.ProcedureCaptureBlocker)) return null;
        var voices = ReadLiveFiniteReceipts(records, state, nativeOwner);
        if (voices is null) return null;
        selection?.Validate(records, state); binding?.Validate(records, state);
        var snapshot = state.CaptureStoppedRetirementSnapshot(records, nativeOwner, selection, binding);
        FalloutReferenceSnapshot.Validate([snapshot]);
        FalloutActorStoppedPose.ValidateTarget(records, snapshot);
        return new(records, state, nativeOwner, snapshot, voices);
    }

    internal void Bind()
    {
        _state.StoppedRetirement = this;
        if (_selectionCapture is not null)
        {
            _state.CanCaptureSelectionFailure = _ready;
            _state.CaptureSelectionFailure = _selectionCapture;
        }
        else
        {
            _state.CanCapturePackageBindingFailure = _ready;
            _state.CapturePackageBindingFailure = _bindingCapture;
        }
        _state.PendingPackageBindingFiniteVoices = _wait;
    }

    private bool Authoritative => ReferenceEquals(_state.StoppedRetirement, this) &&
        ReferenceEquals(_state.PendingPackageBindingFiniteVoices, _wait) &&
        (_selectionCapture is not null
            ? ReferenceEquals(_state.CanCaptureSelectionFailure, _ready) && ReferenceEquals(_state.CaptureSelectionFailure, _selectionCapture)
            : ReferenceEquals(_state.CanCapturePackageBindingFailure, _ready) && ReferenceEquals(_state.CapturePackageBindingFailure, _bindingCapture)) &&
        _state.ProcedureCaptureBlocker == (_committed ? _snapshot.SelectionFailure?.Error ?? _snapshot.PackageBindingFailure!.Error : _blocker) &&
        FailureStamp() == (_committed ? _committedFailure : _initialFailure);

    private string FailureStamp() => JsonSerializer.Serialize(_snapshot.SelectionFailure is not null
        ? (object?)_state.SelectionFailure : _state.PackageBindingFailure);

    // All presentation capture callbacks must have retired before a candidate
    // can wait or commit. No old native callback is invoked to infer readiness.
    private bool ModelCapturesRetired => _state.CaptureEngagement is null && _state.CaptureRagdoll is null &&
        _state.CaptureCorpseEquipment is null && _state.CanCaptureCorpseEquipment is null &&
        _state.CaptureHeadTracking is null && _state.CapturePackageAssignment is null &&
        _state.CaptureFurniture is null && _state.CaptureDialogue is null && _state.CapturePendingPackageSelection is null &&
        _state.CaptureObjectAnimations is null && _state.QuerySpatialPlacement is null &&
        _state.QueryCurrentPackage is null && _state.QuerySitting is null &&
        (_selectionCapture is not null ? _state.CapturePackageBindingFailure is null : _state.CaptureSelectionFailure is null);

    private bool Unchanged => Authoritative && ModelCapturesRetired && Stamp() == _stamp;

    private string Stamp() => JsonSerializer.Serialize(new
    {
        _state.Reference,
        _state.Base,
        _state.Cell,
        _state.Enabled,
        _state.Destroyed,
        _state.DeletePending,
        _state.Deleted,
        _state.KnockedDown,
        _state.Unconscious,
        _state.Restrained,
        _state.Injury,
        _state.DeathCount,
        _state.HitReaction,
        _state.HitReactionFaultCaptureBlocker,
        _state.Engagement,
        ragdoll = _state.CaptureRagdoll is null ? _state.Ragdoll : _snapshot.Ragdoll,
        corpseEquipment = _state.CaptureCorpseEquipment is null ? _state.CorpseEquipment : _snapshot.CorpseEquipment,
        _state.CorpseEquipmentCaptureBlocker,
        animation = _state.Animation.Capture(),
        _state.PackageAssignment,
        _state.ScriptPackage,
        _state.PendingPackageChoice,
        _state.PackageMotion,
        _state.PendingPackageSelection,
        _state.FurnitureContinuation,
        _state.DialogueContinuation,
        otherFailure = _snapshot.SelectionFailure is not null ? (object?)_state.PackageBindingFailure : _state.SelectionFailure,
        _state.HeadTrackingRequired,
        _state.HeadTrackingCaptureBlocker,
        _state.HeadTracking,
        _state.Placement,
        _state.ObjectAnimations,
        soundRandom = _state.SoundRandom.State,
    });

    private bool Observe(out FalloutFiniteSoundVoice[] remaining)
    {
        remaining = [];
        if (!Unchanged) return false;
        var ledger = _state.AnimationSoundEvents;
        if (ledger.Events.Count != _history.Count) return false;
        var active = new List<FalloutFiniteSoundVoice>();
        for (var index = 0; index < _history.Count; index++)
        {
            var original = _history[index]; var current = ledger.Events[index];
            if (original.End == FalloutAnimationSoundEnd.Active)
            {
                if (current.End is not (FalloutAnimationSoundEnd.Active or FalloutAnimationSoundEnd.NativeFinished) ||
                    JsonSerializer.Serialize(current with { End = FalloutAnimationSoundEnd.Active }) != JsonSerializer.Serialize(original)) return false;
                if (current.End == FalloutAnimationSoundEnd.Active)
                    active.Add(_voices.Single(voice => voice.Generation == current.Generation));
            }
            else if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(original)) return false;
        }
        if (active.Count == 0) return ledger.CanCapture;
        var live = ReadLiveFiniteReceipts(_records, _state, NativeOwner);
        if (live is null || !live.SequenceEqual(active)) return false;
        remaining = active.ToArray(); return true;
    }

    private bool Ready => Observe(out var remaining) && remaining.Length == 0;
    private IReadOnlyList<FalloutFiniteSoundVoice>? Pending() =>
        Observe(out var remaining) && remaining.Length != 0 ? Array.AsReadOnly(remaining) : null;

    private void Commit()
    {
        if (!Ready) throw new NotSupportedException("Retired actor lacks its unchanged nonaudio copy and exact native Finished receipts.");
        if (_committed) return;
        // Only the stopped procedure copy is published. Current script locals,
        // inventory and unrelated world writes remain authoritative.
        if (_snapshot.SelectionFailure is { } selection)
        {
            _state.SelectionFailure = selection.Copy(); _state.ProcedureCaptureBlocker = selection.Error;
        }
        else
        {
            _state.PackageBindingFailure = _snapshot.PackageBindingFailure!.Copy();
            _state.ProcedureCaptureBlocker = _snapshot.PackageBindingFailure.Error;
        }
        _committed = true;
    }

    private FalloutActorSelectionFailure CommitSelection()
    { Commit(); return _snapshot.SelectionFailure!.Copy(); }
    private FalloutActorPackageBindingFailure CommitBinding()
    { Commit(); return _snapshot.PackageBindingFailure!.Copy(); }

    internal static IReadOnlyList<FalloutFiniteSoundVoice>? ReadLiveFiniteReceipts(
        FalloutPluginStack records, FalloutReferenceInstance state, ulong nativeOwner)
    {
        if (nativeOwner == 0 || !state.AnimationSoundEvents.CanAwaitNativeCompletion ||
            records.SoundVoices.PendingFiniteSourceVoices(state.Reference) is not { Count: > 0 } voices) return null;
        var active = state.AnimationSoundEvents.Events.Where(entry => entry.End == FalloutAnimationSoundEnd.Active)
            .ToDictionary(entry => entry.Generation);
        if (voices.Count != active.Count) return null;
        foreach (var voice in voices)
        {
            voice.Validate();
            if (voice.NativeOwner != nativeOwner || voice.Reference != state.Reference ||
                !active.Remove(voice.Generation, out var entry) || voice.Sound != entry.Sound ||
                voice.SoundSha256 != entry.SoundSha256 || voice.Path != entry.Path || voice.MediaSha256 != entry.MediaSha256) return null;
            var source = records.GetEffective(voice.Sound);
            if (source.Signature != "SOUN" ||
                !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(voice.SoundSha256, StringComparison.OrdinalIgnoreCase) ||
                FalloutSoundLoop.Read(FalloutSoundRecordReader.Read(source)).Mode != FalloutSoundLoopMode.None) return null;
        }
        return active.Count == 0 ? voices : null;
    }
}
