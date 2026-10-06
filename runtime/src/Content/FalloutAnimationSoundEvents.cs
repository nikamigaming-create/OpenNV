using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal enum FalloutAnimationSoundEnd { Active, NativeFinished, SourceStopped, ChanceSkipped, Cancelled, Faulted }

internal sealed record FalloutAnimationSoundEvent(long Generation, FalloutFormKey Sound, string SoundSha256,
    string TextKey, string LogicalPath, IReadOnlyList<string> Variants, bool Played, string? Path, string? MediaSha256, float PitchScale,
    ulong RandomBefore, ulong RandomAfter, bool StereoOutput, IReadOnlyList<string> PartialLanes,
    FalloutAnimationSoundEnd End, string? Error = null)
{
    internal FalloutAnimationSoundEvent Copy() => this with
    { PartialLanes = Array.AsReadOnly(PartialLanes.ToArray()), Variants = Array.AsReadOnly(Variants.ToArray()) };
}

internal sealed record FalloutAnimationSoundEventsSnapshot(FalloutFormKey Reference, long Generation,
    IReadOnlyList<FalloutAnimationSoundEvent> Events)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Reference.OwnerPlugin) || Reference.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
            Generation < 0 || Events is null || Events.Count != Generation)
            throw new InvalidDataException("Saved animation sound history has invalid owner or generations.");
        long previous = 0;
        foreach (var entry in Events)
        {
            if (entry is null || entry.Generation != ++previous || !Hash(entry.SoundSha256) ||
                string.IsNullOrWhiteSpace(entry.Sound.OwnerPlugin) || entry.Sound.ObjectId is 0 or > FalloutFormKey.ObjectIdMask ||
                string.IsNullOrWhiteSpace(entry.TextKey) || !Path(entry.LogicalPath) || entry.Variants is not { Count: > 0 } ||
                entry.Variants.Any(path => !Path(path)) || entry.Variants.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Variants.Count ||
                !float.IsFinite(entry.PitchScale) || entry.PitchScale <= 0 ||
                entry.PartialLanes is null || entry.PartialLanes.Any(string.IsNullOrWhiteSpace) ||
                entry.PartialLanes.Distinct(StringComparer.Ordinal).Count() != entry.PartialLanes.Count || entry.Error is not null ||
                (entry.Played ? entry.End is not (FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped) ||
                    !Hash(entry.MediaSha256) || !Path(entry.Path) || !entry.Variants.Contains(entry.Path, StringComparer.OrdinalIgnoreCase) :
                    entry.End != FalloutAnimationSoundEnd.ChanceSkipped || entry.Path is not null || entry.MediaSha256 is not null))
                throw new InvalidDataException("Saved animation sound lacks a genuine settled source receipt.");
        }
    }

    internal static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Path(string? value) => value is not null &&
        FalloutBsaArchive.CanonicalPath(value) == value && value.StartsWith("sound\\", StringComparison.OrdinalIgnoreCase);
}

// Native Finished and authored Stop are independent from a zero voice count.
// The ledger schedules no playback and never executes a saved request.
internal sealed class FalloutAnimationSoundEvents(FalloutFormKey reference)
{
    private readonly List<FalloutAnimationSoundEvent> _events = [];
    private long _generation;
    private string? _opaqueError;
    internal FalloutFormKey Reference => reference;
    internal FalloutAnimationSoundHistoryDiagnostic CaptureDiagnostic => new(reference, _generation, CanCapture, _opaqueError,
        _events.Where(entry => entry.End is not (FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped or FalloutAnimationSoundEnd.ChanceSkipped))
            .Select(entry => new FalloutAnimationSoundCaptureBlocker(entry.Generation, entry.Sound, entry.SoundSha256,
                entry.TextKey, entry.Path, entry.MediaSha256, entry.End, entry.Error)).ToArray());
    internal bool CanCapture => _opaqueError is null && _events.All(entry =>
        entry.End is FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped or FalloutAnimationSoundEnd.ChanceSkipped);
    internal IReadOnlyList<FalloutAnimationSoundEvent> Events => _events.AsReadOnly();
    // This is admission to wait in the live session, never admission to capture.
    internal bool CanAwaitNativeCompletion => _opaqueError is null &&
        _events.Any(entry => entry.End == FalloutAnimationSoundEnd.Active) &&
        _events.All(entry => entry.Error is null && (entry.End == FalloutAnimationSoundEnd.Active
            ? entry.Played && FalloutAnimationSoundEventsSnapshot.Hash(entry.MediaSha256)
            : entry.End is FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped or FalloutAnimationSoundEnd.ChanceSkipped));
    internal IEnumerable<string> PartialLanes => _events.SelectMany(entry => entry.PartialLanes.Select(lane => entry.Sound + ":" + lane)).Distinct(StringComparer.Ordinal);

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
        if (end is not (FalloutAnimationSoundEnd.NativeFinished or FalloutAnimationSoundEnd.SourceStopped))
            throw new ArgumentException("Only native Finished or authored Stop completes a voice.", nameof(end));
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

    internal void Fail(long? generation, string error)
    {
        _opaqueError = error;
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
        var snapshot = new FalloutAnimationSoundEventsSnapshot(reference, _generation, _events.Select(entry => entry.Copy()).ToArray());
        snapshot.Validate(); return snapshot;
    }

    internal void Restore(FalloutAnimationSoundEventsSnapshot snapshot, FalloutPluginStack records)
    {
        if (_events.Count != 0 || _generation != 0 || _opaqueError is not null)
            throw new InvalidDataException("Cannot replace an existing animation sound history.");
        ValidateSource(snapshot, records, reference);
        _events.AddRange(snapshot.Events.Select(entry => entry.Copy())); _generation = snapshot.Generation;
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
        }
    }

    internal void ValidateMedia(RuntimeLiveContentSource content)
    {
        foreach (var entry in _events.Where(entry => entry.Played).DistinctBy(entry => (entry.Path, entry.MediaSha256)))
            if (!content.TryRead(entry.Path!, null, out var bytes, out _) ||
                !Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.MediaSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved animation sound media differs from its owned resource.");
    }

    private FalloutAnimationSoundEvent Find(long generation) => generation > 0 && generation <= _events.Count
        ? _events[checked((int)generation - 1)] : throw new InvalidDataException("Unknown animation sound generation.");
    private void Replace(FalloutAnimationSoundEvent entry) => _events[checked((int)entry.Generation - 1)] = entry;
}
