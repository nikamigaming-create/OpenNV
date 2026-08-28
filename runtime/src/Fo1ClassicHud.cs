using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1ClassicHud : Control
{
    private readonly Dictionary<string, Image> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Image> _weaponImages = new(StringComparer.Ordinal);
    private Fo1ClassicInterfaceAssets _assets = null!;
    private Fo1ClassicHudLayout _layout = null!;
    private Image _fontAtlas = null!;
    private ImageTexture _composedTexture = null!;
    private TextureRect _surface = null!;
    private Viewport? _subscribedViewport;
    private string _equippedWeaponSymbol = "";
    private int _weaponArtSwitches;

    internal int WeaponArtSwitches => _weaponArtSwitches;
    internal string EquippedWeaponSymbol => _equippedWeaponSymbol;

    internal void Configure(
        Fo1ClassicInterfaceAssets assets,
        Action openPipBoy,
        Action swapWeapon)
    {
        _assets = assets;
        _layout = assets.Layout;
        foreach (var (id, texture) in assets.Textures)
            _images.Add(id, texture.LoadImage());
        foreach (var (symbol, texture) in assets.WeaponInventoryBySymbol)
            _weaponImages.Add(symbol, texture.LoadImage());
        _fontAtlas = assets.MessageFont.LoadImage();

        Name = "OwnedFallout1GameplayInterface";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _composedTexture = ImageTexture.CreateFromImage(_images["main"]);
        _surface = new TextureRect
        {
            Name = "OwnedIfaceFrmComposedSurface",
            Texture = _composedTexture,
            Size = new Vector2(_layout.Width, _layout.Height),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_surface);

        var pipButton = new Button
        {
            Name = "OwnedIfacePipButton",
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Open Pip-Boy 2000 (P)",
        };
        PlaceOnSurface(pipButton, _layout.Buttons.PipBoy);
        pipButton.Pressed += openPipBoy;
        _surface.AddChild(pipButton);

        var swapButton = new Button
        {
            Name = "OwnedIfaceSwapHandsButton",
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = "Swap equipped weapon",
        };
        PlaceOnSurface(
            swapButton,
            new Fo1HudRect(
                _layout.Buttons.SwapHands.X,
                _layout.Buttons.SwapHands.Y,
                assets.RedButton.Width,
                assets.RedButton.Height));
        swapButton.Pressed += swapWeapon;
        _surface.AddChild(swapButton);
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

    internal void Refresh(
        int hitPoints,
        int maximumHitPoints,
        int armorClass,
        int actionPoints,
        int maximumActionPoints,
        string equippedWeaponSymbol,
        int weaponActionPointCost,
        bool firstPerson,
        string status)
    {
        var canvas = Image.CreateEmpty(
            _layout.Width,
            _layout.Height,
            false,
            Image.Format.Rgba8);
        Blit(canvas, _images["main"], Vector2I.Zero);
        DrawPermanentButtons(canvas);
        if (!_weaponImages.ContainsKey(equippedWeaponSymbol))
            throw new InvalidOperationException(
                $"Fallout HUD cannot draw equipped weapon symbol: {equippedWeaponSymbol}.");
        if (_equippedWeaponSymbol.Length > 0 &&
            !string.Equals(
                _equippedWeaponSymbol,
                equippedWeaponSymbol,
                StringComparison.Ordinal))
            _weaponArtSwitches++;
        _equippedWeaponSymbol = equippedWeaponSymbol;
        DrawItemPanel(canvas, equippedWeaponSymbol, weaponActionPointCost);
        DrawVitals(canvas, hitPoints, maximumHitPoints, armorClass);
        DrawActionPoints(canvas, actionPoints, maximumActionPoints);
        if (!firstPerson)
            DrawCombatWindow(canvas);
        DrawMessage(canvas, status);
        _composedTexture.Update(canvas);
    }

    internal object Report() => new
    {
        source = "owned Fallout 1 IFACE/INTRFACE/INVEN FRMs and FONT1.AAF",
        width = _layout.Width,
        height = _layout.Height,
        compositor = "one source-pixel RGBA surface",
        godotLabels = 0,
        live = new[] { "message", "hit-points", "armor-class", "action-points", "combat-state" },
        item = "owned source-symbol inventory art and SINGLE/AP-cost panels",
        equippedWeaponSymbol = _equippedWeaponSymbol,
        equippedWeaponArt = _equippedWeaponSymbol.Length == 0
            ? null
            : _assets.WeaponInventory(_equippedWeaponSymbol).Report(),
        configuredWeaponSymbols = _assets.WeaponInventoryBySymbol.Keys
            .OrderBy(value => value).ToArray(),
        weaponArtSwitches = _weaponArtSwitches,
        swapHandsAccess = "exact retail swap-hands red-button rectangle",
        pipBoyAccess = "P key or exact retail PIP control rectangle",
    };

    private void LayoutSurface()
    {
        var viewportSize = GetViewportRect().Size;
        var availableScale = MathF.Min(
            viewportSize.X / _layout.Width,
            viewportSize.Y / _layout.Height);
        var scale = availableScale >= 1.0f
            ? MathF.Floor(availableScale)
            : availableScale;
        scale = MathF.Max(scale, 0.01f);
        _surface.Size = new Vector2(_layout.Width * scale, _layout.Height * scale);
        _surface.Position = new Vector2(
            MathF.Floor((viewportSize.X - _surface.Size.X) * 0.5f),
            MathF.Floor(viewportSize.Y - _surface.Size.Y));
    }

    private void DrawPermanentButtons(Image canvas)
    {
        Blit(canvas, _images["inventoryButton"], _layout.Buttons.Inventory.Pixels);
        Blit(canvas, _images["optionsButton"], _layout.Buttons.Options.Pixels);
        Blend(canvas, _images["redButton"], _layout.Buttons.SwapHands.Pixels);
        Blend(canvas, _images["redButton"], _layout.Buttons.Skilldex.Pixels);
        Blend(canvas, _images["automapButton"], _layout.Buttons.Automap.Pixels);
        Blit(canvas, _images["characterButton"], _layout.Buttons.Character.Pixels);
        Blit(canvas, _images["pipBoyButton"], _layout.Buttons.PipBoy.Pixels.Position);
    }

    private void DrawItemPanel(
        Image canvas,
        string equippedWeaponSymbol,
        int actionPointCost)
    {
        var item = _layout.Item;
        Blit(canvas, _images["itemPanel"], item.Bounds.Pixels.Position);
        Blend(canvas, _images["singleAttack"], PanelPoint(item.Bounds, item.Single));
        Blend(canvas, _images["movePoints"], PanelPoint(item.Bounds, item.MovePoints));

        var digit = Math.Clamp(actionPointCost, 0, 9);
        var numbers = _images["moveNumbers"];
        var sourceX = digit * item.MoveDigitWidth;
        var sourceWidth = Math.Min(item.MoveDigitWidth, numbers.GetWidth() - sourceX);
        if (sourceWidth > 0)
        {
            canvas.BlendRect(
                numbers,
                new Rect2I(sourceX, 0, sourceWidth, numbers.GetHeight()),
                PanelPoint(item.Bounds, item.MoveNumber));
        }
        var weapon = _weaponImages[equippedWeaponSymbol];
        var weaponPoint = new Fo1HudPoint(
            item.Weapon.X + (item.WeaponSlotWidth - weapon.GetWidth()) / 2,
            item.Weapon.Y + (item.WeaponSlotHeight - weapon.GetHeight()) / 2);
        Blend(canvas, weapon, PanelPoint(item.Bounds, weaponPoint));
    }

    private void DrawVitals(
        Image canvas,
        int hitPoints,
        int maximumHitPoints,
        int armorClass)
    {
        var redThreshold = (int)(Math.Max(0, maximumHitPoints) * 0.25);
        var yellowThreshold = (int)(Math.Max(0, maximumHitPoints) * 0.5);
        var hitPointColorOffset = hitPoints < redThreshold
            ? _layout.Numbers.RedOffset
            : hitPoints < yellowThreshold
                ? _layout.Numbers.YellowOffset
                : _layout.Numbers.WhiteOffset;
        DrawNumber(canvas, _layout.HitPoints, hitPoints, hitPointColorOffset);
        DrawNumber(canvas, _layout.ArmorClass, armorClass, _layout.Numbers.WhiteOffset);
    }

    private void DrawNumber(Image canvas, Fo1HudPoint destination, int value, int colorOffset)
    {
        var layout = _layout.Numbers;
        var normalized = Math.Clamp(value, -999, 999);
        var magnitude = Math.Abs(normalized);
        var digits = new[] { magnitude / 100, magnitude / 10 % 10, magnitude % 10 };
        var numbers = _images["numbers"];
        var signX = colorOffset + (normalized >= 0 ? layout.PlusX : layout.MinusX);
        canvas.BlitRect(
            numbers,
            new Rect2I(signX, 0, layout.SignWidth, layout.Height),
            destination.Pixels);
        for (var index = 0; index < digits.Length; index++)
        {
            canvas.BlitRect(
                numbers,
                new Rect2I(
                    colorOffset + digits[index] * layout.DigitWidth,
                    0,
                    layout.DigitWidth,
                    layout.Height),
                destination.Pixels + new Vector2I(
                    layout.SignWidth + index * layout.DigitWidth,
                    0));
        }
    }

    private void DrawActionPoints(Image canvas, int actionPoints, int maximumActionPoints)
    {
        var layout = _layout.ActionPoints;
        var available = Math.Clamp(actionPoints, 0, Math.Min(maximumActionPoints, layout.Slots));
        for (var index = 0; index < available; index++)
        {
            Blit(
                canvas,
                _images["actionPointGreen"],
                layout.Bounds.Pixels.Position + new Vector2I(index * layout.Stride, 0));
        }
    }

    private void DrawCombatWindow(Image canvas)
    {
        Blit(canvas, _images["endWindow"], _layout.Combat.Window.Pixels.Position);
        Blit(canvas, _images["endTurn"], _layout.Combat.EndTurn.Pixels);
        Blit(canvas, _images["endCombat"], _layout.Combat.EndCombat.Pixels);
        Blend(canvas, _images["endLightGreen"], _layout.Combat.Window.Pixels.Position);
    }

    private void DrawMessage(Image canvas, string status)
    {
        var lines = WrapMessage(status);
        var font = _assets.MessageFont;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var cursor = new Vector2I(
                _layout.Message.Bounds.X + lineIndex * _layout.Message.LineIndent,
                _layout.Message.Bounds.Y + lineIndex * (font.MaximumHeight + font.LineSpacing));
            foreach (var codePoint in lines[lineIndex])
            {
                var width = font.GlyphWidths[codePoint];
                if (width > 0)
                {
                    canvas.BlendRect(
                        _fontAtlas,
                        new Rect2I(
                            codePoint % 16 * font.CellWidth,
                            codePoint / 16 * font.MaximumHeight,
                            width,
                            font.MaximumHeight),
                        cursor);
                    cursor.X += width + font.LetterSpacing;
                }
                else
                {
                    cursor.X += font.WordSpacing;
                }
            }
        }
    }

    private IReadOnlyList<IReadOnlyList<byte>> WrapMessage(string status)
    {
        var words = status.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lines = new List<IReadOnlyList<byte>>();
        var current = new List<byte> { (byte)_layout.Message.PrefixCodePoint };
        foreach (var word in words)
        {
            var encoded = word.Select(ToFalloutCodePoint).ToArray();
            var candidate = new List<byte>(current);
            if (candidate.Count > 0)
                candidate.Add(32);
            candidate.AddRange(encoded);
            var lineIndex = lines.Count;
            var availableWidth = _layout.Message.Bounds.Width -
                lineIndex * _layout.Message.LineIndent;
            if (current.Count > 1 && Measure(candidate) > availableWidth)
            {
                lines.Add(current);
                if (lines.Count == _layout.Message.MaximumLines)
                    break;
                current = new List<byte>(encoded);
            }
            else
            {
                current = candidate;
            }
        }
        if (lines.Count < _layout.Message.MaximumLines && current.Count > 0)
            lines.Add(current);
        return lines;
    }

    private int Measure(IEnumerable<byte> codePoints)
    {
        var font = _assets.MessageFont;
        var width = 0;
        foreach (var codePoint in codePoints)
        {
            var glyphWidth = font.GlyphWidths[codePoint];
            width += glyphWidth > 0 ? glyphWidth + font.LetterSpacing : font.WordSpacing;
        }
        return width;
    }

    private static byte ToFalloutCodePoint(char value) => value switch
    {
        '•' => 149,
        '…' => 133,
        '—' => 151,
        '–' => 150,
        _ when value <= byte.MaxValue => (byte)value,
        _ => (byte)'?',
    };

    private static Vector2I PanelPoint(Fo1HudRect panel, Fo1HudPoint local) =>
        panel.Pixels.Position + local.Pixels;

    private static void Blit(Image canvas, Image source, Vector2I destination)
    {
        canvas.BlitRect(
            source,
            new Rect2I(0, 0, source.GetWidth(), source.GetHeight()),
            destination);
    }

    private static void Blend(Image canvas, Image source, Vector2I destination)
    {
        canvas.BlendRect(
            source,
            new Rect2I(0, 0, source.GetWidth(), source.GetHeight()),
            destination);
    }

    private void PlaceOnSurface(Control control, Fo1HudRect sourceRect)
    {
        control.AnchorLeft = (float)sourceRect.X / _layout.Width;
        control.AnchorTop = (float)sourceRect.Y / _layout.Height;
        control.AnchorRight = (float)(sourceRect.X + sourceRect.Width) / _layout.Width;
        control.AnchorBottom = (float)(sourceRect.Y + sourceRect.Height) / _layout.Height;
        control.OffsetLeft = 0.0f;
        control.OffsetTop = 0.0f;
        control.OffsetRight = 0.0f;
        control.OffsetBottom = 0.0f;
    }
}
