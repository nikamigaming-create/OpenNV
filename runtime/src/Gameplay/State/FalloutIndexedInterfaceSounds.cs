using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutInterfaceSoundCall(string Owner, long Occurrence, int Index,
    FalloutFormKey? SourceForm = null, int BranchOrdinal = 0)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Owner) || Occurrence <= 0 || BranchOrdinal < 0 ||
            SourceForm is { } form && (form.ObjectId == 0 || string.IsNullOrWhiteSpace(form.OwnerPlugin)))
            throw new InvalidDataException("Interface sound has no actual source caller/occurrence identity.");
    }
}

internal enum FalloutInterfaceVoicePhase { Entered, Resolved, SourceSilent, Allocated, Started, Finished, Stopped, Failed }
internal sealed record FalloutInterfaceSoundVoice(long Ordinal, FalloutInterfaceSoundCall Call,
    FalloutInterfaceSoundEntry Entry, FalloutInterfaceVoicePhase Phase, RuntimeSaveProcessIdentity Process,
    FalloutFormKey? Sound = null, string? Winner = null, string? RecordSha256 = null,
    string? LogicalPath = null, FalloutSoundFlags? OriginalFlags = null,
    ulong? NativePlayer = null, ulong? NativeStream = null, string? MediaSha256 = null,
    bool VoiceRetired = false, bool PlayerDestroyed = false, bool StreamReferenceReleased = false,
    bool StreamDestroyed = false, string? FailureType = null, string? Error = null,
    bool PlayReturned = false, bool FinishObserved = false)
{
    internal bool Pending => Phase is FalloutInterfaceVoicePhase.Entered or FalloutInterfaceVoicePhase.Resolved or
        FalloutInterfaceVoicePhase.Allocated or FalloutInterfaceVoicePhase.Started ||
        NativePlayer is not null && (!VoiceRetired || !PlayerDestroyed || !StreamReferenceReleased || !StreamDestroyed);
}

internal sealed record FalloutIndexedInterfaceSoundSnapshot(string Schema, string SourceSha256,
    long LastOrdinal, IReadOnlyList<FalloutInterfaceSoundVoice> Voices, string? Failure);

// The source call, original named lookup, native Play, Finished, voice stop,
// player deletion and fresh decoded-resource retirement are independent facts.
// Only the actual attached native host may publish those facts through its lease.
internal sealed class FalloutIndexedInterfaceSounds
{
    internal const string Schema = "opennv-indexed-interface-sounds/v1";
    private readonly List<FalloutInterfaceSoundVoice> _voices = [];
    private Guid? _native;
    private Func<FalloutInterfaceSoundCall, long>? _play;
    private bool _retired;
    private string? _failure;
    internal FalloutIndexedInterfaceSoundSource Source { get; }
    internal string? Failure => _failure;
    internal string? SaveBlocker => _voices.Any(row => row.Pending) ? "indexed-interface-finite-native-audio" : null;
    internal long LastOrdinal => _voices.Count;
    internal event Action<FalloutInterfaceSoundVoice>? Changed;
    internal object State => new { source = Source.Identity, catalogue = Source.Catalogue,
        voices = _voices.ToArray(), failure = _failure, nativePublished = _native is not null,
        saveBlocker = SaveBlocker, dialogueAndMessageTileProducer = "unowned" };

    internal FalloutIndexedInterfaceSounds(FalloutIndexedInterfaceSoundSource source,
        FalloutIndexedInterfaceSoundSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(source); Source = source;
        if (restore is null) return;
        if (restore.Schema != Schema || restore.SourceSha256 != source.Identity || restore.Voices is null ||
            restore.LastOrdinal != restore.Voices.Count || restore.Failure is not null && string.IsNullOrWhiteSpace(restore.Failure))
            throw new InvalidDataException("Cold indexed interface audio lacks its complete selected source/caller ledger.");
        var mediaHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in restore.Voices)
        {
            if (row is null || row.Ordinal != _voices.Count + 1L || row.Call is null || row.Entry is null ||
                !Enum.IsDefined(row.Phase) || row.Pending || row.Process is null ||
                row.Entry != source.Catalogue.Resolve(row.Call.Index) ||
                (row.Error is null) != (row.FailureType is null) || row.Error is not null && string.IsNullOrWhiteSpace(row.Error) ||
                (row.Phase == FalloutInterfaceVoicePhase.Failed) != (row.Error is not null) ||
                _voices.Any(prior => prior.Call == row.Call))
                throw new InvalidDataException("Cold indexed interface audio changed an attempted source/native prefix.");
            row.Call.Validate(); row.Process.Validate();
            RequireIdentityShape(row);
            if (row.Phase != FalloutInterfaceVoicePhase.Failed) RequireSource(row);
            else if (row.Sound is not null) RequireSource(row);
            if (row.MediaSha256 is { } expected)
            {
                var path = row.LogicalPath ?? throw new InvalidDataException("Cold indexed media has no actual prepared path.");
                if (!mediaHashes.TryGetValue(path, out var actual))
                    mediaHashes.Add(path, actual = Source.ReadPreparedMediaSha256(path));
                if (actual != expected) throw new InvalidDataException("Cold indexed sound changed its complete winning media bytes.");
            }
            _voices.Add(row);
        }
        if (_voices.Any(row => row.Error is not null) && restore.Failure is null)
            throw new InvalidDataException("Cold indexed interface audio removed its retained failure.");
        _failure = restore.Failure;
        // A settled native prefix is evidence. No old sound or callback is replayed.
    }

    internal Guid BindNative(Func<FalloutInterfaceSoundCall, long> play)
    {
        ObjectDisposedException.ThrowIf(_retired, this); ArgumentNullException.ThrowIfNull(play);
        if (_native is not null || SaveBlocker is not null) throw new InvalidOperationException("Indexed audio already has a native host or an unresolved voice.");
        _play = play; return (_native = Guid.NewGuid()).Value;
    }

    internal FalloutInterfaceSoundVoice Play(FalloutInterfaceSoundCall call)
    {
        ObjectDisposedException.ThrowIf(_retired, this); call.Validate();
        var play = _play ?? throw new NotSupportedException("Original indexed cue has no actual campaign native playback owner.");
        var ordinal = play(call); var row = Voice(ordinal);
        if (row.Call != call || row.Phase is not (FalloutInterfaceVoicePhase.Started or FalloutInterfaceVoicePhase.SourceSilent))
            throw new InvalidOperationException("Indexed cue returned without its genuine source silence or actual native Play receipt.");
        return row;
    }

    internal FalloutInterfaceSoundVoice Enter(Guid lease, FalloutInterfaceSoundCall call)
    {
        RequireNative(lease); call.Validate();
        if (_failure is not null) throw new InvalidOperationException("Indexed interface audio has a closed failed prefix: " + _failure);
        if (_voices.Any(row => row.Call == call)) throw new InvalidOperationException("Original indexed caller occurrence entered twice.");
        if (call.SourceForm is { } form) _ = Source.Records.GetEffective(form);
        var row = new FalloutInterfaceSoundVoice(checked(_voices.Count + 1L), call,
            Source.Catalogue.Resolve(call.Index), FalloutInterfaceVoicePhase.Entered, RuntimeSaveProcessIdentity.Current);
        _voices.Add(row); Notify(row); return row;
    }

    internal FalloutIndexedInterfaceCue Resolve(Guid lease, long ordinal)
    {
        RequireNative(lease); var row = Voice(ordinal);
        if (row.Phase != FalloutInterfaceVoicePhase.Entered) throw new InvalidOperationException("Indexed cue has no first source resolution prefix.");
        var cue = Source.Resolve(row.Call.Index);
        if (cue.Entry != row.Entry) throw new InvalidDataException("Indexed cue changed its original branch.");
        Set(row with { Phase = cue.Descriptor is null ? FalloutInterfaceVoicePhase.SourceSilent : FalloutInterfaceVoicePhase.Resolved,
            Sound = cue.Sound, Winner = cue.Winner, RecordSha256 = cue.RecordSha256,
            LogicalPath = cue.Descriptor?.LogicalPath, OriginalFlags = cue.OriginalFlags });
        return cue;
    }

    internal void Allocated(Guid lease, long ordinal, ulong player, ulong stream)
    {
        RequireNative(lease); var row = Voice(ordinal);
        if (row.Phase != FalloutInterfaceVoicePhase.Resolved || player == 0 || stream == 0 || player == stream)
            throw new InvalidOperationException("Indexed cue has no unique actual player/resource allocation receipt.");
        Set(row with { Phase = FalloutInterfaceVoicePhase.Allocated, NativePlayer = player, NativeStream = stream });
    }
    internal void BoundMedia(Guid lease, long ordinal, ulong player, string mediaSha256)
    {
        RequireNative(lease); var row = Matching(ordinal, player);
        if (row.Phase != FalloutInterfaceVoicePhase.Allocated || row.MediaSha256 is not null ||
            !FalloutAdvancementRuntimeReceipt.Digest(mediaSha256.ToLowerInvariant()))
            throw new InvalidDataException("Original indexed voice has no unique complete owned media identity.");
        Set(row with { MediaSha256 = mediaSha256.ToLowerInvariant() });
    }

    internal void Started(Guid lease, long ordinal, ulong player)
    {
        RequireNative(lease); var row = Matching(ordinal, player);
        if (row.Phase != FalloutInterfaceVoicePhase.Allocated || row.MediaSha256 is null)
            throw new InvalidOperationException("Indexed cue did not enter native Play once with its actual media.");
        Set(row with { Phase = FalloutInterfaceVoicePhase.Started, PlayReturned = true });
    }

    internal void Finished(Guid lease, long ordinal, ulong player)
    {
        RequireNative(lease); var row = Matching(ordinal, player);
        if (row.Phase != FalloutInterfaceVoicePhase.Started) throw new InvalidOperationException("Indexed cue emitted an unowned or repeated Finished callback.");
        Set(row with { Phase = FalloutInterfaceVoicePhase.Finished, VoiceRetired = true, FinishObserved = true });
    }

    internal void Stopped(Guid lease, long ordinal, ulong player)
    {
        RequireNative(lease); var row = Matching(ordinal, player);
        Set(row with { Phase = row.Phase is FalloutInterfaceVoicePhase.Finished or FalloutInterfaceVoicePhase.Failed ? row.Phase : FalloutInterfaceVoicePhase.Stopped,
            VoiceRetired = true });
    }

    internal void ReleasedStream(Guid lease, long ordinal, ulong player, ulong stream)
    {
        RequireNative(lease); var row = Matching(ordinal, player);
        if (!row.VoiceRetired || row.NativeStream != stream) throw new InvalidOperationException("Indexed cue resource release has no original stopped/finished voice.");
        Set(row with { StreamReferenceReleased = true });
    }

    internal void Destroyed(Guid lease, long ordinal, ulong player, ulong stream, bool playerGone, bool streamGone)
    {
        RequireNative(lease); var row = Matching(ordinal, player);
        if (!row.VoiceRetired || !row.StreamReferenceReleased || row.NativeStream != stream || !playerGone || !streamGone)
            throw new InvalidOperationException("Indexed cue lacks independently observed original player/resource deletion.");
        Set(row with { PlayerDestroyed = true, StreamDestroyed = true });
    }

    internal void Failed(Guid lease, long ordinal, Exception error)
    {
        RequireNative(lease); ArgumentNullException.ThrowIfNull(error); var row = Voice(ordinal);
        var detail = error.GetType().Name + ": " + error.Message; _failure ??= detail;
        Set(row with { Phase = FalloutInterfaceVoicePhase.Failed, FailureType = row.FailureType ?? error.GetType().Name,
            Error = row.Error ?? error.Message });
    }

    internal FalloutInterfaceSoundVoice Voice(long ordinal) => ordinal > 0 && ordinal <= _voices.Count ?
        _voices[checked((int)ordinal - 1)] : throw new InvalidDataException("Interface callback has no actual source ordinal.");
    internal void RequireCall(long ordinal, FalloutInterfaceSoundCall call, bool returned)
    {
        var row = Voice(ordinal);
        if (row.Call != call || returned && !row.PlayReturned && row.Phase != FalloutInterfaceVoicePhase.SourceSilent)
            throw new InvalidDataException("Source caller does not share its actual indexed audio receipt.");
        if (row.Sound is not null || row.Phase == FalloutInterfaceVoicePhase.SourceSilent) RequireSource(row);
    }

    internal FalloutIndexedInterfaceSoundSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (SaveBlocker is { } blocker) throw new NotSupportedException("Indexed sound capture requires " + blocker + "; no cold sample cursor is fabricated.");
        return new(Schema, Source.Identity, _voices.Count, _voices.ToArray(), _failure);
    }
    internal void RequireRestContinuation(FalloutRestInterfaceSoundSnapshot saved)
    {
        if (saved.Schema != FalloutRestInterfaceSounds.Schema || saved.Voices is null)
            throw new InvalidDataException("Indexed audio requires the current complete rest caller ledger.");
        foreach (var receipt in saved.Voices)
        {
            if (receipt.IndexedOrdinal is not { } ordinal)
            {
                if (receipt.NativePlayer is not null) throw new InvalidDataException("Cold rest native voice has no shared indexed ordinal.");
                continue;
            }
            var call = new FalloutInterfaceSoundCall("source-sleep-wait-menu-" + receipt.Kind,
                receipt.RequestOrdinal, receipt.Source.CallerIndex);
            RequireCall(ordinal, call, returned: receipt.NativePlayer is not null && receipt.State != FalloutRestInterfaceVoiceState.Failed);
            var voice = Voice(ordinal);
            if (voice.NativePlayer != receipt.NativePlayer || voice.PlayerDestroyed != receipt.NativeObjectRetired ||
                voice.StreamDestroyed != receipt.NativeMediaRetired || voice.VoiceRetired != receipt.NativeVoiceRetired ||
                voice.FinishObserved != receipt.NativeFinishObserved)
                throw new InvalidDataException("Cold rest and indexed audio changed their actual settled native prefix.");
        }
    }
    internal void RetireNative(Guid lease)
    {
        RequireNative(lease);
        if (SaveBlocker is not null) throw new InvalidOperationException("Indexed sound retirement still owns an unresolved native voice/resource.");
        _play = null; _native = null;
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_native is not null || SaveBlocker is not null) throw new InvalidOperationException("Indexed sound source retired before its actual native host.");
        _retired = true; Changed = null;
    }
    private FalloutInterfaceSoundVoice Matching(long ordinal, ulong player)
    {
        var row = Voice(ordinal);
        if (player == 0 || row.NativePlayer != player || !row.Process.SameNativeProcess(RuntimeSaveProcessIdentity.Current))
            throw new InvalidOperationException("Indexed sound callback belongs to another original player/process.");
        return row;
    }
    private void RequireNative(Guid lease)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (lease == Guid.Empty || _native != lease) throw new InvalidOperationException("Indexed sound callback has another native generation.");
    }
    private void RequireSource(FalloutInterfaceSoundVoice row)
    {
        if (Source.Catalogue.Resolve(row.Call.Index) != row.Entry)
            throw new InvalidDataException("Indexed sound continuation changed its original source branch.");
        Source.RequirePreparedDeclaration(row.Entry, row.Sound, row.Winner, row.RecordSha256, row.LogicalPath, row.OriginalFlags);
    }
    private static void RequireIdentityShape(FalloutInterfaceSoundVoice row)
    {
        if (row.Entry.Disposition == FalloutInterfaceSoundDisposition.SourceSilent &&
            (row.Sound is not null || row.NativePlayer is not null || row.NativeStream is not null || row.MediaSha256 is not null ||
                row.Phase is not (FalloutInterfaceVoicePhase.SourceSilent or FalloutInterfaceVoicePhase.Failed)) ||
            (row.NativePlayer is null) != (row.NativeStream is null) || row.NativePlayer == 0 || row.NativeStream == 0 ||
            row.NativePlayer is not null && row.NativePlayer == row.NativeStream ||
            row.NativePlayer is null && (row.VoiceRetired || row.PlayerDestroyed || row.StreamReferenceReleased || row.StreamDestroyed) ||
            row.NativePlayer is not null && (row.Sound is null || row.Winner is null || row.LogicalPath is null ||
                row.OriginalFlags is null || !FalloutAdvancementRuntimeReceipt.Digest(row.RecordSha256?.ToLowerInvariant()) ||
                row.MediaSha256 is not null && !FalloutAdvancementRuntimeReceipt.Digest(row.MediaSha256) ||
                row.PlayReturned && row.MediaSha256 is null) || row.PlayReturned && row.NativePlayer is null ||
            row.FinishObserved && (!row.PlayReturned || !row.VoiceRetired) ||
            row.Phase == FalloutInterfaceVoicePhase.Finished && !row.FinishObserved ||
            (row.Phase is FalloutInterfaceVoicePhase.Finished or FalloutInterfaceVoicePhase.Stopped) && row.NativePlayer is null)
            throw new InvalidDataException("Indexed sound ledger fabricated source silence or native/resource ownership.");
    }
    private void Set(FalloutInterfaceSoundVoice row) { _voices[checked((int)row.Ordinal - 1)] = row; Notify(row); }
    private void Notify(FalloutInterfaceSoundVoice row) => Changed?.Invoke(row);
}
