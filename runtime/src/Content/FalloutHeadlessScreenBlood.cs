namespace OpenNV.Runtime.Content;

// The shared effect owns geometry, random state and its menu-paused lifetime.
// This output adapter keeps CPU media/fade/release receipts without drawing.
internal sealed class FalloutHeadlessScreenBlood : IDisposable
{
    private readonly FalloutScreenBlood _owner;
    private readonly FalloutScreenBloodMediaReader _media;
    private readonly Dictionary<long, float> _fades = [];
    private IDisposable? _binding;
    internal object State => new
    {
        adapter = "headless-source-media-and-effect-lifetime",
        nativeResources = false,
        drawCount = 0,
        bound = _binding is not null,
        validatedMedia = _media.ValidatedCount,
        groups = _fades.Select(pair => new { request = pair.Key, fade = pair.Value }).ToArray(),
        finalPixels = "UNEXECUTED"
    };
    internal int ActiveGroups => _fades.Count;
    internal float Fade(long request) => _fades[request];

    internal FalloutHeadlessScreenBlood(FalloutScreenBlood owner, bool enabled, FalloutScreenBloodMediaReader media)
    {
        _owner = owner; _media = media;
        _binding = owner.Bind(enabled, Prepare, () => State);
    }

    private FalloutScreenBloodPresentation Prepare(FalloutScreenBloodRequest request)
    {
        var mask = _media.Read(request.Mask); var color = _media.Read(request.Color);
        _fades.Add(request.Id, 1);
        var released = false;
        return new([mask, color], fade =>
        {
            if (released) throw new InvalidOperationException("Retired headless blood group received a fade.");
            if (!float.IsFinite(fade)) throw new InvalidDataException("Headless blood fade is non-finite.");
            _fades[request.Id] = fade;
        }, () =>
        {
            if (released) return;
            released = true; _fades.Remove(request.Id);
        }, ["headless-final-pixels-unexecuted", "directional-light-color-uv-offset",
            "directional-light-additive-flare", "source-color-transfer-and-layer-timing"]);
    }

    internal void Advance(double delta, bool gameMode)
    {
        ObjectDisposedException.ThrowIf(_binding is null, this);
        _owner.Advance(delta, gameMode);
    }
    public void Dispose() { _binding?.Dispose(); _binding = null; }
}
