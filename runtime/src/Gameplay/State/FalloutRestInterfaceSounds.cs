using System.Text.Json;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal enum FalloutRestInterfaceCueKind { Start, Cancel }
internal enum FalloutRestInterfaceVoiceState { Requested, NativeAllocated, NativeStarted, NativeFinished, Failed, SessionRetired }
internal sealed record FalloutRestInterfaceVoice(long Sequence, long RequestOrdinal, FalloutRestInterfaceCueKind Kind,
    FalloutRestInterfaceCue Source, FalloutRestInterfaceVoiceState State, RuntimeSaveProcessIdentity Process,
    ulong? NativePlayer, string? Failure, bool NativeVoiceRetired = false);
internal sealed record FalloutRestInterfaceSoundSnapshot(string Schema, string SourceSha256,
    string PlaybackSourceSha256, long LastSequence, IReadOnlyList<FalloutRestInterfaceVoice> Voices, string? Failure);

// A cue request, native start and actual Finished callback are independent
// receipts. Active native menu audio has no invented cold sample cursor.
internal sealed class FalloutRestInterfaceSounds
{
    internal const string Schema = "opennv-rest-interface-sounds/v2";
    private readonly FalloutPluginStack _records;
    private readonly FalloutSleepWait _rest;
    private readonly List<FalloutRestInterfaceVoice> _voices = [];
    private Guid? _native;
    private long _sequence;
    private string? _failure;
    internal FalloutRestInterfaceSoundSource Source { get; }
    internal FalloutMenuCuePlaybackSource PlaybackSource { get; }
    internal string? Failure => _failure;
    internal bool Active => _voices.Any(row => row.State is FalloutRestInterfaceVoiceState.Requested or
        FalloutRestInterfaceVoiceState.NativeAllocated or FalloutRestInterfaceVoiceState.NativeStarted ||
        row.NativePlayer is not null && !row.NativeVoiceRetired);
    internal string? SaveBlocker => Active ? "source-finite-audio" : null;
    internal object State => new { Source, sequence = _sequence, voices = _voices.ToArray(), failure = _failure,
        nativePublished = _native is not null, saveBlocker = SaveBlocker };

    internal FalloutRestInterfaceSounds(FalloutPluginStack records, FalloutSleepWait rest,
        FalloutRestInterfaceSoundSnapshot? restore = null)
    {
        ArgumentNullException.ThrowIfNull(records); ArgumentNullException.ThrowIfNull(rest);
        _records = records; _rest = rest; Source = FalloutRestInterfaceSoundSource.Read(records, rest.Source);
        PlaybackSource = FalloutMenuCuePlaybackSource.Read(rest.Source); PlaybackSource.Validate();
        if (restore is null) return;
        if (restore.Schema != Schema || restore.SourceSha256 != Source.Identity || restore.Voices is null ||
            restore.Voices.Any(row => row is null) ||
            restore.LastSequence != restore.Voices.Count || restore.PlaybackSourceSha256 != PlaybackSource.Identity ||
            restore.Failure is not null && string.IsNullOrWhiteSpace(restore.Failure))
            throw new InvalidDataException("Cold rest interface sounds lack their complete source/playback/voice prefix.");
        foreach (var row in restore.Voices)
        {
            if (row.Sequence != _voices.Count + 1L || row.RequestOrdinal <= 0 || row.RequestOrdinal > rest.RequestOrdinal ||
                !Enum.IsDefined(row.Kind) || !Enum.IsDefined(row.State) || row.Source != Cue(row.Kind) ||
                row.State is FalloutRestInterfaceVoiceState.Requested or FalloutRestInterfaceVoiceState.NativeAllocated or FalloutRestInterfaceVoiceState.NativeStarted ||
                row.NativePlayer is not null && !row.NativeVoiceRetired ||
                row.NativePlayer == 0 || row.NativePlayer is null && row.NativeVoiceRetired || row.Process is null ||
                row.State == FalloutRestInterfaceVoiceState.NativeFinished && (row.NativePlayer is null || !row.NativeVoiceRetired) ||
                row.State == FalloutRestInterfaceVoiceState.SessionRetired && (row.NativePlayer is null || !row.NativeVoiceRetired) ||
                (row.State is FalloutRestInterfaceVoiceState.Failed or FalloutRestInterfaceVoiceState.SessionRetired) != (row.Failure is not null) ||
                row.Failure is not null && string.IsNullOrWhiteSpace(row.Failure) ||
                _voices.Any(prior => prior.RequestOrdinal == row.RequestOrdinal && prior.Kind == row.Kind))
                throw new InvalidDataException("Cold rest interface sound changed its original attempted/finished cue identity.");
            row.Process.Validate(); _ = row.Source.RequireCurrent(records); _voices.Add(row);
        }
        if (_voices.Any(row => row.State == FalloutRestInterfaceVoiceState.Failed) && restore.Failure is null)
            throw new InvalidDataException("Cold rest interface sounds removed a retained native/source failure.");
        _sequence = restore.LastSequence; _failure = restore.Failure;
        // No cue, variant draw, native callback or old player is recreated.
    }

    internal Guid BindNative()
    {
        if (_native is not null || Active) throw new InvalidOperationException("Rest sounds have another native publication or an unresolved active voice.");
        Source.RequireSource(_records, _rest.Source);
        return (_native = Guid.NewGuid()).Value;
    }

    internal FalloutRestInterfaceVoice RequestCue(Guid lease, FalloutRestRequest request, FalloutRestInterfaceCueKind kind)
    {
        RequireNative(lease); RequireHealthy(); request.Validate();
        if (!Enum.IsDefined(kind) || !_rest.Published || !_rest.MenuPending ||
            request.Origin == FalloutRestOrigin.ScriptHours || JsonSerializer.Serialize(request) != JsonSerializer.Serialize(_rest.Request) ||
            kind == FalloutRestInterfaceCueKind.Start && _rest.Phase != FalloutRestPhase.Choosing ||
            kind == FalloutRestInterfaceCueKind.Cancel && _rest.Phase is not (FalloutRestPhase.Choosing or FalloutRestPhase.Running or FalloutRestPhase.Completed) ||
            _voices.Any(row => row.RequestOrdinal == _rest.RequestOrdinal && row.Kind == kind))
            throw new InvalidOperationException("Interface sound requires the actual once-only current start/cancel menu branch.");
        var source = Cue(kind); _ = PlaybackSource.ExactFile(_records, source.RequireCurrent(_records));
        var sequence = checked(_sequence + 1);
        var row = new FalloutRestInterfaceVoice(sequence, _rest.RequestOrdinal, kind, source,
            FalloutRestInterfaceVoiceState.Requested, RuntimeSaveProcessIdentity.Current, null, null);
        _voices.Add(row); _sequence = sequence; return row;
    }

    internal void NativeAllocated(Guid lease, long sequence, ulong player)
    {
        RequireNative(lease); var index = Index(sequence); var row = _voices[index];
        if (player == 0 || row.State != FalloutRestInterfaceVoiceState.Requested)
            throw new InvalidOperationException("Rest sound has no unique actual native allocation receipt.");
        _voices[index] = row with { State = FalloutRestInterfaceVoiceState.NativeAllocated, NativePlayer = player };
    }

    internal void NativeStarted(Guid lease, long sequence, ulong player)
    {
        RequireNative(lease); var index = Index(sequence); var row = _voices[index];
        if (row.State != FalloutRestInterfaceVoiceState.NativeAllocated || row.NativePlayer != player)
            throw new InvalidOperationException("Rest sound has no matching actual native start receipt.");
        _voices[index] = row with { State = FalloutRestInterfaceVoiceState.NativeStarted };
    }

    internal void NativeFinished(Guid lease, long sequence, ulong player)
    {
        RequireNative(lease); var index = Index(sequence); var row = _voices[index];
        if (row.State != FalloutRestInterfaceVoiceState.NativeStarted || row.NativePlayer != player)
            throw new InvalidOperationException("Rest sound has no matching once-only actual Finished callback.");
        _voices[index] = row with { State = FalloutRestInterfaceVoiceState.NativeFinished, NativeVoiceRetired = true };
    }

    internal void Failed(Guid lease, long? sequence, Exception error)
    {
        RequireNative(lease); var failure = error.GetType().Name + ": " + error.Message;
        _failure ??= failure;
        if (sequence is not { } value) return;
        var index = Index(value); var row = _voices[index];
        if (row.State is FalloutRestInterfaceVoiceState.NativeFinished or FalloutRestInterfaceVoiceState.SessionRetired)
            return;
        _voices[index] = row with { State = FalloutRestInterfaceVoiceState.Failed, Failure = row.Failure ?? failure };
    }

    internal void RetireNative(Guid lease)
    {
        RequireNative(lease);
        if (Active) throw new NotSupportedException("Rest sound host still owns an unresolved actual native voice.");
        _native = null;
    }

    // The native host calls this only after the exact original player has
    // stopped. This retires its voice, not its ObjectDB instance, and never
    // emits successful completion or resumes a cancelled menu effect.
    internal void NativeStopped(Guid lease, long sequence, ulong player)
    {
        RequireNative(lease); var index = Index(sequence); var row = _voices[index];
        if (row.NativePlayer != player || player == 0)
            throw new InvalidOperationException("Rest sound stop receipt belongs to another native player.");
        if (row.State == FalloutRestInterfaceVoiceState.NativeFinished) return;
        _voices[index] = row with { State = row.State == FalloutRestInterfaceVoiceState.Failed ? row.State : FalloutRestInterfaceVoiceState.SessionRetired,
            Failure = row.Failure ?? "Actual campaign sound host stopped the voice; no Finished receipt was produced.", NativeVoiceRetired = true };
    }

    internal void NativeDestroyed(Guid lease, long sequence, ulong player) => NativeStopped(lease, sequence, player);

    internal bool FinishedVoice(Guid lease, long sequence)
    {
        RequireNative(lease); return _voices[Index(sequence)].State == FalloutRestInterfaceVoiceState.NativeFinished;
    }

    internal FalloutRestObservation ObserveNativePublication() => _failure is { } failure ?
        new(FalloutRestFactState.Unowned, "actual-rest-interface-sound-host", failure) : _native is null ?
        new(FalloutRestFactState.Unowned, "actual-rest-interface-sound-host", "The actual source sound host has not published.") :
        new(FalloutRestFactState.Satisfied, "actual-rest-interface-sound-host:" + Source.Identity);

    internal FalloutRestObservation ObserveCueCapability(FalloutRestInterfaceCueKind kind)
    {
        var publication = ObserveNativePublication();
        if (publication.State != FalloutRestFactState.Satisfied) return publication;
        try
        {
            _ = PlaybackSource.ExactFile(_records, Cue(kind).RequireCurrent(_records));
            return new(FalloutRestFactState.Satisfied, "actual-exact-menu-cue:" + PlaybackSource.Identity);
        }
        catch (Exception error) when (FalloutPlayerPhysicalActivity.Ordinary(error))
        {
            return new(FalloutRestFactState.Unowned, "actual-exact-menu-cue:" + PlaybackSource.Identity,
                error.GetType().Name + ": " + error.Message);
        }
    }

    internal FalloutRestInterfaceSoundSnapshot Capture()
    {
        if (SaveBlocker is { } blocker)
            throw new NotSupportedException("Rest interface sound capture requires " + blocker + "; active cold sample continuation is unowned.");
        Source.RequireSource(_records, _rest.Source);
        return new(Schema, Source.Identity, PlaybackSource.Identity, _sequence, _voices.ToArray(), _failure);
    }

    private FalloutRestInterfaceCue Cue(FalloutRestInterfaceCueKind kind) => kind switch
    {
        FalloutRestInterfaceCueKind.Start => Source.Start,
        FalloutRestInterfaceCueKind.Cancel => Source.Cancel,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    private int Index(long sequence) => sequence is > 0 && sequence <= _voices.Count
        ? checked((int)sequence - 1) : throw new InvalidDataException("Rest sound callback has no actual request generation.");
    private void RequireNative(Guid lease)
    {
        if (lease == Guid.Empty || _native != lease) throw new InvalidOperationException("Rest sound callback belongs to another native publication.");
    }
    private void RequireHealthy()
    {
        if (_failure is not null) throw new InvalidOperationException("Rest interface sound prefix is closed: " + _failure);
    }
}
