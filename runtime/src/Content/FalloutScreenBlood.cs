using System.Security.Cryptography;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutScreenBloodDrop(float X, float Y, float HalfSize, float Opacity, float AtlasU, float AtlasV);
internal sealed record FalloutScreenBloodRequest(long Id, FalloutFormKey Caller, uint Requested,
    IReadOnlyList<FalloutScreenBloodDrop> Drops, float Duration, string Mask, string Color, ulong RandomBefore, ulong RandomAfter);
internal sealed record FalloutScreenBloodMedia(string Path, string Source, string Sha256);
internal sealed record FalloutScreenBloodPresentation(IReadOnlyList<FalloutScreenBloodMedia> Media,
    Action<float> Fade, Action Release, IReadOnlyList<string> Unbound);

// Transient screen effects share the world/script lifetime, outside campaign
// snapshots. Presentation preparation must succeed before committing geometry,
// random state, or a source command's suffix.
internal sealed class FalloutScreenBlood(FalloutPluginStack records, ulong? seed = null)
{
    private sealed record Binding(bool Enabled, Func<FalloutScreenBloodRequest, FalloutScreenBloodPresentation> Prepare);
    private sealed class Group(FalloutScreenBloodRequest request, FalloutScreenBloodPresentation presentation)
    {
        internal readonly FalloutScreenBloodRequest Request = request;
        internal readonly FalloutScreenBloodPresentation Presentation = presentation;
        internal double Elapsed;
        internal float Fade = 1;
    }
    private Binding? _binding;
    private readonly List<Group> _groups = [];
    private readonly FalloutSoundRandomState _random = new(seed ?? BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))));
    private long _requests, _completed, _cancelled;
    internal int ActiveDrops { get; private set; }
    internal FalloutScreenBloodRequest? LastRequest { get; private set; }
    internal IReadOnlyList<FalloutScreenBloodMedia>? LastMedia { get; private set; }
    internal IReadOnlyList<string> Unbound { get; private set; } = [];
    internal string? LastDisposition { get; private set; }
    internal string? LastError { get; private set; }
    internal object State => new
    {
        bound = _binding is not null,
        enabled = _binding?.Enabled,
        requests = _requests,
        completed = _completed,
        cancelled = _cancelled,
        activeDrops = ActiveDrops,
        groups = _groups.Select(group => new
        {
            request = group.Request,
            group.Elapsed,
            group.Fade,
            media = group.Presentation.Media,
            unbound = group.Presentation.Unbound
        }).ToArray(),
        lastRequest = LastRequest,
        lastMedia = LastMedia,
        unbound = Unbound,
        lastDisposition = LastDisposition,
        error = LastError,
        persistence = "transient-world-effect;not-save-baked;no-cold-replay",
        timing = "shared-menu-paused-game-clock;retail-timing-and-random-stream-unmatched"
    };

    internal static uint Count(double value) => double.IsFinite(value) && value == Math.Truncate(value) &&
        value >= int.MinValue && value <= int.MaxValue ? unchecked((uint)(int)value) :
        throw new InvalidDataException("TriggerScreenBlood count must be a signed integer.");

    internal IDisposable Bind(bool enabled, Func<FalloutScreenBloodRequest, FalloutScreenBloodPresentation> prepare)
    {
        if (_binding is not null) throw new InvalidOperationException("Screen blood already has a presentation owner.");
        ArgumentNullException.ThrowIfNull(prepare);
        var binding = new Binding(enabled, prepare); _binding = binding;
        return new Scope(() => { if (_binding == binding) Clear(); });
    }

    internal void Trigger(FalloutFormKey caller, uint count)
    {
        try
        {
            var binding = _binding ?? throw new NotSupportedException("TriggerScreenBlood has no owned presentation binding.");
            if (!binding.Enabled) { LastDisposition = "source-disabled"; return; }
            var settings = records.NumericSettings;
            var maximum = settings.IntegerBits("iBloodSplatterMaxCount");
            var admitted = Math.Min(count, maximum > ActiveDrops ? maximum - (uint)ActiveDrops : 0);
            if (admitted == 0) { LastDisposition = "zero-or-active-capacity"; return; }
            if (admitted > ushort.MaxValue / 4) throw new NotSupportedException("Screen blood exceeds the source UInt16 quad-index extent.");
            var activeAfter = checked(ActiveDrops + (int)admitted);
            var minimumSize = settings.Float("fBloodSplatterMinSize"); var maximumSize = settings.Float("fBloodSplatterMaxSize");
            var minimumOpacity = settings.Float("fBloodSplatterMinOpacity"); var maximumOpacity = settings.Float("fBloodSplatterMaxOpacity");
            var minimumOpacity2 = settings.Float("fBloodSplatterMinOpacity2"); var maximumOpacity2 = settings.Float("fBloodSplatterMaxOpacity2");
            var chance = settings.Float("fBloodSplatterOpacityChance");
            var duration = settings.Float("fBloodSplatterDuration");
            _ = settings.Float("fBloodSplatterFadeStart");
            var random = new FalloutSoundRandomState(_random.State);
            float Between(float minimum, float maximum) => minimum + (maximum - minimum) * random.NextUnitFloat();
            var drops = new FalloutScreenBloodDrop[admitted];
            for (var index = 0; index < drops.Length; ++index)
            {
                var x = Between(-1, 1); var y = Between(-1, 1); var size = Between(minimumSize, maximumSize);
                var opacity = Between(minimumOpacity, maximumOpacity); var alternate = Between(minimumOpacity2, maximumOpacity2);
                if (random.NextUnitFloat() <= chance) opacity = alternate;
                drops[index] = new(x, y, size, opacity, random.NextUnitFloat() <= .5f ? .5f : 0,
                    random.NextUnitFloat() <= .5f ? .5f : 0);
                if (!float.IsFinite(size) || !float.IsFinite(opacity)) throw new NotSupportedException("Screen blood sampled non-finite geometry or opacity.");
            }
            var request = new FalloutScreenBloodRequest(checked(_requests + 1), caller, count, drops, duration,
                FalloutGameSettingStrings.Read(records, "sBloodSplatterAlpha01OPTFilename"),
                FalloutGameSettingStrings.Read(records, "sBloodSplatterColor01OPTFilename"), _random.State, random.State);
            var presentation = binding.Prepare(request);
            if (presentation.Media.Count != 2 || !Matches(presentation.Media[0], request.Mask) || !Matches(presentation.Media[1], request.Color))
            {
                presentation.Release(); throw new InvalidDataException("Screen blood has no matching owned texture identities.");
            }
            try { presentation.Fade(1); }
            catch { presentation.Release(); throw; }
            _groups.Add(new(request, presentation)); ActiveDrops = activeAfter;
            _random.Restore(random.State); _requests = request.Id;
            LastRequest = request; LastDisposition = "source-blood-active";
            LastMedia = presentation.Media; Unbound = presentation.Unbound; LastError = null;
        }
        catch (FalloutPluginFormatException error) { LastError = error.Message; throw new InvalidDataException(error.Message, error); }
        catch (Exception error) { LastError = error.Message; throw; }
    }

    internal void Advance(double delta, bool gameMode)
    {
        try { AdvanceGroups(delta, gameMode); }
        catch (Exception error) { LastError = error.Message; throw; }
    }

    private void AdvanceGroups(double delta, bool gameMode)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new InvalidDataException("Screen blood clock delta is invalid.");
        if (!gameMode || _groups.Count == 0) return;
        var start = records.NumericSettings.Float("fBloodSplatterFadeStart");
        foreach (var group in _groups.ToArray())
        {
            group.Elapsed += delta;
            if (group.Elapsed >= group.Request.Duration)
            {
                _groups.Remove(group); ActiveDrops -= group.Request.Drops.Count;
                _completed++; group.Presentation.Release(); LastDisposition = "source-blood-expired"; continue;
            }
            if (group.Elapsed / group.Request.Duration > start)
                group.Fade = (float)(1 - (group.Elapsed - group.Request.Duration * start) / (group.Request.Duration * (1 - start)));
            group.Presentation.Fade(group.Fade);
        }
    }

    internal void Clear()
    {
        _binding = null;
        var groups = _groups.ToArray(); _groups.Clear(); ActiveDrops = 0;
        foreach (var group in groups) { _cancelled++; group.Presentation.Release(); }
        if (groups.Length != 0) LastDisposition = "session-retired";
    }
    private static bool Matches(FalloutScreenBloodMedia media, string path) =>
        FalloutBsaArchive.CanonicalPath(media.Path) == FalloutBsaArchive.CanonicalPath(path) &&
        !string.IsNullOrWhiteSpace(media.Source) && media.Sha256.Length == 64 && media.Sha256.All(Uri.IsHexDigit);
    private sealed class Scope(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
    }
}
