using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal enum FalloutAnimationSoundEnd { Active, NativeFinished, SourceStopped, ChanceSkipped, Cancelled, Faulted, SourceUnloaded }
internal sealed record FalloutAnimationSoundBoneEmitterSnapshot(FalloutFormKey Reference,
    string SkeletonPath, string SkeletonSha256, int Block, string Name)
{
    internal void Validate(FalloutFormKey reference)
    {
        if (Reference != reference || string.IsNullOrWhiteSpace(Reference.OwnerPlugin) ||
            Reference.ObjectId is 0 or > FalloutFormKey.ObjectIdMask || Block < 0 ||
            string.IsNullOrWhiteSpace(Name) || !FalloutAnimationSoundEventsSnapshot.Hash(SkeletonSha256) ||
            string.IsNullOrWhiteSpace(SkeletonPath) ||
            FalloutBsaArchive.CanonicalPath(SkeletonPath) != SkeletonPath ||
            !SkeletonPath.StartsWith("meshes\\", StringComparison.OrdinalIgnoreCase) ||
            !SkeletonPath.EndsWith(".nif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved sound bone has an invalid source skeleton identity.");
    }
}
internal sealed record FalloutAnimationSoundPlaybackSnapshot(FalloutPcmPlaybackSnapshot Samples, string EmitterPath,
    bool FollowEmitter, FalloutAnimationSoundBoneEmitterSnapshot? SourceBone = null);
internal sealed record FalloutAnimationSoundFault(string TextKey, string Error);

internal sealed record FalloutAnimationSoundEvent(long Generation, FalloutFormKey Sound, string SoundSha256,
    string TextKey, string LogicalPath, IReadOnlyList<string> Variants, bool Played, string? Path, string? MediaSha256, float PitchScale,
    ulong RandomBefore, ulong RandomAfter, bool StereoOutput, IReadOnlyList<string> PartialLanes,
    FalloutAnimationSoundEnd End, string? Error = null, FalloutAnimationSoundPlaybackSnapshot? Playback = null)
{
    internal FalloutAnimationSoundEvent Copy() => this with
    { PartialLanes = Array.AsReadOnly(PartialLanes.ToArray()), Variants = Array.AsReadOnly(Variants.ToArray()) };
}

internal sealed record FalloutAnimationSoundEventsSnapshot(FalloutFormKey Reference, long Generation,
    IReadOnlyList<FalloutAnimationSoundEvent> Events, string? OpaqueError = null, IReadOnlyList<FalloutAnimationSoundFault>? Faults = null)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Reference.OwnerPlugin) || Reference.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            Generation < 0 || Events is null || Events.Count != Generation)
            throw new InvalidDataException("Saved animation sound history has invalid owner or generations.");
        if (Faults?.Any(fault => fault is null || string.IsNullOrWhiteSpace(fault.TextKey) || string.IsNullOrWhiteSpace(fault.Error)) == true ||
            OpaqueError is not null && Faults?.Any(fault => fault.Error == OpaqueError) != true)
            throw new InvalidDataException("Saved animation sound failure has no original source event.");
        long previous = 0;
        foreach (var entry in Events)
        {
            if (entry is null || entry.Generation != ++previous || !Hash(entry.SoundSha256) ||
                string.IsNullOrWhiteSpace(entry.Sound.OwnerPlugin) || entry.Sound.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
                string.IsNullOrWhiteSpace(entry.TextKey) || !Path(entry.LogicalPath) || entry.Variants is not { Count: > 0 } ||
                entry.Variants.Any(path => !Path(path)) || entry.Variants.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Variants.Count ||
                !float.IsFinite(entry.PitchScale) || entry.PitchScale <= 0 ||
                entry.PartialLanes is null || entry.PartialLanes.Any(string.IsNullOrWhiteSpace) ||
                entry.PartialLanes.Distinct(StringComparer.Ordinal).Count() != entry.PartialLanes.Count ||
                !Enum.IsDefined(entry.End) ||
                (entry.End is FalloutAnimationSoundEnd.Cancelled or FalloutAnimationSoundEnd.Faulted
                    ? string.IsNullOrWhiteSpace(entry.Error) : entry.Error is not null) ||
                (entry.Played ? !Path(entry.Path) || !entry.Variants.Contains(entry.Path, StringComparer.OrdinalIgnoreCase) ||
                    (entry.MediaSha256 is not null && !Hash(entry.MediaSha256)) ||
                    (entry.End != FalloutAnimationSoundEnd.Faulted && entry.MediaSha256 is null) ||
                    entry.End is FalloutAnimationSoundEnd.ChanceSkipped or FalloutAnimationSoundEnd.Cancelled :
                    entry.End != FalloutAnimationSoundEnd.ChanceSkipped || entry.Path is not null || entry.MediaSha256 is not null))
                throw new InvalidDataException("Saved animation sound lacks a genuine settled source receipt.");
            if (entry.End == FalloutAnimationSoundEnd.Faulted &&
                Faults?.Any(fault => fault.TextKey == entry.TextKey && fault.Error == entry.Error) != true)
                throw new InvalidDataException("Failed sound has no matching original source event.");
            if (entry.End == FalloutAnimationSoundEnd.Active)
            {
                if (entry.Playback is not { } playback || string.IsNullOrWhiteSpace(playback.EmitterPath) ||
                    playback.EmitterPath.StartsWith('/') || playback.EmitterPath.Split('/').Contains(".."))
                    throw new InvalidDataException("Active sound has no complete PCM/emitter continuation.");
                playback.Samples.Validate();
                playback.SourceBone?.Validate(Reference);
            }
            else if (entry.Playback is not null) throw new InvalidDataException("Ended sound retains active PCM continuation.");
        }
    }

    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Path(string? value) => value is not null &&
        FalloutBsaArchive.CanonicalPath(value) == value && value.StartsWith("sound\\", StringComparison.OrdinalIgnoreCase);
}

// Native Finished and authored Stop are independent from a zero voice count.
// The ledger schedules no playback and never executes a saved request.
internal sealed class FalloutAnimationSoundEvents(FalloutFormKey reference, Func<bool>? referenceEnabled = null)
{
    private readonly List<FalloutAnimationSoundEvent> _events = [];
    private long _generation;
    private string? _opaqueError;
    private readonly List<FalloutAnimationSoundFault> _faults = [];
    private readonly Dictionary<long, Func<FalloutAnimationSoundPlaybackSnapshot>> _playbackCaptures = [];
    internal FalloutFormKey Reference => reference;
    internal void RequireEnabledSourceEmitter()
    {
        if (referenceEnabled is null)
            throw new NotSupportedException("Source sound bone has no authoritative reference enable owner.");
        if (!referenceEnabled())
            throw new NotSupportedException("Disabled source sound bone requires its independent emitter continuation.");
    }
    internal FalloutAnimationSoundHistoryDiagnostic CaptureDiagnostic => new(reference, _generation, CanCapture, _opaqueError,
        _events.Where(entry => entry.End is not (FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped or FalloutAnimationSoundEnd.ChanceSkipped or FalloutAnimationSoundEnd.SourceUnloaded))
            .Select(entry => new FalloutAnimationSoundCaptureBlocker(entry.Generation, entry.Sound, entry.SoundSha256,
                entry.TextKey, entry.Path, entry.MediaSha256, entry.End, entry.Error)).ToArray());
    internal bool CanCapture => (_opaqueError is null || _faults.Any(fault => fault.Error == _opaqueError)) &&
        _events.All(entry => entry.End switch
        {
            FalloutAnimationSoundEnd.Active => CanCapturePlayback(entry.Generation),
            FalloutAnimationSoundEnd.Faulted => _faults.Any(fault => fault.TextKey == entry.TextKey && fault.Error == entry.Error),
            FalloutAnimationSoundEnd.Cancelled => false,
            _ => entry.End is FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped or FalloutAnimationSoundEnd.ChanceSkipped or FalloutAnimationSoundEnd.SourceUnloaded
        });
    internal IReadOnlyList<FalloutAnimationSoundEvent> Events => _events.AsReadOnly();
    // This is admission to wait in the live session, never admission to capture.
    internal bool CanAwaitNativeCompletion => (_opaqueError is null || _faults.Any(fault => fault.Error == _opaqueError)) &&
        _events.Any(entry => entry.End == FalloutAnimationSoundEnd.Active) &&
        _events.All(entry => entry.End switch
        {
            FalloutAnimationSoundEnd.Active => entry.Played && FalloutAnimationSoundEventsSnapshot.Hash(entry.MediaSha256),
            FalloutAnimationSoundEnd.Faulted => _faults.Any(fault => fault.TextKey == entry.TextKey && fault.Error == entry.Error),
            _ => entry.Error is null && entry.End is FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped or FalloutAnimationSoundEnd.ChanceSkipped or FalloutAnimationSoundEnd.SourceUnloaded
        });
    internal IEnumerable<FalloutAnimationSoundEvent> PendingNativeCompletion =>
        _events.Where(entry => entry.End == FalloutAnimationSoundEnd.Active && !CanCapturePlayback(entry.Generation));
    internal IEnumerable<string> PartialLanes => _events.SelectMany(entry => entry.PartialLanes.Select(lane => entry.Sound + ":" + lane)).Distinct(StringComparer.Ordinal);
    internal IEnumerable<string> OwnedLanes => PartialLanes.Concat(_faults.Select(fault => fault.TextKey + ":" + fault.Error));

    internal void BindPlayback(long generation, Func<FalloutAnimationSoundPlaybackSnapshot> capture)
    {
        if (Find(generation).End != FalloutAnimationSoundEnd.Active || !_playbackCaptures.TryAdd(generation, capture))
            throw new InvalidDataException("PCM continuation has no unique active source generation.");
    }
    private bool CanCapturePlayback(long generation)
    {
        if (!_playbackCaptures.TryGetValue(generation, out var capture)) return false;
        try { capture().Samples.Validate(); return true; }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException) { return false; }
    }

    internal long Begin(FalloutPluginStack records, FalloutAnimationSoundSelection selected, string textKey, bool stereoOutput,
        IReadOnlyList<string> variants)
    {
        var source = records.GetEffective(selected.Source.FormKey);
        if (source.Signature != "SOUN") throw new InvalidDataException("Animation sound event has no winning SOUN.");
        var generation = checked(_generation + 1);
        _events.Add(new(generation, source.FormKey, Convert.ToHexString(SHA256.HashData(source.ReadData())), textKey,
            selected.Source.LogicalPath, Array.AsReadOnly(variants.Select(FalloutBsaArchive.CanonicalPath).ToArray()),
            selected.Play, selected.Play ? FalloutBsaArchive.CanonicalPath(selected.Path!) : null, null, selected.PitchScale,
            selected.RandomBefore, selected.RandomAfter, stereoOutput, Array.AsReadOnly(selected.Unbound.ToArray()),
            selected.Play ? FalloutAnimationSoundEnd.Active : FalloutAnimationSoundEnd.ChanceSkipped));
        _generation = generation;
        return generation;
    }

    internal void BindMedia(long generation, string sha256)
    {
        var entry = Find(generation);
        if (!entry.Played || entry.End != FalloutAnimationSoundEnd.Active || entry.MediaSha256 is not null || !FalloutAnimationSoundEventsSnapshot.Hash(sha256))
            throw new InvalidDataException("Animation sound media binding is missing, repeated or outside its active generation.");
        Replace(entry with { MediaSha256 = sha256 });
    }

    internal bool Complete(long generation, FalloutAnimationSoundEnd end)
    {
        if (end is not (FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped or FalloutAnimationSoundEnd.SourceUnloaded))
            throw new ArgumentException("A voice can end through native Finished, authored Stop or source unload.", nameof(end));
        var entry = Find(generation);
        if (entry.End == end) return false;
        if (entry.End != FalloutAnimationSoundEnd.Active || !FalloutAnimationSoundEventsSnapshot.Hash(entry.MediaSha256))
            throw new InvalidDataException("Animation sound completion has no matching active media generation.");
        Replace(entry with { End = end }); return true;
    }

    internal void Cancel(long generation, string error)
    {
        var entry = Find(generation);
        if (entry.End != FalloutAnimationSoundEnd.Active) return;
        Replace(entry with { End = FalloutAnimationSoundEnd.Cancelled, Error = error });
    }

    internal void Fail(long? generation, string error, string? textKey = null)
    {
        _opaqueError = error;
        if (!string.IsNullOrWhiteSpace(textKey)) _faults.Add(new(textKey, error));
        if (generation is { } id && Find(id) is { End: FalloutAnimationSoundEnd.Active } entry)
            Replace(entry with { End = FalloutAnimationSoundEnd.Faulted, Error = error });
    }

    internal FalloutAnimationSoundEventsSnapshot Capture()
    {
        if (!CanCapture)
        {
            var diagnostic = CaptureDiagnostic;
            var first = diagnostic.Unsettled.FirstOrDefault();
            throw new NotSupportedException($"Animation sound requires its active, cancelled or failed source continuation: reference={reference} " +
                (first is null ? $"opaque={_opaqueError}" : $"generation={first.Generation} sound={first.Sound} end={first.End} " +
                    $"sourceSha256={first.SoundSha256} error={first.Error ?? _opaqueError ?? "none"} unsettled={diagnostic.Unsettled.Count}"));
        }
        var snapshot = new FalloutAnimationSoundEventsSnapshot(reference, _generation, _events.Select(entry => entry.Copy() with
        { Playback = entry.End == FalloutAnimationSoundEnd.Active ? _playbackCaptures[entry.Generation]() : null }).ToArray(),
            _opaqueError, _faults.Count == 0 ? null : _faults.ToArray());
        snapshot.Validate(); return snapshot;
    }

    internal void Restore(FalloutAnimationSoundEventsSnapshot snapshot, FalloutPluginStack records)
    {
        if (_events.Count != 0 || _generation != 0 || _opaqueError is not null)
            throw new InvalidDataException("Cannot replace an existing animation sound history.");
        ValidateSource(snapshot, records, reference);
        _events.AddRange(snapshot.Events.Select(entry => entry.Copy())); _generation = snapshot.Generation;
        _opaqueError = snapshot.OpaqueError; _faults.AddRange(snapshot.Faults ?? []);
    }

    internal static void ValidateSource(FalloutAnimationSoundEventsSnapshot snapshot, FalloutPluginStack records, FalloutFormKey reference)
    {
        snapshot.Validate();
        if (snapshot.Reference != reference) throw new InvalidDataException("Animation sound history belongs to another reference.");
        foreach (var entry in snapshot.Events)
        {
            var record = records.GetEffective(entry.Sound);
            if (record.Signature != "SOUN" || !Convert.ToHexString(SHA256.HashData(record.ReadData())).Equals(entry.SoundSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved animation sound differs from the winning SOUN.");
            // Mutable source file overrides may change again after this ended
            // request. Retain that request's actual path and variant population.
            var source = FalloutSoundRecordReader.Read(record) with { LogicalPath = entry.LogicalPath };
            if (!FalloutAnimationSound.Variants(source, entry.Variants).SequenceEqual(entry.Variants, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved sound variants are outside their source request.");
            var selected = FalloutAnimationSound.Select(source, entry.Variants, new(entry.RandomBefore), true, entry.StereoOutput);
            if (selected.Play != entry.Played || selected.Path != entry.Path || selected.PitchScale != entry.PitchScale ||
                selected.RandomAfter != entry.RandomAfter || !selected.Unbound.SequenceEqual(entry.PartialLanes))
                throw new InvalidDataException("Saved audio selection or partial lanes differ from the consumed source request.");
            if (entry.Playback is { } playback && playback.Samples.Loop != FalloutSoundLoop.Read(source))
                throw new InvalidDataException("Saved PCM loop differs from its original SOUN.");
        }
    }

    internal void ValidateMedia(RuntimeLiveContentSource content)
    {
        foreach (var entry in _events.Where(entry => entry.Played && entry.MediaSha256 is not null).DistinctBy(entry => (entry.Path, entry.MediaSha256)))
            if (!content.TryRead(entry.Path!, null, out var bytes, out _) ||
                !Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.MediaSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved animation sound media differs from its owned resource.");
    }

    private FalloutAnimationSoundEvent Find(long generation) => generation > 0 && generation <= _events.Count
        ? _events[checked((int)generation - 1)] : throw new InvalidDataException("Unknown animation sound generation.");
    private void Replace(FalloutAnimationSoundEvent entry) => _events[checked((int)entry.Generation - 1)] = entry;
}
