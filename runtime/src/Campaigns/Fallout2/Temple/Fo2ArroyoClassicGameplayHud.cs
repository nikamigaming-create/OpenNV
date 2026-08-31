using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2ArroyoClassicHudState(
    int HitPoints,
    int MaximumHitPoints,
    int ArmorClass,
    int ActionPoints,
    int MaximumActionPoints,
    string CharacterId,
    string Authority);

internal sealed partial class Fo2ArroyoClassicGameplayHud : CanvasLayer
{
    private const int HudCanvasLayer = 10;
    private const float MinimumScale = 0.01f;
    private const float CenterFactor = 0.5f;
    private const int StrengthSpecialIndex = 0;
    private const int EnduranceSpecialIndex = 2;
    private const int AgilitySpecialIndex = 5;
    private const int BaseHitPoints = 15;
    private const int HitPointsPerEndurance = 2;
    private const int BaseActionPoints = 5;
    private const int AgilityPerActionPoint = 2;
    private const int NumericMaximum = 999;
    private const int NumericMinimum = -999;
    private const int DecimalBase = 10;
    private const int HundredsPlace = 100;
    private const double RedHitPointFraction = 0.25;
    private const double YellowHitPointFraction = 0.5;
    private const string KamikazeTrait = "Kamikaze";
    private const string CharacterAuthority =
        "selected Fallout 2 GCD character SPECIAL/traits and first-beat runtime state";

    private readonly Dictionary<string, Image> _images = new(StringComparer.Ordinal);
    private Fo2ArroyoClassicHudSurface _surfaceProfile = null!;
    private Fo2ArroyoClassicHudState? _state;
    private ImageTexture _composedTexture = null!;
    private TextureRect _surface = null!;
    private Viewport? _subscribedViewport;
    private bool _blockedSourceLightVisible;

    internal string Mode => _surfaceProfile.Mode;
    internal string RecipeId => _surfaceProfile.RecipeId;
    internal string RecipeSha256 => _surfaceProfile.RecipeSha256;
    internal int OwnedSourceAssetCount => _surfaceProfile.Assets.Count;
    internal bool OwnedFallout2ClassicInterface => true;
    internal bool SourcePixelLayout => true;
    internal bool RetailBehaviorParity => false;
    internal bool FirstMovementBeatStateComplete => _state is not null;
    internal bool VisibleInViewport => Visible && _state is not null &&
        _surface.Texture is not null;
    internal bool BlockedSourceLightVisible => _blockedSourceLightVisible;
    internal Fo2ArroyoClassicHudState State => _state ??
        throw new InvalidOperationException(
            "Fallout 2 classic HUD has no selected-character state.");

    internal static Fo2ArroyoClassicGameplayHud Build(
        Node parent,
        Fo2ArroyoCavesPresentationCatalog catalog)
    {
        var hud = new Fo2ArroyoClassicGameplayHud
        {
            Name = "OWNED_FALLOUT2_CLASSIC_IFACE_HUD",
            Layer = HudCanvasLayer,
        };
        hud.Configure(catalog.ClassicHud);
        parent.AddChild(hud);
        return hud;
    }

    private void Configure(Fo2ArroyoClassicHudSurface surfaceProfile)
    {
        _surfaceProfile = surfaceProfile;
        foreach (var (id, asset) in surfaceProfile.Assets)
        {
            var image = Image.LoadFromFile(asset.PngPath);
            if (image.IsEmpty() || image.GetWidth() != asset.Width ||
                image.GetHeight() != asset.Height)
                throw new InvalidOperationException(
                    $"Fallout 2 classic HUD PNG dimensions drifted: {id}.");
            _images.Add(id, image);
        }
        SetMeta("fo2_hud_recipe_sha256", surfaceProfile.RecipeSha256);
        SetMeta("fo2_hud_mode", surfaceProfile.Mode);
        SetMeta("fo2_owned_classic_interface", true);
        SetMeta("fo2_source_pixel_layout", true);
        SetMeta("fo2_retail_behavior_parity", false);
        SetMeta("fo2_first_movement_beat_state_complete", false);

        _composedTexture = ImageTexture.CreateFromImage(_images["main"]);
        _surface = new TextureRect
        {
            Name = "OWNED_IFACE_FRM_COMPOSED_SOURCE_PIXEL_SURFACE",
            Texture = _composedTexture,
            Size = new Vector2(surfaceProfile.Width, surfaceProfile.Height),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_surface);
        Visible = false;
    }

    internal void BindCharacter(Fo2CharacterSelection character)
    {
        character.Profile.Validate(character.Mode == Fo2CharacterSelection.CreateMode);
        var special = character.Profile.Special;
        var hitPoints = BaseHitPoints +
            special[StrengthSpecialIndex] +
            HitPointsPerEndurance * special[EnduranceSpecialIndex];
        var armorClass = character.Profile.Traits.Contains(
            KamikazeTrait,
            StringComparer.Ordinal)
            ? 0
            : special[AgilitySpecialIndex];
        var actionPoints = BaseActionPoints +
            special[AgilitySpecialIndex] / AgilityPerActionPoint;
        _state = new Fo2ArroyoClassicHudState(
            hitPoints,
            hitPoints,
            armorClass,
            actionPoints,
            actionPoints,
            character.Id,
            CharacterAuthority);
        _composedTexture.Update(Compose());
        SetMeta("fo2_selected_character_id", character.Id);
        SetMeta("fo2_first_movement_beat_state_complete", true);
        Visible = true;
    }

    internal void SetBlockedMovement(bool blocked)
    {
        if (_blockedSourceLightVisible == blocked)
            return;
        _blockedSourceLightVisible = blocked;
        SetMeta("fo2_blocked_source_light_visible", blocked);
        if (_state is not null)
            _composedTexture.Update(Compose());
    }

    public override void _Ready()
    {
        _subscribedViewport = GetViewport();
        _subscribedViewport.SizeChanged += LayoutSurface;
        LayoutSurface();
    }

    public override void _ExitTree()
    {
        if (_subscribedViewport is not null)
            _subscribedViewport.SizeChanged -= LayoutSurface;
        _subscribedViewport = null;
    }

    private Image Compose()
    {
        var state = State;
        var canvas = _images["main"].Duplicate() as Image ??
            throw new InvalidOperationException(
                "Fallout 2 classic IFACE surface could not be copied.");
        canvas.BlitRect(
            _images["itemPanel"],
            new Rect2I(
                Vector2I.Zero,
                new Vector2I(
                    _images["itemPanel"].GetWidth(),
                    _images["itemPanel"].GetHeight())),
            _surfaceProfile.ItemPanel);
        DrawPermanentButtons(canvas);
        DrawNumber(
            canvas,
            _surfaceProfile.HitPoints,
            state.HitPoints,
            HitPointColorOffset(state.HitPoints, state.MaximumHitPoints));
        DrawNumber(
            canvas,
            _surfaceProfile.ArmorClass,
            state.ArmorClass,
            _surfaceProfile.Numbers.WhiteOffset);
        DrawActionPointLights(canvas, state.ActionPoints, state.MaximumActionPoints);
        return canvas;
    }

    private void DrawPermanentButtons(Image canvas)
    {
        Blit(canvas, "inventoryButton", "inventory");
        Blit(canvas, "optionsButton", "options");
        Blend(canvas, "redButton", "swapHands");
        Blend(canvas, "redButton", "skilldex");
        Blend(canvas, "automapButton", "automap");
        Blit(canvas, "characterButton", "character");
        Blit(canvas, "pipBoyButton", "pipBoy");
    }

    private void DrawActionPointLights(Image canvas, int actionPoints, int maximumActionPoints)
    {
        var source = _images[
            _blockedSourceLightVisible ? "actionPointRed" : "actionPointGreen"];
        var available = Math.Clamp(
            actionPoints,
            0,
            Math.Min(maximumActionPoints, _surfaceProfile.ActionPointSlots));
        for (var index = 0; index < available; index++)
        {
            canvas.BlitRect(
                source,
                new Rect2I(0, 0, source.GetWidth(), source.GetHeight()),
                new Vector2I(
                    _surfaceProfile.ActionPointX +
                        index * _surfaceProfile.ActionPointStride,
                    _surfaceProfile.ActionPointY));
        }
    }

    private int HitPointColorOffset(int hitPoints, int maximumHitPoints)
    {
        var redThreshold = (int)(Math.Max(0, maximumHitPoints) * RedHitPointFraction);
        var yellowThreshold =
            (int)(Math.Max(0, maximumHitPoints) * YellowHitPointFraction);
        return hitPoints < redThreshold
            ? _surfaceProfile.Numbers.RedOffset
            : hitPoints < yellowThreshold
                ? _surfaceProfile.Numbers.YellowOffset
                : _surfaceProfile.Numbers.WhiteOffset;
    }

    private void DrawNumber(Image canvas, Vector2I destination, int value, int colorOffset)
    {
        var layout = _surfaceProfile.Numbers;
        var normalized = Math.Clamp(value, NumericMinimum, NumericMaximum);
        var magnitude = Math.Abs(normalized);
        var digits = new[]
        {
            magnitude / HundredsPlace,
            magnitude / DecimalBase % DecimalBase,
            magnitude % DecimalBase,
        };
        var numbers = _images["numbers"];
        var signX = colorOffset + (normalized >= 0 ? layout.PlusX : layout.MinusX);
        canvas.BlitRect(
            numbers,
            new Rect2I(signX, 0, layout.SignWidth, layout.Height),
            destination);
        for (var index = 0; index < digits.Length; index++)
        {
            canvas.BlitRect(
                numbers,
                new Rect2I(
                    colorOffset + digits[index] * layout.DigitWidth,
                    0,
                    layout.DigitWidth,
                    layout.Height),
                destination + new Vector2I(
                    layout.SignWidth + index * layout.DigitWidth,
                    0));
        }
    }

    private void Blit(Image canvas, string assetId, string positionId)
    {
        var source = _images[assetId];
        canvas.BlitRect(
            source,
            new Rect2I(0, 0, source.GetWidth(), source.GetHeight()),
            _surfaceProfile.ButtonPositions[positionId]);
    }

    private void Blend(Image canvas, string assetId, string positionId)
    {
        var source = _images[assetId];
        canvas.BlendRect(
            source,
            new Rect2I(0, 0, source.GetWidth(), source.GetHeight()),
            _surfaceProfile.ButtonPositions[positionId]);
    }

    private void LayoutSurface()
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var scale = SourcePixelScale(viewportSize, _surfaceProfile);
        _surface.Size = new Vector2(
            _surfaceProfile.Width * scale,
            _surfaceProfile.Height * scale);
        _surface.Position = new Vector2(
            MathF.Floor((viewportSize.X - _surface.Size.X) * CenterFactor),
            MathF.Floor(viewportSize.Y - _surface.Size.Y));
    }

    internal static float SourcePixelScale(
        Vector2 viewportSize,
        Fo2ArroyoClassicHudSurface surfaceProfile)
    {
        var availableScale = MathF.Min(
            viewportSize.X / surfaceProfile.Width,
            viewportSize.Y / surfaceProfile.Height);
        var scale = availableScale >= 1.0f
            ? MathF.Floor(availableScale)
            : availableScale;
        return MathF.Max(scale, MinimumScale);
    }
}
