using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGamebryoLoadingBackground : Control
{
    private readonly TextureRect[] _slides;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private FalloutLoadingScreen[] _deck = [];
    private readonly double _interval;
    private readonly int _capacity;
    private double _elapsed;
    private int _deckIndex;
    private int _current;
    private bool _transition;
    private double _fade;
    private const double CrossfadeSeconds = 2;

    internal NativeGamebryoLoadingBackground(FalloutInstallationSettings settings, Texture2D initial)
    {
        Name = "LoadingBackground";
        MouseFilter = MouseFilterEnum.Ignore;
        _interval = settings.Number("Loading", "fMainMenuBkgdUpdateInterval");
        _capacity = checked((int)settings.Number("Loading", "iMaxScreens_MainMenu"));
        if (!double.IsFinite(_interval) || _interval <= 0 || _capacity <= 0)
            throw new InvalidDataException("Loading screen interval and resident screen count must be positive.");
        _slides = [Slide("loading_tile_slide_01", initial), Slide("loading_tile_slide_02", initial)];
        foreach (var slide in _slides) AddChild(slide);
        _slides[1].Modulate = new Color(1, 1, 1, 0);
        Resized += () => { foreach (var slide in _slides) slide.Size = Size; };
        SetMeta("opennv_loading_selection_alignment", "unmatched: retail RNG seed/order has not been observed");
    }

    internal void SetCatalog(IReadOnlyList<FalloutLoadingScreen> screens)
    {
        // This presentation deck is not a parity assertion about retail's RNG.
        // Record eligibility, winning texture bytes and transition alpha are
        // independent from the still-unmatched random selection state.
        var candidates = screens.ToArray();
        Random.Shared.Shuffle(candidates);
        _deck = candidates.Take(_capacity).ToArray();
        _deckIndex = 0;
        if (_deck.Length != 0) SetTexture(_current, _deck[0]);
        _elapsed = 0;
    }

    public override void _Process(double delta)
    {
        if (_deck.Length < 2) return;
        _elapsed += delta;
        if (!_transition && _elapsed >= _interval)
        {
            _elapsed -= _interval;
            _fade = _elapsed;
            _deckIndex = (_deckIndex + 1) % _deck.Length;
            SetTexture(1 - _current, _deck[_deckIndex]);
            _transition = true;
        }
        else if (_transition) _fade += delta;
        if (!_transition) return;
        var fraction = Math.Clamp(_fade / CrossfadeSeconds, 0, 1);
        // Native LoadingMenu publishes independent byte-quantized alpha values;
        // intermediate values sum to 254, not 255 (observed live in both lanes).
        _slides[_current].Modulate = new Color(1, 1, 1, (int)(255 * (1 - fraction)) / 255.0f);
        _slides[1 - _current].Modulate = new Color(1, 1, 1, (int)(255 * fraction) / 255.0f);
        SetMeta("opennv_loading_transition_fraction", fraction);
        if (fraction >= 1) { _current = 1 - _current; _transition = false; }
    }

    private void SetTexture(int slide, FalloutLoadingScreen screen)
    {
        if (!_textures.TryGetValue(screen.TexturePath, out var texture))
            _textures.Add(screen.TexturePath, texture = NativeOwnedMediaLoader.LoadTexture(screen.TexturePath));
        _slides[slide].Texture = texture;
        _slides[slide].SetMeta("opennv_source_record", screen.Identity.ToString());
        _slides[slide].SetMeta("opennv_source_texture", screen.TexturePath);
    }

    private static TextureRect Slide(string name, Texture2D texture) => new()
    {
        Name = name,
        Texture = texture,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        MouseFilter = MouseFilterEnum.Ignore,
    };
}
