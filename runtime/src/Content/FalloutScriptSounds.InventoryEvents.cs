using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutInventorySoundIntent(FalloutFormKey Caller, FalloutFormKey Sound, bool SystemSound = false);

internal sealed partial class FalloutScriptSounds
{
    internal FalloutInventoryChangeLease PrepareInventorySounds(IReadOnlyList<FalloutInventorySoundIntent> intents)
    {
        if (intents.Count == 0) return new(() => { }, () => { });
        var binding = _binding ?? throw new NotSupportedException("Inventory PlaySound has no owned presentation binding.");
        var baseline = _requests; var randomState = _random.State;
        var random = new FalloutSoundRandomState(randomState);
        var prepared = new List<(FalloutScriptSoundRequest Request, FalloutScriptSoundPlayback? Playback, long Revision)>();
        var started = false; var completed = false;
        var oldRequest = LastRequest; var oldMedia = LastMedia; var oldDisposition = LastDisposition;
        try
        {
            foreach (var intent in intents)
            {
                var record = records.GetEffective(intent.Sound);
                if (record.Signature != "SOUN") throw new InvalidDataException("Inventory PlaySound source is not SOUN.");
                var revision = records.SoundPaths.Revision(intent.Sound);
                var source = FalloutSoundRecordReader.Read(records, intent.Sound);
                var selection = FalloutAnimationSound.Select(source with { Flags = source.Flags | FalloutSoundFlags.TwoDimensional },
                    binding.Variants(source), random, ownsLoopStop: true, stereoOutput: binding.Stereo);
                if (selection.Play && !Path.GetExtension(selection.Path!).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                    throw FalloutSoundPlaybackContract.Unsupported(source, "PlaySound for a non-WAV ambient effect");
                var request = new FalloutScriptSoundRequest(checked(baseline + prepared.Count + 1), intent.Caller,
                    source, selection, intent.SystemSound);
                var playback = selection.Play ? binding.Prepare(request) : null;
                if (playback is not null && (!StringComparer.OrdinalIgnoreCase.Equals(playback.Media.Path, selection.Path) ||
                    playback.Media.Sha256.Length != 64 || !playback.Media.Sha256.All(Uri.IsHexDigit) ||
                    string.IsNullOrWhiteSpace(playback.Media.Source) || !double.IsFinite(playback.Media.Duration) || playback.Media.Duration <= 0))
                {
                    playback.Release(); throw new InvalidDataException("Prepared inventory sound has no matching owned media.");
                }
                prepared.Add((request, playback, revision));
            }
        }
        catch
        {
            foreach (var item in prepared) item.Playback?.Release();
            throw;
        }
        void ReleasePrepared(bool unregister)
        {
            var failures = new List<Exception>();
            foreach (var item in prepared)
            {
                if (unregister && _voices.Remove(item.Request.Id, out var voice))
                    try { voice.Registration?.Dispose(); } catch (Exception error) { failures.Add(error); }
                try { item.Playback?.Release(); } catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count != 0) throw new AggregateException("Prepared inventory sound cleanup failed.", failures);
        }
        void CancelPreparation() => ReleasePrepared(false);
        return new(() =>
        {
            if (!ReferenceEquals(binding, _binding) || _requests != baseline || _random.State != randomState ||
                prepared.Any(item => records.SoundPaths.Revision(item.Request.Source.FormKey) != item.Revision))
                throw new InvalidOperationException("Script sound owner changed during inventory-event preparation.");
            started = true;
            _random.Restore(random.State); _requests = checked(baseline + prepared.Count);
            foreach (var item in prepared)
            {
                LastRequest = item.Request; LastMedia = item.Playback?.Media;
                LastDisposition = item.Request.Selection.Play ? "queued-menu" : "source-sound-chance-skipped";
                if (item.Playback is { } playback) _voices.Add(item.Request.Id, new(item.Request, playback));
            }
        }, () =>
        {
            if (completed) return;
            var ownsTail = !started || _requests == baseline + prepared.Count && _random.State == random.State;
            try { ReleasePrepared(started); }
            finally
            {
                if (started && ownsTail)
                {
                    _requests = baseline; _random.Restore(randomState);
                    LastRequest = oldRequest; LastMedia = oldMedia; LastDisposition = oldDisposition;
                }
            }
            if (!ownsTail)
                throw new InvalidOperationException("Inventory sound rollback no longer owns its source random/request tail.");
        }, () =>
        {
            completed = true;
            foreach (var item in prepared)
                if (_voices.TryGetValue(item.Request.Id, out var voice)) Update(voice, menus.Query() == 0);
        }, CancelPreparation);
    }
}
